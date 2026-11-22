using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonsWithin
{
    [Serializable]
    public struct LlmTacticsEvent
    {
        public int requestId, step, latencyMs;
        public string kind, tactic, reason, range;
        public int[] evidence;
    }

    public sealed class LlmTacticsPlanner
    {
        private const int EvidenceLimit = 12;
        private const int AuditLimit = 160;
        private const int MaxEvidenceLag = 4;
        private const string SystemPrompt =
            "You select one bounded attack probe for a third-person duel opponent. " +
            "The player sees the same attack telegraphs as usual. History contains resolved observations only. " +
            "Return an object with tactic, range, evidence (array of exactly two distinct sample ids), and attempts (1 or 2). " +
            "delay_probe (Delayed attack): two EarlyParry observations against Quick. " +
            "thrust_probe (Thrust attack): two Retreat observations. " +
            "quick_probe (Quick attack): two Attack or two Approach observations. " +
            "Pick samples with the same opener and response. Set range to near, mid, far, or any. " +
            "For near, mid, or far both samples must match that range. Favor newer observations. " +
            "When the pattern is weak or contradicted, return {\"tactic\":\"observe\",\"range\":\"any\",\"evidence\":[],\"attempts\":0}. " +
            "Output one JSON object without prose.";

        [Serializable]
        private struct Sample
        {
            public int id;
            public string opener, range, response, result;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public Sample[] samples;
        }

        [Serializable]
        private sealed class Proposal
        {
            public string tactic, range;
            public int[] evidence;
            public int attempts;
        }

        private sealed class Plan
        {
            public int Id, Remaining, Contradictions;
            public DuelAction Action;
            public Sample Pattern;
            public string Tactic, Range;
            public double Requested, Expires;
        }

        private readonly Queue<Sample> samples = new();
        private readonly Queue<LlmTacticsEvent> audit = new();
        private LlmTacticsSettings settings;
        private string settingsError;
        private LlmTacticsRequest request;
        private Sample[] requestEvidence;
        private int requestId, eligible, submittedAt;
        private double nextRequest;
        private Plan plan;
        private bool active;

        public string Status { get; private set; } = "Off";
        public LlmTacticsEvent[] Audit() => audit.ToArray();

        public void SetActive(bool value, int step)
        {
            if (value == active)
                return;

            active = value;
            if (active)
            {
                settings = LlmTacticsSettings.Load(out settingsError);
                settingsError ??= settings.Validate();
                Status = settingsError ?? "Collecting evidence";
            }
            else
            {
                Stop("Mode changed", step);
                Status = "Off";
            }
        }

        public void Reset(int step)
        {
            Stop("Rematch", step);
            samples.Clear();
            audit.Clear();
            requestId = eligible = submittedAt = 0;
            nextRequest = 0;
            Status = active ? settingsError ?? "Collecting evidence" : "Off";
        }

        public void Stop(string reason, int step)
        {
            if (request != null)
            {
                Log(requestId, step, "canceled", reason: reason);
                CloseRequest();
                requestEvidence = null;
            }
            EndPlan(step, reason);
            if (active)
                Status = reason;
        }

        public void Observe(DuelOpportunity exchange)
        {
            int usedPlan = exchange.Choice.PlanId;
            if (usedPlan != 0)
            {
                Log(usedPlan, exchange.EndStep, "outcome", exchange.Choice.PlanTactic,
                    exchange.Result == StrikeResult.None ? exchange.Status.ToString() : exchange.Result.ToString());
                if (plan?.Id == usedPlan && exchange.Result is StrikeResult.Parried or StrikeResult.Mikiri or StrikeResult.Dodged)
                    EndPlan(exchange.EndStep, "Probe countered");
            }

            if (exchange.Status != OpportunityStatus.Eligible)
                return;

            eligible++;
            var sample = new Sample
            {
                id = exchange.StartStep, opener = exchange.Opener.ToString(),
                range = RangeName(exchange.Distance), response = exchange.Response.ToString(),
                result = exchange.Result.ToString()
            };
            if (samples.Count == EvidenceLimit)
                samples.Dequeue();
            samples.Enqueue(sample);
            if (plan != null)
                CheckPattern(sample, exchange.EndStep);
        }

        public void Tick(double now, int step)
        {
            if (!active || settingsError != null)
                return;

            if (plan != null && now >= plan.Expires)
                EndPlan(step, "Plan timed out");
            if (request != null)
            {
                if (request.Poll(now, out string content, out string error))
                {
                    double started = request.Started;
                    int latency = (int)((now - started) * 1000);
                    CloseRequest();
                    if (error != null)
                        Reject(step, error, latency);
                    else
                        Accept(content, started, now, step, latency);
                    requestEvidence = null;
                }
                return;
            }

            if (plan != null || now < nextRequest || eligible < 3 || eligible - submittedAt < 2)
                return;

            requestEvidence = samples.ToArray();
            submittedAt = eligible;
            requestId++;
            nextRequest = now + settings.Interval;
            string snapshot = JsonUtility.ToJson(new Snapshot { samples = requestEvidence });
            request = LlmTacticsRequest.Start(settings, SystemPrompt, snapshot, now, out string startError);
            if (request == null)
            {
                requestEvidence = null;
                Reject(step, startError, 0);
            }
            else
            {
                Status = "Thinking";
                Log(requestId, step, "request");
            }
        }

        public (DuelAction action, int id, string tactic) Suggest(DuelDistance band, double now) =>
            active && plan != null && now < plan.Expires && MatchesRange(plan.Range, RangeName(band))
                ? (plan.Action, plan.Id, plan.Tactic)
                : (DuelAction.None, 0, null);

        public void Commit(int id, int step, double now)
        {
            if (id == 0 || plan?.Id != id)
                return;

            plan.Remaining--;
            Log(id, step, "used", plan.Tactic, latency: (int)((now - plan.Requested) * 1000));
            if (plan.Remaining == 0)
                EndPlan(step, "Probe budget spent");
            else
                Status = $"{plan.Tactic} ({plan.Remaining})";
        }

        private void Accept(string content, double started, double now, int step, int latency)
        {
            if (eligible - submittedAt > MaxEvidenceLag)
            {
                Reject(step, "Stale evidence", latency);
                return;
            }

            Proposal proposal = null;
            try
            {
                proposal = JsonUtility.FromJson<Proposal>(content);
            }
            catch (ArgumentException)
            {
                // Invalid structured output will be rejected below.
            }

            if (proposal?.tactic == "observe" && proposal.range == "any" &&
                proposal.attempts == 0 && proposal.evidence is { Length: 0 })
            {
                Status = "Observing";
                Log(requestId, step, "observe", latency: latency);
                return;
            }

            if (proposal == null || proposal.attempts is < 1 or > 2 ||
                proposal.range is not ("near" or "mid" or "far" or "any") ||
                proposal.evidence is not { Length: 2 } || proposal.evidence[0] == proposal.evidence[1])
            {
                Reject(step, "Invalid proposal", latency);
                return;
            }

            Sample first = Array.Find(requestEvidence, item => item.id == proposal.evidence[0]);
            Sample second = Array.Find(requestEvidence, item => item.id == proposal.evidence[1]);
            DuelAction action = AttackFor(proposal.tactic, first);
            if (action == DuelAction.None || first.opener != second.opener || first.response != second.response ||
                !MatchesRange(proposal.range, first.range) || !MatchesRange(proposal.range, second.range))
            {
                Reject(step, "Unsupported evidence", latency);
                return;
            }

            var validated = new Plan
            {
                Id = requestId, Action = action, Tactic = proposal.tactic, Range = proposal.range,
                Pattern = first, Remaining = proposal.attempts, Requested = started,
                Expires = now + settings.Lifetime
            };
            int latestEvidence = Math.Max(first.id, second.id);
            foreach (Sample sample in samples)
            {
                if (sample.id > latestEvidence && PatternChanged(validated, sample))
                {
                    Reject(step, "Player already changed response", latency);
                    return;
                }
            }

            plan = validated;
            Status = $"{plan.Tactic} ({plan.Remaining})";
            Log(plan.Id, step, "accepted", plan.Tactic, latency: latency, range: plan.Range, evidence: proposal.evidence);
        }

        private void CheckPattern(Sample sample, int step)
        {
            if (PatternChanged(plan, sample))
                EndPlan(step, "Player changed response");
        }

        private static bool PatternChanged(Plan target, Sample sample)
        {
            if (sample.opener != target.Pattern.opener || !MatchesRange(target.Range, sample.range))
                return false;

            target.Contradictions = sample.response == target.Pattern.response ? 0 : target.Contradictions + 1;
            return target.Contradictions >= 2;
        }

        private void EndPlan(int step, string reason)
        {
            if (plan == null)
                return;
            Log(plan.Id, step, "ended", plan.Tactic, reason);
            plan = null;
            Status = reason;
        }

        private void Reject(int step, string reason, int latency)
        {
            Status = reason;
            Log(requestId, step, "rejected", reason: reason, latency: latency);
        }

        private void CloseRequest()
        {
            request.Dispose();
            request = null;
        }

        private void Log(int id, int step, string kind, string tactic = null, string reason = null, int latency = 0,
            string range = null, int[] evidence = null)
        {
            if (audit.Count == AuditLimit)
                audit.Dequeue();
            audit.Enqueue(new LlmTacticsEvent
            {
                requestId = id, step = step, kind = kind, tactic = tactic, reason = reason, latencyMs = latency,
                range = range, evidence = evidence
            });
        }

        private static DuelAction AttackFor(string tactic, Sample sample) => tactic switch
        {
            "delay_probe" when sample.opener == "Quick" && sample.response == "EarlyParry" => DuelAction.Delayed,
            "thrust_probe" when sample.response == "Retreat" => DuelAction.Thrust,
            "quick_probe" when sample.response is "Attack" or "Approach" => DuelAction.Quick,
            _ => DuelAction.None
        };

        private static bool MatchesRange(string requested, string actual) => requested == "any" || requested == actual;

        private static string RangeName(DuelDistance distance) => distance switch
        {
            DuelDistance.Near => "near",
            DuelDistance.Mid => "mid",
            _ => "far"
        };
    }
}
