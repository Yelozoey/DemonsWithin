using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DemonsWithin
{
    public sealed class DuelArena : MonoBehaviour
    {
        [SerializeField] private DuelFighter player;
        [SerializeField] private DuelFighter demon;
        [SerializeField] private DuelOpponent opponent;
        [SerializeField] private Transform playerSpawn;
        [SerializeField] private Transform demonSpawn;
        [SerializeField] private Camera duelCamera;
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private bool comboTraining;

        private static readonly (DuelAction Action, string Binding)[] CombatBindings =
        {
            (DuelAction.Dodge, "Dodge"), (DuelAction.Mikiri, "Mikiri"), (DuelAction.Parry, "Parry"),
            (DuelAction.Thrust, "Thrust"), (DuelAction.Delayed, "Delayed"),
            (DuelAction.AirOpener, "AirOpener"), (DuelAction.ComboOpener, "ComboOpener"), (DuelAction.Quick, "Quick")
        };

        private DuelCameraRig rig;
        private InputActionAsset inputInstance;
        private InputActionMap input;
        private readonly DuelRecording recording = new();
        private DuelObservation observation;
        private int combatStep;
        private string exportStatus;
        private Vector2 movement;
        private bool guard, sprintHeld, walkHeld, resetPressed, showDetails;
        private DuelAction bufferedAction;
        private float bufferTime;
        private string lastExchange = "Ready";
        private GUIStyle label;
        private GUIStyle title;
        private DuelLucyPanel lucyPanel;
        private bool lucyDemo, lucyPair;
        private LucyCombatEntry playerFinisher, demonFinisher;
        private DuelFighter finishingFighter;
        private int trainingChain, trainingBest, trainingHits;

        public DuelFighter Player => player;
        public DuelFighter Demon => demon;
        public DuelCameraRig CameraRig => rig;
        public bool Finished => player.Health == 0 || demon.Health == 0;
        public bool IsComboTraining => comboTraining;
        public string LastExchange => lastExchange;
        public DuelOpportunity LastOpportunity => observation?.LastOpportunity;

        private void Awake()
        {
            InputActionAsset source = controls != null ? controls : Resources.Load<InputActionAsset>("DuelControls");
            if (source == null)
            {
                Debug.LogError("The duel needs a DuelControls Input Action asset.", this);
                enabled = false;
                return;
            }

            inputInstance = Instantiate(source);
            input = inputInstance.FindActionMap("Duel", true);
            input.actionTriggered += RecordActionChange;
            observation = new DuelObservation(opponent.Model, recording);
            observation.Completed += opponent.Tactics.Observe;
            if (!duelCamera.TryGetComponent(out rig))
                rig = duelCamera.gameObject.AddComponent<DuelCameraRig>();
            rig.Bind(player, demon);
        }

        private void OnEnable() => input?.Enable();

        private void OnDisable()
        {
            input?.Disable();
            Cursor.lockState = CursorLockMode.None;
            if (lucyPanel != null && lucyPanel.IsOpen)
                lucyPanel.Toggle();
            SetLucyPair(false);
            if (opponent != null)
                opponent.Tactics.Stop("Arena disabled", combatStep);
        }

        private void OnDestroy()
        {
            if (input != null)
                input.actionTriggered -= RecordActionChange;
            if (inputInstance != null)
                Destroy(inputInstance);
            if (observation != null && opponent != null)
                observation.Completed -= opponent.Tactics.Observe;
        }

        private void Start()
        {
            if (player.Loadout == null || demon.Loadout == null)
            {
                Debug.LogError("The duel needs a fighter loadout. Use Tools > Demons Within > Create Missing Prototype Assets.", this);
                enabled = false;
                return;
            }
            ResetDuel();
        }

        private void Update()
        {
            if (Pressed("LucyLibrary"))
                ToggleLucyLibrary();
            if (lucyPanel != null && lucyPanel.IsOpen)
                return;
            if (lucyDemo)
            {
                movement = input["Move"].ReadValue<Vector2>();
                resetPressed |= Pressed("Reset");
                return;
            }

            movement = input["Move"].ReadValue<Vector2>();
            guard = input["Guard"].IsPressed();
            sprintHeld = (input.FindAction("Sprint") ?? input.FindAction("Run"))?.IsPressed() == true;
            walkHeld = input.FindAction("Walk")?.IsPressed() == true;
            resetPressed |= Pressed("Reset");

            if (Pressed("Lock") && !Finished && !player.Sprinting)
                rig.SetLocked(!rig.Locked);
            if (Pressed("Details"))
                showDetails = !showDetails;
            if (!comboTraining && Pressed("Adaptation"))
            {
                opponent.CycleMode(combatStep);
                exportStatus = null;
            }
            if (!comboTraining && !Finished && !resetPressed)
                opponent.Tactics.Tick(Time.realtimeSinceStartupAsDouble, combatStep);
            if (Pressed("ExportDuel"))
                ExportRecording();
            if (Pressed("Quick") || Pressed("Lock"))
                Cursor.lockState = CursorLockMode.Locked;
            if (Pressed("ReleaseCursor"))
                Cursor.lockState = CursorLockMode.None;
            if (Cursor.lockState == CursorLockMode.Locked)
                rig.Look(input["Look"].ReadValue<Vector2>());

            foreach (var binding in CombatBindings)
            {
                if (!Pressed(binding.Binding))
                    continue;
                if (!Finished)
                {
                    if (bufferedAction != DuelAction.None)
                        recording.RecordInput(combatStep, bufferedAction.ToString(), "Superseded");
                    recording.RecordInput(combatStep, binding.Action.ToString(), "Buffered");
                }
                bufferedAction = binding.Action;
                bufferTime = 0.16f;
                break;
            }
        }

        private bool Pressed(string action) => input.FindAction(action)?.WasPressedThisFrame() == true;

        private void ToggleLucyLibrary()
        {
            if (lucyPanel == null)
            {
                LucyCombatLibrary library = LucyCombatLibrary.Load();
                if (library == null)
                    return;
                lucyPanel = gameObject.AddComponent<DuelLucyPanel>();
                lucyPanel.Bind(this, library);
            }
            lucyPanel.Toggle();
        }

        private void RecordActionChange(InputAction.CallbackContext context)
        {
            if (Finished || lucyDemo || (lucyPanel != null && lucyPanel.IsOpen) ||
                (!context.performed && !context.canceled))
                return;
            string action = context.action.name;
            if (action is "Look" or "Lock" or "Reset" or "ReleaseCursor" or "Details" or "Adaptation" or "ExportDuel" or "LucyLibrary")
                return;
            Vector2 direction = action == "Move" ? context.ReadValue<Vector2>() : Vector2.zero;
            recording.RecordInput(combatStep, action, context.performed ? "Performed" : "Canceled", direction);
        }

        public string ExportRecording()
        {
            try
            {
                string path = recording.Export(combatStep, opponent.IsAdaptive, opponent.Model.EligibleCount,
                    observation.Pending, opponent.IsLlmMode, opponent.Tactics.Audit());
                exportStatus = "Exported: " + Path.GetFileName(path);
                Debug.Log("Duel recording exported to " + path, this);
                return path;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                exportStatus = "Duel export failed";
                Debug.LogWarning(error.Message, this);
                return null;
            }
        }

        private void FixedUpdate()
        {
            if (resetPressed)
            {
                ResetDuel();
                return;
            }

            if (Advance(movement, bufferedAction, guard, Time.fixedDeltaTime, sprintHeld, walkHeld))
            {
                recording.RecordInput(combatStep, bufferedAction.ToString(), "Consumed");
                bufferedAction = DuelAction.None;
            }

            bufferTime -= Time.fixedDeltaTime;
            if (bufferTime <= 0f && bufferedAction != DuelAction.None)
            {
                if (!Finished)
                    recording.RecordInput(combatStep, bufferedAction.ToString(), "Expired");
                bufferedAction = DuelAction.None;
            }
        }

        public bool Advance(Vector2 movement, DuelAction action, bool blocking, float delta, bool running = false, bool walking = false)
        {
            if (lucyDemo)
            {
                TurnPerformancePair(movement, delta);
                Vector2 steering = !lucyPair && player.CurrentAction != null ? movement : Vector2.zero;
                player.Step(demon, steering, DuelAction.None, false, delta, targeted: false, cameraForward: rig.MovementForward);
                if (!comboTraining)
                    demon.Step(player, Vector2.zero, DuelAction.None, false, delta);
                return false;
            }

            if (Finished)
            {
                TurnPerformancePair(movement, delta);
                player.AdvanceDefeat(delta);
                demon.AdvanceDefeat(delta);
                if (finishingFighter != null && finishingFighter.CurrentAction != null)
                    finishingFighter.Step(finishingFighter == player ? demon : player,
                        Vector2.zero, DuelAction.None, false, delta);
                return false;
            }

            combatStep++;
            Vector3 playerBefore = player.transform.position;
            Vector3 demonBefore = demon.transform.position;
            int playerVersionBefore = player.ActionVersion;

            bool sprint = ShouldSprint(movement, blocking, running);
            DuelGait gait = sprint ? DuelGait.Sprint : walking ? DuelGait.Walk : DuelGait.Run;
            var intent = comboTraining ? default : opponent.Decide(player, delta);
            bool targeted = rig.Locked && (!sprint || action != DuelAction.None);
            bool consumed = player.Step(demon, movement, action, blocking, delta, targeted,
                cameraForward: rig.MovementForward, gait: gait);
            if (player.Sprinting)
                rig.SetLocked(false);
            if (comboTraining)
            {
                CountTrainingStrike(player.AssessStrike(demon));
                return consumed;
            }

            if (demon.Step(player, intent.movement, intent.action, intent.guard, delta))
                opponent.Tactics.Commit(opponent.LastChoice.PlanId, combatStep, Time.realtimeSinceStartupAsDouble);
            observation.ObserveStep(combatStep, delta, player, demon, playerBefore, demonBefore,
                playerVersionBefore, opponent.LastChoice);

            DuelActionAsset playerMove = player.Move;
            DuelActionAsset demonMove = demon.Move;
            StrikeResult playerStrike = player.AssessStrike(demon);
            StrikeResult demonStrike = demon.AssessStrike(player);
            int demonVersion = demon.ActionVersion;
            ApplyStrike(player, demon, playerStrike, playerMove);
            ApplyStrike(demon, player, demonStrike, demonMove);
            if (demonStrike != StrikeResult.None)
                observation.Close(combatStep, demonStrike);
            else if (demonVersion != demon.ActionVersion || Finished)
                observation.Close(combatStep, StrikeResult.None, interrupted: true);
            if (Finished)
            {
                opponent.Tactics.Stop("Duel finished", combatStep);
                BeginLucyFinisher();
            }
            return consumed;
        }

        private bool ShouldSprint(Vector2 direction, bool blocking, bool held) =>
            held && !blocking && direction.sqrMagnitude > .01f && player.CanAct && !player.IsAirborne;

        private void CountTrainingStrike(StrikeResult result)
        {
            if (result == StrikeResult.None)
                return;
            if (result == StrikeResult.Hit)
            {
                trainingHits++;
                trainingChain = player.ComboLength > 1 ? trainingChain + 1 : 1;
                trainingBest = Mathf.Max(trainingBest, trainingChain);
            }
            else
                trainingChain = 0;
            lastExchange = $"Training: {result}";
            recording.RecordStrike(combatStep, true, player.Move.Action, result, player.Health, demon.Health);
        }

        public void ResetDuel()
        {
            lucyDemo = false;
            finishingFighter = null;
            SetLucyPair(false);
            player.ResetFighter(playerSpawn.position, playerSpawn.rotation);
            demon.ResetFighter(demonSpawn.position, demonSpawn.rotation);
            opponent.ResetOpponent();
            opponent.Tactics.Reset(combatStep);
            recording.Reset();
            observation.Reset();
            combatStep = 0;
            trainingChain = trainingBest = trainingHits = 0;
            exportStatus = null;
            Physics.SyncTransforms();
            bufferedAction = DuelAction.None;
            bufferTime = 0f;
            resetPressed = false;
            lastExchange = "Ready";
            rig.ResetRig();
        }

        internal void EquipLucy(LucyCombatEntry entry, bool useDemon)
        {
            if (!entry.CanEquip || (comboTraining && (useDemon || entry.Partner != null)))
                return;
            if (entry.Kind == LucyCombatKind.Execution)
            {
                if (useDemon)
                    demonFinisher = entry;
                else
                    playerFinisher = entry;
            }
            else
                (useDemon ? demon : player).EquipLucy(entry);
            ResetDuel();
        }

        internal void ClearLucy()
        {
            player.ClearLucy();
            demon.ClearLucy();
            playerFinisher = demonFinisher = null;
            ResetDuel();
        }

        internal void DemonstrateLucy(LucyCombatEntry entry, bool useDemon)
        {
            if (comboTraining && (useDemon || entry.Partner != null))
                return;
            ResetDuel();
            lucyDemo = true;
            DuelFighter lead = useDemon ? demon : player;
            DuelFighter other = useDemon ? player : demon;
            if (entry.Partner != null)
            {
                Vector3 midpoint = (playerSpawn.position + demonSpawn.position) * 0.5f;
                Quaternion rotation = playerSpawn.rotation;
                PlaceLucyPair(lead, other, midpoint, rotation, entry.Take);
                other.StartLucyPerformance(entry.Partner);
            }
            lead.StartLucyPerformance(entry.Action, entry.Direction);
        }

        private void BeginLucyFinisher()
        {
            if (player.Health == demon.Health)
                return;

            DuelFighter winner = player.Health > 0 ? player : demon;
            DuelFighter loser = winner == player ? demon : player;
            LucyCombatEntry entry = winner == player ? playerFinisher : demonFinisher;
            if (entry?.Partner == null)
                return;

            Vector3 direction = Vector3.ProjectOnPlane(loser.transform.position - winner.transform.position, Vector3.up);
            Quaternion orientation = direction.sqrMagnitude > 0.01f ? Quaternion.LookRotation(direction) : winner.transform.rotation;
            Vector3 midpoint = (winner.transform.position + loser.transform.position) * 0.5f;
            midpoint.y = Mathf.Min(winner.transform.position.y, loser.transform.position.y);
            PlaceLucyPair(winner, loser, midpoint, orientation, entry.Take);
            finishingFighter = winner;
            winner.StartLucyPerformance(entry.IsExecutionReceiver ? entry.Partner : entry.Action);
            loser.StartLucyPerformance(entry.IsExecutionReceiver ? entry.Action : entry.Partner, defeated: true);
        }

        private void PlaceLucyPair(DuelFighter lead, DuelFighter other, Vector3 midpoint, Quaternion rotation, LucyTake take)
        {
            bool sample = take == LucyTake.Sample;
            Vector3 offset = sample ? Vector3.zero : rotation * Vector3.forward * 0.48f;
            lead.PlaceForPerformance(midpoint - offset, rotation);
            other.PlaceForPerformance(midpoint + offset, sample ? rotation : rotation * Quaternion.Euler(0f, 180f, 0f));
            SetLucyPair(true);
            Physics.SyncTransforms();
        }

        private void SetLucyPair(bool paired)
        {
            if (lucyPair == paired)
                return;
            lucyPair = paired;
            Physics.IgnoreCollision(player.GetComponent<CharacterController>(), demon.GetComponent<CharacterController>(), paired);
        }

        private void TurnPerformancePair(Vector2 inputDirection, float delta)
        {
            if (!lucyPair || player.Phase == FighterPhase.Down ||
                player.CurrentAction?.CanTurnAt(player.ActionFrame) != true || inputDirection.sqrMagnitude <= .01f)
                return;

            Vector3 forward = rig.MovementForward;
            Vector3 wish = forward * inputDirection.y + Vector3.Cross(Vector3.up, forward) * inputDirection.x;
            float yaw = player.TurnAmount(wish, delta);
            if (Mathf.Abs(yaw) <= .01f)
                return;

            Quaternion rotation = Quaternion.AngleAxis(yaw, Vector3.up);
            Vector3 midpoint = (player.transform.position + demon.transform.position) * .5f;
            TurnAround(player, midpoint, rotation);
            TurnAround(demon, midpoint, rotation);
            Physics.IgnoreCollision(player.GetComponent<CharacterController>(), demon.GetComponent<CharacterController>());
            Physics.SyncTransforms();
        }

        private static void TurnAround(DuelFighter fighter, Vector3 pivot, Quaternion turn) =>
            fighter.PlaceForPerformance(pivot + turn * (fighter.transform.position - pivot), turn * fighter.transform.rotation);

        private void ApplyStrike(DuelFighter attacker, DuelFighter target, StrikeResult result, DuelActionAsset move)
        {
            if (result == StrikeResult.None)
                return;
            if (result == StrikeResult.Parried || result == StrikeResult.Mikiri)
                attacker.Interrupt();
            else
            {
                target.ReceiveStrike(result, move.Damage, attacker.transform.position - target.transform.position,
                    move.TargetLiftSpeed);
                if (result == StrikeResult.Hit && move.Space == ActionSpace.Air)
                    target.FollowAirHit(attacker);
            }

            bool fromPlayer = attacker == player;
            string name = fromPlayer ? "You" : "Demon";
            lastExchange = $"{name}: {result}";
            recording.RecordStrike(combatStep, fromPlayer, move.Action, result, player.Health, demon.Health);
        }

        private void OnGUI()
        {
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            title ??= new GUIStyle(label) { fontSize = 21, fontStyle = FontStyle.Bold };
            float width = Mathf.Min(680, Screen.width - 32);
            float x = (Screen.width - width) * 0.5f;
            float lane = (width - 28) * 0.5f;
            DrawHealth(x, lane, "YOU", player, new Color(0.28f, 0.83f, 0.75f));
            if (comboTraining)
                GUI.Label(new Rect(x + lane + 28, 14, lane, 28), "TRAINING ROBOT", title);
            else
                DrawHealth(x + lane + 28, lane, "DEMON", demon, new Color(0.95f, 0.34f, 0.31f));

            GUI.Label(new Rect(x, 67, width, 26), lastExchange, label);
            if (comboTraining)
            {
                GUI.Label(new Rect(x, 93, width, 30), $"CHAIN {trainingChain}    BEST {trainingBest}    HITS {trainingHits}", title);
                string feedback = DescribeCombo(player);
                if (feedback != null)
                    GUI.Label(new Rect(x, 122, width, 24), feedback, label);
            }
            if (demon.Move != null && demon.Phase == FighterPhase.Windup)
                GUI.Label(new Rect(x, 93, width, 30),
                    demon.Move.IsThrust ? "THRUST  /  MIKIRI" : demon.Move.Action.ToString().ToUpperInvariant(), title);

            if (showDetails)
            {
                GUI.Label(new Rect(x, comboTraining ? 147 : 131, width, 24), $"You: {Describe(player)}", label);
                if (!comboTraining)
                {
                    GUI.Label(new Rect(x, 155, width, 24), $"Demon: {Describe(demon)}", label);
                    DrawOpponentReadout(x, width);
                }
            }

            if (Finished)
            {
                string result = player.Health == demon.Health ? "DRAW" : demon.Health == 0 ? "VICTORY" : "DEFEAT";
                GUI.Label(new Rect(x, Screen.height * 0.42f, width, 40), $"{result}    R to rematch", title);
            }

            string view = rig != null && rig.Locked ? "TARGET LOCKED" : "FREE CAMERA";
            GUI.Label(new Rect(16, Screen.height - 79, Screen.width - 32, 24),
                $"{view}   |   WASD Run   Shift Sprint   Ctrl Walk   MMB Lock   Space Dodge   Esc Cursor", label);
            GUI.Label(new Rect(16, Screen.height - 56, Screen.width - 32, 24),
                "LMB Attack   C / WASD+C Ground combos   V Air opener   E Delay   T Finish / Thrust   RMB Guard   Q Parry   F Mikiri", label);
            GUI.Label(new Rect(16, Screen.height - 33, Screen.width - 32, 24),
                lucyDemo ? "Lucy demo   |   F4 Animations   R Return" :
                comboTraining ? exportStatus ?? "F1 Details   F3 Export   F4 Lucy library   R Reset training" :
                exportStatus ?? $"F1 Details   F2 Opponent: {opponent.ModeName}   F3 Export duel   F4 Lucy library", label);
        }

        private void DrawOpponentReadout(float x, float width)
        {
            float metres = Vector3.ProjectOnPlane(player.transform.position - demon.transform.position, Vector3.up).magnitude;
            DuelDistance band = OpponentModel.Band(metres);
            HabitEstimate early = opponent.Model.EarlyParryAt(band);
            HabitEstimate retreat = opponent.Model.RetreatAt(band);
            OpponentChoice choice = opponent.LastChoice;
            GUI.Label(new Rect(x, 179, width, 24),
                $"Eligible {opponent.Model.EligibleCount}  |  {band}  |  Early parry {Estimate(early)}  |  Retreat {Estimate(retreat)}", label);
            if (choice.Chosen != DuelAction.None)
                GUI.Label(new Rect(x, 202, width, 24), $"AI chose {choice.Chosen}  |  {choice.Tactic}  |  Base {choice.Baseline}", label);
            if (LastOpportunity is DuelOpportunity last)
                GUI.Label(new Rect(x, 225, width, 24),
                    $"Last exchange: {last.Opener}  |  {last.Status}  |  {last.Response}  |  {last.Result}", label);
            if (opponent.IsLlmMode)
                GUI.Label(new Rect(x, 249, width, 24), $"LLM: {opponent.Tactics.Status}", label);
        }

        private static string Estimate(HabitEstimate estimate) =>
            estimate.Evidence < 0.01f ? "—" : $"{estimate.Probability:P0} ({estimate.Evidence:0.0})";

        private void DrawHealth(float x, float width, string name, DuelFighter fighter, Color fill)
        {
            GUI.Label(new Rect(x, 14, width, 28), $"{name}  {fighter.Health}/{fighter.MaxHealth}", title);
            Fill(new Rect(x, 45, width, 10), new Color(0.08f, 0.10f, 0.13f, 0.92f));
            Fill(new Rect(x, 45, width * fighter.Health / fighter.MaxHealth, 10), fill);
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static string Describe(DuelFighter fighter) =>
            $"{fighter.CurrentAction?.name ?? "-"} | {fighter.Phase} | {Mathf.FloorToInt(fighter.ActionFrame)}f" +
            $" | Defence {(fighter.ParryWindow || fighter.MikiriWindow || fighter.DodgeWindow ? "OPEN" : "-")}" +
            $" | Turn {(fighter.CurrentAction?.CanTurnAt(fighter.ActionFrame) == true ? "OPEN" : "-")}" +
            $" | {(fighter.IsAirborne ? "Air" : fighter.Gait.ToString())}" +
            $" | Chain {fighter.QueuedCombo ?? "-"}";

        private static string DescribeCombo(DuelFighter fighter)
        {
            string message = fighter.LastComboFeedback switch
            {
                ComboFeedback.Buffered => "Buffered",
                ComboFeedback.Branched => $"Follow-up: {fighter.LastComboTarget.name}",
                ComboFeedback.TooEarly => "Too early",
                ComboFeedback.TooLate => "Too late",
                ComboFeedback.OutcomeMismatch => $"Result does not allow follow-up ({fighter.LastComboResult})",
                ComboFeedback.Interrupted => "Interrupted",
                ComboFeedback.WrongSpace => "Follow-up requires a different combat space",
                _ => null
            };
            return message == null ? null : $"{DuelCombo.InputLabel(fighter.LastComboInput)}   |   {message}";
        }
    }
}
