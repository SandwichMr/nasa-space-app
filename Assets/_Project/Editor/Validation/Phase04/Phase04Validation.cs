using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Tooling.Validation.Phase02;
using Trainer.Tooling.Validation.Phase03;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>
    /// Entry point of the managed D5 phase 4 gates (no scene, no build): discovery, scenario validation, D1 equipment and air
    /// conformance, the D2 authority suite in memory, the D3 local-port run, and regression of the phase 2/3 discovery and
    /// fixtures. Every missing implementation yields Pending with its owner; nothing is counted as Passed by absence.
    /// The human session (30+ real minutes, two people) is always reported Pending here: no tool can produce it.
    /// </summary>
    public static class Phase04Validation
    {
        public static List<Phase04Check> RunManaged(bool includeLongRuns = true)
        {
            var checks = new List<Phase04Check>();
            Phase04ImplementationDiscovery d = Phase04ImplementationDiscovery.Discover();
            checks.AddRange(d.Checks);

            checks.AddRange(Safe("fixture scenario", Phase04Kinds.ScenarioValidator,
                () => BaseOperationsScenarioValidator.Validate(BaseOperationsFixtures.Scenario(), "fixture")));

            if (d.EquipmentModelType != null)
                checks.AddRange(Safe("equipment conformance", Phase04Kinds.EquipmentConformance,
                    () => BaseEquipmentConformance.RunAll(() => d.CreateEquipmentModel(BaseOperationsFixtures.Scenario()), d.EquipmentModelType.FullName)));
            else checks.Add(Phase04Checks.Pending("equipment conformance", Phase04Kinds.EquipmentConformance, "pendiente de D1: no IBaseEquipmentModel"));

            if (d.BaseModelType != null)
            {
                BaseOperationsScenario fixture = BaseOperationsFixtures.Scenario();
                checks.AddRange(Safe("air coupling", Phase04Kinds.AirCoupling, () => BaseEquipmentConformance.RunAirCoupling(
                    () => d.CreateBaseModel(fixture), fixture.Expedition.Outpost.Simulation, d.BaseModelType.FullName)));
            }
            else checks.Add(Phase04Checks.Pending("air coupling", Phase04Kinds.AirCoupling, "no single production IOutpostSimulation"));

            if (includeLongRuns)
            {
                checks.AddRange(Safe("authority suite", Phase04Kinds.AuthorityInMemory, () => BaseOperationsAuthorityConformance.RunAll(d)));
                checks.AddRange(Safe("local ports", Phase04Kinds.PortsInMemory, () => BaseOperationsAuthorityConformance.RunThroughLocalPorts(d)));
            }
            else checks.Add(Phase04Checks.Pending("authority suite and local ports", Phase04Kinds.AuthorityInMemory, "skipped in the quick run"));

            checks.AddRange(Regression());
            checks.Add(Phase04Checks.Pending("two people play the build for 30+ real minutes", Phase04Kinds.HumanSession,
                "pendiente: partida con personas (receta en docs/phase-04/handoffs/05_CONTINUITY_VALIDATION_HANDOFF.md); no tool can produce it"));
            return checks;
        }

        /// <summary>Phase 2 discovery stays unambiguous and the phase 3 fixture plan still validates.</summary>
        public static List<Phase04Check> Regression()
        {
            var checks = new List<Phase04Check>();
            checks.AddRange(Safe("phase 2 discovery", Phase04Kinds.Regression, () =>
                Phase02ImplementationDiscovery.Discover().Checks.Select(c => new Phase04Check("phase 2 discovery: " + c.Name, Phase04Kinds.Regression,
                    c.Status == Phase02CheckStatus.Passed ? Phase04CheckStatus.Passed
                    : c.Status == Phase02CheckStatus.Failed ? Phase04CheckStatus.Failed
                    : c.Status == Phase02CheckStatus.Pending ? Phase04CheckStatus.Pending : Phase04CheckStatus.Info, c.Detail)).ToList()));
            checks.AddRange(Safe("phase 3 fixture plan", Phase04Kinds.Regression, () =>
                ExpeditionScenarioValidator.Validate(ExpeditionFixtures.Scenario())
                    .Select(c => Phase04Checks.Result("phase 3 fixture: " + c.Name, Phase04Kinds.Regression, c.Passed, c.Detail)).ToList()));
            checks.Add(Phase04Checks.Info("phase 2/3 finite modes", Phase04Kinds.Regression,
                "the phase 2/3 EditMode and PlayMode suites and Phase02/Phase03 build checks must be run natively (see handoff); not re-implemented here"));
            return checks;
        }

        private static IEnumerable<Phase04Check> Safe(string what, string kind, Func<List<Phase04Check>> run)
        {
            try { return run(); }
            catch (Phase04CheckPending pending) { return new[] { Phase04Checks.Pending(what, kind, pending.Message) }; }
            catch (Exception e)
            {
                return new[] { new Phase04Check(what, kind, Phase04CheckStatus.Failed, "Unexpected " + e.GetType().Name + ": " + e.Message) };
            }
        }
    }
}
