using System;
using Trainer.Application;
using Trainer.Simulation;
using Trainer.Unity.Configuration;
using Trainer.Unity.Presentation;
using Trainer.Unity.Networking;
using Trainer.Unity.World;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>Which side of the shared session this process plays. Exactly one Authority per session.</summary>
    public enum MissionProcessRole
    {
        /// <summary>Owns the MissionSession, the D1 simulation and the single SimulationRunner
        /// (dedicated server, or a single-process local run).</summary>
        Authority,
        /// <summary>Only presents a remote ISessionPort; never creates a session, simulation or runner.</summary>
        Client
    }

    /// <summary>
    /// Scene composition root for the phase 1 mission scene. Decides the process role, enables only the
    /// objects of that role, and wires: D1 simulation -> MissionBootstrap/MissionSession -> SimulationRunner
    /// (authority only) and the session port -> MissionHUD (local port on the authority, remote port on a client).
    ///
    /// Role: the serialized value, overridden by the command line <c>-trainer-role authority|client</c>.
    /// A WebGL player is always a Client (it cannot host). Role-specific objects should be saved inactive in
    /// the scene; this component activates the ones of the resolved role in Awake, before their Start.
    ///
    /// The client network component reports its port and link state through PresentRemoteSession;
    /// the host network component follows
    /// <see cref="MissionBootstrap.Composed"/>/<see cref="MissionBootstrap.ShutDown"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionSceneComposition : MonoBehaviour
    {
        public const string RoleArgument = "-trainer-role";
        public const string LocalAuthorityDetail = "Autoridad local: este proceso ejecuta la misión";
        public const string AuthorityStoppedDetail = "La misión local se detuvo";

        [SerializeField] private MissionProcessRole role = MissionProcessRole.Authority;
        [SerializeField, Tooltip("Read -trainer-role authority|client from the command line (non-WebGL players).")]
        private bool readCommandLine = true;
        [SerializeField, Tooltip("Authority-side composition (scenario + single runner).")]
        private MissionBootstrap bootstrap;
        [SerializeField, Tooltip("Optional HUD instance (D4 prefab).")]
        private MissionHUD hud;
        [SerializeField] private SessionNetworkClient networkClient;
        [SerializeField] private PowerRoomView room;
        [SerializeField] private Camera sceneCamera;
        [SerializeField, Tooltip("On the authority, bind the HUD to the local session port (host with UI / local run).")]
        private bool bindLocalHud = true;
        [SerializeField, Tooltip("Objects that exist only in the authority process (bootstrap, runner, network host).")]
        private GameObject[] authorityOnly = Array.Empty<GameObject>();
        [SerializeField, Tooltip("Objects that exist only in client processes (network client).")]
        private GameObject[] clientOnly = Array.Empty<GameObject>();

        private bool roleApplied;
        private ISessionPort presented;
        private SessionNetworkClient subscribedClient;
        private int viewportWidth = -1, viewportHeight = -1;

        /// <summary>Role resolved in Awake (or by <see cref="ApplyRole"/>).</summary>
        public MissionProcessRole Role { get; private set; }

        /// <summary>The port the HUD currently shows, or null.</summary>
        public ISessionPort PresentedPort => presented;

        /// <summary>Authority session, or null in a client or before composition.</summary>
        public MissionSession Session => bootstrap != null ? bootstrap.Session : null;

        public MissionBootstrap Bootstrap => bootstrap;
        public MissionHUD Hud => hud;
        public SessionNetworkClient NetworkClient => networkClient;
        public PowerRoomView Room => room;
        public Camera SceneCamera => sceneCamera;

        /// <summary>Scenario rules known to this build. Clients use them only to describe a published snapshot
        /// (see <see cref="DescribeOutcome"/>); they never evaluate or advance the mission.</summary>
        public MissionRules Rules
        {
            get
            {
                ScenarioDefinition scenario = bootstrap != null ? bootstrap.Scenario : null;
                return scenario != null && scenario.Validate() == null ? scenario.ToMissionRules() : null;
            }
        }

        /// <summary>Raised after the authority composed its session (authority only).</summary>
        public event Action<MissionSession> AuthorityComposed;

        /// <summary>Resolves the role from the configured value, command line and platform.</summary>
        public static MissionProcessRole ResolveRole(MissionProcessRole configured, string[] commandLine, bool webGlPlayer)
        {
            if (webGlPlayer) return MissionProcessRole.Client;
            if (commandLine == null) return configured;
            for (int i = 0; i < commandLine.Length - 1; i++)
            {
                if (!string.Equals(commandLine[i], RoleArgument, StringComparison.OrdinalIgnoreCase)) continue;
                string value = commandLine[i + 1];
                if (string.Equals(value, "authority", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "server", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "host", StringComparison.OrdinalIgnoreCase))
                    return MissionProcessRole.Authority;
                if (string.Equals(value, "client", StringComparison.OrdinalIgnoreCase))
                    return MissionProcessRole.Client;
                throw new ArgumentException($"Unknown value for {RoleArgument}: '{value}'. Use authority or client.");
            }
            return configured;
        }

        private void Awake()
        {
            bool webGl = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            webGl = true;
#endif
            string[] args = readCommandLine && !webGl ? Environment.GetCommandLineArgs() : null;
            ApplyRole(ResolveRole(role, args, webGl));
        }

        private void Start()
        {
            if (Role == MissionProcessRole.Authority) ComposeAuthority();
        }

        private void OnEnable() => AttachBindings();

        private void OnDisable() => DetachBindings();

        private void LateUpdate()
        {
            if (room == null || sceneCamera == null || hud == null || !sceneCamera.orthographic) return;
            if (viewportWidth == Screen.width && viewportHeight == Screen.height) return;
            viewportWidth = Screen.width;
            viewportHeight = Screen.height;
            Rect viewport = PowerRoomView.CalculateViewport(viewportWidth, viewportHeight, hud.PanelRect.xMax);
            sceneCamera.pixelRect = viewport;
            sceneCamera.aspect = viewport.width / viewport.height;
            // Existing room art is 12 x 8 units. Fit it in the space between HUD/header/controls.
            sceneCamera.orthographicSize = Mathf.Max(4.4f, 6.4f / sceneCamera.aspect);
            Vector3 position = sceneCamera.transform.position;
            sceneCamera.transform.position = new Vector3(room.transform.position.x, room.transform.position.y, position.z);
        }

        /// <summary>Fixes the role once and enables only that role's objects. Called by Awake; public for tests
        /// and for a composition that is created from code.</summary>
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
            // Disable first so a client never runs authority Start (e.g. a network host would listen).
            SetActive(value == MissionProcessRole.Authority ? clientOnly : authorityOnly, false);
            SetActive(value == MissionProcessRole.Authority ? authorityOnly : clientOnly, true);
            AttachBindings();
            Debug.Log($"[MissionSceneComposition] Role: {value}");
        }

        /// <summary>Authority only: creates the D1 simulation, composes the single session and runner, and binds
        /// the local HUD if requested. Idempotent while composed.</summary>
        public MissionSession ComposeAuthority()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Authority)
                throw new InvalidOperationException("Only the authority composes a mission session.");
            if (bootstrap == null) throw new InvalidOperationException("MissionBootstrap not assigned.");
            AttachBindings();
            if (bootstrap.Session != null) return bootstrap.Session;
            // The only place the authoritative simulation is created; MissionSession is its only caller.
            MissionSession session = bootstrap.Compose(CreateSimulation());
            ScenarioDefinition scenario = bootstrap.Scenario;
            Debug.Log($"[MissionSceneComposition] Mission composed: '{scenario.DisplayName}'. {scenario.Provenance}");
            return session;
        }

        /// <summary>
        /// Client only: shows the remote port reported by the network client. Call it on every link change with
        /// (port, confirmed, detail): confirmed = the authority's state arrived on the current link. A loss
        /// unbinds the HUD and erases stale state; the next confirmation binds again (same or new port).
        /// </summary>
        public void PresentRemoteSession(ISessionPort port, bool confirmed, string detail)
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Client)
                throw new InvalidOperationException("The authority presents its local session; it has no remote port.");
            Present(port, confirmed && port != null, detail);
        }

        /// <summary>Describes a published snapshot with this build's scenario rules (null if none assigned).
        /// On the authority prefer <see cref="MissionSession.Outcome"/>.</summary>
        public MissionOutcome? DescribeOutcome(MissionSnapshot snapshot)
        {
            if (snapshot == null) return null;
            MissionSession session = Session;
            if (session != null) return session.Outcome;
            MissionRules rules = Rules;
            return rules == null ? (MissionOutcome?)null : MissionOutcomeEvaluator.Classify(rules, snapshot);
        }

        private static IPowerSimulation CreateSimulation() => new PowerSystem();

        private void AttachBindings()
        {
            if (!roleApplied || !isActiveAndEnabled) return;
            if (hud != null) hud.ConfigureMission(Rules, DescribeOutcome);
            if (room != null) room.Connect(hud, sceneCamera);
            viewportWidth = viewportHeight = -1;
            if (Role == MissionProcessRole.Authority)
            {
                if (bootstrap == null) return;
                bootstrap.Composed -= OnAuthorityComposed;
                bootstrap.Composed += OnAuthorityComposed;
                bootstrap.ShutDown -= OnAuthorityShutDown;
                bootstrap.ShutDown += OnAuthorityShutDown;
                if (bootstrap.Session != null && bindLocalHud) Present(bootstrap.Session, true, LocalAuthorityDetail);
                if (hud != null) hud.ConfigureConnectionActions(null, null, false, false);
            }
            else if (networkClient != null)
            {
                if (subscribedClient != null) subscribedClient.ConnectionStatusChanged -= OnRemoteStatus;
                subscribedClient = networkClient;
                subscribedClient.ConnectionStatusChanged += OnRemoteStatus;
                OnRemoteStatus(SessionConnectionStatus.IsConfirmed(networkClient.State),
                    SessionConnectionStatus.Describe(networkClient.State, networkClient.Connection?.LastDisconnectReason));
            }
        }

        private void DetachBindings()
        {
            if (subscribedClient != null) subscribedClient.ConnectionStatusChanged -= OnRemoteStatus;
            subscribedClient = null;
            if (bootstrap != null)
            {
                bootstrap.Composed -= OnAuthorityComposed;
                bootstrap.ShutDown -= OnAuthorityShutDown;
            }
            if (room != null) room.Connect(null, null);
            presented = null;
            if (hud != null)
            {
                hud.ConfigureConnectionActions(null, null, false, false);
                hud.SetConnectionStatus(false, "Presentación de misión detenida");
                hud.ConfigureMission(null);
            }
        }

        private void OnRemoteStatus(bool confirmed, string detail)
        {
            if (!isActiveAndEnabled || Role != MissionProcessRole.Client || networkClient == null) return;
            PresentRemoteSession(networkClient.Session, confirmed, detail);
            SessionConnectionState state = networkClient.State;
            if (hud != null) hud.ConfigureConnectionActions(networkClient.Connect, networkClient.Disconnect,
                state == SessionConnectionState.Disconnected || state == SessionConnectionState.ConnectionLost,
                state != SessionConnectionState.Disconnected && state != SessionConnectionState.ConnectionLost);
        }

        private void OnAuthorityComposed(MissionSession session)
        {
            if (bindLocalHud) Present(session, true, LocalAuthorityDetail);
            AuthorityComposed?.Invoke(session);
        }

        private void Present(ISessionPort port, bool confirmed, string detail)
        {
            if (hud == null)
            {
                presented = confirmed ? port : null;
                return;
            }
            if (!confirmed)
            {
                presented = null;
                hud.SetConnectionStatus(false, detail);   // MissionHUD unbinds and clears on loss.
                return;
            }
            if (!ReferenceEquals(presented, port))
            {
                hud.Bind(port);
                presented = port;
            }
            hud.SetConnectionStatus(true, detail);
        }

        private void OnAuthorityShutDown(MissionSession session)
        {
            if (ReferenceEquals(presented, session)) Present(null, false, AuthorityStoppedDetail);
        }

        private void OnDestroy() => DetachBindings();

        private static void SetActive(GameObject[] objects, bool active)
        {
            if (objects == null) return;
            foreach (GameObject item in objects)
                if (item != null && item.activeSelf != active) item.SetActive(active);
        }
    }
}
