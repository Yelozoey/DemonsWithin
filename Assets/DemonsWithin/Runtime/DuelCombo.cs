using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    public enum ComboCondition { Any, Hit, Guarded, Miss, Dodged }
    public enum ComboFeedback { None, Buffered, Branched, TooEarly, TooLate, OutcomeMismatch, Interrupted, WrongSpace }

    [Serializable]
    public sealed class ComboLink
    {
        public DuelActionAsset From;
        public DuelAction Input = DuelAction.Quick;
        public DuelActionAsset To;
        [Min(0)] public int BufferFrom;
        [Min(0)] public int BufferUntil;
        [Min(0)] public int CancelFrom;
        [Min(1)] public int CancelUntil = 30;
        public ComboCondition Condition;
        [HideInInspector] public bool RequireHit;

        public ComboCondition RequiredResult => Condition == ComboCondition.Any && RequireHit ? ComboCondition.Hit : Condition;
        public int OpenFrame => From == null ? CancelFrom : Mathf.Max(CancelFrom, From.HitEnd);
        public int CloseFrame => From == null ? CancelUntil : Mathf.Min(CancelUntil, From.Length + 1);
        public int InputCloseFrame => BufferUntil > 0 ? Mathf.Min(BufferUntil, CloseFrame) : CloseFrame;
        public bool IsValid => From != null && To != null && To.Attack && BufferFrom < InputCloseFrame && OpenFrame < CloseFrame;

        internal bool AllowsInput(int frame) => frame >= BufferFrom && frame < InputCloseFrame;

        internal bool MatchesResult(StrikeResult result) => RequiredResult switch
        {
            ComboCondition.Hit => result == StrikeResult.Hit,
            ComboCondition.Guarded => result == StrikeResult.Guarded,
            ComboCondition.Miss => result == StrikeResult.Miss,
            ComboCondition.Dodged => result == StrikeResult.Dodged,
            _ => true
        };
    }

    [CreateAssetMenu(menuName = "Demons Within/Combo")]
    public sealed class DuelCombo : ScriptableObject
    {
        public DuelActionAsset AlternateOpener;
        public DuelActionAsset ForwardOpener, SideOpener, BackOpener;
        public List<ComboLink> Links = new();

        internal DuelActionAsset GroundOpener(Vector2 direction)
        {
            if (direction.sqrMagnitude < .12f)
                return AlternateOpener;
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
                return SideOpener != null ? SideOpener : AlternateOpener;
            DuelActionAsset result = direction.y > 0f ? ForwardOpener : BackOpener;
            return result != null ? result : AlternateOpener;
        }

        public static string InputLabel(DuelAction input) => input switch
        {
            DuelAction.Quick => "LMB",
            DuelAction.Delayed => "E",
            DuelAction.Thrust => "T",
            DuelAction.ComboOpener => "C",
            DuelAction.AirOpener => "V",
            _ => input.ToString()
        };

        internal ComboFeedback ClassifyInput(DuelActionAsset from, DuelAction input, int frame)
        {
            bool tooEarly = false;
            bool hasRoute = false;
            foreach (ComboLink link in Links)
            {
                if (!Matches(link, from, input))
                    continue;
                if (link.AllowsInput(frame))
                    return ComboFeedback.Buffered;
                hasRoute = true;
                tooEarly |= frame < link.BufferFrom;
            }
            return !hasRoute ? ComboFeedback.None : tooEarly ? ComboFeedback.TooEarly : ComboFeedback.TooLate;
        }

        internal ComboLink Resolve(DuelActionAsset from, DuelAction input, int pressedFrame, int frame,
            StrikeResult result, out ComboFeedback status)
        {
            ComboLink fallback = null;
            bool waiting = false;
            bool rejected = false;
            foreach (ComboLink link in Links)
            {
                if (!Matches(link, from, input) || !link.AllowsInput(pressedFrame) || frame >= link.CloseFrame)
                    continue;
                if (!link.MatchesResult(result))
                {
                    waiting |= result == StrikeResult.None;
                    rejected |= result != StrikeResult.None;
                    continue;
                }
                if (frame < link.OpenFrame)
                {
                    waiting = true;
                    continue;
                }
                if (link.RequiredResult != ComboCondition.Any)
                {
                    status = ComboFeedback.Branched;
                    return link;
                }
                fallback ??= link;
            }

            status = fallback != null ? ComboFeedback.Branched : waiting ? ComboFeedback.Buffered :
                rejected ? ComboFeedback.OutcomeMismatch : ComboFeedback.TooLate;
            return fallback;
        }

        private static bool Matches(ComboLink link, DuelActionAsset from, DuelAction input) =>
            link != null && link.From == from && link.Input == input && link.IsValid;
    }
}
