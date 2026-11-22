using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    public enum DuelGait { Walk, Run, Sprint }

    [Serializable]
    public sealed class LocomotionClips
    {
        public AnimationClip Start;
        public AnimationClip Loop;
        public AnimationClip Stop;
        [Min(0f)] public float ReferenceSpeed;
    }

    [Serializable]
    public sealed class AnimationFade
    {
        public AnimationClip From;
        public AnimationClip To;
        [Min(0)] public float Seconds = 0.12f;
    }

    [CreateAssetMenu(menuName = "Demons Within/Animation Profile")]
    public sealed class DuelAnimationProfile : ScriptableObject
    {
        public GameObject PreviewModel;
        public AnimationClip Idle;
        public LocomotionClips Forward, Back, Left, Right, Run;
        public LocomotionClips ForwardRight, BackRight, BackLeft, ForwardLeft;
        public LocomotionClips[] RunDirections;
        public LocomotionClips Sprint;
        [Min(0)] public float LocomotionFade = 0.12f;
        public List<AnimationFade> Fades = new();

        public float FadeBetween(AnimationClip from, AnimationClip to, float fallback)
        {
            foreach (AnimationFade fade in Fades)
                if (fade.From == from && fade.To == to)
                    return Mathf.Max(0f, fade.Seconds);
            return Mathf.Max(0f, fallback);
        }

        public LocomotionClips Select(Vector2 movement, bool running, LocomotionClips previous)
        {
            if (running)
                return Run;
            Vector2 direction = movement.normalized;
            int heading = Heading(direction);
            LocomotionClips diagonal = heading switch
            {
                1 => ForwardRight,
                3 => BackRight,
                5 => BackLeft,
                7 => ForwardLeft,
                _ => null
            };
            if (ForwardRight?.Loop != null && BackRight?.Loop != null &&
                BackLeft?.Loop != null && ForwardLeft?.Loop != null)
            {
                int previousHeading = previous == Forward ? 0 : previous == ForwardRight ? 1 :
                    previous == Right ? 2 : previous == BackRight ? 3 : previous == Back ? 4 :
                    previous == BackLeft ? 5 : previous == Left ? 6 : previous == ForwardLeft ? 7 : -1;
                if (previousHeading >= 0)
                {
                    float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(previousHeading * 45f, angle)) <= 27f)
                        return previous;
                }
                if (diagonal != null)
                    return diagonal;
                return heading == 0 ? Forward : heading == 2 ? Right : heading == 4 ? Back : Left;
            }

            float previousScore = previous == Forward ? direction.y : previous == Back ? -direction.y :
                previous == Right ? direction.x : previous == Left ? -direction.x : -1f;
            float best = Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
            if (previous != null && previousScore >= best - 0.18f)
                return previous;
            if (Mathf.Abs(direction.y) >= Mathf.Abs(direction.x))
                return direction.y >= 0f ? Forward : Back;
            return direction.x >= 0f ? Right : Left;
        }

        public LocomotionClips Select(Vector2 movement, DuelGait gait, LocomotionClips previous)
        {
            if (gait == DuelGait.Walk)
                return Select(movement, false, previous);
            if (gait == DuelGait.Sprint)
                return Sprint?.Loop != null ? Sprint : Run;
            if (RunDirections == null || RunDirections.Length != 8)
                return Run;

            int previousHeading = previous == null ? -1 : Array.IndexOf(RunDirections, previous);
            if (previousHeading >= 0)
            {
                float angle = Mathf.Atan2(movement.x, movement.y) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(previousHeading * 45f, angle)) <= 27f)
                    return previous;
            }
            LocomotionClips clip = RunDirections[Heading(movement)];
            return clip?.Loop != null ? clip : Run;
        }

        internal static int Heading(Vector2 direction) => Mathf.RoundToInt(
            Mathf.Atan2(direction.x, direction.y) * (4f / Mathf.PI)) & 7;
    }
}
