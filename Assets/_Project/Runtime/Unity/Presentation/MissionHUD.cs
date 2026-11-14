using System;
using Trainer.Application;
using UnityEngine;

namespace Trainer.Unity.Presentation
{
    /// <summary>Passive IMGUI view. Inject a main-thread port; no transport or authority is created here.</summary>
    public sealed class MissionHUD : MonoBehaviour
    {
        [SerializeField, Min(260)] private float panelWidth = 430;
        private ISessionPort port;
        private Action<MissionSnapshot> snapshotListener;
        private Action<CommandResult> resultListener;
        private SessionCommand pending;
        private Vector2 scroll;
        private string connectionDetail = "Transporte sin verificar";
        private readonly ConnectionPanel connection = new ConnectionPanel();
        private readonly PowerPanel power = new PowerPanel();
        private readonly ScienceControls science = new ScienceControls();
        private readonly ResultPanel result = new ResultPanel();

        public MissionSnapshot Snapshot { get; private set; }
        public bool IsPending => pending != null;
        public string CommandStatus { get; private set; } = "Sin solicitudes.";
        public string ConnectionText => connection.Text;
        public string PowerText => power.Text;
        public string ScienceText => science.Text;
        public string ResultText => result.Text;
        // Shared presentation state: world views reuse this binding and its single pending command.
        public event Action PresentationChanged;
        public Rect PanelRect => new Rect(12, 12, Mathf.Min(panelWidth, Mathf.Max(200, Screen.width - 24)),
            Mathf.Max(100, Screen.height - 24));

        /// <summary>Also use Bind(null) on transport loss. Rebinding invalidates all retained observations.</summary>
        public void Bind(ISessionPort sessionPort)
        {
            Detach();
            port = sessionPort;
            connectionDetail = "Transporte sin verificar";
            Clear();
            if (isActiveAndEnabled) Attach();
        }

        /// <summary>Optional external connection information. A loss unbinds and erases stale state.
        /// Reconnection requires Bind(port) again; this UI does not implement a network protocol.</summary>
        public void SetConnectionStatus(bool connected, string detail)
        {
            if (!connected) { Bind(null); return; }
            connectionDetail = string.IsNullOrWhiteSpace(detail) ? "Conexión confirmada externamente" : detail;
            connection.Show(isActiveAndEnabled && port != null, connectionDetail);
            PresentationChanged?.Invoke();
        }

        private void OnEnable() { Clear(); Attach(); }
        private void OnDisable() { Detach(); Clear(); connectionDetail = "Transporte sin verificar"; }
        private void OnDestroy() { Detach(); port = null; PresentationChanged = null; }

        private void Attach()
        {
            if (port == null || snapshotListener != null) return;
            ISessionPort source = port;
            // Callback identity also invalidates callbacks already captured by an old publisher.
            Action<MissionSnapshot> onSnapshot = null;
            onSnapshot = value => { if (snapshotListener == onSnapshot) ReceiveSnapshot(value); };
            Action<CommandResult> onResult = null;
            onResult = value => { if (resultListener == onResult) ReceiveResult(value); };
            snapshotListener = onSnapshot;
            resultListener = onResult;
            source.SnapshotChanged += onSnapshot;
            source.CommandCompleted += onResult;
            connection.Show(true, connectionDetail);
            if (source.TryGetSnapshot(out MissionSnapshot current)) onSnapshot(current);
            PresentationChanged?.Invoke();
        }

        private void Detach()
        {
            var oldSnapshot = snapshotListener;
            var oldResult = resultListener;
            snapshotListener = null;
            resultListener = null;
            if (port == null) return;
            if (oldSnapshot != null) port.SnapshotChanged -= oldSnapshot;
            if (oldResult != null) port.CommandCompleted -= oldResult;
        }

        private void Clear()
        {
            Snapshot = null;
            pending = null;
            CommandStatus = "Sin solicitudes.";
            connection.Show(false, null);
            power.Show(null);
            science.Show(null);
            result.Show(null);
            PresentationChanged?.Invoke();
        }

        private void ReceiveSnapshot(MissionSnapshot value)
        {
            if (value == null) return;
            if (Snapshot != null)
            {
                if (value.Epoch < Snapshot.Epoch ||
                    (value.Epoch == Snapshot.Epoch && value.Revision <= Snapshot.Revision)) return;
                if (value.Epoch > Snapshot.Epoch)
                {
                    // A Restart response carries the old epoch and can follow the new snapshot.
                    if (pending == null || pending.Action != SessionAction.Restart ||
                        pending.Epoch == long.MaxValue || value.Epoch != pending.Epoch + 1)
                    {
                        pending = null;
                        CommandStatus = "Nueva época de misión. Sin solicitudes.";
                    }
                }
            }
            Snapshot = value;
            power.Show(value.Power);
            science.Show(value);
            result.Show(value);
            PresentationChanged?.Invoke();
        }

        private void ReceiveResult(CommandResult value)
        {
            if (value == null || pending == null || value.CommandId != pending.Id || value.Epoch != pending.Epoch)
                return;
            pending = null;
            CommandStatus = (value.Accepted ? "Solicitud aceptada: " : "Solicitud rechazada: ") + value.Reason;
            PresentationChanged?.Invoke();
        }

        public void SetScienceEnabled(bool enabled) { Request(SessionAction.SetScienceEnabled, enabled); }
        public void Pause() { Request(SessionAction.Pause); }
        public void Resume() { Request(SessionAction.Resume); }
        public void Restart() { Request(SessionAction.Restart); }

        private void Request(SessionAction action, bool? scienceEnabled = null)
        {
            if (!isActiveAndEnabled || port == null || Snapshot == null || pending != null) return;
            var command = new SessionCommand(Guid.NewGuid(), Snapshot.Epoch, action, scienceEnabled);
            pending = command;
            CommandStatus = "Solicitud pendiente: " + action;
            ISessionPort source = port;
            PresentationChanged?.Invoke();
            if (pending != command || port != source) return;
            // Set pending before Submit: the shared fake may complete synchronously.
            try { source.Submit(command); }
            catch (Exception)
            {
                // Delivery may have happened before the transport threw. Do not invent a rejection
                // or erase a synchronous result/newer request raised during Submit.
                if (pending == command)
                {
                    pending = null;
                    CommandStatus = "Error de envío. Resultado desconocido; consulta el estado confirmado.";
                    PresentationChanged?.Invoke();
                }
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(PanelRect, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("ENTRENADOR DE MISIÓN · Energía y ciencia");
            connection.Draw();
            GUILayout.Space(12);
            result.Draw();
            GUILayout.Space(12);
            power.Draw();
            GUILayout.Space(12);
            science.Draw(Snapshot, !IsPending, (action, value) => Request(action, value));
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label(CommandStatus, wrap);
            GUILayout.Label("Las solicitudes esperan a la sesión. J = energía · W = potencia · s = tiempo.", wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
