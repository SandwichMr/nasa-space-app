using System;
using System.Collections.Generic;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// Scene composition root of the phase 3 mission (<c>Phase03_EvaAndShelter</c>, D2). Same role rule and argument as
    /// phases 1 and 2 (<c>-trainer-role authority|client</c>; WebGL is always a client):
    /// <list type="bullet">
    /// <item>Authority: <see cref="ExpeditionBootstrap.Compose"/> (single authority over D1 base + EVA models and the D3
    /// roster, single runner) → <see cref="ExpeditionNetworkHost.Attach"/>; outside -batchmode the host also plays through
    /// <see cref="ExpeditionNetworkHost.CreateLocalPort"/> like any member.</item>
    /// <item>Client: <see cref="ExpeditionNetworkClient.Prepare"/>; no authority, model, roster or runner.</item>
    /// <item>Both: ONE port goes to the phase 2 movement input and avatars, the phase 3 interaction keys, the crew presenter,
    /// the base view, the phase 2 session controls and every <see cref="IExpeditionView"/> under the presentation mount
    /// (D4's views, or the labelled provisional ones until they arrive).</item>
    /// </list>
    /// No fake or fixture is used. The scenario asset is shared data: a client uses it only to know the plan and tasks of
    /// this build (the published snapshot stays the truth).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class ExpeditionSceneComposition : MonoBehaviour
    {
        public const string LocalPlayerDetail = "Anfitrión: esta ventana ejecuta la misión y juega";
        public const string DedicatedDetail = "Servidor dedicado: sin jugador local";

        [SerializeField] private MissionProcessRole role = MissionProcessRole.Authority;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField] private ExpeditionScenarioDefinition scenario;
        [SerializeField] private ExpeditionBootstrap bootstrap;
        [SerializeField] private ExpeditionNetworkHost networkHost;
        [SerializeField] private ExpeditionNetworkClient networkClient;
        [SerializeField] private CrewKeyboardInput keyboardInput;
        [SerializeField] private CrewAvatarSync avatarSync;
        [SerializeField] private ExpeditionInteractionInput interactionInput;
        [SerializeField] private ExpeditionCrewPresenter crewPresenter;
        [SerializeField] private BaseInteriorView baseView;
        [SerializeField] private OutpostSessionControls sessionControls;
        [SerializeField, Tooltip("Parent of every IExpeditionView (D4 views, or the provisional ones until they arrive).")]
        private Transform presentationMount;
        [SerializeField] private bool authorityPlays = true;
        [SerializeField] private GameObject[] authorityOnly = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] clientOnly = Array.Empty<GameObject>();

        private bool roleApplied;
        private ExpeditionScenario built;
        private LocalExpeditionSessionPort localPort;
        private IExpeditionSessionPort presented;
        private ExpeditionNetworkClient subscribedClient;
        private ExpeditionAuthority composedAuthority;
        private readonly List<IExpeditionView> boundViews = new List<IExpeditionView>();

        public MissionProcessRole Role { get; private set; }
        public IExpeditionSessionPort PresentedPort => presented;
        public ExpeditionScenario Scenario => built;
        public ExpeditionAuthority Authority => bootstrap != null ? bootstrap.Authority : null;
        public IExpeditionIntents Intents => interactionInput != null ? interactionInput.Interaction : null;
        /// <summary>Raised whenever a port is presented (or null when it is withdrawn).</summary>
        public event Action<IExpeditionSessionPort> Presented;

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
            Debug.Log($"[ExpeditionSceneComposition] Role: {value}");
        }

        public ExpeditionAuthority ComposeAuthority()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Authority) throw new InvalidOperationException("Only the authority composes.");
            if (bootstrap == null) throw new InvalidOperationException("ExpeditionBootstrap not assigned.");
            if (bootstrap.Authority != null) return bootstrap.Authority;
            ExpeditionAuthority authority = bootstrap.Compose();
            composedAuthority = authority;
            built = authority.Scenario;
            Debug.Log($"[ExpeditionSceneComposition] Mission composed: '{bootstrap.ScenarioDefinition.DisplayName}'.");
            bootstrap.ShutDown -= OnAuthorityShutDown;
            bootstrap.ShutDown += OnAuthorityShutDown;
            if (networkHost != null) networkHost.Attach(authority);
            else Debug.LogWarning("[ExpeditionSceneComposition] No ExpeditionNetworkHost: only the local player can join.");
            string listening = networkHost != null && networkHost.IsListening ? " · escuchando en el puerto " + networkHost.Transport.Port : " · sin red";
            if (authorityPlays && !UnityEngine.Application.isBatchMode)
            {
                localPort = networkHost != null ? networkHost.CreateLocalPort() : new LocalExpeditionSessionPort(authority);
                Present(localPort);
                OutpostMembership membership = localPort.Join();
                SetStatus(membership.IsJoined, (membership.IsJoined ? LocalPlayerDetail : "La base está completa") + listening);
            }
            else SetStatus(false, DedicatedDetail + listening);
            return authority;
        }

        public void ComposeClient()
        {
            if (!roleApplied) throw new InvalidOperationException("ApplyRole first.");
            if (Role != MissionProcessRole.Client) throw new InvalidOperationException("The authority has no remote port.");
            if (networkClient == null) throw new InvalidOperationException("ExpeditionNetworkClient not assigned.");
            if (scenario == null) throw new InvalidOperationException("ExpeditionScenarioDefinition not assigned.");
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException($"ExpeditionScenarioDefinition '{scenario.name}': {problem}");
            if (built == null) built = scenario.ToScenario();
            ExpeditionNetworkSessionAdapter port = networkClient.Prepare(); // reads launch arguments first (see the client)
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

        /// <summary>Hands one port to every consumer. Null withdraws it everywhere.</summary>
        private void Present(IExpeditionSessionPort port)
        {
            foreach (IExpeditionView view in boundViews) view.Unbind();
            boundViews.Clear();
            presented = port;
            if (keyboardInput != null) keyboardInput.Bind(port, port == null ? null : built.Outpost);
            if (avatarSync != null) avatarSync.Bind(port);
            ExpeditionInteractionClient intents = interactionInput != null ? interactionInput.Bind(port, port == null ? null : built) : null;
            if (crewPresenter != null) crewPresenter.Bind(port, avatarSync);
            if (baseView != null && port != null && built != null) baseView.Configure(built.Outpost);
            if (sessionControls != null) sessionControls.Bind(port);
            if (port != null && intents != null && presentationMount != null)
            {
                var context = new ExpeditionViewContext(port, built, intents, SetMovementBlocked);
                foreach (IExpeditionView view in presentationMount.GetComponentsInChildren<IExpeditionView>(true))
                {
                    view.Bind(context);
                    boundViews.Add(view);
                }
            }
            Presented?.Invoke(port);
        }

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

        private void LateUpdate()
        {
            // Host.Start may open the socket after ComposeAuthority. Display the actual current listener state.
            if (Role != MissionProcessRole.Authority || composedAuthority == null) return;
            string listening = networkHost != null && networkHost.IsListening
                ? " · escuchando en el puerto " + networkHost.Transport.Port : " · sin red";
            SetStatus(localPort != null && localPort.Membership.IsJoined,
                (localPort != null ? LocalPlayerDetail : DedicatedDetail) + listening);
        }

        private void OnAuthorityShutDown(ExpeditionAuthority released)
        {
            if (!ReferenceEquals(composedAuthority, released)) return;
            composedAuthority = null;
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            try { ReleaseLocalPlayer(); }
            finally
            {
                if (networkHost != null && ReferenceEquals(networkHost.Authority, released)) networkHost.Detach();
                SetStatus(false, "La misión local se detuvo");
            }
        }

        private void ReleaseLocalPlayer()
        {
            if (localPort == null) return;
            LocalExpeditionSessionPort port = localPort;
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
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            try
            {
                if (localPort != null) ReleaseLocalPlayer();
                else if (presented != null) Present(null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExpeditionSceneComposition] Teardown: {e.GetType().Name}: {e.Message}");
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
