using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Trainer.Application;
using Trainer.Simulation;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// SDK-neutral host transport seam. No networking SDK is approved yet; a future binding implements this.
    /// Requirements: reliable, ordered delivery per connection; every callback on the Unity main thread;
    /// <c>connectionId</c> assigned by the transport from the real connection, never read from a payload;
    /// ClientConnected(id) before any MessageReceived(id, ...) (earlier messages are dropped as UnknownConnection).
    /// </summary>
    public interface ISessionHostTransport
    {
        /// <summary>Ownership of <paramref name="payload"/> passes to the transport (each call gets its own
        /// array). Should not throw for closed connections; if it throws, the bridge reports SendFailed.</summary>
        void SendToClient(ulong connectionId, byte[] payload);
        /// <summary>Close a failed connection and notify the remote client of link loss.
        /// Must be idempotent. The binding must eventually emit ClientDisconnected.</summary>
        void DisconnectClient(ulong connectionId, string reason);
        event Action<ulong> ClientConnected;
        event Action<ulong> ClientDisconnected;
        event Action<ulong, byte[]> MessageReceived;
    }

    /// <summary>Adapter-level codes. They never describe mission state and are not authority results.</summary>
    public static class SessionNetworkCodes
    {
        public const string NotConnected = "NotConnected";
        public const string ConnectionLost = "ConnectionLost";
        public const string LocalDisconnect = "LocalDisconnect";
        public const string ConnectFailed = "ConnectFailed";
        public const string SendFailed = "SendFailed";
        public const string ReasonTruncated = "ReasonTruncated";
        public const string UnknownConnection = "UnknownConnection";
        public const string EmptyPayload = "EmptyPayload";
        public const string PayloadTooLarge = "PayloadTooLarge";
        public const string BadMagic = "BadMagic";
        public const string UnsupportedVersion = "UnsupportedVersion";
        public const string UnknownMessageKind = "UnknownMessageKind";
        public const string UnexpectedMessageKind = "UnexpectedMessageKind";
        public const string MalformedPayload = "MalformedPayload";
    }

    /// <summary>
    /// Host side. Wraps the single authoritative <see cref="ISessionPort"/>: forwards client intentions,
    /// broadcasts authoritative snapshots and routes each result only to the connection that sent it.
    /// It never validates mission rules (the authority does) and never simulates.
    /// The host's local player uses the authority port directly, not this bridge.
    /// </summary>
    public sealed class NetworkCommandBridge : IDisposable
    {
        private readonly ISessionPort authority;
        private readonly ISessionHostTransport transport;
        private readonly HashSet<ulong> connected = new HashSet<ulong>();
        // Retained for the bridge lifetime so a command ID stays bound to its real sender after disconnect.
        private readonly Dictionary<Guid, ulong> owners = new Dictionary<Guid, ulong>();
        private bool disposed;

        /// <summary>Host diagnostic (connection, <see cref="SessionNetworkCodes"/> value): a message from that
        /// connection was dropped, a send to it failed (SendFailed) or a result reason was cut (ReasonTruncated).
        /// Transport exceptions never escape into the authority's event dispatch.</summary>
        public event Action<ulong, string> ProtocolError;

        public int ConnectedClientCount => connected.Count;

        public NetworkCommandBridge(ISessionPort authority, ISessionHostTransport transport)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            authority.SnapshotChanged += OnAuthoritySnapshot;
            authority.CommandCompleted += OnAuthorityResult;
            transport.ClientConnected += OnClientConnected;
            transport.ClientDisconnected += OnClientDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            authority.SnapshotChanged -= OnAuthoritySnapshot;
            authority.CommandCompleted -= OnAuthorityResult;
            transport.ClientConnected -= OnClientConnected;
            transport.ClientDisconnected -= OnClientDisconnected;
            transport.MessageReceived -= OnMessage;
            connected.Clear();
        }

        private void OnClientConnected(ulong connectionId) => connected.Add(connectionId);

        private void OnClientDisconnected(ulong connectionId) => connected.Remove(connectionId);

        private void OnMessage(ulong sender, byte[] payload)
        {
            if (!connected.Contains(sender))
            {
                ProtocolError?.Invoke(sender, SessionNetworkCodes.UnknownConnection);
                return;
            }
            if (!SessionWireProtocol.TryDecodeFromClient(payload, out ClientMessage message, out string error))
            {
                ProtocolError?.Invoke(sender, error);
                return;
            }
            if (message.Kind == SessionWireProtocol.SyncRequestKind)
            {
                // Initial state for a joining client; none is sent if the authority has no state yet.
                if (authority.TryGetSnapshot(out MissionSnapshot current))
                    Send(sender, SessionWireProtocol.EncodeSnapshot(current));
                return;
            }

            SessionCommand command = message.Command;
            // Guid.Empty cannot be correlated to one sender; same reason the authority uses.
            if (command.Id == Guid.Empty)
            {
                Reject(sender, command, "InvalidCommandId");
                return;
            }
            if (owners.TryGetValue(command.Id, out ulong owner))
            {
                // Another connection may not reuse (or observe the result of) a sender's command ID.
                if (owner != sender)
                {
                    Reject(sender, command, "CommandIdConflict");
                    return;
                }
            }
            else owners.Add(command.Id, sender);

            // Ownership is committed first: the authority may complete synchronously.
            authority.Submit(command);
        }

        private void Reject(ulong sender, SessionCommand command, string reason)
        {
            Send(sender, SessionWireProtocol.EncodeResult(
                new CommandResult(command.Id, command.Epoch, false, reason), out _));
        }

        private void OnAuthoritySnapshot(MissionSnapshot snapshot)
        {
            if (snapshot == null || connected.Count == 0) return;
            byte[] payload = SessionWireProtocol.EncodeSnapshot(snapshot);
            // Copy: a send may synchronously disconnect a client.
            ulong[] targets = new ulong[connected.Count];
            connected.CopyTo(targets);
            for (int i = 0; i < targets.Length; i++)
                Send(targets[i], i == 0 ? payload : (byte[])payload.Clone());
        }

        private void OnAuthorityResult(CommandResult result)
        {
            // Results of host-local commands (no remote owner) are not sent to anyone.
            if (result == null || !owners.TryGetValue(result.CommandId, out ulong owner)) return;
            if (!connected.Contains(owner)) return;
            byte[] payload = SessionWireProtocol.EncodeResult(result, out bool truncated);
            if (truncated) ProtocolError?.Invoke(owner, SessionNetworkCodes.ReasonTruncated);
            Send(owner, payload);
        }

        private void Send(ulong target, byte[] payload)
        {
            try
            {
                transport.SendToClient(target, payload);
            }
            catch (Exception)
            {
                // Runs inside the authority's events: a failing link must not break other subscribers/clients.
                connected.Remove(target);
                try { transport.DisconnectClient(target, SessionNetworkCodes.SendFailed); }
                catch (Exception) { /* Binding must still report link loss when close fails. */ }
                finally { ProtocolError?.Invoke(target, SessionNetworkCodes.SendFailed); }
            }
        }
    }

    internal struct ClientMessage
    {
        public byte Kind;
        public SessionCommand Command;
    }

    internal struct HostMessage
    {
        public byte Kind;
        public CommandResult Result;
        public MissionSnapshot Snapshot;
    }

    /// <summary>
    /// Binary wire format v1, little-endian. Header: 'T' 'S' version kind. Transport-only; converts to
    /// and from the frozen contract types without adding fields. No sender identity or host flag on the wire.
    /// </summary>
    internal static class SessionWireProtocol
    {
        internal const byte Magic0 = 0x54;
        internal const byte Magic1 = 0x53;
        internal const byte Version = 1;
        internal const byte SubmitCommandKind = 1;
        internal const byte SyncRequestKind = 2;
        internal const byte CommandResultKind = 3;
        internal const byte SnapshotKind = 4;
        internal const int MaxReasonBytes = 1024;
        internal const int MaxPayloadBytes = 2048;
        private const int HeaderBytes = 4;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        // Encoding never throws: invalid UTF-16 (lone surrogates) becomes U+FFFD.
        private static readonly UTF8Encoding LenientUtf8 = new UTF8Encoding(false, false);

        internal static byte[] EncodeSubmit(SessionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return Encode(SubmitCommandKind, writer =>
            {
                writer.Write(command.Id.ToByteArray());
                writer.Write(command.Epoch);
                writer.Write((int)command.Action);
                bool? value = command.RequestedScienceEnabled;
                writer.Write((byte)(value.HasValue ? (value.Value ? 2 : 1) : 0));
            });
        }

        internal static byte[] EncodeSyncRequest() => Encode(SyncRequestKind, writer => { });

        /// <summary>Never throws for a long or invalid reason: it is cut at a UTF-8 character boundary
        /// (<paramref name="truncated"/>) so ID, epoch and acceptance always reach the sender.</summary>
        internal static byte[] EncodeResult(CommandResult result, out bool truncated)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            byte[] encoded = LenientUtf8.GetBytes(result.Reason);
            int length = encoded.Length;
            truncated = length > MaxReasonBytes;
            if (truncated)
            {
                length = MaxReasonBytes;
                while (length > 0 && (encoded[length] & 0xC0) == 0x80) length--; // Not inside a character.
            }
            return Encode(CommandResultKind, writer =>
            {
                writer.Write(result.CommandId.ToByteArray());
                writer.Write(result.Epoch);
                writer.Write(result.Accepted);
                writer.Write((ushort)length);
                writer.Write(encoded, 0, length);
            });
        }

        internal static byte[] EncodeSnapshot(MissionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return Encode(SnapshotKind, writer =>
            {
                writer.Write(snapshot.Epoch);
                writer.Write(snapshot.Revision);
                writer.Write((int)snapshot.State);
                PowerSnapshot power = snapshot.Power;
                writer.Write(power.SimulatedTimeSeconds);
                writer.Write(power.EnergyJoules);
                writer.Write(power.CapacityJoules);
                writer.Write(power.GenerationWatts);
                writer.Write(power.EssentialRequestedWatts);
                writer.Write(power.EssentialServedWatts);
                writer.Write(power.ScienceRequestedWatts);
                writer.Write(power.ScienceServedWatts);
                writer.Write(snapshot.ScienceWorkSeconds);
                writer.Write(snapshot.ScienceEnabled);
            });
        }

        /// <summary>Accepts only client-to-host kinds. A client cannot send results or snapshots.</summary>
        internal static bool TryDecodeFromClient(byte[] payload, out ClientMessage message, out string error)
        {
            message = default;
            if (!TryOpen(payload, out byte kind, out error)) return false;
            if (kind == CommandResultKind || kind == SnapshotKind)
            {
                error = SessionNetworkCodes.UnexpectedMessageKind;
                return false;
            }
            if (kind != SubmitCommandKind && kind != SyncRequestKind)
            {
                error = SessionNetworkCodes.UnknownMessageKind;
                return false;
            }
            try
            {
                using (var reader = Body(payload))
                {
                    message.Kind = kind;
                    if (kind == SubmitCommandKind)
                    {
                        Guid id = ReadGuid(reader);
                        long epoch = reader.ReadInt64();
                        // Unknown actions pass through: the authority rejects them with UnknownAction.
                        var action = (SessionAction)reader.ReadInt32();
                        byte flag = reader.ReadByte();
                        if (flag > 2) return Malformed(out error);
                        bool? value = flag == 0 ? (bool?)null : flag == 2;
                        message.Command = new SessionCommand(id, epoch, action, value);
                    }
                    return Finish(reader, out error);
                }
            }
            catch (EndOfStreamException) { return Malformed(out error); }
        }

        /// <summary>Accepts only host-to-client kinds. A host never forwards client intentions.</summary>
        internal static bool TryDecodeFromHost(byte[] payload, out HostMessage message, out string error)
        {
            message = default;
            if (!TryOpen(payload, out byte kind, out error)) return false;
            if (kind == SubmitCommandKind || kind == SyncRequestKind)
            {
                error = SessionNetworkCodes.UnexpectedMessageKind;
                return false;
            }
            if (kind != CommandResultKind && kind != SnapshotKind)
            {
                error = SessionNetworkCodes.UnknownMessageKind;
                return false;
            }
            try
            {
                using (var reader = Body(payload))
                {
                    message.Kind = kind;
                    if (kind == CommandResultKind)
                    {
                        Guid id = ReadGuid(reader);
                        long epoch = reader.ReadInt64();
                        if (!TryReadBool(reader, out bool accepted)) return Malformed(out error);
                        int length = reader.ReadUInt16();
                        if (length > MaxReasonBytes) return Malformed(out error);
                        byte[] bytes = reader.ReadBytes(length);
                        if (bytes.Length != length) return Malformed(out error);
                        message.Result = new CommandResult(id, epoch, accepted, StrictUtf8.GetString(bytes));
                    }
                    else
                    {
                        long epoch = reader.ReadInt64();
                        long revision = reader.ReadInt64();
                        var state = (SessionState)reader.ReadInt32();
                        if (!Enum.IsDefined(typeof(SessionState), state)) return Malformed(out error);
                        var power = new PowerSnapshot(reader.ReadDouble(), reader.ReadDouble(),
                            reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(),
                            reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
                        double scienceWorkSeconds = reader.ReadDouble();
                        if (!TryReadBool(reader, out bool scienceEnabled)) return Malformed(out error);
                        if (epoch < 0 || revision < 0 || !ValidPower(power)
                            || !NonnegativeFinite(scienceWorkSeconds)) return Malformed(out error);
                        message.Snapshot = new MissionSnapshot(epoch, revision, state, power,
                            scienceWorkSeconds, scienceEnabled);
                    }
                    return Finish(reader, out error);
                }
            }
            catch (EndOfStreamException) { return Malformed(out error); }
            catch (ArgumentException) { return Malformed(out error); } // invalid UTF-8 reason
        }

        private static bool NonnegativeFinite(double value) => value >= 0d && !double.IsInfinity(value);

        private static bool ValidPower(PowerSnapshot p) =>
            NonnegativeFinite(p.SimulatedTimeSeconds) && NonnegativeFinite(p.EnergyJoules)
            && NonnegativeFinite(p.CapacityJoules) && NonnegativeFinite(p.GenerationWatts)
            && NonnegativeFinite(p.EssentialRequestedWatts) && NonnegativeFinite(p.EssentialServedWatts)
            && NonnegativeFinite(p.ScienceRequestedWatts) && NonnegativeFinite(p.ScienceServedWatts)
            && p.EnergyJoules <= p.CapacityJoules
            && p.EssentialServedWatts <= p.EssentialRequestedWatts
            && p.ScienceServedWatts <= p.ScienceRequestedWatts;

        private static byte[] Encode(byte kind, Action<BinaryWriter> body)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, StrictUtf8, true))
                {
                    writer.Write(Magic0);
                    writer.Write(Magic1);
                    writer.Write(Version);
                    writer.Write(kind);
                    body(writer);
                }
                return stream.ToArray();
            }
        }

        private static bool TryOpen(byte[] payload, out byte kind, out string error)
        {
            kind = 0;
            error = null;
            if (payload == null || payload.Length == 0) error = SessionNetworkCodes.EmptyPayload;
            else if (payload.Length > MaxPayloadBytes) error = SessionNetworkCodes.PayloadTooLarge;
            else if (payload.Length < HeaderBytes) error = SessionNetworkCodes.MalformedPayload;
            else if (payload[0] != Magic0 || payload[1] != Magic1) error = SessionNetworkCodes.BadMagic;
            else if (payload[2] != Version) error = SessionNetworkCodes.UnsupportedVersion;
            else kind = payload[3];
            return error == null;
        }

        private static BinaryReader Body(byte[] payload)
        {
            var stream = new MemoryStream(payload, HeaderBytes, payload.Length - HeaderBytes, false);
            return new BinaryReader(stream, StrictUtf8, false);
        }

        private static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            return new Guid(bytes);
        }

        private static bool TryReadBool(BinaryReader reader, out bool value)
        {
            byte raw = reader.ReadByte();
            value = raw == 1;
            return raw <= 1;
        }

        private static bool Finish(BinaryReader reader, out string error)
        {
            // Trailing bytes mean a different or corrupted format.
            if (reader.BaseStream.Position != reader.BaseStream.Length) return Malformed(out error);
            error = null;
            return true;
        }

        private static bool Malformed(out string error)
        {
            error = SessionNetworkCodes.MalformedPayload;
            return false;
        }
    }
}
