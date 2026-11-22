using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    public enum DuelBehavior { FinishCommitted, Approach, RecoverSpacing, Strike }
    public enum DuelNodeState { Skipped, Failure, Running, Success }

    [Serializable]
    public sealed class DuelBehaviorNode
    {
        public DuelBehavior Type;
        public bool Enabled = true;
        [Range(0f, 1f)] public float MoveInput = 0.75f;
        [Min(0f)] public float NearDistance = 1.35f;
        [Min(0f)] public float Cooldown = 0.48f;

        public static List<DuelBehaviorNode> CreateDefaultOrder() => new()
        {
            new() { Type = DuelBehavior.FinishCommitted },
            new() { Type = DuelBehavior.Approach, MoveInput = 0.75f },
            new() { Type = DuelBehavior.RecoverSpacing, MoveInput = 0.55f },
            new() { Type = DuelBehavior.Strike }
        };
    }

    [CreateAssetMenu(menuName = "Demons Within/Behavior Tree")]
    public sealed class DuelBehaviorTree : ScriptableObject
    {
        [Min(0f)] public float AttackRangeMargin = 0.15f;
        [Min(2)] public int BaselineProbeInterval = 3;
        public List<DuelBehaviorNode> Nodes = DuelBehaviorNode.CreateDefaultOrder();
    }
}
