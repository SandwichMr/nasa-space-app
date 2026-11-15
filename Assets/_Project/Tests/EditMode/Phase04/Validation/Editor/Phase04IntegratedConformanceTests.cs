using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application.Development;
using Trainer.Tooling.Validation.Phase04;

namespace Trainer.Tests.Phase04.Validation
{
    /// <summary>
    /// D5 phase 4 conformance of the REAL implementations found by <see cref="Phase04ImplementationDiscovery"/>. Mapping: any
    /// Failed check → test failure; no failure but something Pending (implementation not delivered yet) → Inconclusive with
    /// the owner; Passed only when every check of the slice passed. Kinds stay separate: these are in-memory runs (no
    /// sockets, no scene, no people). The long authority runs carry the category "Phase04Long".
    /// </summary>
    public sealed class Phase04IntegratedConformanceTests
    {
        private static Phase04ImplementationDiscovery discovery;

        private static Phase04ImplementationDiscovery Discovery => discovery ?? (discovery = Phase04ImplementationDiscovery.Discover());

        private static void Verdict(IEnumerable<Phase04Check> checks)
        {
            List<Phase04Check> list = checks.ToList();
            string report = Phase04Checks.Format(list);
            TestContext.WriteLine(report);
            if (list.Any(c => c.Status == Phase04CheckStatus.Failed)) Assert.Fail(report);
            if (list.Any(c => c.Status == Phase04CheckStatus.Pending)) Assert.Inconclusive(report);
        }

        [Test]
        public void Discovery_FindsOneOfEachAndNoEditorOnlyProduction() => Verdict(Discovery.Checks);

        [Test]
        public void Equipment_D1ModelConforms()
        {
            if (Discovery.EquipmentModelType == null) Assert.Inconclusive("pendiente de D1: no IBaseEquipmentModel");
            Verdict(BaseEquipmentConformance.RunAll(() => Discovery.CreateEquipmentModel(BaseOperationsFixtures.Scenario()),
                Discovery.EquipmentModelType.FullName));
        }

        [Test]
        public void AirCoupling_OnTheSameBaseModel()
        {
            if (Discovery.BaseModelType == null) Assert.Inconclusive("no single production IOutpostSimulation");
            var scenario = BaseOperationsFixtures.Scenario();
            Verdict(BaseEquipmentConformance.RunAirCoupling(() => Discovery.CreateBaseModel(scenario), scenario.Expedition.Outpost.Simulation,
                Discovery.BaseModelType.FullName));
        }

        [Test, Category("Phase04Long")]
        public void Authority_InMemorySuite() => Verdict(BaseOperationsAuthorityConformance.RunAll(Discovery));

        [Test, Category("Phase04Long")]
        public void LocalPorts_ContinuityThroughD3Intents() => Verdict(BaseOperationsAuthorityConformance.RunThroughLocalPorts(Discovery));

        [Test]
        public void Regression_Phase2And3DiscoveryAndFixtures() =>
            Verdict(Phase04Validation.Regression().Where(c => c.Status != Phase04CheckStatus.Info));
    }
}
