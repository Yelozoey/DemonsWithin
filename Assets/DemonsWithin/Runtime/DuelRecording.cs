using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DemonsWithin
{
    public readonly struct OpponentChoice
    {
        public readonly DuelAction Chosen;
        public readonly DuelAction Baseline;
        public readonly OpponentTactic Tactic;
        public readonly HabitEstimate EarlyParry;
        public readonly HabitEstimate Retreat;
        public readonly bool Adaptive;
        public readonly bool BaselineProbe;
        public readonly int PlanId;
        public readonly string PlanTactic;

        public OpponentChoice(DuelAction chosen, DuelAction baseline, OpponentTactic tactic,
            HabitEstimate earlyParry, HabitEstimate retreat, bool adaptive, bool baselineProbe,
            int planId = 0, string planTactic = null)
        {
            Chosen = chosen;
            Baseline = baseline;
            Tactic = tactic;
            EarlyParry = earlyParry;
            Retreat = retreat;
            Adaptive = adaptive;
            BaselineProbe = baselineProbe;
            PlanId = planId;
            PlanTactic = planTactic;
        }
    }

    public sealed class DuelOpportunity
    {
        public int StartStep, EndStep, ResponseStep;
        public DuelAction Opener;
        public DuelDistance Distance;
        public float StartMetres, ResponseFrame;
        public OpportunityStatus Status;
        public DuelResponse Response;
        public StrikeResult Result;
        public OpponentChoice Choice;
    }

    public sealed class DuelRecording
    {
        private const int InputLimit = 4096;
        private const int ExchangeLimit = 512;

        [Serializable]
        private struct InputEntry
        {
            public int step;
            public string action, phase;
            public Vector2 movement;
        }

        [Serializable]
        private struct StrikeEntry
        {
            public int step;
            public string attacker, action, result;
            public int playerHealth, demonHealth;
        }

        [Serializable]
        private struct OpportunityEntry
        {
            public int startStep, endStep, responseStep;
            public float startMetres, responseFrame;
            public string opener, distance, status, response, result;
            public string baselineAttack, chosenAttack, tactic;
            public bool adaptiveAtStart, baselineProbe;
            public int planId;
            public string planTactic;
            public float earlyParryProbability, earlyParryEvidence, retreatProbability, retreatEvidence;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public int schemaVersion = 2;
            public string matchId;
            public string modeAtExport;
            public int step, eligibleOpportunities;
            public int droppedInput, droppedStrikes, droppedOpportunities;
            public bool hasOpenOpportunity;
            public OpportunityEntry openOpportunity;
            public InputEntry[] input;
            public StrikeEntry[] strikes;
            public OpportunityEntry[] opportunities;
            public LlmTacticsEvent[] llm;
        }

        private readonly Queue<InputEntry> input = new();
        private readonly Queue<StrikeEntry> strikes = new();
        private readonly Queue<OpportunityEntry> opportunities = new();
        private int droppedInput, droppedStrikes, droppedOpportunities;
        private string matchId;

        public void Reset()
        {
            matchId = Guid.NewGuid().ToString("N");
            input.Clear();
            strikes.Clear();
            opportunities.Clear();
            droppedInput = droppedStrikes = droppedOpportunities = 0;
        }

        public void RecordInput(int step, string action, string phase, Vector2 movement = default)
        {
            Append(input, new InputEntry { step = step, action = action, phase = phase, movement = movement },
                InputLimit, ref droppedInput);
        }

        public void RecordStrike(int step, bool fromPlayer, DuelAction action, StrikeResult result,
            int playerHealth, int demonHealth)
        {
            Append(strikes, new StrikeEntry
            {
                step = step, attacker = fromPlayer ? "Player" : "Demon",
                action = action.ToString(), result = result.ToString(),
                playerHealth = playerHealth, demonHealth = demonHealth
            }, ExchangeLimit, ref droppedStrikes);
        }

        public void RecordOpportunity(DuelOpportunity sample) =>
            Append(opportunities, Describe(sample), ExchangeLimit, ref droppedOpportunities);

        private static OpportunityEntry Describe(DuelOpportunity sample)
        {
            OpponentChoice choice = sample.Choice;
            return new OpportunityEntry
            {
                startStep = sample.StartStep, endStep = sample.EndStep, responseStep = sample.ResponseStep,
                startMetres = sample.StartMetres, responseFrame = sample.ResponseFrame,
                opener = sample.Opener.ToString(), distance = sample.Distance.ToString(),
                status = sample.Status.ToString(), response = sample.Response.ToString(), result = sample.Result.ToString(),
                baselineAttack = choice.Baseline.ToString(), chosenAttack = choice.Chosen.ToString(),
                tactic = choice.Tactic.ToString(), earlyParryProbability = choice.EarlyParry.Probability,
                adaptiveAtStart = choice.Adaptive, baselineProbe = choice.BaselineProbe,
                planId = choice.PlanId, planTactic = choice.PlanTactic,
                earlyParryEvidence = choice.EarlyParry.Evidence,
                retreatProbability = choice.Retreat.Probability, retreatEvidence = choice.Retreat.Evidence
            };
        }

        public string Export(int step, bool adaptive, int eligible, DuelOpportunity pending,
            bool llmMode = false, LlmTacticsEvent[] llm = null)
        {
            var data = new Snapshot
            {
                matchId = matchId, modeAtExport = llmMode ? "LLM" : adaptive ? "Adaptive" : "Baseline", step = step,
                eligibleOpportunities = eligible, droppedInput = droppedInput, droppedStrikes = droppedStrikes,
                droppedOpportunities = droppedOpportunities, input = input.ToArray(), strikes = strikes.ToArray(),
                opportunities = opportunities.ToArray(), llm = llm ?? Array.Empty<LlmTacticsEvent>(),
                hasOpenOpportunity = pending != null,
                openOpportunity = pending == null ? default : Describe(pending)
            };
            string directory = Path.Combine(Application.persistentDataPath, "Duels");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{matchId}.json");
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
            return path;
        }

        private static void Append<T>(Queue<T> queue, T entry, int limit, ref int dropped)
        {
            if (queue.Count == limit)
            {
                queue.Dequeue();
                dropped++;
            }
            queue.Enqueue(entry);
        }
    }
}
