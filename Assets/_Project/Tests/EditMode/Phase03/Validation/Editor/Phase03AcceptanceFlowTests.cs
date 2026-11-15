using System;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Configuration;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Validation.Phase03;
using UnityEngine;

/// <summary>
/// D5 phase 3: recorridos A and B on the SHIPPED scenario (asset defaults) over the REAL stack the scene composes: the
/// production authority composition (D1 base + EVA, D3 roster, D2 authority), the phase 3 bridge and cable, a remote
/// client and the host-local port. Two scripted players act only through their ports and intents (the same pilot the
/// build uses across processes). In memory: it does not replace the two-process build run.
/// </summary>
public sealed class Phase03AcceptanceFlowTests
{
    private const double Frame = 1.0 / 30;

    private static (ExpeditionAcceptanceScript explorer, ExpeditionAcceptanceScript support, ExpeditionSessionSnapshot end) Play(AcceptanceFlow flow)
    {
        var definition = ScriptableObject.CreateInstance<ExpeditionScenarioDefinition>();
        try
        {
            Assert.That(definition.Validate(), Is.Null);
            ExpeditionAuthority authority = ExpeditionBootstrap.Create(definition);
            ExpeditionScenario scenario = authority.Scenario;
            var net = new InMemoryOutpostNetwork();
            using (var bridge = new ExpeditionCommandBridge(authority, net.HostTransport))
            using (var host = new LocalExpeditionSessionPort(authority))
            using (var remote = new ExpeditionConnectionController(net.CreateClient()))
            using (var remoteIntents = new ExpeditionInteractionClient(remote.Session, scenario))
            using (var hostIntents = new ExpeditionInteractionClient(host, scenario))
            {
                void Pump()
                {
                    for (int i = 0; i < 3; i++) { bridge.FlushSnapshots(); net.Pump(); }
                }
                Assert.That(host.Join().IsJoined);
                remote.Connect();
                Pump();
                Assert.That(remote.State, Is.EqualTo(SessionConnectionState.Connected));
                var explorer = new ExpeditionAcceptanceScript(remote.Session, remoteIntents, scenario, flow, AcceptanceRole.Explorer);
                var support = new ExpeditionAcceptanceScript(host, hostIntents, scenario, flow, AcceptanceRole.Support);
                for (int frame = 0; frame < 30 * 600 && !(explorer.Finished && support.Finished); frame++)
                {
                    explorer.Update(Frame);
                    support.Update(Frame);
                    Pump();
                    authority.Tick(Frame);
                    Pump();
                }
                Assert.That(explorer.Finished && support.Finished, "both pilots finished; explorer at '" + explorer.CurrentStep +
                    "', support at '" + support.CurrentStep + "'\n" + string.Join("\n", explorer.Log.Concat(support.Log)));
                authority.TryGetSnapshot(out ExpeditionSessionSnapshot end);
                return (explorer, support, end);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void RecorridoA_PrepareGoOutScienceReturnIngressResult()
    {
        var (explorer, support, end) = Play(AcceptanceFlow.A);
        Assert.That(explorer.Passed, explorer.Verdict + "\n" + string.Join("\n", explorer.Log));
        Assert.That(support.Passed, support.Verdict);
        Assert.That(end.Outcome, Is.EqualTo(ExpeditionOutcome.ScienceReturned));
        Assert.That(end.Objectives.Single(o => o.Key == "regolith-sample").Met);
        Assert.That(end.Objectives.Single(o => o.Role == ObjectiveRole.Safety).Met);
        Assert.That(end.Advisories, Is.Empty, "the expedition closed before the authored warning (150 s)");
        Assert.That(end.Disclosure, Is.Not.Null, "the debrief reveals the synthetic event that never reached them");
        Assert.That(end.Outpost.Stations.Single(s => s.Station.Value == "water-recycler").LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed),
            "the base kept working during the EVA");
        Assert.That(end.Incidents, Is.Empty);
    }

    [Test]
    public void RecorridoB_PrepareGoOutNewInformationCancelReturnShelterResult()
    {
        var (explorer, support, end) = Play(AcceptanceFlow.B);
        string detail = "assessments: " + string.Join("; ", end.Assessments.Select(a =>
            $"{a.Player} {a.Category} received {a.ReceivedAtSeconds} protected {a.FirstProtectedAtSeconds}"))
            + "\nsupport: " + support.Verdict + "\n" + string.Join("\n", support.Log) + "\nexplorer: " + explorer.Verdict;
        Assert.That(support.Passed, detail);
        Assert.That(explorer.Passed, detail);
        Assert.That(end.Outcome, Is.EqualTo(ExpeditionOutcome.ShelteredInTime));
        Assert.That(end.Objectives.Single(o => o.Key == "regolith-sample").Met, Is.False, "the science was cancelled: still a correct result");
        Assert.That(end.Assessments.Count, Is.EqualTo(2));
        Assert.That(end.Assessments.All(a => a.Category == ProtectionCategory.InTime));
        Assert.That(end.Decision, Is.Not.Null);
        Assert.That(end.Decision.AdvisoriesKnown, Does.Contain("spe-warning-1"), "the return was decided with the warning received");
        Assert.That(end.Instruction.State, Is.EqualTo(InstructionState.Ended));
        Assert.That(end.Outpost.Stations.Single(s => s.Station.Value == "regolith-site").LastEnd.Reason,
            Is.EqualTo(TaskEndReason.CancelledByPerformer));
    }
}
