using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    public enum DuelDistance { Near, Mid, Far }
    public enum DuelResponse { Wait, EarlyParry, Parry, Mikiri, Dodge, Guard, Attack, Retreat, Approach, Strafe }
    public enum OpportunityStatus { Eligible, BusyAtTell, OutsideReach, NoReactionWindow, Interrupted }
    public enum OpponentTactic { Baseline, DelayAgainstEarlyParry, ThrustAgainstRetreat, LlmPlan }

    public readonly struct HabitEstimate
    {
        public readonly float Evidence;
        public readonly float Probability;

        public HabitEstimate(float evidence, float frequency)
        {
            Evidence = evidence;
            Probability = (frequency + 0.5f) / (evidence + 2f);
        }

        public float Weight => Evidence >= 2.35f && Probability >= 0.58f ? Probability : 0f;
    }

    public sealed class OpponentModel
    {
        private const float Retention = 0.88f;

        private struct Counts
        {
            public int UpdatedAt;
            public float Total, EarlyParry, Retreat;

            public Counts At(int sample)
            {
                float decay = Mathf.Pow(Retention, sample - UpdatedAt);
                return new Counts
                {
                    UpdatedAt = sample,
                    Total = Total * decay,
                    EarlyParry = EarlyParry * decay,
                    Retreat = Retreat * decay
                };
            }

            public Counts Add(Counts other) => new()
            {
                UpdatedAt = UpdatedAt,
                Total = Total + other.Total,
                EarlyParry = EarlyParry + other.EarlyParry,
                Retreat = Retreat + other.Retreat
            };
        }

        private readonly Dictionary<(DuelAction, DuelDistance), Counts> contexts = new();
        public int EligibleCount { get; private set; }

        public void Reset()
        {
            contexts.Clear();
            EligibleCount = 0;
        }

        public void Observe(DuelAction opener, DuelDistance distance, DuelResponse response)
        {
            EligibleCount++;
            var key = (opener, distance);
            contexts.TryGetValue(key, out Counts history);
            history = history.At(EligibleCount);
            history.Total++;
            if (response == DuelResponse.EarlyParry)
                history.EarlyParry++;
            if (response == DuelResponse.Retreat)
                history.Retreat++;
            contexts[key] = history;
        }

        public HabitEstimate EarlyParryAt(DuelDistance distance)
        {
            Counts exact = Sum(DuelAction.Quick, distance);
            Counts chosen = exact.Total >= 2.35f ? exact : Sum(DuelAction.Quick, null);
            return new HabitEstimate(chosen.Total, chosen.EarlyParry);
        }

        public HabitEstimate RetreatAt(DuelDistance distance)
        {
            Counts exact = Sum(null, distance);
            Counts chosen = exact.Total >= 2.35f ? exact : Sum(null, null);
            return new HabitEstimate(chosen.Total, chosen.Retreat);
        }

        public static DuelDistance Band(float metres) =>
            metres <= 1.5f ? DuelDistance.Near : metres <= 2.45f ? DuelDistance.Mid : DuelDistance.Far;

        private Counts Sum(DuelAction? opener, DuelDistance? distance)
        {
            Counts sum = new() { UpdatedAt = EligibleCount };
            foreach (var (key, history) in contexts)
                if ((!opener.HasValue || opener.Value == key.Item1) && (!distance.HasValue || distance.Value == key.Item2))
                    sum = sum.Add(history.At(EligibleCount));
            return sum;
        }
    }
}
