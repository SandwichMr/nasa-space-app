using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Unity.Bootstrap.Incidents;
using Trainer.Unity.Controls;
using Trainer.Unity.Validation.Phase04;
using UnityEngine;

namespace Trainer.Unity.Validation.Phase05
{
    /// <summary>
    /// Phase 5 acceptance pilot (D5). INERT unless the process is launched with <c>-trainer-phase5-autopilot ROLE</c>:
    /// <list type="bullet">
    /// <item><c>observer</c>: only logs (also the only role a dedicated -batchmode authority can have, since it has no player).</item>
    /// <item><c>repair</c>: plays like a member through the SAME port and intents as the keyboard: walks to the power panel when a power
    /// incident is open, inspects, resets the converter or isolates / replaces / restores, and goes to the shelter panel area while a
    /// shelter instruction is in force.</item>
    /// <item><c>science</c>: accepts the first offered assignment (no exterior work is driven; it shows the board flow).</item>
    /// </list>
    /// Every role logs, with the prefix <c>[P5]</c>: the setup (mode, seed, configuration hash, data status) once, each new incident
    /// phase, director decision, issued advisory and assignment state once, and a status line every 30 real seconds. Options:
    /// <c>-trainer-autopilot-minutes N</c> (default 30) then it quits with 0 when the session stayed coherent (clock advanced, no
    /// contract error observed), 1 otherwise. It never touches the authority directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LivingBaseAutopilot : MonoBehaviour
    {
        public const string Argument = "-trainer-phase5-autopilot";
        private const double Arrival = 0.2;

        private enum Role { Observer, Repair, Science }

        private bool active;
        private Role role;
        private double minutes = 30;
        private double elapsed;
        private float nextStatus;
        private double quitAt = -1;
        private LivingBaseSceneComposition composition;
        private ILivingBaseSessionPort port;
        private ILivingBaseIntents intents;
        private LivingBaseScenario scenario;
        private CrewInputSender movement;
        private Phase04Navigator navigator;
        private List<BasePosition> path;
        private StationId pathStation;
        private int pathIndex;
        private bool setupLogged;
        private double lastSimulated = -1;
        private long snapshots;
        private string lastAnswer;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> problems = new List<string>();

        public bool IsActive => active;

        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = Value(args, Argument);
            if (value == null) return;
            active = true;
            role = string.Equals(value, "repair", StringComparison.OrdinalIgnoreCase) ? Role.Repair
                : string.Equals(value, "science", StringComparison.OrdinalIgnoreCase) ? Role.Science : Role.Observer;
            if (double.TryParse(Value(args, "-trainer-autopilot-minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && m > 0)
                minutes = m;
            UnityEngine.Application.targetFrameRate = 60;
            Debug.Log($"[P5] autopilot active role={role} minutes={minutes.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        private void Start()
        {
            if (!active) return;
            composition = FindAnyObjectByType<LivingBaseSceneComposition>();
            if (role == Role.Observer) return;
            foreach (MonoBehaviour behaviour in Phase04.BaseOperationsAutopilot.SceneBehaviours())
                if (behaviour is CrewKeyboardInput || behaviour.GetType().Name.EndsWith("InteractionInput", StringComparison.Ordinal))
                    behaviour.enabled = false;
        }

        private void Update()
        {
            if (!active) return;
            if (quitAt >= 0)
            {
                if (Time.unscaledTime >= quitAt) UnityEngine.Application.Quit(problems.Count == 0 && lastSimulated > 0 ? 0 : 1);
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            Bind();
            if (port != null && port.TryGetLivingBase(out LivingBaseSnapshot s) && s != null)
            {
                Observe(s);
                if (role == Role.Repair) Repair(s);
                else if (role == Role.Science) Science(s);
            }
            if (Time.unscaledTime >= nextStatus)
            {
                nextStatus = Time.unscaledTime + 30f;
                Status();
            }
            if (elapsed >= minutes * 60)
            {
                Status();
                Debug.Log($"[P5] RESULT {(problems.Count == 0 && lastSimulated > 0 ? "PASS" : "FAIL")} role={role} real=" +
                          elapsed.ToString("0.0", CultureInfo.InvariantCulture) + "s simulated=" + lastSimulated.ToString("0.0", CultureInfo.InvariantCulture) +
                          "s snapshots=" + snapshots + (problems.Count > 0 ? " problems=" + string.Join("; ", problems) : ""));
                movement?.Update(0, 0);
                quitAt = Time.unscaledTime + 3f;
            }
        }

        private void Bind()
        {
            if (composition == null || port != null) return;
            ILivingBaseSessionPort p = composition.PresentedPort;
            if (p == null) return;
            if (composition.Authority != null && !setupLogged)
            {
                Debug.Log("[P5] setup " + LivingBaseBootstrap.DescribeData(composition.Authority) + " seed=" + composition.Authority.Setup.Seed);
            }
            if (role != Role.Observer && !p.Membership.IsJoined) return;
            port = p;
            intents = composition.LivingIntents;
            scenario = composition.Scenario;
            port.LivingBaseChanged += _ => snapshots++;
            if (intents != null) intents.Answered += reason => lastAnswer = reason;
            if (role != Role.Observer)
            {
                movement = new CrewInputSender(port);
                navigator = new Phase04Navigator(scenario.BaseOperations.Expedition);
                if (port.TryGetBaseOperations(out var b) && b.Outpost.State == Trainer.Application.SessionState.Ready)
                    port.Submit(ExpeditionCommand.Session(Guid.NewGuid(), b.Epoch, ExpeditionAction.Resume));
            }
        }

        private void Observe(LivingBaseSnapshot s)
        {
            if (!setupLogged)
            {
                setupLogged = true;
                Debug.Log($"[P5] setup mode={s.Setup.RequestedMode} seed={s.Setup.Seed} prng=v{s.Director.PrngVersion} config={s.Director.ConfigurationHash} " +
                          $"data={s.Data.Availability}{(s.Data.ProblemKey != null ? "(" + s.Data.ProblemKey + ")" : "")}" +
                          $"{(s.Data.Dataset != null ? " dataset=" + s.Data.Dataset.DatasetId + " sha256=" + s.Data.Dataset.Sha256 : "")}");
            }
            if (s.SimulatedTimeSeconds + 1e-9 < lastSimulated && s.Epoch == 1) problems.Add("clock went backwards");
            lastSimulated = s.SimulatedTimeSeconds;
            string t = s.SimulatedTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture);
            foreach (IncidentRecord i in s.Incidents)
                Once($"inc:{i.Id}:{i.Phase}", $"[P5] t={t} incident {i.Key} ({i.Kind}) phase={i.Phase}{(i.ResultKey != null ? " result=" + i.ResultKey : "")}");
            foreach (DirectorDecisionRecord d in s.Decisions)
                Once($"dec:{s.Epoch}:{d.Sequence}", $"[P5] t={d.AtSeconds.ToString("0.0", CultureInfo.InvariantCulture)} director candidate={d.CandidateKey ?? "-"} " +
                                                     $"accepted={d.Accepted} reason={d.ReasonKey}");
            if (s.SpaceWeather != null)
                foreach (EpisodeMessageView m in s.SpaceWeather.Issued)
                    Once($"msg:{s.SpaceWeather.IncidentId}:{m.Message.Id}:{m.Validity}",
                        $"[P5] t={t} advisory {m.Message.Id} kind={m.Message.Kind} basis={m.Message.Basis} validity={m.Validity} " +
                        $"label='{s.SpaceWeather.Label}' visibleRecords={s.SpaceWeather.VisibleRecords.Count}");
            foreach (AssignmentRecord a in s.Assignments)
                Once($"asg:{s.Epoch}:{a.Sequence}:{a.State}", $"[P5] t={t} assignment #{a.Sequence} {a.TemplateKey} state={a.State} credits={s.Credits}/{s.CreditsEarned}");
            if (s.Upgrade.Installed) Once("upgrade", $"[P5] t={t} upgrade installed");
        }

        private void Once(string key, string line)
        {
            if (seen.Add(key)) Debug.Log(line);
        }

        private void Status()
        {
            var b = new StringBuilder("[P5] status real=").Append(elapsed.ToString("0", CultureInfo.InvariantCulture)).Append('s');
            if (port != null && port.TryGetLivingBase(out LivingBaseSnapshot s) && s != null)
            {
                int open = 0;
                foreach (IncidentRecord i in s.Incidents) if (i.IsOpen) open++;
                b.Append(" sim=").Append(s.SimulatedTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append("s epoch=").Append(s.Epoch)
                    .Append(" rev=").Append(s.Revision).Append(" state=").Append(s.BaseOperations.Outpost.State).Append(" crew=")
                    .Append(s.BaseOperations.Outpost.Crew.Count).Append(" director=").Append(s.Director.Phase).Append('/').Append(s.Director.Evaluations)
                    .Append(" openIncidents=").Append(open).Append(" credits=").Append(s.Credits).Append(" gen=")
                    .Append(s.Power.EffectiveGenerationWatts.ToString("0", CultureInfo.InvariantCulture)).Append("W data=").Append(s.Data.Availability);
            }
            else b.Append(" (no phase 5 state yet)");
            if (lastAnswer != null) b.Append(" lastAnswer=").Append(lastAnswer);
            Debug.Log(b.ToString());
        }

        // ---- Roles ------------------------------------------------------------------------------------------------

        private void Repair(LivingBaseSnapshot s)
        {
            if (intents == null || intents.HasPending || movement == null) return;
            if (s.BaseOperations.Outpost.State != Trainer.Application.SessionState.Running) return;
            foreach (IncidentOperationProgress op in s.Operations) if (op.Performer == port.Membership.Player) { movement.Update(0, 0); return; }
            if (s.SpaceWeather != null && (s.SpaceWeather.Instruction.State == InstructionState.InForce
                || s.SpaceWeather.Instruction.State == InstructionState.InForceWithoutCurrentInformation))
            {
                WalkTo(s, scenario.BaseOperations.Expedition.Shelter.Panel);
                return;
            }
            IncidentRecord power = null;
            foreach (IncidentRecord i in s.Incidents)
                if (i.IsOpen && (i.Kind == IncidentType.PowerConverterTrip || i.Kind == IncidentType.PowerControllerFault)) power = i;
            if (power == null) { movement.Update(0, 0); return; }
            if (!WalkTo(s, scenario.PowerStation)) return;
            PowerEquipmentStatus p = s.Power;
            bool current = p.Observation != null && p.Observation.Current;
            if (p.Isolated && !p.PartReplaced) intents.RequestPower(IncidentAction.ReplacePowerPart);
            else if (p.Isolated && p.PartReplaced) intents.RequestPower(IncidentAction.RestorePower);
            else if (!current) intents.RequestPower(IncidentAction.InspectPower);
            else if ((p.Observation.Findings & PowerFindings.ControllerDamaged) != 0) intents.RequestPower(IncidentAction.IsolatePower);
            else if ((p.Observation.Findings & PowerFindings.ConverterTripped) != 0) intents.RequestPower(IncidentAction.ResetConverter);
        }

        private void Science(LivingBaseSnapshot s)
        {
            if (intents == null || intents.HasPending) return;
            bool accepted = false;
            foreach (AssignmentRecord a in s.Assignments) if (a.State == AssignmentState.Accepted) accepted = true;
            if (accepted) return;
            foreach (AssignmentRecord a in s.Assignments)
                if (a.State == AssignmentState.Offered && intents.RequestAssignment(IncidentAction.AcceptAssignment, a.Sequence)) return;
        }

        private bool WalkTo(LivingBaseSnapshot s, StationId station)
        {
            if (!station.IsValid || !s.BaseOperations.Outpost.TryGetCrew(port.Membership.Player, out CrewMemberState me)) return false;
            BasePosition position = me.Pose.Position;
            if (!scenario.BaseOperations.Layout.TryGetStation(station, out StationPlacement placement)) return false;
            if (placement.IsWithinReach(position))
            {
                movement.Update(0, 0);
                path = null;
                return true;
            }
            if (path == null || pathStation != station)
            {
                path = navigator.PlanToStation(position, station);
                pathStation = station;
                pathIndex = 0;
                if (path == null) return false;
            }
            while (pathIndex < path.Count - 1 && position.DistanceTo(path[pathIndex]) <= Arrival) pathIndex++;
            BasePosition target = path[pathIndex];
            double dx = target.X - position.X, dy = target.Y - position.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6) { movement.Update(0, 0); return false; }
            movement.Update(dx / d, dy / d);
            return false;
        }

        private static string Value(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }
    }
}
