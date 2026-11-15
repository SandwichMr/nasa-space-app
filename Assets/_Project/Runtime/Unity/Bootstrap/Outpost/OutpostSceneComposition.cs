using System;
using Trainer.Application.Crew;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Presentation.Outpost;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// Scene composition root of the phase 2 interior mission (<c>Phase02_InteriorBase</c>). Decides the process
    /// role with the same rule and argument as phase 1 (<see cref="MissionSceneComposition.ResolveRole"/>,
    /// <c>-trainer-role authority|client</c>; WebGL is always a client), enables only that role's objects and wires:
    ///
    /// <list type="bullet">
    /// <item>Authority: D1 <see cref="OutpostBaseSimulation"/> + D3 <see cref="CrewRoster"/> →
    /// <see cref="OutpostBootstrap.Compose"/> (single <see cref="OutpostAuthority"/> and single runner) →
    /// D3 <see cref="OutpostNetworkHost.Attach"/>. Unless it runs in batch mode (dedicated server), the authority
    /// process also plays: it joins through <see cref="OutpostNetworkHost.CreateLocalPort"/> like any member.</item>
    /// <item>Client: D3 <see cref="OutpostNetworkClient.Prepare"/>. No authority, roster, simulation or runner.</item>
    /// <item>Both: the same port goes to D3 input/avatars, D4 panel/interior and <see cref="OutpostSessionControls"/>.</item>
    /// </list>
    ///
    /// No fake or fixture is used. The scenario asset is shared data: the authority builds its rules from it and a
    /// client uses it only to know the layout and tasks of this build (the published snapshot stays the truth).
    ///
    /// Execution order: runs after the default order so the network components' Start has already run when this
    /// Start composes. <see cref="OutpostNetworkClient"/> reads <c>-trainer-server/-trainer-connect</c> (or the WebGL
    /// query) only if nobody created its connection first; calling Prepare earlier would silently drop them.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class OutpostSceneComposition : MonoBehaviour
    {
        public const string LocalPlayerDetail = "Anfitrión: esta ventana ejecuta la misión y juega";
        public const string DedicatedDetail = "Servidor dedicado: sin jugador local";

        [SerializeField] private MissionProcessRole role = MissionProcessRole.Authority;
        [SerializeField, Tooltip("Read -trainer-role authority|client from the command line (non-WebGL players).")]
        private bool readCommandLine = true;
        [SerializeField, Tooltip("Scenario data of this build (the same asset the authority bootstrap uses).")]
        private OutpostScenarioDefinition scenario;
        [SerializeField, Tooltip("Authority-only composition (scenario + single runner).")]
        private OutpostBootstrap bootstrap;
        [SerializeField] private OutpostNetworkHost networkHost;
        [SerializeField] private OutpostNetworkClient networkClient;
        [SerializeField] private CrewKeyboardInput keyboardInput;
        [SerializeField] private CrewAvatarSync avatarSync;
        [SerializeField] private OutpostPanel panel;
        [SerializeField] private BaseInteriorView interior;
        [SerializeField] private OutpostSessionControls sessionControls;
        [SerializeField, Tooltip("The authority process joins as a player too (ignored in -batchmode).")]
        private bool authorityPlays = true;
        [SerializeField, Tooltip("Objects that exist only in the authority process (bootstrap, runner, network host).")]
        private GameObject[] authorityOnly = Array.Empty<GameObject>();
        [SerializeField, Tooltip("Objects that exist only in client processes (network client).")]
        private GameObject[] clientOnly = Array.Empty<GameObject>();

        private bool roleApplied;
        private OutpostScenario clientScenario;
        private LocalOutpostSessionPort localPort;
        private IOutpostSessionPort presented;
        private OutpostNetworkClient subscribedClient;

        public MissionProcessRole Role { get; private set; }
        public IOutpostSessionPort PresentedPort => presented;
        public OutpostAuthority Authority => bootstrap != null ? bootstrap.Authority : null;
        public LocalOutpostSessionPort LocalPort => localPort;

        private void Awake()
        {
            bool webGl = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            webGl = true;
#endif
            string[] args = readCommandLine && !webGl ? Environment.GetCommandLineArgs() : null;
            ApplyRole(MissionSceneComposition.ResolveRole(role, args, webGl));
        }

        private void Start()
        {
            if (Role == MissionProcessRole.Authority) ComposeAuthority();
            else ComposeClient();
        }

        /// <summary>Fixes the role once and enables only that role's objects (before their Start runs).</summary>
        public void ApplyRole(MissionProcessRole value)
        {
            if (roleApplied)
            {
                if (value != Role) throw new InvalidOperationException("The process role is already fixed.");
                return;
            }
            if (!Enum.IsDefined(typeof(MissionProcessRole), value)) throw new ArgumentOutOfRangeException(nameof(value));
            Role = value;
            roleApplied = true;
            // Disable first so a client never runs authority Start (the network host would listen).
            SetActive(value == MissionProcessRole.Authority ? clientOnly : authorityOnly, false);
            SetActive(value == MissionProcessRole.Authority ? authorityOnly : clientOnly, true);
            Debug.Log($"[OutpostSceneComposition] Role: {value}");
        }

        /// <summary>Authority only: composes the single authority with the real D1/D3 implementations, serves it
        /// on the network and, if this process plays, joins the local player. Idempotent while composed.</summary>
        public OutpostAuthority ComposeAuthority()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Authority) throw new InvalidOperationException("Only the authority composes.");
            if (bootstrap == null) throw new InvalidOperationException("OutpostBootstrap not assigned.");
            if (bootstrap.Authority != null) return bootstrap.Authority;

            // The only place the authoritative model and roster are created; the authority is their only caller.
            OutpostAuthority authority = bootstrap.Compose(CreateSimulation, CreateRoster);
            OutpostScenarioDefinition definition = bootstrap.ScenarioDefinition;
            Debug.Log($"[OutpostSceneComposition] Mission composed: '{definition.DisplayName}'. {definition.Provenance}");
            bootstrap.ShutDown += OnAuthorityShutDown;

            if (networkHost != null) networkHost.Attach(authority);
            else Debug.LogWarning("[OutpostSceneComposition] No OutpostNetworkHost: only the local player can join.");

            string listening = networkHost != null && networkHost.IsListening
                ? " · escuchando en el puerto " + networkHost.Transport.Port : " · sin red";
            bool plays = authorityPlays && !UnityEngine.Application.isBatchMode;
            if (plays)
            {
                localPort = networkHost != null ? networkHost.CreateLocalPort() : new LocalOutpostSessionPort(authority);
                Present(localPort, authority.Scenario);
                OutpostMembership membership = localPort.Join();
                SetStatus(membership.IsJoined, (membership.IsJoined ? LocalPlayerDetail : "La base está completa") + listening);
            }
            else
            {
                SetStatus(false, DedicatedDetail + listening);
            }
            return authority;
        }

        /// <summary>Client only: binds presentation and input to the network adapter (connects per launch
        /// arguments or the Connect button).</summary>
        public void ComposeClient()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Client) throw new InvalidOperationException("The authority has no remote port.");
            if (networkClient == null) throw new InvalidOperationException("OutpostNetworkClient not assigned.");
            if (scenario == null) throw new InvalidOperationException("OutpostScenarioDefinition not assigned.");
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException($"OutpostScenarioDefinition '{scenario.name}': {problem}");
            if (clientScenario == null) clientScenario = scenario.ToScenario();

            OutpostNetworkSessionAdapter port = networkClient.Prepare();
            if (subscribedClient == null)
            {
                subscribedClient = networkClient;
                subscribedClient.ConnectionStatusChanged += OnRemoteStatus;
            }
            if (sessionControls != null) sessionControls.ConfigureConnection(networkClient.Connect, networkClient.Disconnect,
                () => networkClient.State);
            Present(port, clientScenario);
            OnRemoteStatus(OutpostConnectionStatus.IsConfirmed(networkClient.State),
                OutpostConnectionStatus.Describe(networkClient.State, networkClient.Connection?.LastDisconnectReason));
        }

        /// <summary>The production D1 model of this scene (synthetic training parameters, labelled as such by D1).
        /// Public so tests compose exactly what the scene composes.</summary>
        public static IOutpostSimulation CreateSimulation(OutpostScenario built) =>
            new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining());

        /// <summary>The production D3 roster for the scenario's layout and movement settings (empty).</summary>
        public static ICrewRoster CreateRoster(OutpostScenario built) => CrewRoster.For(built);

        /// <summary>Hands one port to every consumer, in the order D4's handoff documents.</summary>
        private void Present(IOutpostSessionPort port, OutpostScenario built)
        {
            presented = port;
            if (keyboardInput != null) keyboardInput.Bind(port, port == null ? null : built);
            if (avatarSync != null) avatarSync.Bind(port);
            if (panel != null)
            {
                panel.Bind(port, port == null ? null : built, interior);
                if (port != null && keyboardInput != null && keyboardInput.Interaction != null)
                {
                    StationInteractionClient interaction = keyboardInput.Interaction;
                    OutpostPanel target = panel;
                    CrewKeyboardInput input = keyboardInput;
                    panel.ConfigureInteraction(
                        interaction.RequestStart,
                        interaction.RequestCancel,
                        () => interaction.Pending,
                        blocked =>
                        {
                            if (input == null) return;
                            if (blocked) input.Block(target);
                            else input.Unblock(target);
                        });
                }
            }
            if (sessionControls != null) sessionControls.Bind(port);
        }

        private void OnRemoteStatus(bool confirmed, string detail)
        {
            if (Role != MissionProcessRole.Client) return;
            string endpoint = networkClient != null && !string.IsNullOrEmpty(networkClient.Endpoint) ? " · " + networkClient.Endpoint : "";
            SetStatus(confirmed, detail + endpoint);
        }

        private void SetStatus(bool confirmed, string detail)
        {
            if (sessionControls != null) sessionControls.SetStatus(confirmed, detail);
        }

        private void OnAuthorityShutDown(OutpostAuthority released)
        {
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            ReleaseLocalPlayer();
            SetStatus(false, "La misión local se detuvo");
        }

        private void ReleaseLocalPlayer()
        {
            if (localPort == null) return;
            if (ReferenceEquals(presented, localPort)) Present(null, null);
            LocalOutpostSessionPort port = localPort;
            localPort = null;
            port.Dispose(); // Leave at the authority: its task ends there, no ghost.
        }

        private void OnDestroy()
        {
            if (subscribedClient != null) subscribedClient.ConnectionStatusChanged -= OnRemoteStatus;
            subscribedClient = null;
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            try
            {
                if (localPort != null) ReleaseLocalPlayer();
                else if (presented != null) Present(null, null);
            }
            catch (Exception e)
            {
                // Scene teardown order is undefined; consumers may already be destroyed.
                Debug.LogWarning($"[OutpostSceneComposition] Teardown: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void SetActive(GameObject[] objects, bool active)
        {
            if (objects == null) return;
            foreach (GameObject item in objects)
                if (item != null && item.activeSelf != active) item.SetActive(active);
        }
    }
}
