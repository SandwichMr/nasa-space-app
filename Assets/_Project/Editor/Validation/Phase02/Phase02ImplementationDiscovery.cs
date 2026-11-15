using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// Finds the REAL phase 2 implementations in the loaded assemblies so that the same checks run unchanged
    /// before and after integration. Fakes, development fixtures and validation doubles are excluded. Supported
    /// shapes are explicit; anything else is reported as Pending with the reason, never adapted by guessing.
    ///
    /// <list type="bullet">
    /// <item>ICrewRoster: ctor (OutpostLayout, CrewMovementSettings) or (OutpostScenario).</item>
    /// <item>IOutpostSimulation: public parameterless ctor.</item>
    /// <item>IOutpostAuthority: ctor (OutpostScenario, IOutpostSimulation, ICrewRoster) or (OutpostScenario,
    /// IOutpostSimulation); tick = public instance void method Advance/Step/Tick(double).</item>
    /// </list>
    /// </summary>
    public sealed class Phase02ImplementationDiscovery
    {
        private static readonly string[] TickNames = { "Advance", "Step", "Tick" };

        public Func<OutpostLayout, CrewMovementSettings, ICrewRoster> Roster { get; private set; }
        public Func<IOutpostSimulation> Simulation { get; private set; }
        public AuthorityHarness Authority { get; private set; } = new AuthorityHarness();
        public string RosterName { get; private set; }
        public string SimulationName { get; private set; }
        public string AuthorityName { get; private set; }
        public List<Phase02Check> Checks { get; } = new List<Phase02Check>();

        public static Phase02ImplementationDiscovery Discover(IEnumerable<Assembly> assemblies = null)
        {
            var result = new Phase02ImplementationDiscovery();
            List<Type> types = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).SelectMany(SafeTypes)
                .Where(IsProductionCandidate).ToList();
            result.FindRoster(types);
            result.FindSimulation(types);
            result.FindAuthority(types);
            return result;
        }

        public static bool IsProductionCandidate(Type t)
        {
            if (t == null || !t.IsClass || t.IsAbstract || t.ContainsGenericParameters) return false;
            string ns = t.Namespace ?? string.Empty;
            if (ns.StartsWith("Trainer.Tooling", StringComparison.Ordinal)) return false;
            if (ns.StartsWith("Trainer.Application.Development", StringComparison.Ordinal)) return false;
            if (t.Name.IndexOf("Fake", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.Assembly.GetName().Name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        private void FindRoster(List<Type> types)
        {
            var found = new List<(Type, Func<OutpostLayout, CrewMovementSettings, ICrewRoster>)>();
            foreach (Type t in types.Where(t => typeof(ICrewRoster).IsAssignableFrom(t)))
            {
                ConstructorInfo a = t.GetConstructor(new[] { typeof(OutpostLayout), typeof(CrewMovementSettings) });
                if (a != null) { found.Add((t, (l, m) => (ICrewRoster)Invoke(a, l, m))); continue; }
                ConstructorInfo b = t.GetConstructor(new[] { typeof(OutpostScenario) });
                if (b != null) { found.Add((t, (l, m) => (ICrewRoster)Invoke(b, Phase02Fixtures.WithLayoutAndMovement(l, m)))); continue; }
                Pending("ICrewRoster " + t.FullName, "Constructor shape not supported; add it to the discovery at integration.");
            }
            if (Pick(found, "ICrewRoster (D3)", out var pick)) { Roster = pick.Item2; RosterName = pick.Item1.FullName; }
        }

        private void FindSimulation(List<Type> types)
        {
            var found = new List<(Type, Func<IOutpostSimulation>)>();
            foreach (Type t in types.Where(t => typeof(IOutpostSimulation).IsAssignableFrom(t)))
            {
                ConstructorInfo c = t.GetConstructor(Type.EmptyTypes);
                if (c != null) { found.Add((t, () => (IOutpostSimulation)Invoke(c))); continue; }
                // ctor(TParameters) where TParameters has exactly one public static parameterless factory
                // returning it (e.g. D1's labelled synthetic parameters). The factory used is reported.
                (ConstructorInfo ctor, MethodInfo factory) = t.GetConstructors()
                    .Where(k => k.GetParameters().Length == 1)
                    .Select(k => (k, Factories(k.GetParameters()[0].ParameterType)))
                    .Where(x => x.Item2.Length == 1).Select(x => (x.k, x.Item2[0])).FirstOrDefault();
                if (ctor != null)
                {
                    found.Add((t, () => (IOutpostSimulation)Invoke(ctor, InvokeStatic(factory))));
                    Checks.Add(new Phase02Check("IOutpostSimulation parameters", Phase02CheckKinds.Discovery, Phase02CheckStatus.Info,
                        t.Name + " built with " + factory.DeclaringType.Name + "." + factory.Name + "() (rates are D1's; conformance does not judge them)."));
                }
                else Pending("IOutpostSimulation " + t.FullName, "No parameterless constructor nor ctor(parameters) with a single static factory.");
            }
            if (Pick(found, "IOutpostSimulation (D1)", out var pick)) { Simulation = pick.Item2; SimulationName = pick.Item1.FullName; }
        }

        private void FindAuthority(List<Type> types)
        {
            var found = new List<(Type, AuthorityHarness)>();
            foreach (Type t in types.Where(t => typeof(IOutpostAuthority).IsAssignableFrom(t)))
            {
                var harness = new AuthorityHarness { Label = t.Name, CreateSimulation = Simulation };
                ConstructorInfo three = t.GetConstructor(new[] { typeof(OutpostScenario), typeof(IOutpostSimulation), typeof(ICrewRoster) });
                ConstructorInfo two = t.GetConstructor(new[] { typeof(OutpostScenario), typeof(IOutpostSimulation) });
                if (three != null && Roster != null)
                    harness.Create = (s, sim) => (IOutpostAuthority)Invoke(three, s, sim, Roster(s.Layout, s.Movement));
                else if (two != null)
                    harness.Create = (s, sim) => (IOutpostAuthority)Invoke(two, s, sim);
                else
                {
                    Pending("IOutpostAuthority " + t.FullName, three != null ? "Needs a roster and none was discovered." : "Constructor shape not supported.");
                    continue;
                }
                MethodInfo tick = TickNames.Select(n => t.GetMethod(n, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(double) }, null))
                    .FirstOrDefault(m => m != null);
                if (tick != null) harness.Advance = (a, dt) => Invoke(tick, a, dt);
                found.Add((t, harness));
            }
            if (Pick(found, "IOutpostAuthority (D2)", out var pick))
            {
                Authority = pick.Item2;
                AuthorityName = pick.Item1.FullName;
                if (Authority.Advance == null)
                    Checks.Add(new Phase02Check("IOutpostAuthority tick", Phase02CheckKinds.Discovery, Phase02CheckStatus.Pending,
                        "No public Advance/Step/Tick(double): timed authority checks stay Pending."));
            }
        }

        private bool Pick<T>(List<(Type, T)> found, string what, out (Type, T) pick)
        {
            pick = default;
            if (found.Count == 0)
            {
                Pending(what, "No production implementation in the loaded assemblies.");
                return false;
            }
            if (found.Count > 1)
            {
                Pending(what, "Several candidates: " + string.Join(", ", found.Select(f => f.Item1.FullName)) + ".");
                return false;
            }
            pick = found[0];
            Checks.Add(new Phase02Check(what, Phase02CheckKinds.Discovery, Phase02CheckStatus.Info, found[0].Item1.FullName));
            return true;
        }

        private void Pending(string what, string why) =>
            Checks.Add(new Phase02Check(what, Phase02CheckKinds.Discovery, Phase02CheckStatus.Pending, why));

        private static object Invoke(ConstructorInfo c, params object[] args)
        {
            try { return c.Invoke(args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        private static MethodInfo[] Factories(Type parameterType) =>
            parameterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == parameterType && m.GetParameters().Length == 0).ToArray();

        private static object InvokeStatic(MethodInfo m)
        {
            try { return m.Invoke(null, null); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        private static void Invoke(MethodInfo m, object target, params object[] args)
        {
            try { m.Invoke(target, args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        /// <summary>Runs every conformance suite that has a discovered implementation.</summary>
        public List<Phase02Check> RunConformance()
        {
            var checks = new List<Phase02Check>(Checks);
            if (Roster != null) checks.AddRange(CrewRosterConformance.Run(Roster, RosterName));
            if (Simulation != null) checks.AddRange(OutpostSimulationConformance.Run(Simulation, SimulationName));
            checks.AddRange(OutpostAuthorityConformance.Run(Authority));
            return checks;
        }
    }
}
