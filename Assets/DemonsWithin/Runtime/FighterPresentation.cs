using Animancer;
using UnityEngine;
using UnityEngine.Events;

namespace DemonsWithin
{
    [DefaultExecutionOrder(80)]
    public sealed class FighterPresentation : MonoBehaviour
    {
        [SerializeField] private DuelFighter fighter;
        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private Renderer tell;
        [SerializeField, Min(0f)] private float soleOffset = .012f;
        [SerializeField] private UnityEvent<string> namedEvent = new();

        private enum LocomotionPhase { Idle, Start, Loop, Stop }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private DuelAnimationProfile profile;
        private MaterialPropertyBlock tint;
        private LocomotionClips locomotion;
        private LocomotionPhase locomotionPhase;
        private DuelGait locomotionGait;
        private float locomotionUntil;
        private AnimancerState state;
        private AnimationClip currentClip;
        private int presentedAction = -1;
        private int presentedMotion = -1;
        private float priorAirOffset, renderedAirOffset;
        private Quaternion lastRenderRotation;
        private Attachment modelAttachment, tellAttachment, markerAttachment;

        private readonly struct Attachment
        {
            private readonly Transform item;
            private readonly Vector3 offset;
            private readonly Quaternion rotation;

            public Attachment(Transform item, Transform fighter)
            {
                this.item = item;
                offset = item == null ? Vector3.zero : fighter.InverseTransformPoint(item.position);
                rotation = item == null ? Quaternion.identity : Quaternion.Inverse(fighter.rotation) * item.rotation;
            }

            public void Apply(Pose pose, Vector3 scale)
            {
                if (item != null)
                    item.SetPositionAndRotation(pose.position + pose.rotation * Vector3.Scale(offset, scale),
                        pose.rotation * rotation);
            }
        }

        public AnimationClip PlayingClip => currentClip;

        private void Awake()
        {
            tint = new MaterialPropertyBlock();
            Animator animator = animancer.Animator;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (!animator.TryGetComponent(out FighterRootMotion rootMotion))
                rootMotion = animator.gameObject.AddComponent<FighterRootMotion>();
            rootMotion.Bind(this);
            lastRenderRotation = fighter.transform.rotation;
            modelAttachment = new Attachment(animator.transform, fighter.transform);
            tellAttachment = new Attachment(tell != null ? tell.transform : null, fighter.transform);
            markerAttachment = new Attachment(fighter.transform.Find("Team Marker"), fighter.transform);
            fighter.CueReached += PlayCue;
        }

        private void Start() => profile = fighter.Loadout != null ? fighter.Loadout.Animation : null;

        private void OnDestroy() => fighter.CueReached -= PlayCue;

        private void Update()
        {
            if (profile == null)
                return;

            DuelActionAsset action = fighter.CurrentAction;
            if (action != null)
                PlayAction(action);
            else
                PlayLocomotion();

            DuelActionAsset attack = fighter.Move;
            bool hasTell = attack != null && (fighter.Phase == FighterPhase.Windup || fighter.Phase == FighterPhase.Active);
            if (tell != null)
            {
                tell.enabled = hasTell;
                if (hasTell)
                {
                    Color color = attack.Action switch
                    {
                        DuelAction.Thrust => new Color(0.98f, 0.28f, 0.25f),
                        DuelAction.Delayed => new Color(0.98f, 0.69f, 0.22f),
                        _ => new Color(0.89f, 0.91f, 0.73f)
                    };
                    tint.SetColor(BaseColor, color);
                    tell.SetPropertyBlock(tint);
                }
            }
        }

        public void ApplyRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            if (presentedMotion >= 0 && presentedAction == fighter.ActionVersion)
            {
                Quaternion toSimulation = fighter.transform.rotation * Quaternion.Inverse(lastRenderRotation);
                fighter.QueueRootMotion(toSimulation * deltaPosition, deltaRotation);
            }
        }

        private void LateUpdate()
        {
            Pose pose = fighter.GetRenderPose();
            lastRenderRotation = pose.rotation;
            Vector3 scale = fighter.transform.lossyScale;
            Pose groundPose = pose;
            Pose modelPose = pose;
            if (fighter.TryGetSurfaceAt(pose.position, out float surface))
            {
                groundPose.position.y = surface;
                modelPose.position.y = surface + soleOffset * scale.y;
            }
            float airOffset = fighter.AirVisualOffsetAt(SampledActionFrame());
            renderedAirOffset = state == null ? airOffset : Mathf.Lerp(priorAirOffset, airOffset, Mathf.Clamp01(state.Weight));
            modelPose.position.y -= renderedAirOffset;
            modelAttachment.Apply(modelPose, scale);
            tellAttachment.Apply(pose, scale);
            markerAttachment.Apply(groundPose, scale);
        }

        private float SampledActionFrame()
        {
            DuelActionAsset move = fighter.CurrentAction;
            if (move == null || move.Space != ActionSpace.GroundLaunch || state == null || presentedMotion < 0 ||
                presentedAction != fighter.ActionVersion)
                return fighter.ActionFrame;
            ActionMotion motion = move.Motions[presentedMotion];
            float progress = Mathf.InverseLerp(motion.ClipTime(0f), motion.ClipTime(1f), (float)state.Time);
            return Mathf.Lerp(motion.Frame, move.MotionEnd(presentedMotion), progress);
        }

        private void PlayAction(DuelActionAsset action)
        {
            int index = action.MotionAt(fighter.ActionFrame);
            if (presentedAction == fighter.ActionVersion && presentedMotion == index)
            {
                if (index >= 0 && fighter.ActionFrame >= action.Length && state.Speed != 0f)
                {
                    state.Time = fighter.ReactionClip != null ? ReactionEnd(action) : action.Motions[index].ClipTime(1f);
                    state.Speed = 0f;
                }
                return;
            }
            presentedAction = fighter.ActionVersion;
            presentedMotion = index;
            locomotionPhase = LocomotionPhase.Idle;
            locomotion = null;
            if (index < 0)
            {
                Play(profile.Idle, profile.LocomotionFade);
                return;
            }

            ActionMotion motion = action.Motions[index];
            float frames = action.MotionEnd(index) - motion.Frame;
            AnimationClip reaction = fighter.ReactionClip;
            float start = reaction != null ? 0f : motion.ClipTime(0f);
            float end = reaction != null ? ReactionEnd(action) : motion.ClipTime(1f);
            AnimancerState actionState = Play(reaction != null ? reaction : motion.Clip, motion.Fade, start);
            actionState.Speed = (end - start) * action.Rate / frames;
        }

        private float ReactionEnd(DuelActionAsset action) =>
            Mathf.Min(fighter.ReactionClip.length, (float)action.Length / action.Rate);

        private void PlayLocomotion()
        {
            presentedAction = -1;
            presentedMotion = -1;
            if (fighter.IsAirborne && fighter.Loadout.Air?.FallLoop is AnimationClip falling)
            {
                if (currentClip != falling)
                    Play(falling, profile.LocomotionFade);
                locomotion = null;
                locomotionPhase = LocomotionPhase.Idle;
                return;
            }
            bool moving = fighter.WantsMovement && fighter.ActualVelocity.sqrMagnitude > 0.01f;
            LocomotionClips alternate = moving && !fighter.Running ? fighter.LucyFootwork(fighter.Movement) : null;
            LocomotionClips next = moving ? alternate ?? profile.Select(fighter.Movement, fighter.Gait, locomotion) : null;
            if (moving && next != null)
            {
                if (next != locomotion || locomotionPhase == LocomotionPhase.Stop || locomotionPhase == LocomotionPhase.Idle)
                {
                    bool startFromRest = locomotionPhase is LocomotionPhase.Idle or LocomotionPhase.Stop;
                    bool changeGait = locomotionGait != fighter.Gait;
                    float cycle = locomotionPhase == LocomotionPhase.Loop && state != null
                        ? Mathf.Repeat(state.NormalizedTime, 1f) : 0f;
                    locomotion = next;
                    locomotionGait = fighter.Gait;
                    if ((startFromRest || changeGait) && next.Start != null)
                        SetLocomotion(LocomotionPhase.Start, next.Start);
                    else
                        SetLocomotion(LocomotionPhase.Loop, next.Loop, cycle);
                }
                else if (locomotionPhase == LocomotionPhase.Start && Time.time >= locomotionUntil)
                    SetLocomotion(LocomotionPhase.Loop, next.Loop);
                if (locomotionPhase == LocomotionPhase.Loop && state != null)
                {
                    float reference = next.ReferenceSpeed > 0.01f ? next.ReferenceSpeed :
                        fighter.Gait == DuelGait.Sprint ? 8.2f : fighter.Running ? 6.2f : 2.75f;
                    float playback = Mathf.Clamp(fighter.ActualVelocity.magnitude / reference, 0.60f, 1.55f);
                    state.Speed = Mathf.MoveTowards(state.Speed, playback, 7f * Time.deltaTime);
                }
                return;
            }

            if ((locomotionPhase == LocomotionPhase.Loop || locomotionPhase == LocomotionPhase.Start) &&
                locomotion?.Stop != null)
            {
                SetLocomotion(LocomotionPhase.Stop, locomotion.Stop);
                return;
            }

            if (locomotionPhase == LocomotionPhase.Stop && Time.time < locomotionUntil)
                return;

            AnimationClip idle = fighter.LucyIdle != null ? fighter.LucyIdle : profile.Idle;
            if (locomotionPhase != LocomotionPhase.Idle || currentClip != idle)
                SetLocomotion(LocomotionPhase.Idle, idle);
            locomotion = null;
        }

        private void SetLocomotion(LocomotionPhase phase, AnimationClip clip, float normalizedTime = 0f)
        {
            locomotionPhase = phase;
            clip = clip != null ? clip : profile.Idle;
            if (clip == null)
                return;
            Play(clip, profile.LocomotionFade, clip.length * normalizedTime);
            locomotionUntil = Time.time + clip.length;
        }

        private AnimancerState Play(AnimationClip clip, float fade, float start = 0f)
        {
            if (clip == null)
                return state;
            priorAirOffset = renderedAirOffset;
            state = animancer.Play(clip, profile.FadeBetween(currentClip, clip, fade), FadeMode.FromStart);
            state.Time = start;
            state.Speed = 1f;
            currentClip = clip;
            return state;
        }

        private void PlayCue(DuelActionAsset action, ActionCue cue)
        {
            Vector3 position = transform.TransformPoint(cue.LocalOffset);
            switch (cue.Track)
            {
                case ActionTrack.Event:
                    namedEvent.Invoke(cue.Event);
                    break;
                case ActionTrack.Audio when cue.Audio != null:
                    AudioSource.PlayClipAtPoint(cue.Audio, position);
                    break;
                case ActionTrack.Effect when cue.Effect != null:
                    Destroy(Instantiate(cue.Effect, position, transform.rotation), Mathf.Max(0.1f, cue.EffectLifetime));
                    break;
            }
        }
    }
}
