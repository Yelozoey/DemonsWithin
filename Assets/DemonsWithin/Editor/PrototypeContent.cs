using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DemonsWithin.Editor
{
    public static class PrototypeContent
    {
        private const string Combat = "Assets/DemonsWithin/Combat";
        private const string Resources = "Assets/DemonsWithin/Resources";
        private const string Lucy = "Assets/Lucy";
        private const string Battle = Lucy + "/Animation/Battle/Root/";
        private const string Walk = Lucy + "/Animation/Movement/Inplace/";

        [MenuItem("Tools/Demons Within/Create Missing Prototype Assets", priority = 120)]
        public static void CreateMissing()
        {
            foreach (string folder in new[] { Combat, Combat + "/Actions", Resources })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();

            DuelActionAsset quick = Action(DuelAction.Quick, Battle + "Lucy_FightFist01_1_Root.FBX", 56, 25, 32, 1.80f, 60f, 17);
            DuelActionAsset followup = Action(DuelAction.QuickFollowup, Battle + "Lucy_FightFist01_2_Root.FBX", 48, 15, 22, 1.85f, 65f, 20);
            DuelActionAsset delayed = Action(DuelAction.Delayed, Battle + "Lucy_FightFist07_Root.FBX", 87, 49, 57, 1.90f, 65f, 26);
            DuelActionAsset thrust = Action(DuelAction.Thrust, Battle + "Lucy_FightFist03_Root.FBX", 80, 39, 47, 2.28f, 25f, 29);
            DuelActionAsset guard = Action(DuelAction.Guard, Battle + "Lucy_Idle2Fight_Root.FBX", 25, clipOut: 0.36f);
            DuelActionAsset parry = Action(DuelAction.Parry, Battle + "Lucy_Idle2Fight_Root.FBX", 25, defenseStart: 2, defenseEnd: 11);
            DuelActionAsset mikiri = Action(DuelAction.Mikiri, Battle + "Lucy_FightFist03_Root.FBX", 24, defenseStart: 2, defenseEnd: 15);
            DuelActionAsset dodge = Action(DuelAction.Dodge, Lucy + "/Animation/Dodge/Root/Lucy_FightAvoid_F_Root.FBX",
                24, defenseStart: 2, defenseEnd: 16);
            DuelActionAsset back = Action(DuelAction.DodgeBack, Lucy + "/Animation/Dodge/Root/Lucy_FightAvoid_B_Root.FBX",
                24, defenseStart: 2, defenseEnd: 16);
            DuelActionAsset hit = Action(DuelAction.HitStun, Lucy + "/Animation/Hit/Root/Lucy_Hit_F_Root.FBX", 17);
            DuelActionAsset counter = Action(DuelAction.CounterStun, Lucy + "/Animation/Hit/Root/Lucy_Large_Hit_Root.FBX", 35);
            DuelActionAsset down = Action(DuelAction.Knockout, Lucy + "/Animation/Die/Root/Lucy_Die01_Root.FBX", 76);

            LucyCombatLibrary library = LucyCombatLibrary.Load();
            DuelCombo combo = Create<DuelCombo>(Combat + "/LucyBasicCombo.asset", asset =>
            {
                asset.AlternateOpener = library?.Entries.Find(entry => entry.Source == "Lucy_FightFist02_1_Root")?.Action;
                asset.Links.Add(new ComboLink { From = quick, Input = DuelAction.Quick, To = followup,
                    BufferFrom = 12, CancelFrom = 33, CancelUntil = 53 });
                asset.Links.Add(new ComboLink { From = followup, Input = DuelAction.Delayed, To = delayed,
                    BufferFrom = 10, CancelFrom = 23, CancelUntil = 45, RequireHit = true });
            });

            DuelAnimationProfile animation = Create<DuelAnimationProfile>(Combat + "/LucyAnimation.asset", asset =>
            {
                asset.PreviewModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DemonsWithin/Prefabs/Player.prefab");
                asset.Idle = Clip(Lucy + "/Animation/Battle/Inplace/Lucy_Fight_Idle_Inplace.FBX");
                asset.Forward = Motion("Walk_F_Start", "Fight_Walk_F", "Walk_F_End");
                asset.Back = Motion("Walk_B_Start", "Fight_Walk_B", "Walk_B_End");
                asset.Left = Motion("Walk_L_Start", "Fight_Walk_L", "Walk_L_End");
                asset.Right = Motion("Walk_R_Start", "Fight_Walk_R", "Walk_R_End");
                asset.Run = Motion("Run_F_Start", "Run_F", "Run_F_Stop");
                asset.Fades.Add(new AnimationFade { From = asset.Idle, To = asset.Forward.Start, Seconds = 0.12f });
                asset.Fades.Add(new AnimationFade { From = asset.Forward.Start, To = asset.Forward.Loop, Seconds = 0.10f });
                asset.Fades.Add(new AnimationFade { From = asset.Run.Loop, To = asset.Run.Stop, Seconds = 0.13f });
                asset.Fades.Add(new AnimationFade { From = quick.Motions[0].Clip, To = followup.Motions[0].Clip, Seconds = 0.08f });
            });

            Create<DuelLoadout>(Resources + "/LucyDuelLoadout.asset", asset =>
            {
                asset.Animation = animation;
                asset.Combo = combo;
                asset.AdditionalCombos = library?.Combo;
                asset.Actions = new[] { quick, followup, delayed, thrust, guard, parry, mikiri, dodge, back, hit, counter, down };
            });
            AssetDatabase.SaveAssets();
            Debug.Log("Demons Within prototype action, combo and animation assets are ready.");
        }

        private static DuelActionAsset Action(DuelAction action, string path, int duration,
            int hitStart = 0, int hitEnd = 0, float reach = 1.8f, float angle = 60f, int damage = 0,
            int defenseStart = 0, int defenseEnd = 0, float clipOut = 1f)
        {
            return Create<DuelActionAsset>($"{Combat}/Actions/{action}.asset", asset =>
            {
                asset.Action = action;
                asset.Duration = duration;
                asset.Attack = hitEnd > hitStart;
                asset.HitStart = hitStart;
                asset.HitEnd = hitEnd;
                asset.DefenseStart = defenseStart;
                asset.DefenseEnd = defenseEnd;
                asset.Reach = reach;
                asset.HalfAngle = angle;
                asset.Damage = damage;
                asset.Motions.Add(new ActionMotion { Clip = Clip(path), ClipOut = clipOut, Fade = 0.07f });
                if (asset.Attack)
                {
                    asset.Cues.Add(new ActionCue { Track = ActionTrack.Event, Frame = 0, Event = "Windup" });
                    asset.Cues.Add(new ActionCue { Track = ActionTrack.Event, Frame = hitStart, Event = "HitWindowOpen" });
                }
            });
        }

        private static LocomotionClips Motion(string start, string loop, string stop) => new()
        {
            Start = Clip(Walk + "Lucy_" + start + "_Inplace.FBX"),
            Loop = Clip(Walk + "Lucy_" + loop + "_Inplace.FBX"),
            Stop = Clip(Walk + "Lucy_" + stop + "_Inplace.FBX")
        };

        private static AnimationClip Clip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(item => !item.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null || !clip.isHumanMotion)
                throw new InvalidOperationException($"Cannot load a humanoid animation from {path}");
            return clip;
        }

        private static T Create<T>(string path, System.Action<T> setup) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing;

            T asset = ScriptableObject.CreateInstance<T>();
            setup(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
