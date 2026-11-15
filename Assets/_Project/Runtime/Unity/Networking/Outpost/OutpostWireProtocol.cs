using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Unity.Networking.Outpost
{
    internal enum OutpostMessageKind : byte
    {
        // Client -> host.
        Join = 1,
        Submit = 2,
        Movement = 3,
        // Host -> client.
        Result = 10,
        Snapshot = 11,
        Membership = 12,
        Chunk = 13
    }

    internal struct OutpostClientMessage
    {
        public OutpostMessageKind Kind;
        public OutpostCommand Command;
        public MovementInput Movement;
    }

    internal struct OutpostHostMessage
    {
        public OutpostMessageKind Kind;
        public CommandResult Result;
        public OutpostSessionSnapshot Snapshot;
        public OutpostMembership Membership;
    }

    /// <summary>
    /// Phase 2 binary wire format, little-endian. Header 'T' 'O' version kind. Separate from the phase 1 format
    /// ('T' 'S'), which is unchanged; a decoder of one rejects the other as BadMagic. It converts to and from the
    /// frozen phase 2 contract types without adding fields and carries no sender identity (the host binds it).
    /// Host messages longer than <see cref="MaxFrameBytes"/> travel as consecutive Chunk frames (TCP keeps
    /// them in order); clients never send chunks. Decoding is strict: unknown kinds or versions, wrong lengths,
    /// invalid keys, NaN/infinity and anything the contract constructors reject are MalformedPayload.
    /// </summary>
    internal static class OutpostWireProtocol
    {
        internal const byte Magic0 = 0x54; // 'T'
        internal const byte Magic1 = 0x4F; // 'O'
        internal const byte Version = 1;
        internal const int HeaderBytes = 4;
        /// <summary>Largest frame on the transport; Unity Transport accepts about 1400 bytes per message.</summary>
        internal const int MaxFrameBytes = 1100;
        /// <summary>Largest reassembled host message (bounds memory; the largest valid snapshot is ~3.4 KB).</summary>
        internal const int MaxMessageBytes = 16 * 1024;
        internal const int MaxClientMessageBytes = 256;
        internal const int MaxReasonBytes = 256;
        private const int ChunkHeaderBytes = HeaderBytes + 4;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly UTF8Encoding LenientUtf8 = new UTF8Encoding(false, false);

        // ---- Client -> host ---------------------------------------------------------------------------------

        internal static byte[] EncodeJoin() => Encode(OutpostMessageKind.Join, w => { });

        internal static byte[] EncodeSubmit(OutpostCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return Encode(OutpostMessageKind.Submit, w =>
            {
                w.Write(command.Id.ToByteArray());
                w.Write(command.Epoch);
                w.Write((int)command.Action);
                WriteKey(w, command.Station.IsValid ? command.Station.Value : null);
                WriteKey(w, command.Task.IsValid ? command.Task.Value : null);
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

        /// <summary>Accepts only client kinds. A client cannot send results, snapshots, memberships or chunks.</summary>
        internal static bool TryDecodeFromClient(byte[] payload, out OutpostClientMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > MaxClientMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!TryOpen(payload, out OutpostMessageKind kind, out error)) return false;
            if (kind != OutpostMessageKind.Join && kind != OutpostMessageKind.Submit && kind != OutpostMessageKind.Movement)
                return Fail(IsHostKind(kind) ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            var decoded = new OutpostClientMessage { Kind = kind };
            bool ok = Read(payload, out error, r =>
            {
                if (kind == OutpostMessageKind.Submit)
                {
                    Guid id = ReadGuid(r);
                    long epoch = r.ReadInt64();
                    var action = (OutpostAction)r.ReadInt32(); // Unknown actions reach the authority (UnknownAction).
                    string station = ReadKey(r), task = ReadKey(r);
                    decoded.Command = new OutpostCommand(id, epoch, action,
                        station == null ? StationId.None : new StationId(station),
                        task == null ? TaskTypeId.None : new TaskTypeId(task));
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
            byte[] reason = Utf8Cut(result.Reason, MaxReasonBytes, out truncated);
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
            byte[] reason = Utf8Cut(membership.Reason, MaxReasonBytes, out _);
            return Encode(OutpostMessageKind.Membership, w =>
            {
                w.Write((byte)membership.Status);
                w.Write(membership.Player.Value);
                w.Write((ushort)reason.Length);
                w.Write(reason);
            });
        }

        internal static byte[] EncodeSnapshot(OutpostSessionSnapshot s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return Encode(OutpostMessageKind.Snapshot, w =>
            {
                w.Write(s.Epoch);
                w.Write(s.Revision);
                w.Write((int)s.State);
                w.Write((int)s.Outcome);
                WriteKey(w, s.ScenarioId);
                w.Write((byte)s.Crew.Count);
                foreach (CrewMemberState m in s.Crew)
                {
                    w.Write(m.Player.Value);
                    w.Write((byte)m.Slot);
                    w.Write(m.Pose.Position.X);
                    w.Write(m.Pose.Position.Y);
                    w.Write(m.Pose.Facing.X);
                    w.Write(m.Pose.Facing.Y);
                    w.Write((byte)m.Pose.Motion);
                    w.Write(m.LastInputSequence);
                }
                w.Write((byte)s.Stations.Count);
                foreach (StationState st in s.Stations)
                {
                    WriteKey(w, st.Station.Value);
                    w.Write((byte)st.Availability);
                    w.Write(st.ActiveTask != null);
                    if (st.ActiveTask != null)
                    {
                        WriteKey(w, st.ActiveTask.Task.Value);
                        w.Write(st.ActiveTask.Performer.Value);
                        w.Write(st.ActiveTask.WorkSeconds);
                        w.Write(st.ActiveTask.DurationSeconds);
                        w.Write(st.ActiveTask.Stalled);
                    }
                    w.Write(st.LastEnd != null);
                    if (st.LastEnd != null)
                    {
                        WriteKey(w, st.LastEnd.Task.Value);
                        w.Write(st.LastEnd.Performer.Value);
                        w.Write((byte)st.LastEnd.Reason);
                        w.Write(st.LastEnd.EndedAtSeconds);
                    }
                }
                WriteResources(w, s.Resources);
                w.Write((byte)s.Objectives.Count);
                foreach (ObjectiveProgress o in s.Objectives)
                {
                    WriteKey(w, o.Key);
                    w.Write(o.Current);
                    w.Write(o.Target);
                    w.Write((byte)o.Unit);
                    w.Write(o.Required);
                }
            });
        }

        /// <summary>Splits a host message into frames of at most <see cref="MaxFrameBytes"/>. A message that fits
        /// is returned as is (one frame). Each frame is a new array, as the transport takes ownership.</summary>
        internal static List<byte[]> Frame(byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Length > MaxMessageBytes) throw new ArgumentException("Message too large.", nameof(message));
            var frames = new List<byte[]>();
            if (message.Length <= MaxFrameBytes)
            {
                frames.Add(message);
                return frames;
            }
            int body = MaxFrameBytes - ChunkHeaderBytes;
            int count = (message.Length + body - 1) / body;
            for (int i = 0; i < count; i++)
            {
                int offset = i * body, length = Math.Min(body, message.Length - offset);
                var frame = new byte[ChunkHeaderBytes + length];
                frame[0] = Magic0;
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

        /// <summary>
        /// Client-side reassembly of one link (create a new one per link). Feed every received frame; returns
        /// true with a complete host message when one is ready. A gap, repeat or oversize ends the partial
        /// message and reports MalformedPayload.
        /// </summary>
        internal sealed class Assembler
        {
            private MemoryStream partial;
            private int expected, count;

            internal bool TryAccept(byte[] frame, out byte[] message, out string error)
            {
                message = null;
                if (!TryOpen(frame, out OutpostMessageKind kind, out error)) return Reset(false);
                if (kind != OutpostMessageKind.Chunk)
                {
                    if (partial != null) return Reset(Fail(SessionNetworkCodes.MalformedPayload, out error));
                    message = frame;
                    return true;
                }
                if (frame.Length <= ChunkHeaderBytes) return Reset(Fail(SessionNetworkCodes.MalformedPayload, out error));
                int index = frame[4] | (frame[5] << 8), total = frame[6] | (frame[7] << 8);
                if (total < 2 || index >= total || (partial == null ? index != 0 : index != expected || total != count))
                    return Reset(Fail(SessionNetworkCodes.MalformedPayload, out error));
                if (partial == null)
                {
                    partial = new MemoryStream();
                    count = total;
                }
                partial.Write(frame, ChunkHeaderBytes, frame.Length - ChunkHeaderBytes);
                if (partial.Length > MaxMessageBytes) return Reset(Fail(SessionNetworkCodes.PayloadTooLarge, out error));
                expected = index + 1;
                if (expected < count) return false;
                message = partial.ToArray();
                partial = null;
                if (message.Length >= HeaderBytes && message[3] == (byte)OutpostMessageKind.Chunk)
                {
                    message = null;
                    return Fail(SessionNetworkCodes.MalformedPayload, out error);
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

        /// <summary>Accepts only complete host kinds (not chunks: reassemble first).</summary>
        internal static bool TryDecodeFromHost(byte[] payload, out OutpostHostMessage message, out string error)
        {
            message = default;
            if (payload != null && payload.Length > MaxMessageBytes)
            {
                error = SessionNetworkCodes.PayloadTooLarge;
                return false;
            }
            if (!TryOpen(payload, out OutpostMessageKind kind, out error)) return false;
            if (kind != OutpostMessageKind.Result && kind != OutpostMessageKind.Snapshot && kind != OutpostMessageKind.Membership)
                return Fail(kind == OutpostMessageKind.Join || kind == OutpostMessageKind.Submit || kind == OutpostMessageKind.Movement
                    || kind == OutpostMessageKind.Chunk ? SessionNetworkCodes.UnexpectedMessageKind : SessionNetworkCodes.UnknownMessageKind, out error);
            var decoded = new OutpostHostMessage { Kind = kind };
            bool ok = Read(payload, out error, r =>
            {
                if (kind == OutpostMessageKind.Result)
                {
                    Guid id = ReadGuid(r);
                    long epoch = r.ReadInt64();
                    bool accepted = ReadBool(r);
                    decoded.Result = new CommandResult(id, epoch, accepted, ReadText(r));
                }
                else if (kind == OutpostMessageKind.Membership) decoded.Membership = ReadMembership(r);
                else decoded.Snapshot = ReadSnapshot(r);
            });
            if (ok) message = decoded;
            return ok;
        }

        private static OutpostMembership ReadMembership(BinaryReader r)
        {
            var status = (MembershipStatus)r.ReadByte();
            int player = r.ReadInt32();
            string reason = ReadText(r);
            switch (status)
            {
                case MembershipStatus.Joined: return OutpostMembership.Joined(new PlayerId(player));
                case MembershipStatus.Rejected: return OutpostMembership.Rejected(reason);
                case MembershipStatus.Left: return OutpostMembership.Left(new PlayerId(player), reason);
                default: throw new InvalidDataException(); // NotJoined is never sent.
            }
        }

        private static OutpostSessionSnapshot ReadSnapshot(BinaryReader r)
        {
            long epoch = r.ReadInt64(), revision = r.ReadInt64();
            var state = (SessionState)r.ReadInt32();
            var outcome = (OutpostOutcome)r.ReadInt32();
            string scenario = ReadKey(r) ?? throw new InvalidDataException();
            int crewCount = r.ReadByte();
            if (crewCount > OutpostLimits.MaxCrew) throw new InvalidDataException();
            var crew = new List<CrewMemberState>(crewCount);
            for (int i = 0; i < crewCount; i++)
            {
                var player = new PlayerId(r.ReadInt32());
                int slot = r.ReadByte();
                var position = new BasePosition(r.ReadDouble(), r.ReadDouble());
                var facing = new MoveDirection(r.ReadDouble(), r.ReadDouble());
                var motion = (CrewMotion)r.ReadByte();
                crew.Add(new CrewMemberState(player, slot, new CrewPose(position, facing, motion), r.ReadInt64()));
            }
            int stationCount = r.ReadByte();
            if (stationCount > OutpostLimits.MaxStations) throw new InvalidDataException();
            var stations = new List<StationState>(stationCount);
            for (int i = 0; i < stationCount; i++)
            {
                var id = new StationId(ReadKey(r) ?? throw new InvalidDataException());
                var availability = (StationAvailability)r.ReadByte();
                TaskProgress task = null;
                if (ReadBool(r))
                    task = new TaskProgress(new TaskTypeId(ReadKey(r) ?? throw new InvalidDataException()),
                        new PlayerId(r.ReadInt32()), r.ReadDouble(), r.ReadDouble(), ReadBool(r));
                TaskEnd end = null;
                if (ReadBool(r))
                    end = new TaskEnd(new TaskTypeId(ReadKey(r) ?? throw new InvalidDataException()),
                        new PlayerId(r.ReadInt32()), (TaskEndReason)r.ReadByte(), r.ReadDouble());
                stations.Add(new StationState(id, availability, task, end));
            }
            OutpostSimulationSnapshot resources = ReadResources(r);
            int objectiveCount = r.ReadByte();
            if (objectiveCount > OutpostLimits.MaxObjectives) throw new InvalidDataException();
            var objectives = new List<ObjectiveProgress>(objectiveCount);
            for (int i = 0; i < objectiveCount; i++)
                objectives.Add(new ObjectiveProgress(ReadKey(r) ?? throw new InvalidDataException(), r.ReadDouble(),
                    r.ReadDouble(), (ProgressUnit)r.ReadByte(), ReadBool(r)));
            return new OutpostSessionSnapshot(epoch, revision, state, outcome, scenario, crew, stations, resources, objectives);
        }

        private static void WriteResources(BinaryWriter w, OutpostSimulationSnapshot s)
        {
            PowerSnapshot p = s.Power;
            foreach (double v in new[] { p.SimulatedTimeSeconds, p.EnergyJoules, p.CapacityJoules, p.GenerationWatts,
                         p.EssentialRequestedWatts, p.EssentialServedWatts, p.ScienceRequestedWatts, p.ScienceServedWatts })
                w.Write(v);
            w.Write(s.ServiceLoadRequestedWatts);
            w.Write(s.ServiceLoadServedWatts);
            WaterStock water = s.LifeSupport.Water;
            w.Write(water.PotableKilograms);
            w.Write(water.UntreatedKilograms);
            w.Write(water.PotableCapacityKilograms);
            w.Write(water.UntreatedCapacityKilograms);
            w.Write(s.LifeSupport.Food.StoredKilograms);
            w.Write(s.LifeSupport.Food.CapacityKilograms);
            WriteReading(w, s.LifeSupport.Cabin.Oxygen);
            WriteReading(w, s.LifeSupport.Cabin.CarbonDioxide);
        }

        private static OutpostSimulationSnapshot ReadResources(BinaryReader r)
        {
            var values = new double[8];
            for (int i = 0; i < values.Length; i++) values[i] = r.ReadDouble();
            var power = new PowerSnapshot(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]);
            if (!ValidPower(power)) throw new InvalidDataException();
            double requested = r.ReadDouble(), served = r.ReadDouble();
            var water = new WaterStock(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
            var food = new FoodStock(r.ReadDouble(), r.ReadDouble());
            var cabin = new CabinAirObservation(ReadReading(r), ReadReading(r));
            return new OutpostSimulationSnapshot(power, requested, served, new LifeSupportSnapshot(water, food, cabin));
        }

        private static void WriteReading(BinaryWriter w, GasReading reading)
        {
            w.Write((byte)reading.Status);
            if (reading.Status == ReadingStatus.Unavailable) return;
            w.Write(reading.PartialPressureKilopascals.Value);
            w.Write(reading.ObservedAtSeconds.Value);
        }

        private static GasReading ReadReading(BinaryReader r)
        {
            var status = (ReadingStatus)r.ReadByte();
            if (status == ReadingStatus.Unavailable) return GasReading.Unavailable;
            return new GasReading(status, r.ReadDouble(), r.ReadDouble());
        }

        private static bool ValidPower(PowerSnapshot p)
        {
            foreach (double v in new[] { p.SimulatedTimeSeconds, p.EnergyJoules, p.CapacityJoules, p.GenerationWatts,
                         p.EssentialRequestedWatts, p.EssentialServedWatts, p.ScienceRequestedWatts, p.ScienceServedWatts })
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) return false;
            return p.EnergyJoules <= p.CapacityJoules && p.EssentialServedWatts <= p.EssentialRequestedWatts
                && p.ScienceServedWatts <= p.ScienceRequestedWatts;
        }

        // ---- Primitives ---------------------------------------------------------------------------------------

        private static bool IsHostKind(OutpostMessageKind kind) =>
            kind == OutpostMessageKind.Result || kind == OutpostMessageKind.Snapshot
            || kind == OutpostMessageKind.Membership || kind == OutpostMessageKind.Chunk;

        private static byte[] Encode(OutpostMessageKind kind, Action<BinaryWriter> body)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, StrictUtf8, true))
                {
                    writer.Write(Magic0);
                    writer.Write(Magic1);
                    writer.Write(Version);
                    writer.Write((byte)kind);
                    body(writer);
                }
                return stream.ToArray();
            }
        }

        private static bool TryOpen(byte[] payload, out OutpostMessageKind kind, out string error)
        {
            kind = 0;
            error = null;
            if (payload == null || payload.Length == 0) error = SessionNetworkCodes.EmptyPayload;
            else if (payload.Length < HeaderBytes) error = SessionNetworkCodes.MalformedPayload;
            else if (payload[0] != Magic0 || payload[1] != Magic1) error = SessionNetworkCodes.BadMagic;
            else if (payload[2] != Version) error = SessionNetworkCodes.UnsupportedVersion;
            else kind = (OutpostMessageKind)payload[3];
            return error == null;
        }

        /// <summary>Runs <paramref name="body"/>; any read past the end, trailing byte or value the contract
        /// constructors reject becomes MalformedPayload.</summary>
        private static bool Read(byte[] payload, out string error, Action<BinaryReader> body)
        {
            try
            {
                using (var stream = new MemoryStream(payload, HeaderBytes, payload.Length - HeaderBytes, false))
                using (var reader = new BinaryReader(stream, StrictUtf8, false))
                {
                    body(reader);
                    if (stream.Position != stream.Length) return Fail(SessionNetworkCodes.MalformedPayload, out error);
                }
                error = null;
                return true;
            }
            catch (Exception e) when (e is EndOfStreamException || e is InvalidDataException || e is ArgumentException
                || e is DecoderFallbackException)
            {
                return Fail(SessionNetworkCodes.MalformedPayload, out error);
            }
        }

        private static bool Fail(string code, out string error)
        {
            error = code;
            return false;
        }

        private static void WriteKey(BinaryWriter w, string key)
        {
            if (key == null)
            {
                w.Write((byte)0);
                return;
            }
            byte[] bytes = Encoding.ASCII.GetBytes(key); // Stable keys are ASCII by construction.
            w.Write((byte)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadKey(BinaryReader r)
        {
            int length = r.ReadByte();
            if (length == 0) return null;
            if (length > OutpostLimits.MaxKeyLength) throw new InvalidDataException();
            byte[] bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string key = Encoding.ASCII.GetString(bytes);
            if (!StableKey.IsValid(key)) throw new InvalidDataException();
            return key;
        }

        private static string ReadText(BinaryReader r)
        {
            int length = r.ReadUInt16();
            if (length > MaxReasonBytes) throw new InvalidDataException();
            byte[] bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return StrictUtf8.GetString(bytes);
        }

        private static bool ReadBool(BinaryReader r)
        {
            byte raw = r.ReadByte();
            if (raw > 1) throw new InvalidDataException();
            return raw == 1;
        }

        private static Guid ReadGuid(BinaryReader r)
        {
            byte[] bytes = r.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            return new Guid(bytes);
        }

        /// <summary>UTF-8 of <paramref name="text"/>, cut at a character boundary to fit <paramref name="max"/>.</summary>
        private static byte[] Utf8Cut(string text, int max, out bool truncated)
        {
            byte[] bytes = LenientUtf8.GetBytes(text ?? string.Empty);
            truncated = bytes.Length > max;
            if (!truncated) return bytes;
            int length = max;
            while (length > 0 && (bytes[length] & 0xC0) == 0x80) length--;
            var cut = new byte[length];
            Buffer.BlockCopy(bytes, 0, cut, 0, length);
            return cut;
        }
    }
}
