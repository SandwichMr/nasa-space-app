using System;
using NUnit.Framework;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using A = Phase04NetworkTestAuthority;

/// <summary>
/// Integration regression (phase 4): a host send that fails must release the member ONCE without depending on a
/// ClientDisconnected that may never arrive (here DisconnectClient itself throws, so the binding never reports the loss).
/// Before the fix the member stayed joined (ghost occupancy, held reservations) until the bridge was disposed.
/// </summary>
public sealed class Phase04HostSendFailureTests
{
    /// <summary>Wraps the in-memory host: sends can be made to throw and DisconnectClient always throws.</summary>
    private sealed class FailingHost : ISessionHostTransport
    {
        private readonly ISessionHostTransport inner;
        public bool FailSends;
        public int DisconnectCalls;

        public FailingHost(ISessionHostTransport inner) => this.inner = inner;

        public void SendToClient(ulong connectionId, byte[] payload)
        {
            if (FailSends) throw new InvalidOperationException("send queue full (test)");
            inner.SendToClient(connectionId, payload);
        }

        public void DisconnectClient(ulong connectionId, string reason)
        {
            DisconnectCalls++;
            throw new InvalidOperationException("driver already released (test)");
        }

        public event Action<ulong> ClientConnected { add => inner.ClientConnected += value; remove => inner.ClientConnected -= value; }
        public event Action<ulong> ClientDisconnected { add => inner.ClientDisconnected += value; remove => inner.ClientDisconnected -= value; }
        public event Action<ulong, byte[]> MessageReceived { add => inner.MessageReceived += value; remove => inner.MessageReceived -= value; }
    }

    [Test]
    public void AFailedSendReleasesTheMemberOnceWithoutADisconnectEvent()
    {
        var authority = new A(A.NearStationsScenario());
        var net = new InMemoryOutpostNetwork();
        var host = new FailingHost(net.HostTransport);
        var bridge = new BaseOperationsCommandBridge(authority, host);
        var client = new BaseOperationsConnectionController(net.CreateClient());
        try
        {
            client.Connect();
            for (int i = 0; i < 3; i++) { bridge.FlushSnapshots(); net.Pump(); }
            Assert.That(client.State, Is.EqualTo(SessionConnectionState.Connected), "fixture: joined");
            Assert.That(bridge.JoinedClientCount, Is.EqualTo(1));
            Assert.That(client.Session.TryGetBaseOperations(out BaseOperationsSnapshot seen), Is.True);

            host.FailSends = true;
            client.Session.Submit(ExpeditionCommand.Session(Guid.NewGuid(), seen.Epoch, ExpeditionAction.Resume));
            net.Pump();                 // the host receives it; the result send throws
            bridge.FlushSnapshots();    // the failed link is released here, once

            Assert.That(host.DisconnectCalls, Is.GreaterThanOrEqualTo(1), "the bridge tried to close the link");
            Assert.That(bridge.LeaveCount, Is.EqualTo(1), "Leave once, without a ClientDisconnected");
            Assert.That(bridge.JoinedClientCount, Is.EqualTo(0), "no ghost member in the bridge");
            Assert.That(authority.TryGetSnapshot(out BaseOperationsSnapshot truth), Is.True);
            Assert.That(truth.Outpost.Crew.Count, Is.EqualTo(0), "no ghost member in the authority");

            bridge.FlushSnapshots();
            bridge.Dispose();
            Assert.That(bridge.LeaveCount, Is.EqualTo(1), "never a second Leave (flush again, dispose)");
        }
        finally
        {
            client.Dispose();
            bridge.Dispose();
        }
    }
}
