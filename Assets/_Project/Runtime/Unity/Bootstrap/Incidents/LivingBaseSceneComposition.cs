using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Incidents;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.Presentation.BaseOperations;
using Trainer.Unity.Presentation.Incidents;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.Incidents
{
    /// <summary>
    /// Scene composition root of the phase 5 living base (<c>Phase05_LivingBase</c>, D2). Same role rule and argument as phases 1–4
    /// (<c>-trainer-role authority|client</c>; WebGL is always a client):
    /// <list type="bullet">
    /// <item>Authority: <see cref="LivingBaseBootstrap.Compose"/> (ONE <see cref="LivingBaseAuthority"/> over the single phase 4
    /// authority, ONE runner) → D3 <see cref="LivingBaseNetworkHost.Attach"/>. Outside -batchmode the host also plays through a
    /// local port like any member; in -batchmode it is a dedicated server.</item>
    /// <item>Client: D3 <see cref="LivingBaseNetworkClient.Prepare"/>; no authority, director, model, clock or runner.</item>
    /// <item>Both: ONE port goes to the phase 2 movement and avatars, the phase 3 R key and crew presenter, the phase 4 views and
    /// key behaviour (its own E disabled) and the phase 5 E key (<see cref="LivingBaseInteractionInput"/>), the base view, the
    /// session controls and every <see cref="ILivingBaseView"/> (D4) under the phase 5 mount.</item>
    /// </list>
    /// Any view that owns the keyboard blocks movement AND every interaction key through the same owner (Escape releases it in the
    /// D4 views). E at a phase 5 station opens the D4 phase 5 panel; elsewhere it falls back to phase 4 (then phase 3).
    /// The scenario asset is shared data: a client uses it only to know the plan of this build.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class LivingBaseSceneComposition : MonoBehaviour
    {
        public const string LocalPlayerDetail = "Anfitrión: esta ventana ejecuta la base viva y juega";
        public const string DedicatedDetail = "Servidor dedicado: sin jugador local";

        /// <summary>Same virtual canvas as phase 4 (the reused phase 4 panel and the phase 5 views share it).</summary>
        public const float CanvasWidth = BaseOperationsSceneComposition.CanvasWidth, CanvasHeight = BaseOperationsSceneComposition.CanvasHeight,
            SessionStripY = BaseOperationsSceneComposition.SessionStripY;

        [SerializeField] private MissionProcessRole role = MissionProcessRole.Authority;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField] private LivingBaseScenarioDefinition scenario;
        [SerializeField] private LivingBaseBootstrap bootstrap;
        [SerializeField] private LivingBaseNetworkHost networkHost;
        [SerializeField] private LivingBaseNetworkClient networkClient;
        [SerializeField] private CrewKeyboardInput keyboardInput;
        [SerializeField] private CrewAvatarSync avatarSync;
        [SerializeField] private ExpeditionInteractionInput interactionInput;
        [SerializeField] private BaseOperationsInteractionInput operationsInput;
        [SerializeField] private LivingBaseInteractionInput livingInput;
        [SerializeField] private ExpeditionCrewPresenter crewPresenter;
        [SerializeField] private BaseInteriorView baseView;
        [SerializeField] private Sprite lunarInteriorArtwork;
        [SerializeField, Tooltip("Themed floors, walls and furniture for the lunar interior. Visual only.")] private LunarInteriorSkin lunarInteriorSkin = new LunarInteriorSkin();
        [SerializeField] private OutpostSessionControls sessionControls;
        [SerializeField, Tooltip("Parent of every IExpeditionView bound on the same port.")] private Transform phase3Mount;
        [SerializeField, Tooltip("Parent of every IBaseOperationsView (phase 4 D4 views, reused).")] private Transform phase4Mount;
        [SerializeField, Tooltip("Parent of every ILivingBaseView (phase 5 D4 views).")] private Transform phase5Mount;
        [SerializeField] private bool authorityPlays = true;
        [SerializeField] private GameObject[] authorityOnly = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] clientOnly = Array.Empty<GameObject>();

        private bool roleApplied;
        private LivingBaseScenario built;
        private LocalLivingBaseSessionPort localPort;
        private ILivingBaseSessionPort presented;
        private LivingBaseNetworkClient subscribedClient;
        private BaseOperationsInteractionInput subscribedInput;
        private LivingBaseInteractionInput subscribedLivingInput;
        private LivingBaseAuthority composedAuthority;
        private readonly List<IExpeditionView> boundPhase3 = new List<IExpeditionView>();
        private readonly List<IBaseOperationsView> boundPhase4 = new List<IBaseOperationsView>();
        private readonly List<ILivingBaseView> boundPhase5 = new List<ILivingBaseView>();

        public MissionProcessRole Role { get; private set; }
        public ILivingBaseSessionPort PresentedPort => presented;
        public LivingBaseScenario Scenario => built;
        public LivingBaseAuthority Authority => bootstrap != null ? bootstrap.Authority : null;
        /// <summary>The D3 phase 5 intents of the local player (null while no port is presented).</summary>
        public ILivingBaseIntents LivingIntents => presented != null && livingInput != null ? livingInput.Interaction : null;
        public LivingBaseInteractionInput LivingInput => livingInput;
        /// <summary>The D3 phase 4 intents of the local player (null while no port is presented).</summary>
        public IBaseOperationsIntents Intents => presented != null && operationsInput != null ? operationsInput.Interaction : null;
        public IExpeditionIntents ExpeditionIntents => presented != null && interactionInput != null ? interactionInput.Interaction : null;
        public LivingBaseNetworkHost NetworkHost => networkHost;
        public LivingBaseNetworkClient NetworkClient => networkClient;
        public int BoundPhase3Views => boundPhase3.Count;
        public int BoundPhase4Views => boundPhase4.Count;
        public int BoundPhase5Views => boundPhase5.Count;
        /// <summary>Raised whenever a port is presented (or null when it is withdrawn).</summary>
        public event Action<ILivingBaseSessionPort> Presented;

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
            Debug.Log($"[LivingBaseSceneComposition] Role: {value}");
        }

        public LivingBaseAuthority ComposeAuthority()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Authority) throw new InvalidOperationException("Only the authority composes.");
            if (bootstrap == null) throw new InvalidOperationException("LivingBaseBootstrap not assigned.");
            if (bootstrap.Authority != null) return bootstrap.Authority;
            LivingBaseAuthority authority = bootstrap.Compose();
            composedAuthority = authority;
            built = authority.Scenario;
            bootstrap.ShutDown -= OnAuthorityShutDown;
            bootstrap.ShutDown += OnAuthorityShutDown;
            Debug.Log($"[LivingBaseSceneComposition] Living base composed: '{bootstrap.ScenarioDefinition.DisplayName}'.");
            if (networkHost != null) networkHost.Attach(authority);
            else Debug.LogWarning("[LivingBaseSceneComposition] No LivingBaseNetworkHost: only the local player can join.");
            if (authorityPlays && !UnityEngine.Application.isBatchMode)
            {
                localPort = networkHost != null ? networkHost.CreateLocalPort() : new LocalLivingBaseSessionPort(authority);
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
            if (networkClient == null) throw new InvalidOperationException("LivingBaseNetworkClient not assigned.");
            if (scenario == null) throw new InvalidOperationException("LivingBaseScenarioDefinition not assigned.");
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException($"LivingBaseScenarioDefinition '{scenario.name}': {problem}");
            if (built == null) built = scenario.ToScenario(null); // plan only: the episode content always comes from the authority
            LivingBaseNetworkSessionAdapter port = networkClient.Prepare(); // reads the launch arguments first
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
        private void Present(ILivingBaseSessionPort port)
        {
            foreach (ILivingBaseView view in boundPhase5) view.Unbind();
            foreach (IExpeditionView view in boundPhase3) view.Unbind();
            foreach (IBaseOperationsView view in boundPhase4) view.Unbind();
            boundPhase3.Clear();
            boundPhase4.Clear();
            boundPhase5.Clear();
            presented = port;
            BaseOperationsScenario phase4 = built?.BaseOperations;
            ExpeditionScenario expedition = phase4?.Expedition;
            if (keyboardInput != null) keyboardInput.Bind(port, port == null ? null : expedition.Outpost);
            if (avatarSync != null) avatarSync.Bind(port);
            ExpeditionInteractionClient phase3Intents = interactionInput != null ? interactionInput.Bind(port, port == null ? null : expedition) : null;
            BaseOperationsInteractionClient phase4Intents = operationsInput != null
                ? operationsInput.Bind(port, port == null ? null : phase4, phase3Intents) : null;
            LivingBaseInteractionClient phase5Intents = livingInput != null ? livingInput.Bind(port, port == null ? null : built) : null;
            SubscribeStationKey(port != null ? operationsInput : null);
            SubscribeLivingKey(port != null ? livingInput : null);
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
                    var p4 = new BaseOperationsViewContext(port, phase4, phase4Intents, phase3Intents, SetMovementBlocked);
                    foreach (IBaseOperationsView view in phase4Mount.GetComponentsInChildren<IBaseOperationsView>(true))
                    {
                        view.Bind(p4);
                        boundPhase4.Add(view);
                    }
                }
                if (phase5Mount != null && phase4Intents != null && phase5Intents != null)
                {
                    var p5 = new LivingBaseViewContext(port, built, phase5Intents, phase4Intents, phase3Intents, SetMovementBlocked);
                    foreach (ILivingBaseView view in phase5Mount.GetComponentsInChildren<ILivingBaseView>(true))
                    {
                        view.Bind(p5);
                        boundPhase5.Add(view);
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
            if (livingInput != null)
            {
                if (blocked) livingInput.Block(owner);
                else livingInput.Unblock(owner);
            }
        }

        private void SubscribeLivingKey(LivingBaseInteractionInput input)
        {
            if (ReferenceEquals(subscribedLivingInput, input)) return;
            if (subscribedLivingInput != null) subscribedLivingInput.StationActivated -= OnLivingStationActivated;
            subscribedLivingInput = input;
            if (subscribedLivingInput != null) subscribedLivingInput.StationActivated += OnLivingStationActivated;
        }

        /// <summary>E at a phase 5 station: the D4 phase 5 panel offers that station's decisions.</summary>
        private void OnLivingStationActivated(StationId station)
        {
            if (phase5Mount == null) return;
            foreach (ILivingBaseStationView view in phase5Mount.GetComponentsInChildren<ILivingBaseStationView>(false))
                if (view.OpenStation(station)) return;
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

        private void OnAuthorityShutDown(LivingBaseAuthority released)
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
            LocalLivingBaseSessionPort port = localPort;
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
            SubscribeLivingKey(null);
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            try
            {
                if (localPort != null) ReleaseLocalPlayer();
                else if (presented != null) Present(null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LivingBaseSceneComposition] Teardown: {e.GetType().Name}: {e.Message}");
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
