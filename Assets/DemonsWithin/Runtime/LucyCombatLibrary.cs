using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    public enum LucyCombatKind { GroundAttack, AirAttack, Dodge, Hit, Knockout, Footwork, Stance, Execution }
    public enum LucyTake { Root, Inplace, Sample, Pose }

    [Serializable]
    public sealed class LucyCombatEntry
    {
        public string Source;
        public LucyCombatKind Kind;
        public LucyTake Take;
        public DuelActionAsset Action;
        public DuelActionAsset Partner;
        public bool IsIdle;
        public int Direction;

        public AnimationClip Clip => Action != null && Action.Motions.Count > 0 ? Action.Motions[0].Clip : null;
        public bool CanEquip => Clip != null && !Action.Attack && (Kind != LucyCombatKind.Execution || Partner != null);
        public bool IsExecutionReceiver => Source.StartsWith("Lucy_Executed", StringComparison.Ordinal) ||
            Source.StartsWith("Lucy_Ambushed", StringComparison.Ordinal);

        public static string Label(LucyCombatKind kind) => kind switch
        {
            LucyCombatKind.GroundAttack => "地面攻击",
            LucyCombatKind.AirAttack => "空中攻击",
            LucyCombatKind.Dodge => "闪避",
            LucyCombatKind.Hit => "受击",
            LucyCombatKind.Knockout => "倒地",
            LucyCombatKind.Footwork => "战斗步法",
            LucyCombatKind.Stance => "架势",
            _ => "双人处决／伏击"
        };
    }

    public sealed class LucyCombatLibrary : ScriptableObject
    {
        public List<LucyCombatEntry> Entries = new();
        public DuelCombo Combo;

        public static LucyCombatLibrary Load() => Resources.Load<LucyCombatLibrary>("LucyCombatLibrary");
    }
}
