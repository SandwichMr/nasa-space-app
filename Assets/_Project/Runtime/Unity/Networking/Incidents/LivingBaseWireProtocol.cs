using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Outpost;
using W = Trainer.Unity.Networking.Outpost.OutpostWireProtocol;

namespace Trainer.Unity.Networking.Incidents
{
    internal struct LivingBaseClientMessage
    {
        public OutpostMessageKind Kind;
        public ExpeditionCommand Command;
        public BaseOperationCommand Operation;
        public IncidentCommand Incident;
        public MovementInput Movement;
    }

    internal struct LivingBaseHostMessage
    {
        public OutpostMessageKind Kind;
        public CommandResult Result;
        public LivingBaseSnapshot Snapshot;
        public OutpostMembership Membership;
    }

    /// <summary>
    /// Phase 5 binary wire format ('T' 'L', version 1, little-endian) on the same Unity Transport/WebSocket links (D3).
    /// <list type="bullet">
    /// <item>Kinds 1–4 and Result/Membership carry EXACTLY the phase 4 'T' 'B' bodies: they are produced and checked by the
    /// unchanged phase 4 codec, only the second header byte differs. Kind <see cref="Incident"/> (5) carries an
    /// <see cref="IncidentCommand"/> (id, epoch, action, station, assignment, observation, zone): no sender, position, stock,
    /// observation content or result.</item>
    /// <item>A snapshot body is the complete phase 4 snapshot message (length-prefixed, decoded by the phase 4 codec) followed by
    /// the phase 5 extension: setup, data status, director, incidents, power, space-weather status (only what the authority
    /// published: issued messages and the records they quote, never future peaks or ends), assignments, credits, upgrade,
    /// operations, feedback and decisions. Always the FULL state, never a delta.</item>
    /// <item>Framing: the phase 2 chunk layout under this header with its own bound <see cref="MaxMessageBytes"/>.</item>
    /// <item>Decoding is strict: wrong header or version, unknown kinds, short or trailing bytes, counts above the contract limits
    /// and anything the contract constructors refuse is rejected as a network code; nothing partial is published.</item>
    /// </list>
    /// The 'T' 'S', 'T' 'O', 'T' 'E' and 'T' 'B' cables are untouched; each decoder rejects the others as BadMagic.
    /// </summary>
    internal static class LivingBaseWireProtocol
    {
        internal const byte Magic1 = 0x4C; // 'L'
        internal const byte Version = 1;
        internal const OutpostMessageKind Incident = (OutpostMessageKind)5;
        internal const int MaxMessageBytes = 64 * 1024;
        internal const int MaxClientMessageBytes = W.MaxClientMessageBytes;
        private const int MaxTextBytes = 1200;
        private const ushort NullText = 0xFFFF;
        private const int ChunkHeaderBytes = W.HeaderBytes + 4;

        // ---- Header re-labelling of phase 4 messages -------------------------------------------------------

        private static byte[] Relabel(byte[] message, byte from, byte to)
        {
            if (message == null || message.Length < W.HeaderBytes || message[0] != W.Magic0 || message[1] != from) return message;
            var copy = (byte[])message.Clone();
            copy[1] = to;
            return copy;
        }

        private static byte[] FromPhase4(byte[] message) => Relabel(message, BaseOperationsWireProtocol.Magic1, Magic1);
        private static byte[] ToPhase4(byte[] message) => Relabel(message, Magic1, BaseOperationsWireProtocol.Magic1);

        // ---- Client -> host ---------------------------------------------------------------------------------

        internal static byte[] EncodeJoin() => FromPhase4(BaseOperationsWireProtocol.EncodeJoin());
        internal static byte[] EncodeSubmit(ExpeditionCommand command) => FromPhase4(BaseOperationsWireProtocol.EncodeSubmit(command));
        internal static bool CanCarry(BaseOperationCommand command) => BaseOperationsWireProtocol.CanCarry(command);
        internal static byte[] EncodeOperation(BaseOperationCommand command) => FromPhase4(BaseOperationsWireProtocol.EncodeOperation(command));
        internal static byte[] EncodeMovement(MovementInput input) => FromPhase4(BaseOperationsWireProtocol.EncodeMovement(input));

        internal static byte[] EncodeIncident(IncidentCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Station.IsValid && !StableKey.IsValid(command.Station.Value)) throw new ArgumentException("Station must be a stable key.", nameof(command));
            return W.Encode(Incident, w =>
            {
                w.Write(command.Id.ToByteArray());
                w.Write(command.Epoch);
                w.Write((int)command.Action);
                W.WriteKey(w, command.Station.IsValid ? command.Station.Value : null);
                w.Write(command.AssignmentSequence);
                w.Write(command.ObservationSequence);
                w.Write(command.ZoneSequence);
            }, Magic1, Version);
        }

        internal static bool TryDecodeFromClient(byte[] payload, out LivingBaseClientMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > MaxClientMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!W.TryOpen(payload, out OutpostMessageKind kind, out error, Magic1, Version)) return false;
            if (kind != Incident)
            {
                if (!BaseOperationsWireProtocol.TryDecodeFromClient(ToPhase4(payload), out BaseOperationsClientMessage inner, out error)) return false;
                message = new LivingBaseClientMessage
                {
                    Kind = inner.Kind, Command = inner.Command, Operation = inner.Operation, Movement = inner.Movement
                };
                return true;
            }
            var decoded = new LivingBaseClientMessage { Kind = kind };
            bool ok = W.Read(payload, out error, r =>
            {
                Guid id = W.ReadGuid(r);
                long epoch = r.ReadInt64();
                var action = (IncidentAction)r.ReadInt32(); // unknown values reach the authority (UnknownAction)
                string station = W.ReadKey(r);
                long assignment = r.ReadInt64(), observation = r.ReadInt64(), zone = r.ReadInt64();
                decoded.Incident = new IncidentCommand(id, epoch, action, station == null ? StationId.None : new StationId(station),
                    assignment, observation, zone);
            });
            if (ok) message = decoded;
            return ok;
        }

        // ---- Host -> client ---------------------------------------------------------------------------------

        internal static byte[] EncodeResult(CommandResult result, out bool truncated) =>
            FromPhase4(BaseOperationsWireProtocol.EncodeResult(result, out truncated));

        internal static byte[] EncodeMembership(OutpostMembership membership) => FromPhase4(BaseOperationsWireProtocol.EncodeMembership(membership));

        internal static byte[] EncodeSnapshot(LivingBaseSnapshot s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            byte[] inner = BaseOperationsWireProtocol.EncodeSnapshot(s.BaseOperations);
            byte[] message = W.Encode(OutpostMessageKind.Snapshot, w =>
            {
                w.Write(inner.Length);
                w.Write(inner);
                WriteExtension(w, s);
            }, Magic1, Version);
            if (message.Length > MaxMessageBytes) throw new InvalidOperationException($"Snapshot of {message.Length} bytes exceeds the cable bound.");
            return message;
        }

        internal static bool TryDecodeFromHost(byte[] payload, out LivingBaseHostMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > MaxMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!W.TryOpen(payload, out OutpostMessageKind kind, out error, Magic1, Version)) return false;
            if (kind == OutpostMessageKind.Result || kind == OutpostMessageKind.Membership)
            {
                if (!BaseOperationsWireProtocol.TryDecodeFromHost(ToPhase4(payload), out BaseOperationsHostMessage inner, out error)) return false;
                message = new LivingBaseHostMessage { Kind = inner.Kind, Result = inner.Result, Membership = inner.Membership };
                return true;
            }
            if (kind != OutpostMessageKind.Snapshot)
                return W.Fail(kind == OutpostMessageKind.Join || kind == OutpostMessageKind.Submit || kind == OutpostMessageKind.Movement
                    || kind == BaseOperationsWireProtocol.Operation || kind == Incident || kind == OutpostMessageKind.Chunk
                    ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            LivingBaseSnapshot snapshot = null;
            string innerError = null;
            bool ok = W.Read(payload, out error, r =>
            {
                int length = r.ReadInt32();
                if (length < W.HeaderBytes || length > BaseOperationsWireProtocol.MaxMessageBytes) throw new InvalidDataException();
                byte[] inner = r.ReadBytes(length);
                if (inner.Length != length) throw new EndOfStreamException();
                if (!BaseOperationsWireProtocol.TryDecodeFromHost(inner, out BaseOperationsHostMessage phase4, out innerError)
                    || phase4.Kind != OutpostMessageKind.Snapshot)
                    throw new InvalidDataException();
                snapshot = ReadExtension(r, phase4.Snapshot);
            });
            if (!ok)
            {
                if (innerError != null) error = innerError;
                return false;
            }
            message = new LivingBaseHostMessage { Kind = kind, Snapshot = snapshot };
            return true;
        }

        // ---- Phase 5 extension ------------------------------------------------------------------------------

        private static void WriteExtension(BinaryWriter w, LivingBaseSnapshot s)
        {
            w.Write((byte)s.Setup.RequestedMode);
            w.Write(s.Setup.Seed);

            DataStatus d = s.Data;
            w.Write((byte)d.Requested);
            w.Write((byte)d.InUse);
            w.Write((byte)d.Availability);
            WriteManifest(w, d.Dataset);
            WriteText(w, d.ProblemKey);

            DirectorStatus dir = s.Director;
            w.Write((byte)dir.Phase);
            w.Write(dir.Seed);
            w.Write(dir.PrngVersion);
            w.Write(dir.Evaluations);
            WriteText(w, dir.ConfigurationHash);

            w.Write((byte)s.Incidents.Count);
            foreach (IncidentRecord i in s.Incidents)
            {
                w.Write(i.Id.ToByteArray());
                w.Write(i.Sequence);
                w.Write((byte)i.Kind);
                WriteText(w, i.Key);
                w.Write((byte)i.Phase);
                w.Write(i.AnnouncedAtSeconds);
                WriteOptional(w, i.ActiveAtSeconds);
                WriteOptional(w, i.ClosedAtSeconds);
                WriteText(w, i.ResultKey);
            }

            PowerEquipmentStatus p = s.Power;
            WriteText(w, p.Station.Value);
            w.Write(p.Isolated);
            w.Write(p.PartReplaced);
            w.Write((byte)p.Knowledge);
            w.Write(p.StateSequence);
            w.Write(p.Observation != null);
            if (p.Observation != null)
            {
                PowerObservation o = p.Observation;
                w.Write(o.Sequence);
                w.Write(o.ObservedBy.Value);
                w.Write(o.ObservedAtSeconds);
                w.Write(o.ValidUntilSeconds);
                w.Write(o.StateSequence);
                w.Write((int)o.Findings);
                w.Write(o.GenerationWatts);
                w.Write(o.MaxDischargeWatts);
                w.Write(o.Current);
            }
            w.Write(p.EffectiveGenerationWatts);
            w.Write(p.EffectiveMaxDischargeWatts);
            w.Write(p.AddedEssentialWatts);

            w.Write(s.SpaceWeather != null);
            if (s.SpaceWeather != null) WriteSpaceWeather(w, s.SpaceWeather);

            w.Write((byte)s.Assignments.Count);
            foreach (AssignmentRecord a in s.Assignments)
            {
                w.Write(a.Sequence);
                WriteText(w, a.TemplateKey);
                WriteText(w, a.ActivityKey);
                w.Write(a.Exterior);
                w.Write((byte)a.State);
                w.Write(a.OfferedAtSeconds);
                w.Write(a.DueAtSeconds);
                WriteOptional(w, a.AcceptedAtSeconds);
                WriteOptional(w, a.ClosedAtSeconds);
                w.Write(a.AcceptedBy.Value);
                w.Write(a.RewardCredits);
                w.Write(a.Rewarded);
            }
            w.Write(s.Credits);
            w.Write(s.CreditsEarned);

            UpgradeStatus u = s.Upgrade;
            WriteText(w, u.Key);
            w.Write(u.Installed);
            w.Write(u.CreditCost);
            WriteText(w, u.SpareKey);
            w.Write(u.RecoveryFraction);
            w.Write(u.AddedEssentialWatts);
            WriteOptional(w, u.InstalledAtSeconds);

            w.Write((byte)s.Operations.Count);
            foreach (IncidentOperationProgress op in s.Operations)
            {
                w.Write(op.CommandId.ToByteArray());
                WriteText(w, op.Station.Value);
                w.Write((byte)op.Action);
                w.Write(op.Performer.Value);
                w.Write(op.WorkSeconds);
                w.Write(op.DurationSeconds);
            }
            w.Write((byte)s.Feedback.Count);
            foreach (IncidentFeedback f in s.Feedback)
            {
                w.Write(f.CommandId.ToByteArray());
                w.Write(f.Performer.Value);
                w.Write((byte)f.Action);
                w.Write(f.AtSeconds);
                w.Write((byte)f.Assessment);
                WriteText(w, f.ExplanationKey);
            }
            w.Write((byte)s.Decisions.Count);
            foreach (DirectorDecisionRecord dd in s.Decisions)
            {
                w.Write(dd.Sequence);
                w.Write(dd.AtSeconds);
                WriteText(w, dd.CandidateKey);
                w.Write(dd.Accepted);
                WriteText(w, dd.ReasonKey);
            }
        }

        private static void WriteSpaceWeather(BinaryWriter w, SpaceWeatherStatus s)
        {
            w.Write(s.IncidentId.ToByteArray());
            WriteText(w, s.EpisodeKey);
            w.Write((byte)s.Mode);
            WriteText(w, s.Label);
            WriteText(w, s.TimelineNote);
            WriteManifest(w, s.Dataset);
            w.Write((byte)s.VisibleRecords.Count);
            foreach (SourceRecord r in s.VisibleRecords)
            {
                WriteText(w, r.Provider);
                w.Write((byte)r.Kind);
                WriteText(w, r.SourceId);
                w.Write(r.Version.HasValue);
                if (r.Version.HasValue) w.Write(r.Version.Value);
                WriteText(w, r.Uri);
                WriteText(w, r.EventTimeUtc);
                WriteText(w, r.IssuedUtc);
                WriteText(w, r.RetrievedUtc);
                WriteText(w, r.Summary);
                w.Write((byte)r.Measurements.Count);
                foreach (SourceMeasurement m in r.Measurements)
                {
                    WriteText(w, m.Quantity);
                    WriteOptional(w, m.Value);
                    WriteText(w, m.Unit);
                    WriteText(w, m.Location);
                    w.Write((byte)m.Classification);
                }
            }
            w.Write((byte)s.Issued.Count);
            foreach (EpisodeMessageView v in s.Issued)
            {
                EpisodeMessage m = v.Message;
                WriteText(w, m.Id);
                w.Write((byte)m.Kind);
                w.Write((byte)m.Basis);
                w.Write(m.OffsetSeconds);
                w.Write(m.ValidForSeconds);
                WriteOptional(w, m.ShelterByOffsetSeconds);
                WriteText(w, m.Supersedes);
                WriteText(w, m.TextKey);
                w.Write((byte)m.SourceRecordIds.Count);
                foreach (string id in m.SourceRecordIds) WriteText(w, id);
                WriteText(w, m.SourceIssuedUtc);
                w.Write(v.IssuedAtSeconds);
                w.Write(v.ValidUntilSeconds);
                WriteOptional(w, v.ShelterBySeconds);
                w.Write((byte)v.Validity);
            }
            w.Write((byte)s.Receptions.Count);
            foreach (AdvisoryReception r in s.Receptions)
            {
                WriteText(w, r.MessageId);
                w.Write(r.Player.Value);
                w.Write(r.ReceivedAtSeconds);
                w.Write(r.AfterShelterBy);
            }
            ShelterInstructionStatus i = s.Instruction;
            w.Write((byte)i.State);
            WriteText(w, i.LatestMessageId);
            WriteOptional(w, i.ShelterBySeconds);
            WriteOptional(w, i.ValidUntilSeconds);
            WriteOptional(w, i.EndedAtSeconds);
            w.Write((byte)s.Members.Count);
            foreach (MemberProtection m in s.Members)
            {
                w.Write(m.Player.Value);
                w.Write((byte)m.State);
                w.Write(m.ReceivedWatch);
                w.Write(m.ReceivedInstruction);
            }
            w.Write((byte)s.Assessments.Count);
            foreach (ProtectionAssessment a in s.Assessments)
            {
                w.Write(a.Player.Value);
                w.Write((byte)a.Category);
                WriteOptional(w, a.ReceivedAtSeconds);
                WriteOptional(w, a.FirstProtectedAtSeconds);
                w.Write(a.ReceivedAfterShelterBy);
            }
        }

        private static LivingBaseSnapshot ReadExtension(BinaryReader r, BaseOperationsSnapshot phase4)
        {
            var setup = new LivingBaseSetup((DataMode)r.ReadByte(), r.ReadUInt64());

            var requested = (DataMode)r.ReadByte();
            var inUse = (DataMode)r.ReadByte();
            var availability = (DataAvailability)r.ReadByte();
            DatasetManifest dataset = ReadManifest(r);
            var data = new DataStatus(requested, inUse, availability, dataset, ReadText(r));

            var directorPhase = (DirectorPhase)r.ReadByte();
            ulong seed = r.ReadUInt64();
            int prng = r.ReadInt32();
            long evaluations = r.ReadInt64();
            var director = new DirectorStatus(directorPhase, seed, prng, evaluations, ReadText(r));

            int count = Count(r, IncidentLimits.MaxIncidents);
            var incidents = new List<IncidentRecord>(count);
            for (int k = 0; k < count; k++)
            {
                Guid id = W.ReadGuid(r);
                long sequence = r.ReadInt64();
                var kind = (IncidentType)r.ReadByte();
                string key = ReadText(r);
                var phase = (IncidentPhase)r.ReadByte();
                double announced = r.ReadDouble();
                double? active = ReadOptional(r), closed = ReadOptional(r);
                incidents.Add(new IncidentRecord(id, sequence, kind, key, phase, announced, active, closed, ReadText(r)));
            }

            var station = new StationId(Required(r));
            bool isolated = W.ReadBool(r), replaced = W.ReadBool(r);
            var knowledge = (EquipmentKnowledge)r.ReadByte();
            long stateSequence = r.ReadInt64();
            PowerObservation observation = null;
            if (W.ReadBool(r))
            {
                long sequence = r.ReadInt64();
                var by = new PlayerId(r.ReadInt32());
                double at = r.ReadDouble(), valid = r.ReadDouble();
                long observedState = r.ReadInt64();
                var findings = (PowerFindings)r.ReadInt32();
                double generation = r.ReadDouble(), discharge = r.ReadDouble();
                observation = new PowerObservation(sequence, by, at, valid, observedState, findings, generation, discharge, W.ReadBool(r));
            }
            var power = new PowerEquipmentStatus(station, isolated, replaced, knowledge, stateSequence, observation, r.ReadDouble(), r.ReadDouble(),
                r.ReadDouble());

            SpaceWeatherStatus weather = W.ReadBool(r) ? ReadSpaceWeather(r) : null;

            count = Count(r, IncidentLimits.MaxAssignments);
            var assignments = new List<AssignmentRecord>(count);
            for (int k = 0; k < count; k++)
            {
                long sequence = r.ReadInt64();
                string template = ReadText(r), activity = ReadText(r);
                bool exterior = W.ReadBool(r);
                var state = (AssignmentState)r.ReadByte();
                double offered = r.ReadDouble(), due = r.ReadDouble();
                double? accepted = ReadOptional(r), closed = ReadOptional(r);
                int byValue = r.ReadInt32();
                PlayerId by = byValue == 0 ? PlayerId.None : new PlayerId(byValue); // an offered assignment has no acceptor
                int reward = r.ReadInt32();
                assignments.Add(new AssignmentRecord(sequence, template, activity, exterior, state, offered, due, accepted, closed, by, reward, W.ReadBool(r)));
            }
            int credits = r.ReadInt32(), earned = r.ReadInt32();

            string upgradeKey = ReadText(r);
            bool installed = W.ReadBool(r);
            int cost = r.ReadInt32();
            string spare = ReadText(r);
            double recovery = r.ReadDouble(), added = r.ReadDouble();
            var upgrade = new UpgradeStatus(upgradeKey, installed, cost, spare, recovery, added, ReadOptional(r));

            count = Count(r, IncidentLimits.MaxOperations);
            var operations = new List<IncidentOperationProgress>(count);
            for (int k = 0; k < count; k++)
            {
                Guid id = W.ReadGuid(r);
                var at = new StationId(Required(r));
                var action = (IncidentAction)r.ReadByte();
                var performer = new PlayerId(r.ReadInt32());
                operations.Add(new IncidentOperationProgress(id, at, action, performer, r.ReadDouble(), r.ReadDouble()));
            }
            count = Count(r, IncidentLimits.MaxFeedback);
            var feedback = new List<IncidentFeedback>(count);
            for (int k = 0; k < count; k++)
            {
                Guid id = W.ReadGuid(r);
                var performer = new PlayerId(r.ReadInt32());
                var action = (IncidentAction)r.ReadByte();
                double at = r.ReadDouble();
                var assessment = (DecisionAssessment)r.ReadByte();
                feedback.Add(new IncidentFeedback(id, performer, action, at, assessment, ReadText(r)));
            }
            count = Count(r, IncidentLimits.MaxDirectorDecisions);
            var decisions = new List<DirectorDecisionRecord>(count);
            for (int k = 0; k < count; k++)
            {
                long sequence = r.ReadInt64();
                double at = r.ReadDouble();
                string candidate = ReadText(r);
                bool accepted = W.ReadBool(r);
                decisions.Add(new DirectorDecisionRecord(sequence, at, candidate, accepted, ReadText(r)));
            }
            return new LivingBaseSnapshot(phase4, setup, data, director, incidents, power, weather, assignments, credits, earned, upgrade,
                operations, feedback, decisions);
        }

        private static SpaceWeatherStatus ReadSpaceWeather(BinaryReader r)
        {
            Guid incident = W.ReadGuid(r);
            string episodeKey = ReadText(r);
            var mode = (DataMode)r.ReadByte();
            string label = ReadText(r), note = ReadText(r);
            DatasetManifest dataset = ReadManifest(r);
            int count = Count(r, IncidentLimits.MaxSourceRecords);
            var records = new List<SourceRecord>(count);
            for (int k = 0; k < count; k++)
            {
                string provider = ReadText(r);
                var kind = (SourceRecordKind)r.ReadByte();
                string sourceId = ReadText(r);
                int? version = W.ReadBool(r) ? r.ReadInt32() : (int?)null;
                string uri = ReadText(r), eventTime = ReadText(r), issued = ReadText(r), retrieved = ReadText(r), summary = ReadText(r);
                int mc = Count(r, IncidentLimits.MaxMeasurements);
                var measurements = new List<SourceMeasurement>(mc);
                for (int j = 0; j < mc; j++)
                {
                    string quantity = ReadText(r);
                    double? value = ReadOptional(r);
                    string unit = ReadText(r), location = ReadText(r);
                    measurements.Add(new SourceMeasurement(quantity, value, unit, location, (ValueClassification)r.ReadByte()));
                }
                records.Add(new SourceRecord(provider, kind, sourceId, version, uri, eventTime, issued, retrieved, summary, measurements));
            }
            count = Count(r, IncidentLimits.MaxEpisodeMessages);
            var issuedMessages = new List<EpisodeMessageView>(count);
            for (int k = 0; k < count; k++)
            {
                string id = ReadText(r);
                var kind = (EpisodeMessageKind)r.ReadByte();
                var basis = (MessageBasis)r.ReadByte();
                double offset = r.ReadDouble(), validFor = r.ReadDouble();
                double? shelterOffset = ReadOptional(r);
                string supersedes = ReadText(r), textKey = ReadText(r);
                int sc = Count(r, IncidentLimits.MaxSourceRecords);
                var sources = new List<string>(sc);
                for (int j = 0; j < sc; j++) sources.Add(Required(r));
                string sourceIssued = ReadText(r);
                var message = new EpisodeMessage(id, kind, basis, offset, validFor, shelterOffset, supersedes, textKey, sources, sourceIssued);
                double issuedAt = r.ReadDouble(), validUntil = r.ReadDouble();
                double? shelterBy = ReadOptional(r);
                issuedMessages.Add(new EpisodeMessageView(message, issuedAt, validUntil, shelterBy, (AdvisoryValidity)r.ReadByte()));
            }
            count = Count(r, IncidentLimits.MaxReceptions);
            var receptions = new List<AdvisoryReception>(count);
            for (int k = 0; k < count; k++)
                receptions.Add(new AdvisoryReception(Required(r), new PlayerId(r.ReadInt32()), r.ReadDouble(), W.ReadBool(r)));
            var state = (InstructionState)r.ReadByte();
            string latest = ReadText(r);
            var instruction = new ShelterInstructionStatus(state, latest, ReadOptional(r), ReadOptional(r), ReadOptional(r));
            count = Count(r, OutpostLimits.MaxCrew);
            var members = new List<MemberProtection>(count);
            for (int k = 0; k < count; k++)
                members.Add(new MemberProtection(new PlayerId(r.ReadInt32()), (MemberShelterState)r.ReadByte(), W.ReadBool(r), W.ReadBool(r)));
            count = Count(r, IncidentLimits.MaxAssessments);
            var assessments = new List<ProtectionAssessment>(count);
            for (int k = 0; k < count; k++)
                assessments.Add(new ProtectionAssessment(new PlayerId(r.ReadInt32()), (ProtectionCategory)r.ReadByte(), ReadOptional(r), ReadOptional(r),
                    W.ReadBool(r)));
            return new SpaceWeatherStatus(incident, episodeKey, mode, label, note, dataset, records, issuedMessages, receptions, instruction, members,
                assessments);
        }

        private static void WriteManifest(BinaryWriter w, DatasetManifest m)
        {
            w.Write(m != null);
            if (m == null) return;
            WriteText(w, m.DatasetId);
            w.Write((byte)m.Mode);
            w.Write(m.SchemaVersion);
            WriteText(w, m.Sha256);
            WriteText(w, m.RetrievedUtc);
            WriteText(w, m.License);
        }

        private static DatasetManifest ReadManifest(BinaryReader r)
        {
            if (!W.ReadBool(r)) return null;
            string id = ReadText(r);
            var mode = (DataMode)r.ReadByte();
            int schema = r.ReadInt32();
            return new DatasetManifest(id, mode, schema, ReadText(r), ReadText(r), ReadText(r));
        }

        // ---- Primitive helpers ------------------------------------------------------------------------------

        /// <summary>Nullable UTF-8 text: ushort length (0xFFFF = null) + bytes, at most <see cref="MaxTextBytes"/>.</summary>
        private static void WriteText(BinaryWriter w, string text)
        {
            if (text == null)
            {
                w.Write(NullText);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > MaxTextBytes) throw new InvalidOperationException("Text exceeds the cable bound.");
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadText(BinaryReader r)
        {
            ushort length = r.ReadUInt16();
            if (length == NullText) return null;
            if (length > MaxTextBytes) throw new InvalidDataException();
            byte[] bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        private static string Required(BinaryReader r) => ReadText(r) ?? throw new InvalidDataException();

        private static void WriteOptional(BinaryWriter w, double? value)
        {
            w.Write(value.HasValue);
            if (value.HasValue) w.Write(value.Value);
        }

        private static double? ReadOptional(BinaryReader r) => W.ReadBool(r) ? r.ReadDouble() : (double?)null;

        private static int Count(BinaryReader r, int max)
        {
            int count = r.ReadByte();
            if (count > max) throw new InvalidDataException();
            return count;
        }

        // ---- Framing (phase 2 chunk layout under 'T' 'L', own bound) ---------------------------------------

        internal static List<byte[]> Frame(byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Length > MaxMessageBytes) throw new ArgumentException("Message too large.", nameof(message));
            var frames = new List<byte[]>();
            if (message.Length <= W.MaxFrameBytes)
            {
                frames.Add(message);
                return frames;
            }
            int body = W.MaxFrameBytes - ChunkHeaderBytes;
            int count = (message.Length + body - 1) / body;
            for (int i = 0; i < count; i++)
            {
                int offset = i * body, length = Math.Min(body, message.Length - offset);
                var frame = new byte[ChunkHeaderBytes + length];
                frame[0] = W.Magic0;
                frame[1] = Magic1;
                frame[2] = Version;
                frame[3] = (byte)OutpostMessageKind.Chunk;
                frame[4] = (byte)i;
                frame[5] = (byte)(i >> 8);
                frame[6] = (byte)count;
                frame[7] = (byte)(count >> 8);
                Buffer.BlockCopy(message, offset, frame, ChunkHeaderBytes, length);
                frames.Add(frame);
            }
            return frames;
        }

        /// <summary>Client-side reassembly of ONE link. A gap, repeat, mixed message or oversize drops the partial message.</summary>
        internal sealed class Assembler
        {
            private MemoryStream partial;
            private int expected, count;

            internal bool TryAccept(byte[] frame, out byte[] message, out string error)
            {
                message = null;
                if (!W.TryOpen(frame, out OutpostMessageKind kind, out error, Magic1, Version)) return Reset(false);
                if (kind != OutpostMessageKind.Chunk)
                {
                    if (partial != null) return Reset(W.Fail(SessionNetworkCodes.MalformedPayload, out error));
                    message = frame;
                    return true;
                }
                if (frame.Length <= ChunkHeaderBytes) return Reset(W.Fail(SessionNetworkCodes.MalformedPayload, out error));
                int index = frame[4] | (frame[5] << 8), total = frame[6] | (frame[7] << 8);
                if (total < 2 || index >= total || (partial == null ? index != 0 : index != expected || total != count))
                    return Reset(W.Fail(SessionNetworkCodes.MalformedPayload, out error));
                if (partial == null)
                {
                    partial = new MemoryStream();
                    count = total;
                }
                partial.Write(frame, ChunkHeaderBytes, frame.Length - ChunkHeaderBytes);
                if (partial.Length > MaxMessageBytes) return Reset(W.Fail(SessionNetworkCodes.PayloadTooLarge, out error));
                expected = index + 1;
                if (expected < count) return false;
                message = partial.ToArray();
                partial = null;
                if (message.Length >= W.HeaderBytes && message[3] == (byte)OutpostMessageKind.Chunk)
                {
                    message = null;
                    return W.Fail(SessionNetworkCodes.MalformedPayload, out error);
                }
                return true;
            }

            internal void Clear() => partial = null;

            private bool Reset(bool value)
            {
                partial = null;
                return value;
            }
        }
    }
}
