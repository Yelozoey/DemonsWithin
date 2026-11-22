using System;
using UnityEngine;

namespace DemonsWithin
{
    public sealed class DuelOpponent : MonoBehaviour
    {
        [SerializeField] private DuelFighter fighter;
        [SerializeField, Min(0)] private float pauseBetweenMoves = 0.48f;
        [SerializeField] private bool adaptToPlayer = true;
        [SerializeField] private DuelBehaviorTree behaviorTree;

        private static readonly DuelAction[] AttackOrder =
            { DuelAction.Quick, DuelAction.Delayed, DuelAction.Thrust };

        private static readonly System.Collections.Generic.List<DuelBehaviorNode> FallbackNodes =
            DuelBehaviorNode.CreateDefaultOrder();

        private DuelNodeState[] trace = Array.Empty<DuelNodeState>();
        private int attackCount;
        private float wait;
        private bool llmMode;

        public OpponentModel Model { get; } = new();
        public LlmTacticsPlanner Tactics { get; } = new();
        public OpponentChoice LastChoice { get; private set; }
        public bool IsAdaptive => adaptToPlayer;
        public bool IsLlmMode => llmMode;
        public string ModeName => llmMode ? "LLM" : adaptToPlayer ? "Adaptive" : "Fixed";
        public DuelBehaviorTree BehaviorTree => behaviorTree;
        public float CooldownRemaining => wait;
        public int ActiveNode { get; private set; } = -1;

        private void Awake()
        {
            if (behaviorTree == null)
                behaviorTree = Resources.Load<DuelBehaviorTree>("DemonBehaviorTree");
        }

        public void ToggleAdaptation()
        {
            llmMode = false;
            adaptToPlayer = !adaptToPlayer;
            Tactics.SetActive(false, 0);
        }

        public void CycleMode(int step)
        {
            if (llmMode)
                llmMode = false;
            else if (adaptToPlayer)
                adaptToPlayer = false;
            else
                llmMode = adaptToPlayer = true;
            Tactics.SetActive(llmMode, step);
        }

        public DuelNodeState StateAt(int index) =>
            index >= 0 && index < trace.Length ? trace[index] : DuelNodeState.Skipped;

        public void ResetOpponent()
        {
            attackCount = 0;
            wait = StrikeCooldown();
            LastChoice = default;
            ActiveNode = -1;
            Array.Clear(trace, 0, trace.Length);
            Model.Reset();
        }

        public (Vector2 movement, DuelAction action, bool guard) Decide(DuelFighter player, float delta)
        {
            var nodes = behaviorTree != null ? behaviorTree.Nodes : FallbackNodes;
            if (trace.Length != nodes.Count)
                trace = new DuelNodeState[nodes.Count];
            else
                Array.Clear(trace, 0, trace.Length);
            ActiveNode = -1;

            bool ready = fighter.CanAct && !fighter.IsAirborne && player.Phase != FighterPhase.Down;
            if (ready)
                wait = Mathf.Max(0f, wait - delta);
            float distance = Vector3.ProjectOnPlane(player.transform.position - fighter.transform.position, Vector3.up).magnitude;
            DuelAction baseline = AttackOrder[attackCount % AttackOrder.Length];
            int interval = behaviorTree != null ? Mathf.Max(2, behaviorTree.BaselineProbeInterval) : 3;
            OpponentChoice choice = ready
                ? Choose(baseline, distance, includeAdaptation: attackCount % interval != 0)
                : default;

            // A priority selector ticks enabled children until one succeeds or is running.
            for (int index = 0; index < nodes.Count; index++)
            {
                DuelBehaviorNode node = nodes[index];
                if (!node.Enabled)
                    continue;
                trace[index] = TickNode(node, ready, distance, choice, out Vector2 movement, out DuelAction action);
                if (trace[index] == DuelNodeState.Failure)
                    continue;

                ActiveNode = index;
                return (movement, action, false);
            }

            return (Vector2.zero, DuelAction.None, false);
        }

        private DuelNodeState TickNode(DuelBehaviorNode node, bool ready, float distance,
            OpponentChoice choice, out Vector2 movement, out DuelAction action)
        {
            movement = Vector2.zero;
            action = DuelAction.None;
            if (node.Type == DuelBehavior.FinishCommitted)
                return ready ? DuelNodeState.Failure : DuelNodeState.Running;
            if (!ready)
                return DuelNodeState.Failure;

            bool inReach = InReach(choice.Chosen, distance);
            switch (node.Type)
            {
                case DuelBehavior.Approach when !inReach:
                    movement = Vector2.up * Mathf.Clamp01(node.MoveInput);
                    return DuelNodeState.Running;
                case DuelBehavior.RecoverSpacing when wait > 0f && inReach:
                    if (distance < node.NearDistance)
                        movement = Vector2.down * Mathf.Clamp01(node.MoveInput);
                    return DuelNodeState.Running;
                case DuelBehavior.Strike when wait <= 0f && inReach:
                    LastChoice = choice;
                    attackCount++;
                    wait = behaviorTree != null ? Mathf.Max(0f, node.Cooldown) : pauseBetweenMoves;
                    action = choice.Chosen;
                    return DuelNodeState.Success;
                default:
                    return DuelNodeState.Failure;
            }
        }

        private float StrikeCooldown()
        {
            if (behaviorTree != null)
                foreach (DuelBehaviorNode node in behaviorTree.Nodes)
                    if (node.Enabled && node.Type == DuelBehavior.Strike)
                        return Mathf.Max(0f, node.Cooldown);
            return pauseBetweenMoves;
        }

        private OpponentChoice Choose(DuelAction baseline, float distance, bool includeAdaptation)
        {
            DuelDistance band = OpponentModel.Band(distance);
            HabitEstimate early = Model.EarlyParryAt(band);
            HabitEstimate retreat = Model.RetreatAt(band);
            DuelAction selected = baseline;
            OpponentTactic tactic = OpponentTactic.Baseline;

            if (adaptToPlayer && includeAdaptation)
            {
                if (llmMode)
                {
                    var plan = Tactics.Suggest(band, Time.realtimeSinceStartupAsDouble);
                    if (plan.action != DuelAction.None && InReach(plan.action, distance))
                        return new OpponentChoice(plan.action, baseline, OpponentTactic.LlmPlan,
                            early, retreat, true, false, plan.id, plan.tactic);
                }

                float delay = InReach(DuelAction.Delayed, distance) ? early.Weight : 0f;
                float thrust = InReach(DuelAction.Thrust, distance) ? retreat.Weight : 0f;
                if (thrust > delay)
                {
                    selected = DuelAction.Thrust;
                    tactic = OpponentTactic.ThrustAgainstRetreat;
                }
                else if (delay > 0f)
                {
                    selected = DuelAction.Delayed;
                    tactic = OpponentTactic.DelayAgainstEarlyParry;
                }
            }

            return new OpponentChoice(selected, baseline, tactic, early, retreat, adaptToPlayer,
                adaptToPlayer && !includeAdaptation);
        }

        private bool InReach(DuelAction action, float distance)
        {
            DuelActionAsset move = fighter.GetMove(action);
            return move != null && distance <= move.Reach - (behaviorTree != null ? behaviorTree.AttackRangeMargin : 0.15f);
        }
    }
}
