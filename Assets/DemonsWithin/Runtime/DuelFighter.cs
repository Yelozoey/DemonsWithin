using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    [RequireComponent(typeof(CharacterController))]
    public sealed partial class DuelFighter : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0.1f)] private float moveSpeed = 3.3f;
        [SerializeField, Min(0.1f)] private float runSpeed = 6.2f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 8.2f;
        [SerializeField, Min(1)] private float turnSpeed = 820f;
        [SerializeField, Min(0.1f)] private float walkAcceleration = 20f;
        [SerializeField, Min(0.1f)] private float runAcceleration = 27f;
        [SerializeField, Min(0.1f)] private float braking = 34f;
        [SerializeField, Min(0.1f)] private float reversalAcceleration = 40f;
        [SerializeField] private DuelLoadout loadout;

        private CharacterController motor;
        private Vector3 velocity;
        private Vector3 dodgeDirection;
        private float dodgeFacingOffset;
        private Vector3 pendingRootDisplacement;
        private float pendingRootYaw;
        private Pose previousPose;
        private bool strikePending;
        private StrikeResult strikeResult;
        private bool crossedHitWindow;
        private float actionTime;
        private DuelAction comboInput;
        private int comboInputFrame;
        private Vector2 comboOpenerDirection;
        private LucyCombatEntry lucyDodge, lucyHit, lucyKnockout, lucyGuard;
        private readonly Dictionary<int, LocomotionClips> lucyFootwork = new();

        public DuelLoadout Loadout { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth => maxHealth;
        public FighterPhase Phase { get; private set; }
        public DuelActionAsset CurrentAction { get; private set; }
        public DuelActionAsset Move => CurrentAction != null && CurrentAction.Attack ? CurrentAction : null;
        public float ActionFrame
        {
            get
            {
                DuelActionAsset action = CurrentAction;
                if (action == null)
                    return 0;
                return actionTime >= (float)action.Length / action.Rate
                    ? action.Length : actionTime * action.Rate;
            }
        }
        public int ActionVersion { get; private set; }
        public Vector2 Movement { get; private set; }
        public Vector3 ActualVelocity { get; private set; }
        public bool WantsMovement { get; private set; }
        public DuelGait Gait { get; private set; }
        public bool Running => Gait != DuelGait.Walk;
        public bool Sprinting => Gait == DuelGait.Sprint;
        public bool CanAct => Phase == FighterPhase.Ready || Phase == FighterPhase.Guard;
        public bool ParryWindow => Phase == FighterPhase.Parry && DefenseWindow();
        public bool MikiriWindow => Phase == FighterPhase.Mikiri && DefenseWindow();
        public bool DodgeWindow => Phase == FighterPhase.Dodge && DefenseWindow();
        public string QueuedCombo => comboInput == DuelAction.None ? null : DuelCombo.InputLabel(comboInput);
        public ComboFeedback LastComboFeedback { get; private set; }
        public DuelAction LastComboInput { get; private set; }
        public DuelActionAsset LastComboTarget { get; private set; }
        public StrikeResult LastComboResult { get; private set; }
        public int ComboLength { get; private set; }
        public AnimationClip LucyIdle { get; private set; }
        public AnimationClip ReactionClip { get; private set; }

        public event Action<DuelActionAsset, ActionCue> CueReached;

        public DuelActionAsset GetMove(DuelAction action)
        {
            if (action == DuelAction.ComboOpener)
            {
                DuelActionAsset opener = Loadout.Combo?.AlternateOpener;
                return opener != null && opener.Attack ? opener : null;
            }

            LucyCombatEntry variant = action switch
            {
                DuelAction.Dodge or DuelAction.DodgeBack => lucyDodge,
                DuelAction.HitStun => lucyHit,
                DuelAction.Knockout => lucyKnockout,
                DuelAction.Guard => lucyGuard,
                _ => null
            };
            return variant?.Action ?? Loadout.Find(action);
        }

        internal void EquipLucy(LucyCombatEntry entry)
        {
            switch (entry.Kind)
            {
                case LucyCombatKind.Dodge: lucyDodge = entry; break;
                case LucyCombatKind.Hit: lucyHit = entry; break;
                case LucyCombatKind.Knockout: lucyKnockout = entry; break;
                case LucyCombatKind.Stance when entry.IsIdle: LucyIdle = entry.Clip; break;
                case LucyCombatKind.Stance: lucyGuard = entry; break;
                case LucyCombatKind.Footwork:
                    lucyFootwork[entry.Direction] = new LocomotionClips { Loop = entry.Clip, ReferenceSpeed = 2.75f };
                    break;
            }
        }

        internal void ClearLucy()
        {
            lucyDodge = lucyHit = lucyKnockout = lucyGuard = null;
            LucyIdle = null;
            lucyFootwork.Clear();
        }

        internal LocomotionClips LucyFootwork(Vector2 direction) =>
            lucyFootwork.TryGetValue(DuelAnimationProfile.Heading(direction), out LocomotionClips clips) ? clips : null;

        internal void StartLucyPerformance(DuelActionAsset action, int direction = 0, bool defeated = false)
        {
            if (action.Action == DuelAction.Dodge)
            {
                dodgeDirection = transform.rotation * Quaternion.Euler(0f, direction * 45f, 0f) * Vector3.forward;
                dodgeFacingOffset = -direction * 45f;
                BeginAction(action, FighterPhase.Dodge, rehearsal: true);
                return;
            }
            BeginAction(action, defeated ? FighterPhase.Down : action.Attack ? FighterPhase.Windup : FighterPhase.Stagger,
                rehearsal: true);
        }

        private void Awake()
        {
            motor = GetComponent<CharacterController>();
            Loadout = loadout == null ? DuelLoadout.Default : loadout;
            Health = maxHealth;
            CapturePreviousPose();
        }

        public void ResetFighter(Vector3 position, Quaternion rotation)
        {
            PlaceForPerformance(position, rotation);
            Health = maxHealth;
            Movement = Vector2.zero;
            ActualVelocity = Vector3.zero;
            WantsMovement = false;
            Gait = DuelGait.Walk;
            velocity = Vector3.zero;
            IsAirborne = false;
            verticalSpeed = 0f;
            comboOpenerDirection = Vector2.zero;
            BeginAction(null, FighterPhase.Ready);
            ReportCombo(ComboFeedback.None, DuelAction.None);
            CapturePreviousPose();
        }

        internal void PlaceForPerformance(Vector3 position, Quaternion rotation)
        {
            motor.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            motor.enabled = true;
            CapturePreviousPose();
        }

        public bool Step(DuelFighter opponent, Vector2 input, DuelAction action, bool guard, float delta,
            bool targeted = true, bool run = false, Vector3 cameraForward = default, DuelGait? gait = null)
        {
            CapturePreviousPose();
            crossedHitWindow = false;
            Movement = Vector2.zero;
            ActualVelocity = Vector3.zero;
            WantsMovement = false;
            Gait = DuelGait.Walk;
            if (!targeted || (input - comboOpenerDirection).sqrMagnitude > .01f)
                comboOpenerDirection = Vector2.zero;
            bool consumed = Move != null && TryQueueCombo(action);
            if (TryChain())
                action = DuelAction.None;

            if (CanAct)
            {
                Vector3 opponentForward = Planar(opponent.transform.position - transform.position).normalized;
                Vector3 basis = targeted ? opponentForward : Planar(cameraForward).normalized;
                if (basis.sqrMagnitude < 0.01f)
                    basis = transform.forward;
                Vector3 wish = ToWorld(basis, Vector2.ClampMagnitude(input, 1f));
                if (action != DuelAction.None && TryBegin(action, basis, wish, opponentForward, targeted))
                {
                    consumed = true;
                    comboOpenerDirection = action == DuelAction.ComboOpener && targeted ? input : Vector2.zero;
                    if (comboOpenerDirection == Vector2.zero)
                        SteerAction(wish, delta);
                }
                else if (!IsAirborne)
                    Walk(wish, basis, targeted, guard, gait ?? (run ? DuelGait.Run : DuelGait.Walk), delta);
            }
            else if (input.sqrMagnitude > 0.01f && comboOpenerDirection == Vector2.zero)
            {
                Vector3 basis = Planar(targeted ? opponent.transform.position - transform.position : cameraForward).normalized;
                if (basis.sqrMagnitude < 0.01f)
                    basis = transform.forward;
                SteerAction(ToWorld(basis, Vector2.ClampMagnitude(input, 1f)), delta);
            }

            if (Phase == FighterPhase.Dodge)
                DriveDodge(delta);
            else if (CurrentAction != null || IsAirborne)
                ApplyQueuedRootMotion(delta);

            AdvanceAction(delta);
            return consumed;
        }

        private void SteerAction(Vector3 wish, float delta)
        {
            if (CurrentAction == null || Phase == FighterPhase.Down ||
                wish.sqrMagnitude <= 0.01f || !CurrentAction.CanTurnAt(ActionFrame))
                return;

            if (Phase == FighterPhase.Dodge)
            {
                dodgeDirection = Vector3.RotateTowards(dodgeDirection, wish.normalized,
                    turnSpeed * Mathf.Deg2Rad * delta, 0f);
                Vector3 modelForward = Quaternion.Euler(0f, dodgeFacingOffset, 0f) * dodgeDirection;
                transform.rotation = Quaternion.LookRotation(modelForward);
                return;
            }
            TurnTowards(wish, delta);
        }

        internal float TurnAmount(Vector3 wish, float delta)
        {
            float angle = Vector3.SignedAngle(transform.forward, wish, Vector3.up);
            return Mathf.Clamp(angle, -turnSpeed * delta, turnSpeed * delta);
        }

        private void Walk(Vector3 wish, Vector3 basis, bool targeted, bool guard, DuelGait gait, float delta)
        {
            bool moving = wish.sqrMagnitude > 0.01f;
            FighterPhase next = guard ? FighterPhase.Guard : FighterPhase.Ready;
            DuelActionAsset nextAction = guard && !moving ? GetMove(DuelAction.Guard) : null;
            if (Phase != next || CurrentAction != nextAction)
                BeginAction(nextAction, next);

            WantsMovement = moving;
            Gait = moving && !guard ? gait : DuelGait.Walk;
            float speed = Gait switch
            {
                DuelGait.Sprint => sprintSpeed,
                DuelGait.Run => runSpeed,
                _ => guard ? moveSpeed * 0.45f : moveSpeed
            };
            Vector3 desired = wish * speed;
            float acceleration = !moving ? braking :
                Vector3.Dot(velocity, desired) < 0f ? reversalAcceleration : Running ? runAcceleration : walkAcceleration;
            velocity = Vector3.MoveTowards(velocity, desired, acceleration * delta);

            Vector3 facing = targeted ? basis : velocity.normalized;
            if (facing.sqrMagnitude > 0.01f)
                TurnTowards(facing, delta);
            if (CurrentAction != null)
                return;

            MoveCapsule(velocity * delta, delta);
            velocity = ActualVelocity;
            Vector3 local = transform.InverseTransformDirection(ActualVelocity);
            Movement = targeted ? new Vector2(local.x, local.z) : Vector2.up * ActualVelocity.magnitude;
        }

        private bool TryBegin(DuelAction action, Vector3 basis, Vector3 wish, Vector3 opponentForward, bool targeted)
        {
            bool backwardDodge = wish.sqrMagnitude < 0.01f ||
                (targeted && Vector3.Dot(wish, basis) < -0.35f);
            if (action == DuelAction.Dodge && backwardDodge)
                action = DuelAction.DodgeBack;

            DuelActionAsset asset = ResolveStarter(action, wish, basis);
            if (asset == null || !CanBeginInSpace(asset))
                return false;

            FighterPhase phase = action switch
            {
                DuelAction.Parry => FighterPhase.Parry,
                DuelAction.Mikiri => FighterPhase.Mikiri,
                DuelAction.Dodge or DuelAction.DodgeBack => FighterPhase.Dodge,
                _ => FighterPhase.Windup
            };

            Vector3 facing = targeted ? opponentForward : wish.normalized;
            if (phase == FighterPhase.Dodge)
            {
                dodgeDirection = action == DuelAction.DodgeBack
                    ? -(targeted ? basis : transform.forward)
                    : (wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward);
                dodgeFacingOffset = lucyDodge != null ? -lucyDodge.Direction * 45f :
                    action == DuelAction.DodgeBack ? 180f : 0f;
                facing = Quaternion.Euler(0f, dodgeFacingOffset, 0f) * dodgeDirection;
            }
            if (facing.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(facing);

            BeginAction(asset, phase);
            return true;
        }

        private bool TryQueueCombo(DuelAction input)
        {
            if (input == DuelAction.None)
                return false;
            int frame = Mathf.FloorToInt(ActionFrame);
            ComboFeedback status = Loadout.Combo?.ClassifyInput(CurrentAction, input, frame) ?? ComboFeedback.None;
            if (status != ComboFeedback.Buffered && Loadout.AdditionalCombos != null)
            {
                ComboFeedback alternate = Loadout.AdditionalCombos.ClassifyInput(CurrentAction, input, frame);
                if (alternate != ComboFeedback.None)
                    status = alternate;
            }
            if (status == ComboFeedback.None)
                return false;
            if (status == ComboFeedback.Buffered)
            {
                comboInput = input;
                comboInputFrame = frame;
            }
            // A combo input belongs to this attack, including a rejected press.
            ReportCombo(status, input);
            return true;
        }

        private bool TryChain()
        {
            if (comboInput == DuelAction.None)
                return false;
            int frame = Mathf.FloorToInt(ActionFrame);
            ComboFeedback status = ComboFeedback.TooLate;
            ComboFeedback alternate = ComboFeedback.TooLate;
            ComboLink route = Loadout.Combo?.Resolve(CurrentAction, comboInput, comboInputFrame, frame, strikeResult, out status);
            route ??= Loadout.AdditionalCombos?.Resolve(CurrentAction, comboInput, comboInputFrame, frame, strikeResult, out alternate);
            if (route == null)
            {
                if (status == ComboFeedback.Buffered || alternate == ComboFeedback.Buffered)
                    return false;
                bool rejected = status == ComboFeedback.OutcomeMismatch || alternate == ComboFeedback.OutcomeMismatch;
                ReportCombo(rejected ? ComboFeedback.OutcomeMismatch : ComboFeedback.TooLate, comboInput);
                comboInput = DuelAction.None;
                return false;
            }

            if (!CanBeginInSpace(route.To))
            {
                ReportCombo(ComboFeedback.WrongSpace, comboInput);
                comboInput = DuelAction.None;
                return false;
            }
            ReportCombo(ComboFeedback.Branched, comboInput, route.To);
            int previousLength = ComboLength;
            if (CurrentAction.Space == ActionSpace.GroundLaunch && route.To.Space == ActionSpace.Air)
                verticalSpeed = Mathf.Min(verticalSpeed, 4f);
            BeginAction(route.To, FighterPhase.Windup);
            ComboLength = previousLength + 1;
            return true;
        }

        private void ReportCombo(ComboFeedback feedback, DuelAction input, DuelActionAsset target = null)
        {
            LastComboFeedback = feedback;
            LastComboInput = input;
            LastComboTarget = target;
            LastComboResult = feedback == ComboFeedback.None ? StrikeResult.None : strikeResult;
        }

        private void AdvanceAction(float delta)
        {
            DuelActionAsset action = CurrentAction;
            if (action == null)
                return;

            float before = ActionFrame;
            if (before >= action.Length)
            {
                if (Phase != FighterPhase.Guard && Phase != FighterPhase.Down)
                    BeginAction(null, FighterPhase.Ready);
                return;
            }

            actionTime = Mathf.Min(actionTime + delta, (float)action.Length / action.Rate);
            float after = ActionFrame;
            EmitCues(action, before, after);

            if (action.Attack)
            {
                crossedHitWindow = before < action.HitEnd && after >= action.HitStart;
                Phase = after < action.HitStart ? FighterPhase.Windup :
                    after < action.HitEnd ? FighterPhase.Active : FighterPhase.Recovery;
            }
        }

        public void AdvanceDefeat(float delta)
        {
            CapturePreviousPose();
            if (Phase == FighterPhase.Down)
            {
                ApplyQueuedRootMotion(delta);
                AdvanceAction(delta);
            }
        }

        public StrikeResult AssessStrike(DuelFighter target)
        {
            if (!strikePending || !crossedHitWindow)
                return StrikeResult.None;

            DuelActionAsset attack = CurrentAction;
            Vector3 offset = Planar(target.transform.position - transform.position);
            if (!attack.HitBox.Intersects(transform, target.motor))
                return ActionFrame >= attack.HitEnd ? ConsumeStrike(StrikeResult.Miss) : StrikeResult.None;

            bool facing = Vector3.Dot(target.transform.forward, -offset.normalized) >= 0.35f;
            if (target.DodgeWindow)
                return ConsumeStrike(StrikeResult.Dodged);
            if (facing && attack.IsThrust && target.MikiriWindow)
                return ConsumeStrike(StrikeResult.Mikiri);
            if (facing && !attack.IsThrust && target.ParryWindow)
                return ConsumeStrike(StrikeResult.Parried);
            if (facing && !attack.IsThrust && target.Phase == FighterPhase.Guard)
                return ConsumeStrike(StrikeResult.Guarded);
            return ConsumeStrike(StrikeResult.Hit);
        }

        public void ReceiveStrike(StrikeResult result, int damage, Vector3 directionFromAttacker = default,
            float liftSpeed = 0f)
        {
            if (result != StrikeResult.Hit && result != StrikeResult.Guarded)
                return;
            Health = Mathf.Max(0, Health - (result == StrikeResult.Guarded ? damage / 4 : damage));
            if (Health == 0)
                BeginAction(GetMove(DuelAction.Knockout), FighterPhase.Down);
            else if (result == StrikeResult.Hit)
            {
                if (liftSpeed > 0f)
                    LiftFromHit(liftSpeed);
                Vector3 local = transform.InverseTransformDirection(directionFromAttacker);
                AnimationClip reaction = IsAirborne ? Loadout.Air?.Hit :
                    lucyHit == null ? Loadout.HitFrom(local) : null;
                BeginAction(GetMove(DuelAction.HitStun), FighterPhase.Stagger, reaction);
            }
        }

        public void Interrupt()
        {
            if (Phase != FighterPhase.Down)
                BeginAction(GetMove(DuelAction.CounterStun), FighterPhase.Stagger);
        }

        public Pose GetRenderPose()
        {
            float alpha = Mathf.Clamp01((float)((Time.timeAsDouble - Time.fixedTimeAsDouble) / Time.fixedDeltaTime));
            return new Pose(Vector3.Lerp(previousPose.position, transform.position, alpha),
                Quaternion.Slerp(previousPose.rotation, transform.rotation, alpha));
        }

        internal bool TryGetSurfaceAt(Vector3 position, out float height)
        {
            height = position.y;
            // Keep the visual contact plane independent of the capsule's skin and grounding slack.
            if (!motor.isGrounded || IsAirborne ||
                (CurrentAction?.ApplyRootHeight == true && (isRehearsal || CurrentAction.Space == ActionSpace.Ground)))
                return false;
            float sole = position.y + (motor.center.y - motor.height * 0.5f) * transform.lossyScale.y;
            float probe = motor.skinWidth + .12f;
            Vector3 origin = new(position.x, sole + probe, position.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe * 2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < .5f)
                return false;
            height = hit.point.y;
            return true;
        }

        private void CapturePreviousPose() => previousPose = new Pose(transform.position, transform.rotation);

        public void QueueRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (CurrentAction == null || Phase == FighterPhase.Dodge)
                return;
            pendingRootDisplacement += transform.InverseTransformDirection(
                CurrentAction.ApplyRootHeight && (isRehearsal || CurrentAction.Space == ActionSpace.Ground)
                    ? deltaPosition : Planar(deltaPosition));
            pendingRootYaw += Mathf.DeltaAngle(0f, deltaRotation.eulerAngles.y);
        }

        private void ApplyQueuedRootMotion(float delta)
        {
            Vector3 displacement = transform.TransformDirection(pendingRootDisplacement) * (CurrentAction?.RootMotionScale ?? 1f);
            if (!isRehearsal && (IsAirborne || CurrentAction?.Space == ActionSpace.GroundLaunch))
                StepFlight(Planar(displacement), delta);
            else
                MoveCapsule(displacement, delta, CurrentAction?.ApplyRootHeight == true);
            if (CurrentAction?.ApplyRootYaw == true)
                transform.Rotate(0f, pendingRootYaw, 0f, Space.Self);
            pendingRootDisplacement = Vector3.zero;
            pendingRootYaw = 0f;
        }

        private void DriveDodge(float delta)
        {
            int startFrame = Mathf.Min(CurrentAction.TurnWindow.y, CurrentAction.Length - 1);
            float startTime = (float)startFrame / CurrentAction.Rate;
            float remaining = (float)(CurrentAction.Length - startFrame) / CurrentAction.Rate;
            float travelTime = Mathf.Min(Mathf.Max(0.02f, CurrentAction.DodgeTravelSeconds), remaining);
            float before = DodgeProgress((actionTime - startTime) / travelTime);
            float after = DodgeProgress((actionTime + delta - startTime) / travelTime);
            MoveCapsule(dodgeDirection * ((after - before) * CurrentAction.DodgeDistance), delta);
        }

        private static float DodgeProgress(float normalized)
        {
            float remaining = 1f - Mathf.Clamp01(normalized);
            return 1f - remaining * remaining;
        }

        private CollisionFlags MoveCapsule(Vector3 displacement, float delta, bool animatedHeight = false)
        {
            Vector3 before = transform.position;
            CollisionFlags flags = motor.Move(displacement + (animatedHeight ? Vector3.zero : Vector3.down * (2f * delta)));
            ActualVelocity = Planar(transform.position - before) / Mathf.Max(0.0001f, delta);
            return flags;
        }

        private StrikeResult ConsumeStrike(StrikeResult result)
        {
            strikePending = false;
            strikeResult = result;
            if (comboInput != DuelAction.None && result is StrikeResult.Parried or StrikeResult.Mikiri)
            {
                ReportCombo(ComboFeedback.Interrupted, comboInput);
                comboInput = DuelAction.None;
            }
            return result;
        }

        private void BeginAction(DuelActionAsset action, FighterPhase phase, AnimationClip reaction = null, bool rehearsal = false)
        {
            if (comboInput != DuelAction.None && phase is FighterPhase.Stagger or FighterPhase.Down)
                ReportCombo(ComboFeedback.Interrupted, comboInput);
            CurrentAction = action;
            ReactionClip = reaction;
            isRehearsal = rehearsal;
            blockedLift = false;
            Phase = phase;
            actionTime = 0;
            strikePending = Move != null;
            ComboLength = strikePending ? 1 : 0;
            strikeResult = StrikeResult.None;
            crossedHitWindow = false;
            comboInput = DuelAction.None;
            ActionVersion++;
            pendingRootDisplacement = Vector3.zero;
            pendingRootYaw = 0f;
            if (action != null)
            {
                velocity = Vector3.zero;
                EmitCues(action, -1f, 0f);
            }
        }

        private void EmitCues(DuelActionAsset action, float before, float after)
        {
            foreach (ActionCue cue in action.Cues)
                if (cue.Frame > before && cue.Frame <= after)
                    CueReached?.Invoke(action, cue);
        }

        private bool DefenseWindow() => CurrentAction != null &&
            ActionFrame >= CurrentAction.DefenseStart && ActionFrame < CurrentAction.DefenseEnd;

        private void TurnTowards(Vector3 direction, float delta) =>
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                turnSpeed * delta);

        private static Vector3 ToWorld(Vector3 forward, Vector2 input) =>
            Vector3.Cross(Vector3.up, forward) * input.x + forward * input.y;

        private static Vector3 Planar(Vector3 vector) => Vector3.ProjectOnPlane(vector, Vector3.up);
    }
}
