using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Tooling.Validation.Phase02;
using Trainer.Tooling.Validation.Phase02.Doubles;

/// <summary>
/// D5 phase 2: the conformance suites themselves. A contract-abiding test double must pass every check and
/// each injected defect must be caught by the check that targets it. This proves the suites, NOT D1/D3.
/// </summary>
public sealed class Phase02ConformanceSelfTests
{
    private static string Dump(IEnumerable<Phase02Check> checks) => Phase02Checks.Format(checks);

    private static void AllPassed(List<Phase02Check> checks)
    {
        Assert.That(checks.Count, Is.GreaterThan(5));
        Assert.That(checks.All(c => c.Status == Phase02CheckStatus.Passed), Is.True, Dump(checks));
    }

    private static void Caught(List<Phase02Check> checks, string nameFragment)
    {
        Assert.That(checks.Any(c => c.Status == Phase02CheckStatus.Failed && c.Name.Contains(nameFragment)), Is.True,
            "Defect not caught by '" + nameFragment + "':\n" + Dump(checks));
    }

    [Test] public void RosterSuiteAcceptsReferenceDouble() =>
        AllPassed(CrewRosterConformance.Run((l, m) => new ReferenceCrewRoster(l, m), "reference-double"));

    [TestCase(RosterDefect.DiagonalFaster, "diagonal")]
    [TestCase(RosterDefect.Tunnels, "tunnel")]
    [TestCase(RosterDefect.ReusesIds, "never reused")]
    [TestCase(RosterDefect.KeepsDirectionOnRestart, "restart")]
    public void RosterSuiteCatchesDefect(RosterDefect defect, string check) =>
        Caught(CrewRosterConformance.Run((l, m) => new ReferenceCrewRoster(l, m, defect), "mutant"), check);

    [Test] public void SimulationSuiteAcceptsReferenceDouble() =>
        AllPassed(OutpostSimulationConformance.Run(() => new ReferenceOutpostSimulation(), "reference-double"));

    [TestCase(SimulationDefect.AppliesTwice, "exactly once")]
    [TestCase(SimulationDefect.PartialApply, "insufficient stock is all-or-nothing")]
    [TestCase(SimulationDefect.MassFromNowhere, "varied run")]
    public void SimulationSuiteCatchesDefect(SimulationDefect defect, string check) =>
        Caught(OutpostSimulationConformance.Run(() => new ReferenceOutpostSimulation(defect), "mutant"), check);

    [Test] public void AuthoritySuiteIsPendingWithoutRealImplementation()
    {
        List<Phase02Check> checks = OutpostAuthorityConformance.Run(new AuthorityHarness());
        Assert.That(checks.Count, Is.EqualTo(1));
        Assert.That(checks[0].Status, Is.EqualTo(Phase02CheckStatus.Pending));
    }

    [Test] public void TwoPortScriptIsPendingWithoutRealPorts()
    {
        List<Phase02Check> checks = TwoPortSessionScript.Run(new TwoPortPlan { Scenario = Phase02Fixtures.Scenario() });
        Assert.That(checks.Single().Status, Is.EqualTo(Phase02CheckStatus.Pending));
    }

    [Test] public void DiscoveryExcludesDoublesFakesAndTooling()
    {
        Phase02ImplementationDiscovery d = Phase02ImplementationDiscovery.Discover(new[]
        {
            typeof(ReferenceCrewRoster).Assembly, typeof(Trainer.Application.Development.FakeOutpostSessionPort).Assembly
        });
        Assert.That(Phase02ImplementationDiscovery.IsProductionCandidate(typeof(ReferenceCrewRoster)), Is.False);
        Assert.That(Phase02ImplementationDiscovery.IsProductionCandidate(typeof(Trainer.Application.Development.FakeOutpostSessionPort)), Is.False);
        Assert.That(d.Roster, Is.Null, "A double was taken for D3's roster.");
        Assert.That(d.Simulation, Is.Null, "A double was taken for D1's simulation.");
    }
}

/// <summary>
/// D5 phase 2 gate on the REAL implementations present in this build. Before integration there are none, so
/// the result is Inconclusive with the reason; it never passes on doubles or fakes.
/// </summary>
public sealed class Phase02IntegrationGateTests
{
    [Test] public void RealImplementationsConformToTheContracts()
    {
        Phase02ImplementationDiscovery d = Phase02ImplementationDiscovery.Discover();
        List<Phase02Check> checks = d.RunConformance();
        string report = Phase02Checks.Format(checks);
        Assert.That(checks.Where(c => c.Status == Phase02CheckStatus.Failed), Is.Empty, report);
        if (d.Roster == null || d.Simulation == null || d.Authority.Create == null || d.Authority.Advance == null)
            Assert.Inconclusive("Real phase 2 implementations missing or incomplete in this build:\n" + report);
    }
}
