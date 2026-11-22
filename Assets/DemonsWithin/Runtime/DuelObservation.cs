using System;
using UnityEngine;

namespace DemonsWithin
{
    public sealed class DuelObservation
    {
        private const float DistanceMargin = 0.25f;
        private const float MovementThreshold = 0.28f;

        private readonly OpponentModel model;
        private readonly DuelRecording recording;
        private DuelOpportunity pending;
        private int actionVersion = -1;
        private float radialTravel, lateralTravel;

        public DuelOpportunity LastOpportunity { get; private set; }
        public DuelOpportunity Pending => pending;
        public event Action<DuelOpportunity> Completed;

        public DuelObservation(OpponentModel model, DuelRecording recording)
        {
            this.model = model;
            this.recording = recording;
        }

        public void Reset()
        {
            pending = null;
            LastOpportunity = null;
            actionVersion = -1;
        }

        public void ObserveStep(int step, float delta, DuelFighter player, DuelFighter demon,
            Vector3 playerBefore, Vector3 demonBefore, int playerVersionBefore, OpponentChoice choice)
        {
            if (actionVersion != demon.ActionVersion)
            {
                Close(step, StrikeResult.None, interrupted: true);
                actionVersion = demon.ActionVersion;
                DuelActionAsset move = demon.Move;
                if (move != null)
                    Open(step, delta, player, demon, move, choice);
                return;
            }

            if (pending == null || pending.Status != OpportunityStatus.Eligible || pending.Response != DuelResponse.Wait)
                return;

            if (player.ActionVersion != playerVersionBefore)
            {
                DuelResponse response = ResponseFor(player, demon, delta);
                if (response != DuelResponse.Wait)
                {
                    RecordResponse(step, demon.ActionFrame, response);
                    return;
                }
            }

            if (player.Movement.sqrMagnitude <= 0.01f)
                return;

            Vector3 away = Vector3.ProjectOnPlane(playerBefore - demonBefore, Vector3.up).normalized;
            Vector3 travel = Vector3.ProjectOnPlane(player.transform.position - playerBefore, Vector3.up);
            radialTravel += Vector3.Dot(travel, away);
            lateralTravel += Vector3.Dot(travel, Vector3.Cross(Vector3.up, away));

            if (radialTravel >= MovementThreshold)
                RecordResponse(step, demon.ActionFrame, DuelResponse.Retreat);
            else if (radialTravel <= -MovementThreshold)
                RecordResponse(step, demon.ActionFrame, DuelResponse.Approach);
            else if (Mathf.Abs(lateralTravel) >= MovementThreshold)
                RecordResponse(step, demon.ActionFrame, DuelResponse.Strafe);
        }

        public void Close(int step, StrikeResult result, bool interrupted = false)
        {
            if (pending == null)
                return;
            if (interrupted && pending.Status == OpportunityStatus.Eligible)
                pending.Status = OpportunityStatus.Interrupted;
            pending.EndStep = step;
            pending.Result = result;
            if (pending.Status == OpportunityStatus.Eligible)
                model.Observe(pending.Opener, pending.Distance, pending.Response);
            recording.RecordOpportunity(pending);
            LastOpportunity = pending;
            pending = null;
            Completed?.Invoke(LastOpportunity);
        }

        private void Open(int step, float delta, DuelFighter player, DuelFighter demon, DuelActionAsset move,
            OpponentChoice choice)
        {
            float distance = Vector3.ProjectOnPlane(player.transform.position - demon.transform.position, Vector3.up).magnitude;
            OpportunityStatus status = OpportunityStatus.Eligible;
            if (!player.CanAct)
                status = OpportunityStatus.BusyAtTell;
            else if (distance > move.Reach + DistanceMargin)
                status = OpportunityStatus.OutsideReach;
            else if (move.HitStart <= delta * move.Rate)
                status = OpportunityStatus.NoReactionWindow;

            radialTravel = lateralTravel = 0f;
            pending = new DuelOpportunity
            {
                StartStep = step,
                EndStep = -1,
                Opener = move.Action,
                StartMetres = distance,
                Distance = OpponentModel.Band(distance),
                Status = status,
                ResponseStep = -1,
                ResponseFrame = -1f,
                Choice = choice
            };
            if (status == OpportunityStatus.Eligible && player.Phase == FighterPhase.Guard)
                RecordResponse(step, 0f, DuelResponse.Guard);
        }

        private void RecordResponse(int step, float frame, DuelResponse response)
        {
            pending.Response = response;
            pending.ResponseStep = step;
            pending.ResponseFrame = frame;
        }

        private static DuelResponse ResponseFor(DuelFighter player, DuelFighter demon, float delta)
        {
            if (player.Move != null)
                return DuelResponse.Attack;

            switch (player.Phase)
            {
                case FighterPhase.Guard: return DuelResponse.Guard;
                case FighterPhase.Dodge: return DuelResponse.Dodge;
                case FighterPhase.Mikiri: return DuelResponse.Mikiri;
                case FighterPhase.Parry:
                    DuelActionAsset opener = demon.Move;
                    DuelActionAsset parry = player.CurrentAction;
                    // Both actions have already advanced by this step when observed.
                    float untilContact = (opener.HitStart - demon.ActionFrame) / opener.Rate;
                    float untilParryCloses = (parry.DefenseEnd - player.ActionFrame) / parry.Rate;
                    return untilContact > untilParryCloses + delta ? DuelResponse.EarlyParry : DuelResponse.Parry;
                default: return DuelResponse.Wait;
            }
        }
    }
}
