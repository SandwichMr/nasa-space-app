using System;
using System.Collections.Generic;
using Trainer.Unity.Networking;

/// <summary>
/// IN-MEMORY test network (not a socket, not a process): one host transport and any number of client
/// transports with reliable, ordered delivery. Nothing is delivered until <see cref="Pump"/>, so results and
/// snapshots arrive later than the call that caused them, as on a real link. Connection ids are assigned here
/// and never reused. Every frame the host sends is recorded for size/format checks.
/// </summary>
public sealed class InMemoryOutpostNetwork
{
    private readonly Queue<Action> queue = new Queue<Action>();
    private ulong lastId;

    public Host HostTransport { get; }
    public List<byte[]> HostFrames { get; } = new List<byte[]>();

    public InMemoryOutpostNetwork() => HostTransport = new Host(this);

    public Client CreateClient() => new Client(this);

    /// <summary>Delivers everything queued, including what deliveries queue in turn.</summary>
    public void Pump()
    {
        for (int guard = 0; queue.Count > 0; guard++)
        {
            if (guard > 100000) throw new InvalidOperationException("Delivery loop.");
            queue.Dequeue()();
        }
    }

    private void Enqueue(Action action) => queue.Enqueue(action);

    public sealed class Host : ISessionHostTransport
    {
        private readonly InMemoryOutpostNetwork network;
        internal readonly Dictionary<ulong, (Client client, long link)> Links = new Dictionary<ulong, (Client, long)>();

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<ulong, byte[]> MessageReceived;

        internal Host(InMemoryOutpostNetwork network) => this.network = network;

        public int ConnectionCount => Links.Count;

        public void SendToClient(ulong connectionId, byte[] payload)
        {
            if (!Links.TryGetValue(connectionId, out var target)) return;
            network.HostFrames.Add(payload);
            network.Enqueue(() => target.client.Deliver(target.link, payload));
        }

        public void DisconnectClient(ulong connectionId, string reason)
        {
            if (!Links.TryGetValue(connectionId, out var target)) return;
            Links.Remove(connectionId);
            target.client.Forget(target.link);
            network.Enqueue(() =>
            {
                target.client.RaiseDisconnected(target.link, "ClosedByRemote");
                ClientDisconnected?.Invoke(connectionId);
            });
        }

        /// <summary>Test hook: a raw payload arrives from a connection (forged or replayed bytes).</summary>
        public void Inject(ulong connectionId, byte[] payload) =>
            network.Enqueue(() => MessageReceived?.Invoke(connectionId, payload));

        internal ulong Accept(Client client, long link)
        {
            ulong id = ++network.lastId;
            Links.Add(id, (client, link));
            ClientConnected?.Invoke(id);
            return id;
        }

        internal void Receive(ulong id, byte[] payload)
        {
            if (Links.ContainsKey(id)) MessageReceived?.Invoke(id, payload);
        }

        internal void Drop(ulong id)
        {
            if (Links.Remove(id)) ClientDisconnected?.Invoke(id);
        }
    }

    public sealed class Client : ISessionClientTransport
    {
        private readonly InMemoryOutpostNetwork network;
        private readonly Dictionary<long, ulong> connections = new Dictionary<long, ulong>();

        public event Action<long> Connected;
        public event Action<long, string> Disconnected;
        public event Action<long, byte[]> MessageReceived;

        internal Client(InMemoryOutpostNetwork network) => this.network = network;

        /// <summary>Host-side id of the current link, or 0.</summary>
        public ulong ConnectionId { get; private set; }
        public List<byte[]> Sent { get; } = new List<byte[]>();

        public void Connect(long link) => network.Enqueue(() =>
        {
            ulong id = network.HostTransport.Accept(this, link);
            connections[link] = id;
            ConnectionId = id;
            Connected?.Invoke(link);
        });

        public void Disconnect(long link)
        {
            if (!connections.TryGetValue(link, out ulong id)) return;
            connections.Remove(link);
            network.Enqueue(() => network.HostTransport.Drop(id));
        }

        public void SendToHost(long link, byte[] payload)
        {
            if (!connections.TryGetValue(link, out ulong id)) return;
            Sent.Add(payload);
            network.Enqueue(() => network.HostTransport.Receive(id, payload));
        }

        /// <summary>Every frame delivered to this client (whatever the adapter then does with it).</summary>
        public List<byte[]> Received { get; } = new List<byte[]>();

        internal void Deliver(long link, byte[] payload)
        {
            if (!connections.ContainsKey(link)) return;
            Received.Add(payload);
            MessageReceived?.Invoke(link, payload);
        }

        internal void Forget(long link) => connections.Remove(link);

        internal void RaiseDisconnected(long link, string reason) => Disconnected?.Invoke(link, reason);
    }
}
