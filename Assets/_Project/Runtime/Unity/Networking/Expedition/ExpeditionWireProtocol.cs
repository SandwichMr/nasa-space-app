using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Unity.Networking.Outpost;
using W = Trainer.Unity.Networking.Outpost.OutpostWireProtocol;

namespace Trainer.Unity.Networking.Expedition
{
    internal struct ExpeditionClientMessage
    {
        public OutpostMessageKind Kind;
        public ExpeditionCommand Command;
        public MovementInput Movement;
    }

    internal struct ExpeditionHostMessage
    {
        public OutpostMessageKind Kind;
        public CommandResult Result;
        public ExpeditionSessionSnapshot Snapshot;
        public OutpostMembership Membership;
    }

    /// <summary>
    /// Phase 3 binary wire format ('T' 'E', version 1, little-endian). Same message kinds, framing (chunks ≤ 1100 B,
    /// reassembly ≤ 16 KB), size limits and strict decoding as the phase 2 cable ('T' 'O'), whose encoders it reuses:
    /// movement, results and memberships have the phase 2 bodies, and a snapshot is the unchanged phase 2 snapshot
    /// body followed by the phase 3 extension. The phase 1 ('T' 'S') and phase 2 cables are untouched; each decoder
    /// rejects the others as BadMagic. Commands carry no sender identity and no position.
    /// </summary>
    internal static class ExpeditionWireProtocol
    {
        internal const byte Magic1 = 0x45; // 'E'
        internal const byte Version = 1;
        internal const int MaxLongTextBytes = 1024;

        internal static W.Assembler NewAssembler() => new W.Assembler(Magic1, Version);

        internal static List<byte[]> Frame(byte[] message) => W.Frame(message, Magic1, Version);

        private static byte[] Encode(OutpostMessageKind kind, Action<BinaryWriter> body) => W.Encode(kind, body, Magic1, Version);

        // ---- Client -> host ---------------------------------------------------------------------------------

        internal static byte[] EncodeJoin() => Encode(OutpostMessageKind.Join, w => { });

        internal static byte[] EncodeSubmit(ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return Encode(OutpostMessageKind.Submit, w =>
            {
                w.Write(command.Id.ToByteArray());
                w.Write(command.Epoch);
                w.Write((int)command.Action);
                W.WriteKey(w, command.Station.IsValid ? command.Station.Value : null);
                W.WriteKey(w, command.Task.IsValid ? command.Task.Value : null);
                w.Write(command.ZoneSequence);
                w.Write(command.AirlockCycle);
            });
        }

        internal static byte[] EncodeMovement(MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Encode(OutpostMessageKind.Movement, w =>
            {
                w.Write(input.Epoch);
                w.Write(input.Sequence);
                w.Write(input.Direction.X);
                w.Write(input.Direction.Y);
            });
        }

        internal static bool TryDecodeFromClient(byte[] payload, out ExpeditionClientMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > W.MaxClientMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!W.TryOpen(payload, out OutpostMessageKind kind, out error, Magic1, Version)) return false;
            if (kind != OutpostMessageKind.Join && kind != OutpostMessageKind.Submit && kind != OutpostMessageKind.Movement)
                return W.Fail(IsHostKind(kind) ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            var decoded = new ExpeditionClientMessage { Kind = kind };
            bool ok = W.Read(payload, out error, r =>
            {
                if (kind == OutpostMessageKind.Submit)
                {
                    Guid id = W.ReadGuid(r);
                    long epoch = r.ReadInt64();
                    var action = (ExpeditionAction)r.ReadInt32(); // unknown actions reach the authority (UnknownAction)
                    string station = W.ReadKey(r), task = W.ReadKey(r);
                    long zone = r.ReadInt64(), cycle = r.ReadInt64();
                    decoded.Command = new ExpeditionCommand(id, epoch, action,
                        station == null ? StationId.None : new StationId(station),
                        task == null ? TaskTypeId.None : new TaskTypeId(task), zone, cycle);
                }
                else if (kind == OutpostMessageKind.Movement)
                {
                    long epoch = r.ReadInt64(), sequence = r.ReadInt64();
                    double x = r.ReadDouble(), y = r.ReadDouble();
                    if (!MoveDirection.TryCreate(x, y, out MoveDirection direction)) throw new InvalidDataException();
                    decoded.Movement = new MovementInput(epoch, sequence, direction);
                }
            });
            if (ok) message = decoded;
            return ok;
        }

        // ---- Host -> client ---------------------------------------------------------------------------------

        internal static byte[] EncodeResult(CommandResult result, out bool truncated)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            byte[] reason = W.Utf8Cut(result.Reason, W.MaxReasonBytes, out truncated);
            return Encode(OutpostMessageKind.Result, w =>
            {
                w.Write(result.CommandId.ToByteArray());
                w.Write(result.Epoch);
                w.Write(result.Accepted);
                w.Write((ushort)reason.Length);
                w.Write(reason);
            });
        }

        internal static byte[] EncodeMembership(OutpostMembership membership)
        {
            if (membership == null) throw new ArgumentNullException(nameof(membership));
            byte[] reason = W.Utf8Cut(membership.Reason, W.MaxReasonBytes, out _);
            return Encode(OutpostMessageKind.Membership, w =>
            {
                w.Write((byte)membership.Status);
                w.Write(membership.Player.Value);
                w.Write((ushort)reason.Length);
                w.Write(reason);
            });
        }

        internal static byte[] EncodeSnapshot(ExpeditionSessionSnapshot s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return Encode(OutpostMessageKind.Snapshot, w =>
            {
                W.WriteSnapshotBody(w, s.Outpost);
                WriteExtension(w, s);
            });
        }

        internal static bool TryDecodeFromHost(byte[] payload, out ExpeditionHostMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > W.MaxMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!W.TryOpen(payload, out OutpostMessageKind kind, out error, Magic1, Version)) return false;
            if (kind != OutpostMessageKind.Result && kind != OutpostMessageKind.Snapshot && kind != OutpostMessageKind.Membership)
                return W.Fail(kind == OutpostMessageKind.Join || kind == OutpostMessageKind.Submit || kind == OutpostMessageKind.Movement
                    || kind == OutpostMessageKind.Chunk ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            var decoded = new ExpeditionHostMessage { Kind = kind };
            bool ok = W.Read(payload, out error, r =>
            {
                if (kind == OutpostMessageKind.Result)
                {
                    Guid id = W.ReadGuid(r);
                    long epoch = r.ReadInt64();
                    bool accepted = W.ReadBool(r);
                    decoded.Result = new CommandResult(id, epoch, accepted, W.ReadText(r));
                }
                else if (kind == OutpostMessageKind.Membership) decoded.Membership = W.ReadMembership(r);
                else
                {
                    OutpostSessionSnapshot outpost = W.ReadSnapshot(r);
                    decoded.Snapshot = ReadExtension(r, outpost);
                }
            });
            if (ok) message = decoded;
            return ok;
        }

        private static bool IsHostKind(OutpostMessageKind kind) =>
            kind == OutpostMessageKind.Result || kind == OutpostMessageKind.Snapshot
            || kind == OutpostMessageKind.Membership || kind == OutpostMessageKind.Chunk;

        // ---- Phase 3 extension --------------------------------------------------------------------------------

        internal static void WriteExtension(BinaryWriter w, ExpeditionSessionSnapshot s)
        {
            w.Write((byte)s.Crew.Count);
            foreach (ExpeditionCrewState c in s.Crew)
            {
                w.Write(c.Player.Value);
                w.Write((byte)c.Zone);
                w.Write(c.ZoneSequence);
                w.Write((sbyte)c.SuitIndex);
                w.Write(c.OnSuitSupply);
                w.Write((byte)c.Activity);
                w.Write((byte)c.Protection);
                WriteMargin(w, c.Margin);
            }
            w.Write((byte)s.Suits.Count);
            foreach (SuitState suit in s.Suits)
            {
                W.WriteKey(w, suit.Key);
                w.Write((byte)suit.Status);
                w.Write(suit.Wearer.Value);
                w.Write((byte)suit.Completed);
                w.Write(suit.Reading.Levels.OxygenKilograms);
                w.Write(suit.Reading.Levels.BatteryJoules);
                w.Write(suit.Reading.Levels.FeedwaterKilograms);
                w.Write(suit.Reading.OnInternalSupply);
                w.Write(Mask(suit.Reading.BelowReserve));
                w.Write(Mask(suit.Reading.Depleted));
            }
            AirlockStatus a = s.Airlock;
            w.Write((byte)a.State);
            w.Write(a.CycleSequence);
            w.Write(a.CycleWorkSeconds);
            w.Write(a.CycleDurationSeconds);
            w.Write(a.Stalled);
            w.Write(a.CycleRequestedBy.Value);
            w.Write((byte)a.Occupants.Count);
            foreach (PlayerId occupant in a.Occupants) w.Write(occupant.Value);
            w.Write(s.Shelter.Prepared);
            w.Write(s.Shelter.VentilationRunning);
            w.Write(s.Shelter.ServiceAvailable);
            w.Write((byte)s.Shelter.Occupants);
            w.Write(s.Supply.OxygenRechargeKilograms);
            w.Write(s.Supply.MakeupGasKilograms);
            w.Write((byte)s.Advisories.Count);
            foreach (IssuedAdvisory advisory in s.Advisories)
            {
                AdvisoryMessage m = advisory.Message;
                W.WriteKey(w, m.Id);
                w.Write((byte)m.Kind);
                w.Write(m.IssuedAtSeconds);
                WriteOptional(w, m.ShelterBySeconds);
                w.Write(m.ValidUntilSeconds);
                W.WriteKey(w, m.Supersedes);
                W.WriteKey(w, m.TextKey);
                w.Write((byte)advisory.Validity);
            }
            w.Write((byte)s.Receptions.Count);
            foreach (AdvisoryReception reception in s.Receptions)
            {
                W.WriteKey(w, reception.MessageId);
                w.Write(reception.Player.Value);
                w.Write(reception.ReceivedAtSeconds);
                w.Write(reception.AfterShelterBy);
            }
            ShelterInstructionStatus i = s.Instruction;
            w.Write((byte)i.State);
            W.WriteKey(w, i.LatestMessageId);
            WriteOptional(w, i.ShelterBySeconds);
            WriteOptional(w, i.ValidUntilSeconds);
            WriteOptional(w, i.EndedAtSeconds);
            w.Write((byte)s.Phase);
            w.Write((byte)s.Outcome);
            w.Write(s.Decision != null);
            if (s.Decision != null)
            {
                w.Write(s.Decision.DeclaredBy.Value);
                w.Write(s.Decision.AtSeconds);
                w.Write((byte)s.Decision.AdvisoriesKnown.Count);
                foreach (string id in s.Decision.AdvisoriesKnown) W.WriteKey(w, id);
                WriteOptional(w, s.Decision.TeamMarginSeconds);
            }
            w.Write((byte)s.Objectives.Count);
            foreach (ExpeditionObjective o in s.Objectives)
            {
                W.WriteKey(w, o.Key);
                w.Write((byte)o.Role);
                w.Write(o.Current);
                w.Write(o.Target);
            }
            w.Write((byte)s.Assessments.Count);
            foreach (ProtectionAssessment p in s.Assessments)
            {
                w.Write(p.Player.Value);
                w.Write((byte)p.Category);
                WriteOptional(w, p.ReceivedAtSeconds);
                WriteOptional(w, p.FirstProtectedAtSeconds);
                w.Write(p.ReceivedAfterShelterBy);
            }
            w.Write((byte)s.Incidents.Count);
            foreach (ExpeditionIncident incident in s.Incidents)
            {
                w.Write((byte)incident.Kind);
                w.Write(incident.Player.Value);
                w.Write(incident.AtSeconds);
                W.WriteKey(w, incident.SuitKey);
                w.Write(incident.Service.HasValue);
                if (incident.Service.HasValue) w.Write((byte)incident.Service.Value);
            }
            w.Write(s.Disclosure != null);
            if (s.Disclosure != null)
            {
                W.WriteKey(w, s.Disclosure.EpisodeId);
                w.Write(s.Disclosure.Synthetic);
                WriteLongText(w, s.Disclosure.Provenance);
                w.Write(s.Disclosure.EventOnsetSeconds);
                w.Write(s.Disclosure.EventEndSeconds);
            }
        }

        internal static ExpeditionSessionSnapshot ReadExtension(BinaryReader r, OutpostSessionSnapshot outpost)
        {
            int crewCount = Count(r, OutpostLimits.MaxCrew);
            var crew = new List<ExpeditionCrewState>(crewCount);
            for (int k = 0; k < crewCount; k++)
            {
                var player = new PlayerId(r.ReadInt32());
                var zone = (CrewZone)r.ReadByte();
                long zoneSequence = r.ReadInt64();
                int suit = r.ReadSByte();
                bool supply = W.ReadBool(r);
                var activity = (SuitActivity)r.ReadByte();
                var protection = (ProtectionState)r.ReadByte();
                crew.Add(new ExpeditionCrewState(player, zone, zoneSequence, suit, supply, activity, ReadMargin(r), protection));
            }
            int suitCount = Count(r, ExpeditionLimits.MaxSuits);
            var suits = new List<SuitState>(suitCount);
            for (int k = 0; k < suitCount; k++)
            {
                string key = Key(r);
                var status = (SuitStatus)r.ReadByte();
                int wearer = r.ReadInt32();
                var completed = (ChecklistSteps)r.ReadByte();
                var levels = new SuitLevels(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
                bool internalSupply = W.ReadBool(r);
                var reading = new SuitReading(k, key, levels, internalSupply, Services(r.ReadByte()), Services(r.ReadByte()));
                suits.Add(new SuitState(k, key, status, wearer == 0 ? PlayerId.None : new PlayerId(wearer), completed, reading));
            }
            var airlockState = (AirlockState)r.ReadByte();
            long cycle = r.ReadInt64();
            double work = r.ReadDouble(), duration = r.ReadDouble();
            bool stalled = W.ReadBool(r);
            int requester = r.ReadInt32();
            int occupantCount = Count(r, OutpostLimits.MaxCrew);
            var occupants = new List<PlayerId>(occupantCount);
            for (int k = 0; k < occupantCount; k++) occupants.Add(new PlayerId(r.ReadInt32()));
            var airlock = new AirlockStatus(airlockState, cycle, work, duration, stalled, occupants,
                requester == 0 ? PlayerId.None : new PlayerId(requester));
            var shelter = new ShelterStatus(W.ReadBool(r), W.ReadBool(r), W.ReadBool(r), r.ReadByte());
            var supplyReading = new EvaSupplyReading(r.ReadDouble(), r.ReadDouble());
            int advisoryCount = Count(r, ExpeditionLimits.MaxAdvisories);
            var advisories = new List<IssuedAdvisory>(advisoryCount);
            for (int k = 0; k < advisoryCount; k++)
            {
                string id = Key(r);
                var kind = (AdvisoryKind)r.ReadByte();
                double issued = r.ReadDouble();
                double? shelterBy = ReadOptional(r);
                double validUntil = r.ReadDouble();
                string supersedes = W.ReadKey(r);
                string text = Key(r);
                var validity = (AdvisoryValidity)r.ReadByte();
                advisories.Add(new IssuedAdvisory(new AdvisoryMessage(id, kind, issued, shelterBy, validUntil, supersedes, text), validity));
            }
            int receptionCount = Count(r, ExpeditionLimits.MaxReceptions);
            var receptions = new List<AdvisoryReception>(receptionCount);
            for (int k = 0; k < receptionCount; k++)
                receptions.Add(new AdvisoryReception(Key(r), new PlayerId(r.ReadInt32()), r.ReadDouble(), W.ReadBool(r)));
            var instructionState = (InstructionState)r.ReadByte();
            string latest = W.ReadKey(r);
            var instruction = new ShelterInstructionStatus(instructionState, latest, ReadOptional(r), ReadOptional(r), ReadOptional(r));
            var phase = (ExpeditionPhase)r.ReadByte();
            var outcome = (ExpeditionOutcome)r.ReadByte();
            ReturnDecision decision = null;
            if (W.ReadBool(r))
            {
                var by = new PlayerId(r.ReadInt32());
                double at = r.ReadDouble();
                int knownCount = Count(r, ExpeditionLimits.MaxKnownAdvisories);
                var known = new List<string>(knownCount);
                for (int k = 0; k < knownCount; k++) known.Add(Key(r));
                decision = new ReturnDecision(by, at, known, ReadOptional(r));
            }
            int objectiveCount = Count(r, OutpostLimits.MaxObjectives);
            var objectives = new List<ExpeditionObjective>(objectiveCount);
            for (int k = 0; k < objectiveCount; k++)
                objectives.Add(new ExpeditionObjective(Key(r), (ObjectiveRole)r.ReadByte(), r.ReadInt32(), r.ReadInt32()));
            int assessmentCount = Count(r, ExpeditionLimits.MaxAssessments);
            var assessments = new List<ProtectionAssessment>(assessmentCount);
            for (int k = 0; k < assessmentCount; k++)
                assessments.Add(new ProtectionAssessment(new PlayerId(r.ReadInt32()), (ProtectionCategory)r.ReadByte(),
                    ReadOptional(r), ReadOptional(r), W.ReadBool(r)));
            int incidentCount = Count(r, ExpeditionLimits.MaxIncidents);
            var incidents = new List<ExpeditionIncident>(incidentCount);
            for (int k = 0; k < incidentCount; k++)
            {
                var kind = (IncidentKind)r.ReadByte();
                var player = new PlayerId(r.ReadInt32());
                double at = r.ReadDouble();
                string suitKey = W.ReadKey(r);
                SuitService? service = W.ReadBool(r) ? (SuitService?)(SuitService)r.ReadByte() : null;
                incidents.Add(new ExpeditionIncident(kind, player, at, suitKey, service));
            }
            EventDisclosure disclosure = null;
            if (W.ReadBool(r))
                disclosure = new EventDisclosure(Key(r), W.ReadBool(r), ReadLongText(r), r.ReadDouble(), r.ReadDouble());
            return new ExpeditionSessionSnapshot(outpost, crew, suits, airlock, shelter, supplyReading, advisories, receptions,
                instruction, phase, decision, outcome, objectives, assessments, incidents, disclosure);
        }

        private static void WriteMargin(BinaryWriter w, ReturnMarginEstimate m)
        {
            w.Write((byte)m.Status);
            w.Write(m.ComputedAtSeconds);
            if (m.Status == MarginStatus.NotApplicable) return;
            w.Write(m.MarginSeconds);
            w.Write((byte)m.LimitingService);
            foreach (ServiceMargin s in m.Services)
            {
                w.Write(s.Usable);
                w.Write(s.RequiredForReturn);
                w.Write(s.CurrentRate);
                w.Write(s.MarginSeconds);
            }
            w.Write(m.Plan.PathMeters);
            w.Write(m.Plan.WalkSeconds);
            w.Write(m.Plan.AirlockWaitSeconds);
            w.Write(m.Plan.IngressSeconds);
            w.Write(m.Plan.Reachable);
        }

        private static ReturnMarginEstimate ReadMargin(BinaryReader r)
        {
            var status = (MarginStatus)r.ReadByte();
            double at = r.ReadDouble();
            if (status == MarginStatus.NotApplicable) return ReturnMarginEstimate.NotApplicable(at);
            double margin = r.ReadDouble();
            var limiting = (SuitService)r.ReadByte();
            var services = new ServiceMargin[3];
            for (int k = 0; k < services.Length; k++)
                services[k] = new ServiceMargin((SuitService)k, r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            var plan = new ReturnPlan(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), W.ReadBool(r));
            return new ReturnMarginEstimate(status, at, margin, limiting, services, plan);
        }

        private static byte Mask(IReadOnlyList<SuitService> services)
        {
            int mask = 0;
            foreach (SuitService service in services) mask |= 1 << (int)service;
            return (byte)mask;
        }

        private static List<SuitService> Services(byte mask)
        {
            if (mask > 7) throw new InvalidDataException();
            var list = new List<SuitService>();
            for (int k = 0; k < 3; k++)
                if ((mask & (1 << k)) != 0) list.Add((SuitService)k);
            return list;
        }

        internal static void WriteOptional(BinaryWriter w, double? value)
        {
            w.Write(value.HasValue);
            if (value.HasValue) w.Write(value.Value);
        }

        internal static double? ReadOptional(BinaryReader r) => W.ReadBool(r) ? r.ReadDouble() : (double?)null;

        internal static int Count(BinaryReader r, int max)
        {
            int count = r.ReadByte();
            if (count > max) throw new InvalidDataException();
            return count;
        }

        internal static string Key(BinaryReader r) => W.ReadKey(r) ?? throw new InvalidDataException();

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static void WriteLongText(BinaryWriter w, string text)
        {
            byte[] bytes = W.Utf8Cut(text, MaxLongTextBytes, out _);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadLongText(BinaryReader r)
        {
            int length = r.ReadUInt16();
            if (length > MaxLongTextBytes) throw new InvalidDataException();
            byte[] bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return StrictUtf8.GetString(bytes);
        }
    }
}
