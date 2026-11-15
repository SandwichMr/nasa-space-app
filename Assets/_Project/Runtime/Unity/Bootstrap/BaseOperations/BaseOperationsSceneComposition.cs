using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>
    /// Scene composition root of the phase 4 living base (<c>Phase04_LivingBase</c>, D2, single scene editor).
    /// <list type="bullet">
    /// <item>Authority: <see cref="BaseOperationsBootstrap.Compose"/> (ONE continuous authority, ONE runner over D1 base,
    /// EVA and equipment models and the D3 roster). Outside -batchmode the host also plays through a local port.</item>
    /// <item>ONE port goes to the phase 2 movement input and avatars, the phase 3 interaction keys, crew presenter, base
    /// view and session controls, every <see cref="IExpeditionView"/> under the phase 3 mount and every
    /// <see cref="IBaseOperationsView"/> under the phase 4 mount.</item>
    /// </list>
    /// In this branch (before <c>integration/phase-04</c>) the local port, the phase 4 intents and the phase 4 view are the
    /// labelled PROVISIONAL ones of <c>Provisional/</c>, and there is no phase 4 network: a client role is reported as not
    /// available. The integration recipe (D3's BaseOperationsNetworkHost/Client, LocalBaseOperationsSessionPort,
    /// BaseOperationsInteractionInput and D4's prefabs) is in docs/phase-04/handoffs/02_LIVING_BASE_HANDOFF.md.
    /// No fake or fixture is used.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class BaseOperationsSceneComposition : MonoBehaviour
    {
        public const string LocalPlayerDetail = "Anfitrión: esta ventana ejecuta la base viva y juega";
        public const string DedicatedDetail = "Servidor dedicado: sin jugador local";
        public const string NoClientYet = "Fase 4: el cliente en red llega con D3 en integration/phase-04";

        [SerializeField] private MissionProcessRole role = MissionProcessRole.Authority;
        [SerializeField] private bool readCommandLine = true;
        [SerializeField] private BaseOperationsScenarioDefinition scenario;
        [SerializeField] private BaseOperationsBootstrap bootstrap;
        [SerializeField] private CrewKeyboardInput keyboardInput;
        [SerializeField] private CrewAvatarSync avatarSync;
        [SerializeField] private ExpeditionInteractionInput interactionInput;
        [SerializeField] private ExpeditionCrewPresenter crewPresenter;
        [SerializeField] private BaseInteriorView baseView;
        [SerializeField] private OutpostSessionControls sessionControls;
        [SerializeField, Tooltip("Parent of every IExpeditionView bound on the same port.")] private Transform phase3Mount;
        [SerializeField, Tooltip("Parent of every IBaseOperationsView (D4 views, or the provisional one until they arrive).")]
        private Transform phase4Mount;
        [SerializeField] private bool authorityPlays = true;
        [SerializeField] private GameObject[] authorityOnly = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] clientOnly = Array.Empty<GameObject>();

        private bool roleApplied;
        private BaseOperationsScenario built;
        private ProvisionalLocalBaseOperationsPort localPort;
        private ProvisionalBaseOperationsIntents intents;
        private IBaseOperationsSessionPort presented;
        private BaseOperationsAuthority composedAuthority;
        private readonly List<IExpeditionView> boundPhase3 = new List<IExpeditionView>();
        private readonly List<IBaseOperationsView> boundPhase4 = new List<IBaseOperationsView>();

        public MissionProcessRole Role { get; private set; }
        public IBaseOperationsSessionPort PresentedPort => presented;
        public BaseOperationsScenario Scenario => built;
        public BaseOperationsAuthority Authority => bootstrap != null ? bootstrap.Authority : null;
        public IBaseOperationsIntents Intents => intents;
        public event Action<IBaseOperationsSessionPort> Presented;

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
            else
            {
                Debug.LogError("[BaseOperationsSceneComposition] " + NoClientYet);
                SetStatus(false, NoClientYet);
            }
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
            if (authorityPlays && !UnityEngine.Application.isBatchMode)
            {
                localPort = new ProvisionalLocalBaseOperationsPort(authority);
                Present(localPort);
                OutpostMembership membership = localPort.Join();
                SetStatus(membership.IsJoined, (membership.IsJoined ? LocalPlayerDetail : "La base está completa") + " · sin red (provisional)");
            }
            else SetStatus(false, DedicatedDetail + " · sin red (provisional)");
            return authority;
        }

        /// <summary>Hands one port to every consumer. Null withdraws it everywhere.</summary>
        private void Present(IBaseOperationsSessionPort port)
        {
            foreach (IExpeditionView view in boundPhase3) view.Unbind();
            foreach (IBaseOperationsView view in boundPhase4) view.Unbind();
            boundPhase3.Clear();
            boundPhase4.Clear();
            intents?.Dispose();
            intents = null;
            presented = port;
            ExpeditionScenario expedition = built?.Expedition;
            if (keyboardInput != null) keyboardInput.Bind(port, port == null ? null : expedition.Outpost);
            if (avatarSync != null) avatarSync.Bind(port);
            ExpeditionInteractionClient phase3Intents = interactionInput != null ? interactionInput.Bind(port, port == null ? null : expedition) : null;
            if (crewPresenter != null) crewPresenter.Bind(port, avatarSync);
            if (baseView != null && port != null && expedition != null) baseView.Configure(expedition.Outpost);
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
                if (phase4Mount != null)
                {
                    intents = new ProvisionalBaseOperationsIntents(port, built);
                    var p4 = new BaseOperationsViewContext(port, built, intents, phase3Intents, SetMovementBlocked);
                    foreach (IBaseOperationsView view in phase4Mount.GetComponentsInChildren<IBaseOperationsView>(true))
                    {
                        view.Bind(p4);
                        boundPhase4.Add(view);
                    }
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

        private void SetStatus(bool confirmed, string detail)
        {
            if (sessionControls != null) sessionControls.SetStatus(confirmed, detail);
        }

        private void OnAuthorityShutDown(BaseOperationsAuthority released)
        {
            if (!ReferenceEquals(composedAuthority, released)) return;
            composedAuthority = null;
            if (bootstrap != null) bootstrap.ShutDown -= OnAuthorityShutDown;
            ReleaseLocalPlayer();
            SetStatus(false, "La base local se detuvo");
        }

        private void ReleaseLocalPlayer()
        {
            if (localPort == null) return;
            ProvisionalLocalBaseOperationsPort port = localPort;
            localPort = null;
            try
            {
                if (ReferenceEquals(presented, port)) Present(null);
            }
            finally { port.Dispose(); }
        }

        private void OnDestroy()
        {
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
