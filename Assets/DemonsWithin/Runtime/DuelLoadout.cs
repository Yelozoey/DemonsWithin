using System;
using UnityEngine;

namespace DemonsWithin
{
    [Serializable]
    public sealed class DuelAirMoves
    {
        public DuelActionAsset NeutralLaunch, ForwardLaunch, SideLaunch, BackLaunch;
        public DuelActionAsset Quick, Delayed, Finisher, Landing;
        public AnimationClip FallLoop, Hit;

        public DuelActionAsset Launch(Vector2 input)
        {
            if (input.sqrMagnitude < .12f)
                return NeutralLaunch;
            if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
                return SideLaunch != null ? SideLaunch : NeutralLaunch;
            DuelActionAsset directional = input.y > 0f ? ForwardLaunch : BackLaunch;
            return directional != null ? directional : NeutralLaunch;
        }
    }

    [CreateAssetMenu(menuName = "Demons Within/Loadout")]
    public sealed class DuelLoadout : ScriptableObject
    {
        public DuelAnimationProfile Animation;
        public DuelCombo Combo;
        public DuelCombo AdditionalCombos;
        public DuelActionAsset[] Actions;
        public DuelAirMoves Air;
        public AnimationClip[] DirectionalHits;
        public AnimationClip FrontDownHit;

        public static DuelLoadout Default => Resources.Load<DuelLoadout>("LucyDuelLoadout");

        public DuelActionAsset Find(DuelAction action) =>
            Array.Find(Actions, asset => asset != null && asset.Action == action);

        internal AnimationClip HitFrom(Vector3 local)
        {
            if (DirectionalHits == null || DirectionalHits.Length != 8)
                return null;
            int heading = DuelAnimationProfile.Heading(new Vector2(local.x, local.z));
            if (local.y > .55f && local.z > 0f && (heading is 0 or 1 or 7) && FrontDownHit != null)
                return FrontDownHit;
            return DirectionalHits[heading];
        }
    }
}
