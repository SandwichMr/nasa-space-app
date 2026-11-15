using System;
using System.Collections.Generic;
using System.IO;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking.Outpost;
using E = Trainer.Unity.Networking.Expedition.ExpeditionWireProtocol;
using W = Trainer.Unity.Networking.Outpost.OutpostWireProtocol;

namespace Trainer.Unity.Networking.BaseOperations
{
    internal struct BaseOperationsClientMessage
    {
        public OutpostMessageKind Kind;
        public ExpeditionCommand Command;
        public BaseOperationCommand Operation;
        public MovementInput Movement;
    }

    internal struct BaseOperationsHostMessage
    {
        public OutpostMessageKind Kind;
        public CommandResult Result;
        public BaseOperationsSnapshot Snapshot;
        public OutpostMembership Membership;
    }

    /// <summary>
    /// Phase 4 binary wire format ('T' 'B', version 1, little-endian) on the same Unity Transport/WebSocket links.
    /// <list type="bullet">
    /// <item>Message kinds of phases 2–3 with the same bodies (Join 1, Submit 2 = phase 3 <see cref="ExpeditionCommand"/>,
    /// Movement 3, Result 10, Snapshot 11, Membership 12, Chunk 13) plus <see cref="Operation"/> (4) for a
    /// <see cref="BaseOperationCommand"/>. Commands carry no sender identity, position, stock or result.</item>
    /// <item>A snapshot is the unchanged phase 2 body, then the unchanged phase 3 extension (the 'T' 'E' encoders), then the
    /// phase 4 extension. Always the FULL state, never a delta.</item>
    /// <item>Framing: the phase 2 chunk layout (header + index + count, frames ≤ <see cref="OutpostWireProtocol.MaxFrameBytes"/>)
    /// under this header, with its own reassembly bound <see cref="MaxMessageBytes"/>, because a worst-case phase 4 snapshot
    /// (~22 KB, see the handoff) does not fit the 16 KB bound of the older cables. Only the host sends chunks.</item>
    /// <item>Decoding is strict: wrong header or version, unknown or unexpected kinds, short or trailing bytes, counts above the
    /// contract limits, unknown flag bits and anything the contract constructors refuse (NaN, ranges, bands, invariants) is
    /// rejected as a network code; nothing partial is ever published.</item>
    /// </list>
    /// The 'T' 'S', 'T' 'O' and 'T' 'E' cables are untouched; each decoder rejects the others as BadMagic.
    /// </summary>
    internal static class BaseOperationsWireProtocol
    {
        internal const byte Magic1 = 0x42; // 'B'
        internal const byte Version = 1;
        /// <summary>Client → host phase 4 station operation (a kind number unused by the older cables).</summary>
        internal const OutpostMessageKind Operation = (OutpostMessageKind)4;
        /// <summary>Largest reassembled host message. The analytic worst case of a valid snapshot is ~22 KB.</summary>
        internal const int MaxMessageBytes = 32 * 1024;
        internal const int MaxClientMessageBytes = W.MaxClientMessageBytes;
        private const int ChunkHeaderBytes = W.HeaderBytes + 4;
        private const Findings AllFindings = Findings.ProductOffSpec | Findings.ProcessorFilterRestricted | Findings.ProcessorFailed
            | Findings.ActiveBedSaturated | Findings.StandbyBedLoaded | Findings.InletFlowRestricted;
        private const RepairSteps AllSteps = RepairSteps.Inspect | RepairSteps.Isolate | RepairSteps.Intervene | RepairSteps.Test
            | RepairSteps.Restore;

        /// <summary>
        /// Base defect (common base 9b96f80, reported to D2 for a coordinated base): the constructors of
        /// <see cref="ActivityRecord"/> and <see cref="BaseSessionStatus"/> validate their end time but never store it, so the
        /// authority publishes every closed activity and every ended session with <c>EndedAtSeconds == null</c>, which the same
        /// constructors refuse when the decoder rebuilds them. While the defect holds (probed here, not assumed), a missing end
        /// time is filled with a placeholder that the constructor validates and drops, so the client object equals the
        /// authority's. Once the base stores the value, these probes turn false and a missing end time is malformed again.
        /// </summary>
        internal static readonly bool ActivityEndTimeDropped =
            new ActivityRecord(1, ActivityKind.Repair, "probe", ActivityStatus.Completed, PlayerId.None, 0, 1, null).EndedAtSeconds == null;
        internal static readonly bool SessionEndTimeDropped =
            new BaseSessionStatus(BaseSessionMode.Continuous, 1, 1, IrreversibleCause.SimulationFault, 1, 0, 0).EndedAtSeconds == null;

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

        /// <summary>True when the cable can carry <paramref name="command"/> unchanged: its manifest key is null or a stable
        /// key (every other field is representable). Malformed values otherwise travel as they are, for the authority to answer.</summary>
        internal static bool CanCarry(BaseOperationCommand command) =>
            command != null && (command.ManifestKey == null || StableKey.IsValid(command.ManifestKey));

        internal static byte[] EncodeOperation(BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!CanCarry(command)) throw new ArgumentException("A manifest key must be null or a stable key.", nameof(command));
            return Encode(Operation, w =>
            {
                w.Write(command.Id.ToByteArray());
                w.Write(command.Epoch);
                w.Write((int)command.Action);
                W.WriteKey(w, command.Station.IsValid ? command.Station.Value : null);
                w.Write((int)command.Choice);
                W.WriteKey(w, command.ManifestKey);
                w.Write(command.OrderSequence);
                w.Write(command.ObservationSequence);
                w.Write(command.ZoneSequence);
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

        internal static bool TryDecodeFromClient(byte[] payload, out BaseOperationsClientMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > MaxClientMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!W.TryOpen(payload, out OutpostMessageKind kind, out error, Magic1, Version)) return false;
            if (kind != OutpostMessageKind.Join && kind != OutpostMessageKind.Submit && kind != OutpostMessageKind.Movement && kind != Operation)
                return W.Fail(IsHostKind(kind) ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            var decoded = new BaseOperationsClientMessage { Kind = kind };
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
                else if (kind == Operation)
                {
                    Guid id = W.ReadGuid(r);
                    long epoch = r.ReadInt64();
                    var action = (BaseOperationAction)r.ReadInt32(); // unknown values reach the authority (ShapeError)
                    string station = W.ReadKey(r);
                    var choice = (OperationChoice)r.ReadInt32();
                    string manifest = W.ReadKey(r);
                    long order = r.ReadInt64(), observation = r.ReadInt64(), zone = r.ReadInt64();
                    decoded.Operation = new BaseOperationCommand(id, epoch, action, station == null ? StationId.None : new StationId(station),
                        choice, manifest, order, observation, zone);
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

        internal static byte[] EncodeSnapshot(BaseOperationsSnapshot s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            byte[] message = Encode(OutpostMessageKind.Snapshot, w =>
            {
                W.WriteSnapshotBody(w, s.Outpost);
                E.WriteExtension(w, s.Expedition);
                WriteExtension(w, s);
            });
            if (message.Length > MaxMessageBytes) throw new InvalidOperationException($"Snapshot of {message.Length} bytes exceeds the cable bound.");
            return message;
        }

        internal static bool TryDecodeFromHost(byte[] payload, out BaseOperationsHostMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > MaxMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!W.TryOpen(payload, out OutpostMessageKind kind, out error, Magic1, Version)) return false;
            if (kind != OutpostMessageKind.Result && kind != OutpostMessageKind.Snapshot && kind != OutpostMessageKind.Membership)
                return W.Fail(kind == OutpostMessageKind.Join || kind == OutpostMessageKind.Submit || kind == OutpostMessageKind.Movement
                    || kind == Operation || kind == OutpostMessageKind.Chunk
                    ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            var decoded = new BaseOperationsHostMessage { Kind = kind };
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
                    ExpeditionSessionSnapshot expedition = E.ReadExtension(r, outpost);
                    decoded.Snapshot = ReadExtension(r, expedition);
                }
            });
            if (ok) message = decoded;
            return ok;
        }

        private static bool IsHostKind(OutpostMessageKind kind) =>
            kind == OutpostMessageKind.Result || kind == OutpostMessageKind.Snapshot
            || kind == OutpostMessageKind.Membership || kind == OutpostMessageKind.Chunk;

        // ---- Framing (phase 2 chunk layout under 'T' 'B', own bound) ---------------------------------------

        /// <summary>Splits a host message into frames of at most <see cref="OutpostWireProtocol.MaxFrameBytes"/> (a message that
        /// fits is returned as is). Each frame is a new array, as the transport takes ownership.</summary>
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

        /// <summary>Client-side reassembly of ONE link (a new one per link). A gap, repeat, mixed message or oversize drops the
        /// partial message and reports a code; a complete message is returned only when every chunk arrived in order.</summary>
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

        // ---- Phase 4 extension --------------------------------------------------------------------------------

        private static void WriteExtension(BinaryWriter w, BaseOperationsSnapshot s)
        {
            BaseSessionStatus session = s.Session;
            w.Write((byte)session.Mode);
            w.Write(session.EssentialUnservedSeconds);
            w.Write(session.EssentialUnservedGraceSeconds);
            w.Write((byte)session.EndCause);
            E.WriteOptional(w, session.EndedAtSeconds);
            w.Write(session.ActivitySequence);
            w.Write(session.ExpeditionsFinished);

            w.Write((byte)s.Gauges.Count);
            foreach (ResourceGauge g in s.Gauges)
            {
                w.Write((byte)g.Kind);
                WriteReading(w, g.Reading);
                E.WriteOptional(w, g.Capacity);
                E.WriteOptional(w, g.TrendPerSecond);
                E.WriteOptional(w, g.ReserveAt);
                E.WriteOptional(w, g.LimitAt);
                w.Write((byte)g.Band);
            }

            w.Write((byte)s.Equipment.Count);
            foreach (EquipmentStatus e in s.Equipment)
            {
                w.Write((byte)e.Equipment);
                W.WriteKey(w, e.Station.Value);
                w.Write((byte)e.Operating);
                w.Write((byte)e.Knowledge);
                w.Write((byte)e.VisibleSymptoms);
                w.Write(e.StateSequence);
                w.Write(e.LatestObservation != null);
                if (e.LatestObservation != null) WriteObservation(w, e.LatestObservation);
                w.Write(e.Repair != null);
                if (e.Repair != null)
                {
                    RepairJob job = e.Repair;
                    w.Write((byte)job.Equipment);
                    w.Write((byte)job.Completed);
                    w.Write(job.StartedAtSeconds);
                    w.Write(job.ObservationSequence);
                    WriteOptionalBool(w, job.LastTestPassed);
                }
            }

            WaterTreatmentStatus water = s.Water;
            w.Write(water.HoldKilograms);
            WriteReading(w, water.ProductConductivity);
            w.Write(water.ProductSpecLimitMicrosiemens);
            w.Write((byte)water.PotableQuality);
            w.Write(water.SuspectKilograms);
            w.Write(water.BatchesProcessed);

            w.Write((byte)s.Spares.Count);
            foreach (SpareStock spare in s.Spares)
            {
                W.WriteKey(w, spare.Key);
                w.Write(spare.Count);
                w.Write(spare.Capacity);
            }

            w.Write((byte)s.Operations.Count);
            foreach (OperationProgress op in s.Operations)
            {
                w.Write(op.CommandId.ToByteArray());
                W.WriteKey(w, op.Station.Value);
                w.Write((byte)op.Action);
                w.Write((byte)op.Choice);
                w.Write(op.Performer.Value);
                w.Write(op.WorkSeconds);
                w.Write(op.DurationSeconds);
                w.Write(op.Stalled);
                W.WriteKey(w, op.ReservedSpare);
            }

            w.Write((byte)s.Orders.Count);
            foreach (ResupplyOrder order in s.Orders)
            {
                w.Write(order.Sequence);
                W.WriteKey(w, order.ManifestKey);
                w.Write(order.PlacedBy.Value);
                w.Write(order.PlacedAtSeconds);
                w.Write(order.CommitAtSeconds);
                w.Write(order.ArriveAtSeconds);
                w.Write((byte)order.State);
                w.Write((byte)order.Remaining.Count);
                foreach (ManifestItem item in order.Remaining)
                {
                    w.Write(item.IsSpare);
                    if (item.IsSpare)
                    {
                        W.WriteKey(w, item.SpareKey);
                        w.Write(item.Count);
                    }
                    else
                    {
                        w.Write((byte)item.Resource.Value);
                        w.Write(item.Kilograms);
                    }
                }
                E.WriteOptional(w, order.ClosedAtSeconds);
            }

            w.Write((byte)s.Activities.Count);
            foreach (ActivityRecord a in s.Activities)
            {
                w.Write(a.Sequence);
                w.Write((byte)a.Kind);
                W.WriteKey(w, a.Key);
                w.Write((byte)a.Status);
                w.Write(a.StartedBy.Value);
                w.Write(a.StartedAtSeconds);
                E.WriteOptional(w, a.EndedAtSeconds);
                W.WriteKey(w, a.ResultKey);
            }

            w.Write((byte)s.Decisions.Count);
            foreach (DecisionRecord d in s.Decisions)
            {
                w.Write(d.CommandId.ToByteArray());
                w.Write(d.Performer.Value);
                w.Write((byte)d.Action);
                w.Write((byte)d.Choice);
                w.Write((byte)d.Equipment);
                w.Write(d.AtSeconds);
                w.Write((byte)d.Assessment);
                W.WriteKey(w, d.ExplanationKey);
            }

            w.Write(s.LatestReport != null);
            if (s.LatestReport != null)
            {
                OperationsReport report = s.LatestReport;
                w.Write(report.Sequence);
                w.Write(report.FromSeconds);
                w.Write(report.ToSeconds);
                w.Write(report.ActivitiesCompleted);
                w.Write(report.ActivitiesCancelled);
                w.Write(report.PotableDeltaKilograms);
                w.Write(report.FoodDeltaKilograms);
                w.Write(report.EnergyDeltaJoules);
            }
        }

        private static BaseOperationsSnapshot ReadExtension(BinaryReader r, ExpeditionSessionSnapshot expedition)
        {
            var mode = (BaseSessionMode)r.ReadByte();
            double unserved = r.ReadDouble(), grace = r.ReadDouble();
            var cause = (IrreversibleCause)r.ReadByte();
            double? endedAt = E.ReadOptional(r);
            if (endedAt == null && cause != IrreversibleCause.None && SessionEndTimeDropped) endedAt = expedition.SimulatedTimeSeconds;
            long activitySequence = r.ReadInt64();
            int expeditionsFinished = r.ReadInt32();
            var session = new BaseSessionStatus(mode, unserved, grace, cause, endedAt, activitySequence, expeditionsFinished);

            int gaugeCount = E.Count(r, BaseOperationsLimits.MaxGauges);
            var gauges = new List<ResourceGauge>(gaugeCount);
            for (int k = 0; k < gaugeCount; k++)
            {
                var kind = (GaugeKind)r.ReadByte();
                InstrumentReading reading = ReadReading(r);
                double? capacity = E.ReadOptional(r), trend = E.ReadOptional(r), reserve = E.ReadOptional(r), limit = E.ReadOptional(r);
                gauges.Add(new ResourceGauge(kind, reading, capacity, trend, reserve, limit, (GaugeBand)r.ReadByte()));
            }

            int equipmentCount = E.Count(r, Enum.GetValues(typeof(EquipmentKind)).Length);
            var equipment = new List<EquipmentStatus>(equipmentCount);
            for (int k = 0; k < equipmentCount; k++)
            {
                var kind = (EquipmentKind)r.ReadByte();
                var station = new StationId(E.Key(r));
                var operating = (EquipmentOperatingState)r.ReadByte();
                var knowledge = (EquipmentKnowledge)r.ReadByte();
                Findings symptoms = ReadFindings(r);
                long stateSequence = r.ReadInt64();
                EquipmentObservation observation = W.ReadBool(r) ? ReadObservation(r) : null;
                RepairJob repair = null;
                if (W.ReadBool(r))
                {
                    var repairEquipment = (EquipmentKind)r.ReadByte();
                    var completed = (RepairSteps)r.ReadByte();
                    if ((completed & ~AllSteps) != 0) throw new InvalidDataException();
                    double started = r.ReadDouble();
                    long observationSequence = r.ReadInt64();
                    repair = new RepairJob(repairEquipment, completed, started, observationSequence, ReadOptionalBool(r));
                }
                equipment.Add(new EquipmentStatus(kind, station, operating, knowledge, symptoms, stateSequence, observation, repair));
            }

            double hold = r.ReadDouble();
            InstrumentReading conductivity = ReadReading(r);
            double specLimit = r.ReadDouble();
            var quality = (PotableQuality)r.ReadByte();
            double suspect = r.ReadDouble();
            long batches = r.ReadInt64();
            var water = new WaterTreatmentStatus(hold, conductivity, specLimit, quality, suspect, batches);

            int spareCount = E.Count(r, BaseEquipmentLimits.MaxSpareKinds);
            var spares = new List<SpareStock>(spareCount);
            for (int k = 0; k < spareCount; k++) spares.Add(new SpareStock(E.Key(r), r.ReadInt32(), r.ReadInt32()));

            int operationCount = E.Count(r, BaseOperationsLimits.MaxOperations);
            var operations = new List<OperationProgress>(operationCount);
            for (int k = 0; k < operationCount; k++)
            {
                Guid id = W.ReadGuid(r);
                var station = new StationId(E.Key(r));
                var action = (BaseOperationAction)r.ReadByte();
                var choice = (OperationChoice)r.ReadByte();
                PlayerId performer = Player(r.ReadInt32());
                double work = r.ReadDouble(), duration = r.ReadDouble();
                bool stalled = W.ReadBool(r);
                operations.Add(new OperationProgress(id, station, action, choice, performer, work, duration, stalled, W.ReadKey(r)));
            }

            int orderCount = E.Count(r, BaseOperationsLimits.MaxOrders);
            var orders = new List<ResupplyOrder>(orderCount);
            for (int k = 0; k < orderCount; k++)
            {
                long sequence = r.ReadInt64();
                string manifest = E.Key(r);
                PlayerId placedBy = Player(r.ReadInt32());
                double placed = r.ReadDouble(), commit = r.ReadDouble(), arrive = r.ReadDouble();
                var state = (OrderState)r.ReadByte();
                int itemCount = E.Count(r, BaseOperationsLimits.MaxManifestItems);
                var items = new List<ManifestItem>(itemCount);
                for (int i = 0; i < itemCount; i++)
                {
                    if (W.ReadBool(r)) items.Add(ManifestItem.Spares(E.Key(r), r.ReadInt32()));
                    else
                    {
                        var resource = (ResourceKind)r.ReadByte();
                        items.Add(ManifestItem.Mass(resource, r.ReadDouble()));
                    }
                }
                orders.Add(new ResupplyOrder(sequence, manifest, placedBy, placed, commit, arrive, state, items, E.ReadOptional(r)));
            }

            int activityCount = E.Count(r, BaseOperationsLimits.MaxActivities);
            var activities = new List<ActivityRecord>(activityCount);
            for (int k = 0; k < activityCount; k++)
            {
                long sequence = r.ReadInt64();
                var kind = (ActivityKind)r.ReadByte();
                string key = E.Key(r);
                var status = (ActivityStatus)r.ReadByte();
                PlayerId by = Player(r.ReadInt32());
                double started = r.ReadDouble();
                double? ended = E.ReadOptional(r);
                if (ended == null && status != ActivityStatus.Active && ActivityEndTimeDropped) ended = started;
                activities.Add(new ActivityRecord(sequence, kind, key, status, by, started, ended, W.ReadKey(r)));
            }

            int decisionCount = E.Count(r, BaseOperationsLimits.MaxDecisions);
            var decisions = new List<DecisionRecord>(decisionCount);
            for (int k = 0; k < decisionCount; k++)
            {
                Guid id = W.ReadGuid(r);
                PlayerId performer = Player(r.ReadInt32());
                var action = (BaseOperationAction)r.ReadByte();
                var choice = (OperationChoice)r.ReadByte();
                var machine = (EquipmentKind)r.ReadByte();
                double at = r.ReadDouble();
                var assessment = (DecisionAssessment)r.ReadByte();
                decisions.Add(new DecisionRecord(id, performer, action, choice, machine, at, assessment, E.Key(r)));
            }

            OperationsReport report = null;
            if (W.ReadBool(r))
            {
                long sequence = r.ReadInt64();
                double from = r.ReadDouble(), to = r.ReadDouble();
                int completed = r.ReadInt32(), cancelled = r.ReadInt32();
                report = new OperationsReport(sequence, from, to, completed, cancelled, r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            }

            return new BaseOperationsSnapshot(expedition, session, gauges, equipment, water, spares, operations, orders, activities,
                decisions, report);
        }

        private static void WriteObservation(BinaryWriter w, EquipmentObservation o)
        {
            w.Write(o.Sequence);
            w.Write((byte)o.Equipment);
            w.Write(o.ObservedBy.Value);
            w.Write(o.ObservedAtSeconds);
            w.Write(o.ValidUntilSeconds);
            w.Write(o.StateSequence);
            w.Write((byte)o.Findings);
            w.Write((byte)o.Readings.Count);
            foreach (ObservedReading reading in o.Readings)
            {
                W.WriteKey(w, reading.Key);
                WriteReading(w, reading.Reading);
            }
            w.Write(o.Current);
        }

        private static EquipmentObservation ReadObservation(BinaryReader r)
        {
            long sequence = r.ReadInt64();
            var equipment = (EquipmentKind)r.ReadByte();
            PlayerId by = Player(r.ReadInt32());
            double at = r.ReadDouble(), until = r.ReadDouble();
            long stateSequence = r.ReadInt64();
            Findings findings = ReadFindings(r);
            int count = E.Count(r, BaseOperationsLimits.MaxReadingsPerObservation);
            var readings = new List<ObservedReading>(count);
            for (int k = 0; k < count; k++) readings.Add(new ObservedReading(E.Key(r), ReadReading(r)));
            return new EquipmentObservation(sequence, equipment, by, at, until, stateSequence, findings, readings, W.ReadBool(r));
        }

        private static void WriteReading(BinaryWriter w, InstrumentReading reading)
        {
            w.Write((byte)reading.Status);
            w.Write((byte)reading.Unit);
            E.WriteOptional(w, reading.Value);
            E.WriteOptional(w, reading.ObservedAtSeconds);
        }

        private static InstrumentReading ReadReading(BinaryReader r)
        {
            var status = (ReadingStatus)r.ReadByte();
            var unit = (ReadingUnit)r.ReadByte();
            double? value = E.ReadOptional(r);
            return new InstrumentReading(status, unit, value, E.ReadOptional(r));
        }

        private static Findings ReadFindings(BinaryReader r)
        {
            var findings = (Findings)r.ReadByte();
            if ((findings & ~AllFindings) != 0) throw new InvalidDataException();
            return findings;
        }

        private static void WriteOptionalBool(BinaryWriter w, bool? value) => w.Write((byte)(value.HasValue ? (value.Value ? 2 : 1) : 0));

        private static bool? ReadOptionalBool(BinaryReader r)
        {
            switch (r.ReadByte())
            {
                case 0: return null;
                case 1: return false;
                case 2: return true;
                default: throw new InvalidDataException();
            }
        }

        /// <summary>0 is "nobody" (for example a scenario-started activity); negative ids are refused by PlayerId.</summary>
        private static PlayerId Player(int value) => value == 0 ? PlayerId.None : new PlayerId(value);
    }
}
