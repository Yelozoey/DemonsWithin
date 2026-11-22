using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    internal static class DodgeTurnCalibration
    {
        private const string DodgeFolder = "Assets/Lucy/Animation/Dodge/Root/";

        [MenuItem("Tools/Demons Within/按根位移校准闪避转向", priority = 126)]
        public static void Calibrate()
        {
            var affected = new HashSet<UnityEngine.Object>();
            DuelLoadout loadout = DuelLoadout.Default;
            if (loadout != null)
                foreach (DuelActionAsset action in loadout.Actions)
                    if (action != null && action.IsDodge)
                        UpdateAction(action, affected);

            LucyCombatLibrary library = LucyCombatLibrary.Load();
            if (library != null)
                foreach (LucyCombatEntry entry in library.Entries)
                {
                    if (entry.Kind == LucyCombatKind.Dodge)
                        UpdateAction(entry.Action, affected);
                    else if (entry.Kind == LucyCombatKind.Execution && entry.IsExecutionReceiver && !entry.Action.PassiveReaction)
                    {
                        entry.Action.PassiveReaction = true;
                        affected.Add(entry.Action);
                    }
                }

            foreach (UnityEngine.Object asset in affected)
                EditorUtility.SetDirty(asset);
            if (affected.Count > 0)
                AssetDatabase.SaveAssets();
        }

        private static void UpdateAction(DuelActionAsset action, HashSet<UnityEngine.Object> affected)
        {
            if (action.Motions.Count == 0 || action.Motions[0].Clip == null)
                return;
            ActionMotion motion = action.Motions[0];
            string original = motion.Clip.name.Replace("_Inplace", "_Root");
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(DodgeFolder + original + ".FBX").OfType<AnimationClip>()
                .FirstOrDefault(candidate => candidate.name == original);
            if (clip == null || !TryRootCurves(clip, out AnimationCurve x, out AnimationCurve z))
                return;

            int frames = Mathf.Max(2, action.MotionEnd(0) - motion.Frame);
            float from = Mathf.Clamp01(motion.ClipIn) * clip.length;
            float to = Mathf.Clamp(motion.ClipOut, motion.ClipIn, 1f) * clip.length;
            float step = (to - from) / frames;
            if (step <= 0f)
                return;

            var path = new Vector2[frames + 1];
            for (int frame = 0; frame < path.Length; frame++)
            {
                float time = from + step * frame;
                path[frame] = new Vector2(x.Evaluate(time), z.Evaluate(time));
            }

            int lead = OpeningFrames(path, action.Rate);
            int cap = Mathf.Min(action.Length - 1, action.DefenseEnd > 1 ? action.DefenseEnd - 1 : action.Length - 1);
            lead = Mathf.Clamp(motion.Frame + lead, 0, cap);
            if (action.DodgeTurnEnd == lead)
                return;
            action.DodgeTurnEnd = lead;
            affected.Add(action);
        }

        private static bool TryRootCurves(AnimationClip clip, out AnimationCurve x, out AnimationCurve z)
        {
            x = z = null;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.propertyName == "RootT.x") x = AnimationUtility.GetEditorCurve(clip, binding);
                if (binding.propertyName == "RootT.z") z = AnimationUtility.GetEditorCurve(clip, binding);
            }
            return x != null && z != null;
        }

        private static int OpeningFrames(Vector2[] path, int rate)
        {
            // Look for the first sustained burst, or clear displacement from the start.
            float peakSpeed = 0f;
            for (int i = 1; i < path.Length; i++)
                peakSpeed = Mathf.Max(peakSpeed, (path[i] - path[i - 1]).magnitude * rate);
            float speedThreshold = Mathf.Max(0.65f, peakSpeed * 0.32f);
            int limit = Mathf.Min(path.Length - 2, Mathf.CeilToInt(rate * 0.16f));
            for (int i = 1; i <= limit; i++)
            {
                float speed = (path[i] - path[i - 1]).magnitude * rate;
                float next = (path[i + 1] - path[i]).magnitude * rate;
                if ((path[i] - path[0]).sqrMagnitude >= 0.06f * 0.06f || (speed >= speedThreshold && next >= speedThreshold))
                    return i;
            }
            return Mathf.Max(1, limit);
        }
    }
}
