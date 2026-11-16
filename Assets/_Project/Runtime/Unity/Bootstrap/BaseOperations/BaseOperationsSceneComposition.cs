using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Presentation.BaseOperations;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>
    /// Scene composition root of the phase 4 living base (<c>Phase04_LivingBase</c>, D2, single scene editor). Same role rule
    /// and argument as phases 1–3 (<c>-trainer-role authority|client</c>; WebGL is always a client):
    /// <list type="bullet">
    /// <item>Authority: <see cref="BaseOperationsBootstrap.Compose"/> (ONE continuous authority, ONE runner over D1 base,
    /// EVA and equipment models and the D3 roster) → D3 <see cref="BaseOperationsNetworkHost.Attach"/>. Outside -batchmode
    /// the host also plays through <see cref="BaseOperationsNetworkHost.CreateLocalPort"/> like any member; in -batchmode
    /// it is a dedicated server without a player.</item>
    /// <item>Client: D3 <see cref="BaseOperationsNetworkClient.Prepare"/>; no authority, model, roster, clock or runner.</item>
    /// <item>Both: ONE port goes to the phase 2 movement input and avatars, the phase 3 R key and crew presenter, the D3
    /// phase 4 E key (<see cref="BaseOperationsInteractionInput"/>, which falls back to the phase 3 interaction), the base
    /// view, the session controls, every <see cref="IExpeditionView"/> under the phase 3 mount and every
    /// <see cref="IBaseOperationsView"/> (D4) under the phase 4 mount.</item>
    /// </list>
    /// Any view that owns the keyboard blocks movement AND every interaction key (phase 3 E/R and phase 4 E) through the
    /// same owner; unblocking is symmetric. E at a phase 4 station opens D4's panel for the station in reach. No fake or
    /// fixture is used. The scenario asset is shared data: a client uses it only to know the plan of this build.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class BaseOperationsSceneComposition : MonoBehaviour
    {
        public const string LocalPlayerDetail = "Anfitrión: esta ventana ejecuta la base viva y juega";
        public const string DedicatedDetail = "Servidor dedicado: sin jugador local";

        /// <summary>Virtual canvas shared with D4's phase 4 panel; the session strip uses the row the panel leaves free.</summary>
        public const float CanvasWidth = 1440f, CanvasHeight = 900f, SessionStripY = BaseOperationsPanel.SessionStripY;

        [SerializeField] private MissionProcessRole role = MissionProcessRole.Authority;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField] private BaseOperationsScenarioDefinition scenario;
        [SerializeField] private BaseOperationsBootstrap bootstrap;
        [SerializeField] private BaseOperationsNetworkHost networkHost;
        [SerializeField] private BaseOperationsNetworkClient networkClient;
        [SerializeField] private CrewKeyboardInput keyboardInput;
        [SerializeField] private CrewAvatarSync avatarSync;
        [SerializeField] private ExpeditionInteractionInput interactionInput;
        [SerializeField] private BaseOperationsInteractionInput operationsInput;
        [SerializeField] private ExpeditionCrewPresenter crewPresenter;
        [SerializeField] private BaseInteriorView baseView;
        [SerializeField] private Sprite lunarInteriorArtwork;
        [SerializeField, Tooltip("Themed floors, walls and furniture for the lunar interior. Visual only.")] private LunarInteriorSkin lunarInteriorSkin = new LunarInteriorSkin();
        [SerializeField] private OutpostSessionControls sessionControls;
        [SerializeField, Tooltip("Parent of every IExpeditionView bound on the same port.")] private Transform phase3Mount;
        [SerializeField, Tooltip("Parent of every IBaseOperationsView (D4 views).")] private Transform phase4Mount;
        [SerializeField] private bool authorityPlays = true;
        [SerializeField] private GameObject[] authorityOnly = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] clientOnly = Array.Empty<GameObject>();

        private bool roleApplied;
        private BaseOperationsScenario built;
        private LocalBaseOperationsSessionPort localPort;
        private IBaseOperationsSessionPort presented;
        private BaseOperationsNetworkClient subscribedClient;
        private BaseOperationsInteractionInput subscribedInput;
        private BaseOperationsAuthority composedAuthority;
        private readonly List<IExpeditionView> boundPhase3 = new List<IExpeditionView>();
        private readonly List<IBaseOperationsView> boundPhase4 = new List<IBaseOperationsView>();

        public MissionProcessRole Role { get; private set; }
        public IBaseOperationsSessionPort PresentedPort => presented;
        public BaseOperationsScenario Scenario => built;
        public BaseOperationsAuthority Authority => bootstrap != null ? bootstrap.Authority : null;
        /// <summary>The D3 phase 4 intents of the local player (null while no port is presented).</summary>
        public IBaseOperationsIntents Intents => presented != null && operationsInput != null ? operationsInput.Interaction : null;
        public IExpeditionIntents ExpeditionIntents => presented != null && interactionInput != null ? interactionInput.Interaction : null;
        public BaseOperationsNetworkHost NetworkHost => networkHost;
        public BaseOperationsNetworkClient NetworkClient => networkClient;
        public int BoundPhase3Views => boundPhase3.Count;
        public int BoundPhase4Views => boundPhase4.Count;
        /// <summary>Raised whenever a port is presented (or null when it is withdrawn).</summary>
        public event Action<IBaseOperationsSessionPort> Presented;

        private void Awake()
        {
            bool webGl = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            webGl = true;
#endif
            string[] args = readCommandLine && !webGl ? Environment.GetCommandLineArgs() : null;
            ApplyRole(MissionSceneComposition.ResolveRole(role, args, webGl));
            if (sessionControls != null)
            {
                sessionControls.ConfigureCanvas(CanvasWidth, CanvasHeight, SessionStripY);
                sessionControls.ConfigureDrawDepth(BaseOperationsPanel.SessionControlsDepth);
                var dashboard = phase4Mount != null ? phase4Mount.GetComponentInChildren<BaseOperationsPanel>(true) : null;
                if (dashboard != null)
                {
                    sessionControls.EmbedIn(dashboard.Skin);
                    dashboard.ConfigureSessionDock(sessionControls.DrawDock);
                }
            }
        }

        private void Start()
        {
            if (Role == MissionProcessRole.Authority) ComposeAuthority();
            else ComposeClient();
        }

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
            SetActive(value == MissionProcessRole.Authority ? clientOnly : authorityOnly, false);
            SetActive(value == MissionProcessRole.Authority ? authorityOnly : clientOnly, true);
            Debug.Log($"[BaseOperationsSceneComposition] Role: {value}");
        }

        public BaseOperationsAuthority ComposeAuthority()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Authority) throw new InvalidOperationException("Only the authority composes.");
            if (bootstrap == null) throw new InvalidOperationException("BaseOperationsBootstrap not assigned.");
            if (bootstrap.Authority != null) return bootstrap.Authority;
            BaseOperationsAuthority authority = bootstrap.Compose();
            composedAuthority = authority;
            built = authority.Scenario;
            bootstrap.ShutDown -= OnAuthorityShutDown;
            bootstrap.ShutDown += OnAuthorityShutDown;
            Debug.Log($"[BaseOperationsSceneComposition] Living base composed: '{bootstrap.ScenarioDefinition.DisplayName}'.");
            if (networkHost != null) networkHost.Attach(authority);
            else Debug.LogWarning("[BaseOperationsSceneComposition] No BaseOperationsNetworkHost: only the local player can join.");
            if (authorityPlays && !UnityEngine.Application.isBatchMode)
            {
                localPort = networkHost != null ? networkHost.CreateLocalPort() : new LocalBaseOperationsSessionPort(authority);
                Present(localPort);
                OutpostMembership membership = localPort.Join();
                SetStatus(membership.IsJoined, (membership.IsJoined ? LocalPlayerDetail : "La base está completa") + Listening());
            }
            else SetStatus(false, DedicatedDetail + Listening());
            return authority;
        }

        public void ComposeClient()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Client) throw new InvalidOperationException("The authority has no remote port.");
            if (networkClient == null) throw new InvalidOperationException("BaseOperationsNetworkClient not assigned.");
            if (scenario == null) throw new InvalidOperationException("BaseOperationsScenarioDefinition not assigned.");
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException($"BaseOperationsScenarioDefinition '{scenario.name}': {problem}");
            if (built == null) built = scenario.ToScenario();
            BaseOperationsNetworkSessionAdapter port = networkClient.Prepare(); // reads the launch arguments first
            if (subscribedClient == null)
            {
                subscribedClient = networkClient;
                subscribedClient.ConnectionStatusChanged += OnRemoteStatus;
            }
            if (sessionControls != null)
                sessionControls.ConfigureConnection(networkClient.Connect, networkClient.Disconnect, () => networkClient.State);
            Present(port);
            OnRemoteStatus(OutpostConnectionStatus.IsConfirmed(networkClient.State),
                OutpostConnectionStatus.Describe(networkClient.State, networkClient.Connection?.LastDisconnectReason));
        }

        /// <summary>Hands one port to every consumer. Null withdraws it everywhere (views unbind before inputs).</summary>
        private void Present(IBaseOperationsSessionPort port)
        {
            foreach (IExpeditionView view in boundPhase3) view.Unbind();
            foreach (IBaseOperationsView view in boundPhase4) view.Unbind();
            boundPhase3.Clear();
            boundPhase4.Clear();
            presented = port;
            ExpeditionScenario expedition = built?.Expedition;
            if (keyboardInput != null) keyboardInput.Bind(port, port == null ? null : expedition.Outpost);
            if (avatarSync != null) avatarSync.Bind(port);
            ExpeditionInteractionClient phase3Intents = interactionInput != null ? interactionInput.Bind(port, port == null ? null : expedition) : null;
            BaseOperationsInteractionClient phase4Intents = operationsInput != null
                ? operationsInput.Bind(port, port == null ? null : built, phase3Intents) : null;
            SubscribeStationKey(port != null ? operationsInput : null);
            if (crewPresenter != null) crewPresenter.Bind(port, avatarSync);
            if (baseView != null && port != null && expedition != null)
            {
                baseView.SetLunarArtwork(lunarInteriorArtwork, lunarInteriorSkin);
                baseView.Configure(expedition.Outpost);
            }
            if (sessionControls != null) sessionControls.Bind(port);
            if (port != null && phase3Intents != null)
            {
                if (phase3Mount != null)
                {
                    var p3 = new ExpeditionViewContext(port, expedition, phase3Intents, SetMovementBlocked);
                    foreach (IExpeditionView view in phase3Mount.GetComponentsInChildren<IExpeditionView>(true))
                    {
                        view.Bind(p3);
                        boundPhase3.Add(view);
                    }
                }
                if (phase4Mount != null && phase4Intents != null)
                {
                    var p4 = new BaseOperationsViewContext(port, built, phase4Intents, phase3Intents, SetMovementBlocked);
                    foreach (IBaseOperationsView view in phase4Mount.GetComponentsInChildren<IBaseOperationsView>(true))
                    {
                        view.Bind(p4);
                        boundPhase4.Add(view);
                    }
                }
            }
            Presented?.Invoke(port);
        }

        /// <summary>One owner blocks (or releases) movement and every interaction key together.</summary>
        private void SetMovementBlocked(object owner, bool blocked)
        {
            if (keyboardInput != null)
            {
                if (blocked) keyboardInput.Block(owner);
                else keyboardInput.Unblock(owner);
            }
            if (interactionInput != null)
            {
                if (blocked) interactionInput.Block(owner);
                else interactionInput.Unblock(owner);
            }
            if (operationsInput != null)
            {
                if (blocked) operationsInput.Block(owner);
                else operationsInput.Unblock(owner);
            }
        }

        private void SubscribeStationKey(BaseOperationsInteractionInput input)
        {
            if (ReferenceEquals(subscribedInput, input)) return;
            if (subscribedInput != null) subscribedInput.StationActivated -= OnStationActivated;
            subscribedInput = input;
            if (subscribedInput != null) subscribedInput.StationActivated += OnStationActivated;
        }

        /// <summary>E at a phase 4 station: D4's panel offers that station's decisions (the key never chooses).</summary>
        private void OnStationActivated(StationId station)
        {
            if (phase4Mount == null) return;
            foreach (BaseOperationsPanel panel in phase4Mount.GetComponentsInChildren<BaseOperationsPanel>(false))
                if (panel.Live && panel.OpenNearbyStation()) return;
        }

        private void OnRemoteStatus(bool confirmed, string detail)
        {
            if (Role != MissionProcessRole.Client) return;
            string endpoint = networkClient != null && !string.IsNullOrEmpty(networkClient.Endpoint) ? " · " + networkClient.Endpoint : "";
            SetStatus(confirmed, detail + endpoint);
        }

        private string Listening() => networkHost != null && networkHost.IsListening
            ? " · escuchando en el puerto " + networkHost.Transport.Port : " · sin red";

        private void SetStatus(bool confirmed, string detail)
        {
            if (sessionControls != null) sessionControls.SetStatus(confirmed, detail);
        }

        private void LateUpdate()
        {
            // The host's Start may open the socket after ComposeAuthority. Display the actual listener state.
            if (Role != MissionProcessRole.Authority || composedAuthority == null) return;
            SetStatus(localPort != null && localPort.Membership.IsJoined, (localPort != null ? LocalPlayerDetail : DedicatedDetail) + Listening());
        }

        private void OnAuthorityShutDown(BaseOperationsAuthority released)
        {
            if (!ReferenceEquals(composedAuthority, released)) return;
            composedAuthority = null;
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            try { ReleaseLocalPlayer(); }
            finally
            {
                if (networkHost != null && ReferenceEquals(networkHost.Authority, released)) networkHost.Detach();
                SetStatus(false, "La base local se detuvo");
            }
        }

        private void ReleaseLocalPlayer()
        {
            if (localPort == null) return;
            LocalBaseOperationsSessionPort port = localPort;
            localPort = null;
            try
            {
                if (ReferenceEquals(presented, port)) Present(null);
            }
            finally { port.Dispose(); }
        }

        private void OnDestroy()
        {
            if (subscribedClient != null) subscribedClient.ConnectionStatusChanged -= OnRemoteStatus;
            subscribedClient = null;
            SubscribeStationKey(null);
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            try
            {
                if (localPort != null) ReleaseLocalPlayer();
                else if (presented != null) Present(null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BaseOperationsSceneComposition] Teardown: {e.GetType().Name}: {e.Message}");
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
