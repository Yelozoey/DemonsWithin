using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public static class LucyCombatLibraryBuilder
    {
        private const string SourceRoot = "Assets/Lucy/Animation/";
        private const string LibraryPath = "Assets/DemonsWithin/Resources/LucyCombatLibrary.asset";
        private static readonly string[] DirectionNames = { "F", "FR", "R", "BR", "B", "BL", "L", "FL" };
        private static readonly Regex ComboStage = new(
            @"^(Lucy_(?:(?:FightFist|Fist)\d{2}|(?:Air_Attack_Combo|Kick_Combo)\d{2}))_(\d)([ab]?)$");

        [MenuItem("Tools/Demons Within/生成 Lucy 动作库", priority = 125)]
        public static void Build()
        {
            // Validate imported clips before creating or updating assets.
            var sources = AssetDatabase.GetAllAssetPaths()
                .Where(path => IsCombatPath(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => (path, clip: LoadClip(path))).ToArray();
            if (sources.Length == 0)
                throw new InvalidOperationException("Lucy combat animations were not found.");

            var library = AssetDatabase.LoadAssetAtPath<LucyCombatLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<LucyCombatLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var entries = library.Entries.ToDictionary(item => item.Source, StringComparer.Ordinal);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var item in sources)
                {
                    string name = Path.GetFileNameWithoutExtension(item.path);
                    if (entries.ContainsKey(name))
                        continue;
                    var entry = CreateEntry(item.path, name, item.clip);
                    AssetDatabase.AddObjectToAsset(entry.Action, library);
                    library.Entries.Add(entry);
                    entries.Add(name, entry);
                }

                foreach (LucyCombatEntry entry in library.Entries.Where(item => item.Kind == LucyCombatKind.Execution))
                {
                    string partner = PartnerName(entry.Source);
                    if (!entries.TryGetValue(partner, out LucyCombatEntry match))
                        throw new InvalidOperationException($"Missing paired Lucy clip: {partner}");
                    entry.Partner = match.Action;
                }
                AddComboBranches(library);
                BindDiagonalFootwork(entries);
                library.Entries = library.Entries.OrderBy(entry => entry.Kind)
                    .ThenBy(entry => entry.Source, StringComparer.Ordinal).ToList();
                EditorUtility.SetDirty(library);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Lucy combat library: {library.Entries.Count} clips, " +
                $"{library.Combo.Links.Count} combo branches, " +
                $"{library.Entries.Count(entry => entry.Partner != null) / 2} paired takes.");
        }

        private static AnimationClip LoadClip(string path)
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            if (clips.Length != 1)
                throw new InvalidOperationException($"{path}: expected one imported clip, found {clips.Length}.");
            return clips[0];
        }

        private static bool IsCombatPath(string path)
        {
            if (!path.StartsWith(SourceRoot, StringComparison.Ordinal) ||
                !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            string relative = path.Substring(SourceRoot.Length);
            return relative.StartsWith("Battle/", StringComparison.Ordinal) ||
                relative.StartsWith("Dodge/", StringComparison.Ordinal) ||
                relative.StartsWith("Hit/", StringComparison.Ordinal) ||
                relative.StartsWith("Die/", StringComparison.Ordinal) ||
                relative.StartsWith("Execution/", StringComparison.Ordinal) ||
                relative.StartsWith("Movement/", StringComparison.Ordinal) &&
                    Path.GetFileName(relative).StartsWith("Lucy_Fight_Walk_", StringComparison.Ordinal) ||
                relative == "Lucy_Fight_Idle_Pose.FBX";
        }

        private static LucyCombatEntry CreateEntry(string path, string name, AnimationClip clip)
        {
            string first = path.Substring(SourceRoot.Length).Split('/')[0];
            LucyCombatKind kind = first switch
            {
                "Battle" when name.StartsWith("Lucy_Air_", StringComparison.Ordinal) => LucyCombatKind.AirAttack,
                "Battle" when !name.Contains("Idle") => LucyCombatKind.GroundAttack,
                "Dodge" => LucyCombatKind.Dodge,
                "Hit" => LucyCombatKind.Hit,
                "Die" => LucyCombatKind.Knockout,
                "Movement" => LucyCombatKind.Footwork,
                "Execution" => LucyCombatKind.Execution,
                _ => LucyCombatKind.Stance
            };
            int separator = name.LastIndexOf('_');
            if (!Enum.TryParse(name.Substring(separator + 1), out LucyTake take))
                throw new InvalidOperationException($"Unknown Lucy take: {name}");
            string stem = name.Substring(0, separator);
            bool idle = stem == "Lucy_Fight_Idle";
            int direction = 0;
            foreach (string word in stem.Split('_'))
            {
                int index = Array.IndexOf(DirectionNames, word);
                if (index >= 0)
                    direction = index;
            }
            return new LucyCombatEntry
            {
                Source = name, Kind = kind, Take = take, IsIdle = idle, Direction = direction,
                Action = CreateAction(name, clip, kind, take)
            };
        }

        private static DuelActionAsset CreateAction(string name, AnimationClip clip, LucyCombatKind kind, LucyTake take)
        {
            var action = ScriptableObject.CreateInstance<DuelActionAsset>();
            action.name = name;
            action.FrameRate = Mathf.Clamp(Mathf.RoundToInt(clip.frameRate), 1, 240);
            action.Duration = Mathf.Max(1, Mathf.RoundToInt(clip.length * action.FrameRate));
            action.Action = kind switch
            {
                LucyCombatKind.GroundAttack or LucyCombatKind.AirAttack => DuelAction.Quick,
                LucyCombatKind.Dodge => DuelAction.Dodge,
                LucyCombatKind.Hit => DuelAction.HitStun,
                LucyCombatKind.Knockout => DuelAction.Knockout,
                LucyCombatKind.Stance => DuelAction.Guard,
                _ => DuelAction.None
            };
            action.Attack = action.Action == DuelAction.Quick;
            action.HitStart = 0;
            action.HitEnd = 0;
            action.DefenseStart = 0;
            action.DefenseEnd = 0;
            action.Damage = 0;
            action.RootMotionScale = take == LucyTake.Root && kind is not (LucyCombatKind.Footwork or LucyCombatKind.Dodge) ? 1f : 0f;
            action.ApplyRootHeight = take == LucyTake.Root && kind == LucyCombatKind.AirAttack;
            action.ApplyRootYaw = take == LucyTake.Root && kind == LucyCombatKind.Execution;

            if (action.Attack)
            {
                action.HitStart = Mathf.Clamp(Mathf.RoundToInt(action.Duration * 0.36f), 0, action.Duration - 1);
                action.HitEnd = Mathf.Min(action.Duration, action.HitStart + Mathf.Max(1, action.FrameRate / 8));
                bool kick = name.Contains("Kick");
                action.Reach = kick ? 2.12f : 1.8f;
                action.Damage = kick ? 20 : 17;
            }
            else if (kind == LucyCombatKind.Dodge)
            {
                action.DefenseStart = Math.Min(action.Duration - 1, Mathf.RoundToInt(action.FrameRate * 0.05f));
                action.DefenseEnd = Mathf.Min(action.Duration, Mathf.Max(action.DefenseStart + 1, Mathf.RoundToInt(action.FrameRate * 0.28f)));
                action.DodgeTravelSeconds = Mathf.Clamp(clip.length * 0.42f, 0.14f, 0.38f);
            }
            action.Motions.Add(new ActionMotion { Clip = clip });
            return action;
        }

        private static string PartnerName(string name)
        {
            if (name.StartsWith("Lucy_Execution", StringComparison.Ordinal))
                return name.Replace("Lucy_Execution", "Lucy_Executed");
            if (name.StartsWith("Lucy_Executed", StringComparison.Ordinal))
                return name.Replace("Lucy_Executed", "Lucy_Execution");
            if (name.StartsWith("Lucy_Ambushed", StringComparison.Ordinal))
                return name.Replace("Lucy_Ambushed", "Lucy_Ambush");
            return name.Replace("Lucy_Ambush", "Lucy_Ambushed");
        }

        private static void AddComboBranches(LucyCombatLibrary library)
        {
            if (library.Combo == null)
            {
                library.Combo = ScriptableObject.CreateInstance<DuelCombo>();
                library.Combo.name = "Lucy Animation Combos";
                AssetDatabase.AddObjectToAsset(library.Combo, library);
            }

            var stages = library.Entries.Where(entry => entry.Action.Attack)
                .Select(entry => (entry, match: ComboStage.Match(entry.Source[..entry.Source.LastIndexOf('_')])))
                .Where(item => item.match.Success)
                .Select(item => (item.entry, family: item.match.Groups[1].Value,
                    index: item.match.Groups[2].Value[0] - '0', branch: item.match.Groups[3].Value))
                .GroupBy(item => (item.family, item.entry.Take));

            foreach (var family in stages)
                foreach (var from in family)
                {
                    foreach (var to in family.Where(item => item.index == from.index + 1))
                    {
                        DuelAction input = to.branch == "b" ? DuelAction.Delayed : DuelAction.Quick;
                        DuelActionAsset action = from.entry.Action;
                        int cancel = Mathf.Max(action.HitEnd, Mathf.RoundToInt(action.Length * 0.58f));
                        if (cancel >= action.Length || library.Combo.Links.Any(link => link.From == action && link.Input == input))
                            continue;

                        library.Combo.Links.Add(new ComboLink
                        {
                            From = action, Input = input, To = to.entry.Action, BufferFrom = action.HitStart,
                            CancelFrom = cancel, CancelUntil = action.Length + 1
                        });
                    }
                }
            EditorUtility.SetDirty(library.Combo);
        }

        private static void BindDiagonalFootwork(Dictionary<string, LucyCombatEntry> entries)
        {
            DuelLoadout loadout = DuelLoadout.Default;
            DuelAnimationProfile profile = loadout != null ? loadout.Animation : null;
            if (profile == null)
                return;

            profile.ForwardRight = MissingLoop(profile.ForwardRight, "FR", entries);
            profile.BackRight = MissingLoop(profile.BackRight, "BR", entries);
            profile.BackLeft = MissingLoop(profile.BackLeft, "BL", entries);
            profile.ForwardLeft = MissingLoop(profile.ForwardLeft, "FL", entries);
            EditorUtility.SetDirty(profile);
        }

        private static LocomotionClips MissingLoop(LocomotionClips clips, string direction,
            Dictionary<string, LucyCombatEntry> entries)
        {
            if (clips?.Loop != null || !entries.TryGetValue($"Lucy_Fight_Walk_{direction}_Inplace", out LucyCombatEntry entry))
                return clips;

            clips ??= new LocomotionClips();
            clips.Loop = entry.Clip;
            if (clips.ReferenceSpeed <= 0f)
                clips.ReferenceSpeed = 2.75f;
            return clips;
        }
    }
}
