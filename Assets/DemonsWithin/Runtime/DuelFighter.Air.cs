using UnityEngine;

namespace DemonsWithin
{
    public sealed partial class DuelFighter
    {
        private const float LiftThreshold = .035f;
        private const float Gravity = 10.5f;
        private float verticalSpeed;
        private bool isRehearsal;
        private bool blockedLift;

        public bool IsAirborne { get; private set; }

        // Lucy bakes vertical displacement into the pose. Transfer the launcher curve to the motor once.
        public float AirVisualOffsetAt(float frame) => isRehearsal || CurrentAction == null ? 0f : CurrentAction.Space switch
        {
            ActionSpace.GroundLaunch => LaunchHeight(CurrentAction, frame),
            ActionSpace.Air => CurrentAction.AirPoseOffset,
            _ => 0f
        };

        private DuelActionAsset ResolveStarter(DuelAction input, Vector3 wish, Vector3 basis)
        {
            DuelAirMoves moves = Loadout.Air;
            if (IsAirborne)
                return input switch
                {
                    DuelAction.Quick => moves?.Quick,
                    DuelAction.Delayed => moves?.Delayed,
                    DuelAction.Thrust => moves?.Finisher,
                    _ => null
                };
            if (input is DuelAction.AirOpener or DuelAction.ComboOpener)
            {
                Vector2 relative = new(Vector3.Dot(wish, Vector3.Cross(Vector3.up, basis)), Vector3.Dot(wish, basis));
                if (input == DuelAction.AirOpener)
                    return moves?.Launch(relative);
                DuelActionAsset starter = Loadout.Combo?.GroundOpener(relative);
                return starter != null && starter.Attack ? starter : null;
            }
            return GetMove(input);
        }

        private bool CanBeginInSpace(DuelActionAsset move) =>
            move.Space == ActionSpace.Air ? IsAirborne : !IsAirborne;

        private void LiftFromHit(float speed)
        {
            IsAirborne = true;
            verticalSpeed = Mathf.Max(verticalSpeed, speed);
        }

        internal void FollowAirHit(DuelFighter attacker)
        {
            if (!IsAirborne || !attacker.IsAirborne || Phase == FighterPhase.Down)
                return;
            float heightError = attacker.transform.position.y - transform.position.y;
            verticalSpeed = attacker.verticalSpeed + Mathf.Clamp(heightError * 3f, -2.4f, 2.4f);
        }

        private void StepFlight(Vector3 planar, float delta)
        {
            float travelY;
            if (!blockedLift && CurrentAction?.Space == ActionSpace.GroundLaunch && CurrentAction.LaunchHeight != null &&
                ActionFrame < CurrentAction.Length)
            {
                float now = LaunchHeight(CurrentAction, ActionFrame);
                float next = LaunchHeight(CurrentAction, Mathf.Min(CurrentAction.Length, ActionFrame + delta * CurrentAction.Rate));
                if (!IsAirborne && next < LiftThreshold)
                {
                    MoveCapsule(planar, delta);
                    return;
                }
                travelY = IsAirborne ? next - now : next;
                if (IsAirborne && next < LiftThreshold && next <= now)
                {
                    blockedLift = true;
                    travelY = Mathf.Min(travelY, -.06f);
                }
                IsAirborne = true;
                verticalSpeed = travelY / Mathf.Max(.0001f, delta);
            }
            else if (IsAirborne)
            {
                float next = verticalSpeed - Gravity * delta;
                travelY = (verticalSpeed + next) * .5f * delta;
                verticalSpeed = next;
            }
            else
            {
                MoveCapsule(planar, delta);
                return;
            }

            CollisionFlags contacts = MoveCapsule(planar + Vector3.up * travelY, delta, animatedHeight: true);
            if ((contacts & CollisionFlags.Above) != 0)
            {
                verticalSpeed = 0f;
                blockedLift = true;
            }
            if ((contacts & CollisionFlags.Below) == 0 || travelY > 0f)
                return;

            IsAirborne = false;
            verticalSpeed = 0f;
            if (Phase is not (FighterPhase.Down or FighterPhase.Stagger) && Loadout.Air?.Landing is DuelActionAsset landing)
                BeginAction(landing, FighterPhase.Landing);
        }

        private static float LaunchHeight(DuelActionAsset action, float frame) =>
            action.LaunchHeight?.Evaluate(frame) ?? 0f;
    }
}
