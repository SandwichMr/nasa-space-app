using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>One in-memory composition of the REAL phase 4 authority built by the discovery (no scene, no network).</summary>
    public sealed class AuthorityInstance
    {
        public IBaseOperationsAuthority Authority;
        /// <summary>The authority's own tick (public void Tick/Advance/Step(double)).</summary>
        public Action<double> Advance;
        /// <summary>Spies when the constructor takes the interfaces (null when it takes concrete types).</summary>
        public RecordingBaseSimulation Base;
        public RecordingEquipmentModel Equipment;
        public OutpostBaseSimulation PhysicalBase;
        public IEvaSimulation Eva;
        /// <summary>How each constructor argument was obtained (for the report).</summary>
        public string Composition;
    }

    /// <summary>
    /// Finds the REAL phase 4 implementations in the loaded assemblies so the same D5 checks run unchanged before and after
    /// integration. Fakes, development fixtures, tooling, tests and Editor-only types are excluded from production; an
    /// Editor-only type that implements a production contract is reported as Failed (it cannot ship in a player). Shapes are
    /// explicit; anything the resolver cannot build is Pending with the reason, never adapted by guessing.
    ///
    /// <list type="bullet">
    /// <item>D1 <see cref="IBaseEquipmentModel"/>: exactly one; built like any argument below.</item>
    /// <item>D1 <see cref="IAirServiceCapacity"/>: implemented by the (single) phase 2 <see cref="IOutpostSimulation"/> type and by no other type.</item>
    /// <item>D2 <see cref="IBaseOperationsAuthority"/>: exactly one, implementing neither IOutpostAuthority nor IExpeditionAuthority;
    /// the public constructor with the most parameters whose arguments all resolve; tick = public void Tick/Advance/Step(double).</item>
    /// <item>D3 local port: an <see cref="IBaseOperationsSessionPort"/> with a constructor taking the authority, and a public
    /// parameterless <c>Join()</c>; intents: an <see cref="IBaseOperationsIntents"/> built from (port[, scenario]).</item>
    /// <item>D4 views: <see cref="IBaseOperationsView"/> MonoBehaviours (listed; bound by D2's composition, audited in the scene).</item>
    /// </list>
    /// Argument resolution (per parameter type): the scenario (phase 4, 3 or 2 view of it); a parameter with a default value;
    /// the single production type assignable to it, built by a public parameterless constructor, a public static
    /// <c>For(scenario)</c>, a constructor (OutpostLayout, CrewMovementSettings), or a constructor taking a parameters type
    /// with one public static parameterless factory (or <c>SyntheticTraining()</c> when several). IOutpostSimulation and
    /// IBaseEquipmentModel interface parameters receive the recording spies around the real objects.
    /// </summary>
    public sealed class Phase04ImplementationDiscovery
    {
        private static readonly string[] TickNames = { "Tick", "Advance", "Step" };

        private List<Type> production = new List<Type>();

        public Type EquipmentModelType { get; private set; }
        public Type BaseModelType { get; private set; }
        public Type AuthorityType { get; private set; }
        public Type LocalPortType { get; private set; }
        public Type IntentsType { get; private set; }
        public List<Type> ViewTypes { get; } = new List<Type>();
        public List<Phase04Check> Checks { get; } = new List<Phase04Check>();

        public static Phase04ImplementationDiscovery Discover(IEnumerable<Assembly> assemblies = null)
        {
            var result = new Phase04ImplementationDiscovery();
            List<Assembly> all = (assemblies ?? AppDomain.CurrentDomain.GetAssemblies()).ToList();
            List<Type> types = all.SelectMany(SafeTypes).Where(t => t != null && t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters).ToList();
            result.production = types.Where(IsProductionCandidate).ToList();
            result.CheckEditorOnly(types);
            result.FindEquipment();
            result.FindAirCapacity();
            result.FindAuthority();
            result.FindPortsAndIntents();
            result.FindViews();
            result.CheckEarlierPhases();
            return result;
        }

        /// <summary>The runtime (player) assembly that holds the contracts; visible; not tooling, tests, development or fakes.</summary>
        public static bool IsProductionCandidate(Type t)
        {
            if (t == null || !t.IsClass || t.IsAbstract || t.ContainsGenericParameters) return false;
            if (t.Assembly != typeof(IBaseOperationsAuthority).Assembly || !t.IsVisible) return false;
            return !IsExcludedByName(t);
        }

        private static bool IsExcludedByName(Type t)
        {
            // The project's tests share the default assemblies; some network fixtures use the global namespace.
            if (t.DeclaringType != null && IsExcludedByName(t.DeclaringType)) return true;
            if (t.Name.EndsWith("Tests", StringComparison.Ordinal) || t.Name.EndsWith("TestAuthority", StringComparison.Ordinal)) return true;
            string ns = t.Namespace ?? string.Empty;
            if (ns.StartsWith("Trainer.Tooling", StringComparison.Ordinal)) return true;
            if (ns == "Trainer.Tests" || ns.StartsWith("Trainer.Tests.", StringComparison.Ordinal)) return true;
            if (ns.StartsWith("Trainer.Application.Development", StringComparison.Ordinal)) return true;
            if (t.Name.IndexOf("Fake", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.Assembly.GetName().Name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static readonly Type[] Phase4Contracts =
        {
            typeof(IBaseEquipmentModel), typeof(IAirServiceCapacity), typeof(IBaseOperationsAuthority), typeof(IBaseOperationsSessionPort),
            typeof(IBaseOperationsIntents), typeof(IBaseOperationsView)
        };

        /// <summary>An implementation of a phase 4 production contract outside the player assembly can never ship.</summary>
        private void CheckEditorOnly(List<Type> types)
        {
            var offenders = types.Where(t => t.Assembly != typeof(IBaseOperationsAuthority).Assembly && !IsExcludedByName(t)
                    && Phase4Contracts.Any(c => c.IsAssignableFrom(t)))
                .Select(t => t.FullName + " [" + t.Assembly.GetName().Name + "]").ToList();
            Checks.Add(Phase04Checks.Result("phase 4 contracts implemented only in the player assembly", Phase04Kinds.Discovery, offenders.Count == 0,
                offenders.Count == 0 ? "no Editor-only or foreign-assembly production implementation"
                    : "cannot ship in a player: " + string.Join(", ", offenders)));
        }

        private List<Type> Implementing(Type contract) => production.Where(contract.IsAssignableFrom).ToList();

        private Type PickOne(Type contract, string owner)
        {
            List<Type> found = Implementing(contract);
            string what = contract.Name + " (" + owner + ")";
            if (found.Count == 0)
            {
                Checks.Add(Phase04Checks.Pending(what, Phase04Kinds.Discovery, "pendiente de " + owner + ": no production implementation in the loaded assemblies"));
                return null;
            }
            if (found.Count > 1)
            {
                Checks.Add(new Phase04Check(what, Phase04Kinds.Discovery, Phase04CheckStatus.Failed,
                    "several production implementations (one authority, one model, one inventory): " + string.Join(", ", found.Select(f => f.FullName))));
                return null;
            }
            Checks.Add(Phase04Checks.Info(what, Phase04Kinds.Discovery, found[0].FullName));
            return found[0];
        }

        private void FindEquipment() => EquipmentModelType = PickOne(typeof(IBaseEquipmentModel), "D1");

        private void FindAirCapacity()
        {
            List<Type> models = Implementing(typeof(IOutpostSimulation));
            BaseModelType = models.Count == 1 ? models[0] : null;
            List<Type> capacity = Implementing(typeof(IAirServiceCapacity));
            if (capacity.Count == 0)
            {
                Checks.Add(Phase04Checks.Pending("IAirServiceCapacity on the base model (D1)", Phase04Kinds.Discovery,
                    "pendiente de D1: OutpostBaseSimulation does not implement IAirServiceCapacity yet"));
                return;
            }
            bool same = BaseModelType != null && capacity.Count == 1 && capacity[0] == BaseModelType;
            Checks.Add(Phase04Checks.Result("IAirServiceCapacity on the SAME object as the single IOutpostSimulation (D1)", Phase04Kinds.Discovery, same,
                "IOutpostSimulation: " + string.Join(",", models.Select(m => m.Name)) + "; IAirServiceCapacity: " + string.Join(",", capacity.Select(c => c.Name))));
        }

        private void FindAuthority()
        {
            AuthorityType = PickOne(typeof(IBaseOperationsAuthority), "D2");
            if (AuthorityType == null) return;
            bool separate = !typeof(IOutpostAuthority).IsAssignableFrom(AuthorityType) && !typeof(IExpeditionAuthority).IsAssignableFrom(AuthorityType);
            Checks.Add(Phase04Checks.Result("phase 4 authority implements neither IOutpostAuthority nor IExpeditionAuthority", Phase04Kinds.Discovery,
                separate, AuthorityType.Name));
            if (TickOf(AuthorityType) == null)
                Checks.Add(Phase04Checks.Pending("phase 4 authority tick", Phase04Kinds.Discovery,
                    "no public void Tick/Advance/Step(double) on " + AuthorityType.Name + ": timed in-memory checks stay Pending"));
        }

        private void FindPortsAndIntents()
        {
            List<Type> ports = Implementing(typeof(IBaseOperationsSessionPort));
            Checks.Add(Phase04Checks.Info("IBaseOperationsSessionPort implementations (D3)", Phase04Kinds.Discovery,
                ports.Count == 0 ? "none yet" : string.Join(", ", ports.Select(p => p.FullName))));
            LocalPortType = ports.FirstOrDefault(p => p.GetConstructors().Any(c => c.GetParameters().Length >= 1
                && c.GetParameters()[0].ParameterType.IsAssignableFrom(AuthorityType ?? typeof(IBaseOperationsAuthority))
                && c.GetParameters().Skip(1).All(x => x.HasDefaultValue)));
            if (LocalPortType == null)
                Checks.Add(Phase04Checks.Pending("local IBaseOperationsSessionPort over the authority (D3)", Phase04Kinds.Discovery,
                    "pendiente de D3: no port with a constructor (IBaseOperationsAuthority)"));
            else if (LocalPortType.GetMethod("Join", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) == null)
                Checks.Add(Phase04Checks.Pending("local port Join()", Phase04Kinds.Discovery, LocalPortType.Name + " has no public Join(): port runs stay Pending"));
            List<Type> intents = Implementing(typeof(IBaseOperationsIntents));
            if (intents.Count == 0)
                Checks.Add(Phase04Checks.Pending("IBaseOperationsIntents (D3)", Phase04Kinds.Discovery, "pendiente de D3: no interaction client"));
            else if (intents.Count > 1)
                Checks.Add(new Phase04Check("IBaseOperationsIntents (D3)", Phase04Kinds.Discovery, Phase04CheckStatus.Failed,
                    "several interaction clients: " + string.Join(", ", intents.Select(i => i.FullName))));
            else
            {
                IntentsType = intents[0];
                Checks.Add(Phase04Checks.Info("IBaseOperationsIntents (D3)", Phase04Kinds.Discovery, IntentsType.FullName));
            }
        }

        private void FindViews()
        {
            ViewTypes.AddRange(Implementing(typeof(IBaseOperationsView)));
            bool provisionalOnly = ViewTypes.Count > 0 && ViewTypes.All(v => (v.Namespace ?? "").Contains("Provisional"));
            if (ViewTypes.Count == 0 || provisionalOnly)
                Checks.Add(Phase04Checks.Pending("IBaseOperationsView (D4)", Phase04Kinds.Discovery,
                    "pendiente de D4: " + (ViewTypes.Count == 0 ? "no view" : "only provisional views: " + string.Join(", ", ViewTypes.Select(v => v.Name)))));
            else
                Checks.Add(Phase04Checks.Info("IBaseOperationsView (D4)", Phase04Kinds.Discovery, string.Join(", ", ViewTypes.Select(v => v.FullName))));
        }

        /// <summary>Phase 2/3 discovery must stay unambiguous: phase 4 adds no second base model, roster, EVA model or old authority.</summary>
        private void CheckEarlierPhases()
        {
            string[] Names(Type contract) => Implementing(contract).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] outpost = Names(typeof(IOutpostAuthority)), expedition = Names(typeof(IExpeditionAuthority));
            string[] models = Names(typeof(IOutpostSimulation)), rosters = Names(typeof(ICrewRoster)), eva = Names(typeof(IEvaSimulation));
            bool ok = outpost.Length == 1 && expedition.Length == 1 && models.Length == 1 && rosters.Length == 1 && eva.Length == 1;
            Checks.Add(Phase04Checks.Result("one base model, one roster, one EVA model; phase 2/3 authorities unchanged", Phase04Kinds.Regression, ok,
                "IOutpostAuthority: " + string.Join(",", outpost) + "; IExpeditionAuthority: " + string.Join(",", expedition)
                + "; IOutpostSimulation: " + string.Join(",", models) + "; ICrewRoster: " + string.Join(",", rosters)
                + "; IEvaSimulation: " + string.Join(",", eva)));
        }

        // ---- construction ------------------------------------------------------------------------------------------

        public static MethodInfo TickOf(Type type) =>
            TickNames.Select(n => type.GetMethod(n, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(double) }, null))
                .FirstOrDefault(m => m != null && m.ReturnType == typeof(void));

        /// <summary>Builds the discovered equipment model (throws Phase04CheckPending when it cannot).</summary>
        public IBaseEquipmentModel CreateEquipmentModel(BaseOperationsScenario scenario)
        {
            if (EquipmentModelType == null) throw new Phase04CheckPending("pendiente de D1: no IBaseEquipmentModel");
            return (IBaseEquipmentModel)Build(EquipmentModelType, scenario, new List<string>());
        }

        /// <summary>Builds the single production IOutpostSimulation (D1).</summary>
        public IOutpostSimulation CreateBaseModel(BaseOperationsScenario scenario)
        {
            if (BaseModelType == null) throw new Phase04CheckPending("no single production IOutpostSimulation");
            return (IOutpostSimulation)Build(BaseModelType, scenario, new List<string>());
        }

        /// <summary>The real authority over the real models, with spies where the constructor takes the interfaces.</summary>
        public AuthorityInstance CreateAuthority(BaseOperationsScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (AuthorityType == null) throw new Phase04CheckPending("pendiente de D2: no IBaseOperationsAuthority");
            MethodInfo tick = TickOf(AuthorityType);
            if (tick == null) throw new Phase04CheckPending("pendiente de D2: no public Tick/Advance/Step(double) on " + AuthorityType.Name);
            var notes = new List<string>();
            string lastProblem = "no public constructor";
            foreach (ConstructorInfo ctor in AuthorityType.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
            {
                var instance = new AuthorityInstance();
                var args = new List<object>();
                notes.Clear();
                string problem = null;
                foreach (ParameterInfo p in ctor.GetParameters())
                {
                    try { args.Add(Argument(p, scenario, instance, notes)); }
                    catch (Phase04CheckPending pending)
                    {
                        problem = p.Name + ": " + pending.Message;
                        break;
                    }
                }
                if (problem != null)
                {
                    lastProblem = problem;
                    continue;
                }
                instance.Authority = (IBaseOperationsAuthority)Invoke(ctor, args.ToArray());
                IBaseOperationsAuthority target = instance.Authority;
                instance.Advance = dt => Invoke(tick, target, dt);
                instance.Composition = AuthorityType.Name + "(" + string.Join(", ", notes) + ")." + tick.Name + "(double)";
                return instance;
            }
            throw new Phase04CheckPending("cannot build " + AuthorityType.Name + ": " + lastProblem);
        }

        private object Argument(ParameterInfo p, BaseOperationsScenario scenario, AuthorityInstance instance, List<string> notes)
        {
            Type t = p.ParameterType;
            if (t == typeof(IOutpostSimulation))
            {
                instance.Base = new RecordingBaseSimulation(CreateBaseModel(scenario));
                instance.PhysicalBase = instance.Base.Inner as OutpostBaseSimulation;
                notes.Add("spy(" + BaseModelType.Name + ")");
                return instance.Base;
            }
            if (t == typeof(IBaseEquipmentModel))
            {
                instance.Equipment = new RecordingEquipmentModel(CreateEquipmentModel(scenario));
                notes.Add("spy(" + EquipmentModelType.Name + ")");
                return instance.Equipment;
            }
            if (t == typeof(OutpostBaseSimulation))
            {
                instance.PhysicalBase = (OutpostBaseSimulation)Build(t, scenario, notes);
                return instance.PhysicalBase;
            }
            if (t == typeof(IEvaSimulation))
            {
                instance.Eva = (IEvaSimulation)Build(t, scenario, notes);
                return instance.Eva;
            }
            if (TryScenario(t, scenario, out object view))
            {
                notes.Add(t.Name);
                return view;
            }
            if (p.HasDefaultValue && (t.IsPrimitive || t == typeof(string) || t.IsEnum || Nullable.GetUnderlyingType(t) != null))
            {
                notes.Add(p.Name + "=default");
                return p.DefaultValue;
            }
            object built = Build(t, scenario, notes);
            return built;
        }

        private static bool TryScenario(Type t, BaseOperationsScenario scenario, out object value)
        {
            value = null;
            if (t == typeof(BaseOperationsScenario)) value = scenario;
            else if (t == typeof(ExpeditionScenario)) value = scenario.Expedition;
            else if (t == typeof(OutpostScenario)) value = scenario.Expedition.Outpost;
            else if (t == typeof(OutpostLayout)) value = scenario.Layout;
            else if (t == typeof(CrewMovementSettings)) value = scenario.Expedition.Outpost.Movement;
            else if (t == typeof(OutpostConfiguration)) value = scenario.Expedition.Outpost.Simulation;
            else if (t == typeof(BaseEquipmentConfiguration)) value = scenario.Equipment;
            return value != null;
        }

        /// <summary>Builds the single production type assignable to <paramref name="wanted"/>.</summary>
        private object Build(Type wanted, BaseOperationsScenario scenario, List<string> notes)
        {
            List<Type> candidates = wanted.IsInterface || wanted.IsAbstract ? Implementing(wanted)
                : (IsProductionCandidate(wanted) ? new List<Type> { wanted } : new List<Type>());
            if (candidates.Count != 1)
                throw new Phase04CheckPending(wanted.Name + ": " + (candidates.Count == 0 ? "no production implementation"
                    : "several: " + string.Join(", ", candidates.Select(c => c.Name))));
            Type c0 = candidates[0];

            ConstructorInfo empty = c0.GetConstructor(Type.EmptyTypes);
            if (empty != null)
            {
                notes.Add(c0.Name + "()");
                return Invoke(empty);
            }
            foreach (Type scenarioType in new[] { typeof(BaseOperationsScenario), typeof(ExpeditionScenario), typeof(OutpostScenario) })
            {
                MethodInfo forMethod = c0.GetMethod("For", BindingFlags.Public | BindingFlags.Static, null, new[] { scenarioType }, null);
                if (forMethod != null && wanted.IsAssignableFrom(forMethod.ReturnType))
                {
                    TryScenario(scenarioType, scenario, out object arg);
                    notes.Add(c0.Name + ".For(" + scenarioType.Name + ")");
                    return Invoke(forMethod, null, arg);
                }
            }
            ConstructorInfo layout = c0.GetConstructor(new[] { typeof(OutpostLayout), typeof(CrewMovementSettings) });
            if (layout != null)
            {
                notes.Add(c0.Name + "(layout, movement)");
                return Invoke(layout, scenario.Layout, scenario.Expedition.Outpost.Movement);
            }
            foreach (ConstructorInfo ctor in c0.GetConstructors().Where(k => k.GetParameters().Length == 1))
            {
                Type parameters = ctor.GetParameters()[0].ParameterType;
                if (TryScenario(parameters, scenario, out object direct))
                {
                    notes.Add(c0.Name + "(" + parameters.Name + ")");
                    return Invoke(ctor, direct);
                }
                MethodInfo[] factories = parameters.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.ReturnType == parameters && m.GetParameters().Length == 0).ToArray();
                MethodInfo factory = factories.Length == 1 ? factories[0] : factories.FirstOrDefault(m => m.Name == "SyntheticTraining");
                if (factory != null)
                {
                    notes.Add(c0.Name + "(" + parameters.Name + "." + factory.Name + "())");
                    return Invoke(ctor, Invoke(factory, null));
                }
            }
            throw new Phase04CheckPending(c0.Name + ": constructor shape not supported by the D5 resolver (add it at integration)");
        }

        /// <summary>D3's local port over <paramref name="authority"/> and its interaction client (Pending when missing).</summary>
        public (IBaseOperationsSessionPort port, IBaseOperationsIntents intents, Func<OutpostMembership> join) CreateLocalClient(
            IBaseOperationsAuthority authority, BaseOperationsScenario scenario)
        {
            if (LocalPortType == null) throw new Phase04CheckPending("pendiente de D3: no local IBaseOperationsSessionPort(IBaseOperationsAuthority)");
            if (IntentsType == null) throw new Phase04CheckPending("pendiente de D3: no IBaseOperationsIntents");
            ConstructorInfo portCtor = LocalPortType.GetConstructors().First(c => c.GetParameters().Length >= 1
                && c.GetParameters()[0].ParameterType.IsAssignableFrom(authority.GetType()) && c.GetParameters().Skip(1).All(x => x.HasDefaultValue));
            object[] portArgs = portCtor.GetParameters().Select((x, i) => i == 0 ? authority : x.DefaultValue).ToArray();
            var port = (IBaseOperationsSessionPort)Invoke(portCtor, portArgs);
            MethodInfo join = LocalPortType.GetMethod("Join", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (join == null) throw new Phase04CheckPending(LocalPortType.Name + " has no public Join()");
            ConstructorInfo withScenario = IntentsType.GetConstructor(new[] { typeof(IBaseOperationsSessionPort), typeof(BaseOperationsScenario) });
            ConstructorInfo portOnly = IntentsType.GetConstructor(new[] { typeof(IBaseOperationsSessionPort) });
            object intents = withScenario != null ? Invoke(withScenario, port, scenario)
                : portOnly != null ? Invoke(portOnly, port)
                : throw new Phase04CheckPending(IntentsType.Name + ": constructor (port[, scenario]) not found");
            Func<OutpostMembership> joinCall = () =>
            {
                object result = Invoke(join, port);
                return result as OutpostMembership ?? port.Membership;
            };
            return (port, (IBaseOperationsIntents)intents, joinCall);
        }

        private static object Invoke(ConstructorInfo c, params object[] args)
        {
            try { return c.Invoke(args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        private static object Invoke(MethodInfo m, object target, params object[] args)
        {
            try { return m.Invoke(target, args); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
            catch (Exception) { return Enumerable.Empty<Type>(); }
        }
    }
}
