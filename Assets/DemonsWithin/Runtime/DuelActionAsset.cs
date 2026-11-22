using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    public enum DuelAction
    {
        None, Quick, Delayed, Thrust, Parry, Mikiri, Dodge,
        QuickFollowup, Guard, HitStun, CounterStun, Knockout, DodgeBack, ComboOpener, AirOpener
    }

    public enum FighterPhase { Ready, Guard, Windup, Active, Recovery, Parry, Mikiri, Dodge, Stagger, Down, Landing }
    public enum ActionSpace { Ground, GroundLaunch, Air }
    public enum StrikeResult { None, Miss, Hit, Guarded, Parried, Mikiri, Dodged }
    public enum ActionTrack { Event, Audio, Effect }

    public readonly struct AttackBox
    {
        public readonly Vector3 Center;
        public readonly Vector3 Size;
        public readonly float Yaw;

        public AttackBox(Vector3 center, Vector3 size, float yaw)
        {
            Center = center;
            Size = Vector3.Max(size, Vector3.one * 0.01f);
            Yaw = yaw;
        }

        public Matrix4x4 LocalMatrix =>
            Matrix4x4.TRS(Center, Quaternion.Euler(0f, Yaw, 0f), Size);

        public bool Intersects(Transform origin, CharacterController target)
        {
            Vector3 centre = origin.TransformPoint(Center);
            Quaternion rotation = origin.rotation * Quaternion.Euler(0f, Yaw, 0f);
            Vector3 extent = Vector3.Scale(Size, Abs(origin.lossyScale)) * 0.5f;
            Vector3 capsule = Quaternion.Inverse(rotation) * (target.transform.TransformPoint(target.center) - centre);
            Vector3 scale = Abs(target.transform.lossyScale);
            float radius = target.radius * Mathf.Max(scale.x, scale.z);
            float segment = Mathf.Max(0f, target.height * scale.y * 0.5f - radius);
            Vector3 gap = Vector3.Max(Abs(capsule) - extent - Vector3.up * segment, Vector3.zero);
            return gap.sqrMagnitude <= radius * radius;
        }

        private static Vector3 Abs(Vector3 value) =>
            new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    [Serializable]
    public sealed class ActionMotion
    {
        [Min(0)] public int Frame;
        public AnimationClip Clip;
        [Range(0, 1)] public float ClipIn;
        [Range(0, 1)] public float ClipOut = 1f;
        [Min(0)] public float Fade = 0.08f;

        public float ClipTime(float progress) =>
            Clip.length * Mathf.Lerp(ClipIn, Mathf.Max(ClipIn, ClipOut), progress);
    }

    [Serializable]
    public sealed class ActionCue
    {
        [Min(0)] public int Frame;
        public ActionTrack Track;
        public string Event;
        public AudioClip Audio;
        public GameObject Effect;
        public Vector3 LocalOffset = Vector3.up;
        [Min(0.1f)] public float EffectLifetime = 3f;
    }

    [CreateAssetMenu(menuName = "Demons Within/Combat Action")]
    public sealed class DuelActionAsset : ScriptableObject
    {
        public DuelAction Action;
        [Range(1, 240)] public int FrameRate = 60;
        [Min(1)] public int Duration = 45;
        public bool Attack;
        [Min(0)] public int HitStart = 20;
        [Min(0)] public int HitEnd = 26;
        [Min(0)] public int DefenseStart = 3;
        [Min(0)] public int DefenseEnd = 12;
        [Min(0.1f)] public float Reach = 1.8f;
        [Range(1, 90)] public float HalfAngle = 60f;
        [Min(0)] public int Damage = 17;
        public bool CustomHitBox;
        public Vector3 HitBoxCenter = new(0f, 0.95f, 0.9f);
        public Vector3 HitBoxSize = new(1.3f, 1.15f, 1.5f);
        public float HitBoxYaw;
        [Min(0f)] public float DodgeDistance = 1.08f;
        [Min(0.02f)] public float DodgeTravelSeconds = 0.30f;
        [Min(0)] public float RootMotionScale = 1f;
        public bool ApplyRootYaw;
        public bool ApplyRootHeight;
        public bool PassiveReaction;
        public ActionSpace Space;
        public AnimationCurve LaunchHeight;
        [Min(0f)] public float AirPoseOffset;
        [Min(0f)] public float TargetLiftSpeed;
        public bool CustomTurnWindow;
        [Min(0)] public int TurnStart;
        [Min(0)] public int TurnEnd;
        [Min(0)] public int DodgeTurnEnd;
        public List<ActionMotion> Motions = new();
        public List<ActionCue> Cues = new();

        public bool IsThrust => Action == DuelAction.Thrust;
        public int Rate => Mathf.Max(1, FrameRate);
        public int Length => Mathf.Max(1, Duration);
        public bool IsDodge => Action is DuelAction.Dodge or DuelAction.DodgeBack;
        public bool CanSteer => !PassiveReaction && Action is not (DuelAction.HitStun or DuelAction.CounterStun or DuelAction.Knockout);

        public Vector2Int TurnWindow
        {
            get
            {
                if (!CanSteer)
                    return Vector2Int.zero;
                if (CustomTurnWindow)
                {
                    int start = Mathf.Clamp(TurnStart, 0, Length);
                    return new Vector2Int(start, Mathf.Clamp(TurnEnd, start, Length));
                }

                int end = Attack ? HitStart : IsDodge ? Mathf.Max(DodgeTurnEnd, Mathf.CeilToInt(Rate * 0.045f))
                    : DefenseStart > 0 ? DefenseStart : Length;
                return new Vector2Int(0, Mathf.Clamp(end, 0, IsDodge ? Length - 1 : Length));
            }
        }

        public bool CanTurnAt(float frame)
        {
            Vector2Int range = TurnWindow;
            return frame >= range.x && frame < range.y;
        }

        public AttackBox HitBox
        {
            get
            {
                if (CustomHitBox)
                    return new AttackBox(HitBoxCenter, HitBoxSize, HitBoxYaw);
                const float back = 0.30f;
                float front = Mathf.Max(back + 0.05f, Reach - 0.32f);
                float halfWidth = Mathf.Clamp(Reach * Mathf.Sin(HalfAngle * Mathf.Deg2Rad) * 0.46f, 0.18f, 0.95f);
                return new AttackBox(new Vector3(0f, 0.95f, (front + back) * 0.5f),
                    new Vector3(halfWidth * 2f, 1.15f, front - back), 0f);
            }
        }

        public int MotionAt(float frame)
        {
            int result = -1;
            int latest = -1;
            for (int i = 0; i < Motions.Count; i++)
            {
                ActionMotion motion = Motions[i];
                if (motion.Clip != null && motion.Frame <= frame && motion.Frame >= latest)
                {
                    latest = motion.Frame;
                    result = i;
                }
            }

            return result;
        }

        public int MotionEnd(int index)
        {
            int start = Motions[index].Frame;
            int end = Length;
            foreach (ActionMotion motion in Motions)
                if (motion.Clip != null && motion.Frame > start && motion.Frame < end)
                    end = motion.Frame;
            return Mathf.Max(start + 1, end);
        }
    }
}
