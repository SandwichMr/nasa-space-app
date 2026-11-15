using System;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking;
using Trainer.Unity.Presentation.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap
{
    /// <summary>
    /// Session-level intents of the phase 2 scene: start (Resume from Ready), pause, resume and restart, plus the
    /// client's connect/disconnect. The authority starts every epoch in Ready and D4's <see cref="OutpostPanel"/>
    /// only covers station tasks, so without this strip nobody could start or retry the mission.
    ///
    /// It only sends <see cref="OutpostCommand"/>s through the bound port with the epoch of the last confirmed
    /// snapshot; the authority validates them (any present member may pause/restart: still an open team decision,
    /// as in phase 1). One request in flight; nothing changes on screen until the next snapshot. Drawn with IMGUI
    /// in the same 1280×720 virtual canvas as the panel, in the row the panel leaves free (y 102–132).
    /// Integration control owned by D2; D4 may replace it with designed buttons later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutpostSessionControls : MonoBehaviour
    {
        private IOutpostSessionPort port;
        private OutpostCommand pending;
        private string feedback;
        private bool confirmed;
        private string status = "";
        private Action connect;
        private Action disconnect;
        private Func<SessionConnectionState> connectionState;
        private GUIStyle label, button;
        private float canvasWidth = DefaultCanvasWidth, canvasHeight = DefaultCanvasHeight, stripY = DefaultStripY;
        private int drawDepth;

        /// <summary>Phase 2/3 layout: 1280×720 virtual canvas, strip at y 102.</summary>
        public const float DefaultCanvasWidth = 1280f, DefaultCanvasHeight = 720f, DefaultStripY = 102f;

        public OutpostCommand Pending => pending;
        public string Feedback => feedback;
        public string Status => status;
        public int DrawDepth => drawDepth;

        /// <summary>Optional overlay depth for a scene whose HUD paints a background behind this strip.</summary>
        public void ConfigureDrawDepth(int value) => drawDepth = value;

        /// <summary>Connects to the presentation port (host-local or network adapter). Null unbinds.</summary>
        public void Bind(IOutpostSessionPort value)
        {
            if (ReferenceEquals(value, port)) return;
            Unsubscribe();
            pending = null;
            feedback = null;
            port = value;
            if (port == null) return;
            port.CommandCompleted += OnCompleted;
            port.CommandAbandoned += OnAbandoned;
            port.MembershipChanged += OnMembership;
        }

        /// <summary>Client only: connection actions. The host leaves them unset.</summary>
        public void ConfigureConnection(Action connectAction, Action disconnectAction, Func<SessionConnectionState> state)
        {
            connect = connectAction;
            disconnect = disconnectAction;
            connectionState = state;
        }

        /// <summary>Optional: draw the strip in another scene's virtual canvas (phase 4: D4's 1440×900 HUD) at the row that
        /// scene's panel leaves free. Without it the phase 2/3 layout is unchanged.</summary>
        public void ConfigureCanvas(float width, float height, float stripTop)
        {
            if (!(width >= 640f) || !(height >= 360f) || !(stripTop >= 0f) || stripTop > height - 34f)
                throw new ArgumentOutOfRangeException(nameof(stripTop), "Canvas at least 640×360 and the strip inside it.");
            canvasWidth = width;
            canvasHeight = height;
            stripY = stripTop;
        }

        public void SetStatus(bool isConfirmed, string detail)
        {
            confirmed = isConfirmed;
            status = detail ?? "";
        }

        /// <summary>Sends a session action with the confirmed epoch. False if not joined, no snapshot or busy.</summary>
        public bool Request(OutpostAction action)
        {
            if (action != OutpostAction.Pause && action != OutpostAction.Resume && action != OutpostAction.Restart)
                throw new ArgumentOutOfRangeException(nameof(action), "Only session actions; tasks go through D3's interaction.");
            if (port == null || pending != null || !port.Membership.IsJoined) return false;
            if (!port.TryGetSnapshot(out OutpostSessionSnapshot snapshot) || snapshot == null) return false;
            var command = new OutpostCommand(Guid.NewGuid(), snapshot.Epoch, action);
            pending = command;
            feedback = null;
            try { port.Submit(command); } // A local port may answer synchronously.
            catch
            {
                if (ReferenceEquals(pending, command)) OnAbandoned(command, OutpostReasons.ConnectionLost);
                throw;
            }
            return true;
        }

        private void OnCompleted(CommandResult result)
        {
            if (pending == null || result.CommandId != pending.Id || result.Epoch != pending.Epoch) return;
            pending = null;
            feedback = result.Accepted ? null : OutpostPanel.DescribeReason(result.Reason);
        }

        private void OnAbandoned(OutpostCommand command, string reason)
        {
            if (pending == null || command.Id != pending.Id || command.Epoch != pending.Epoch) return;
            pending = null;
            feedback = "Resultado sin confirmar. " + OutpostPanel.DescribeReason(reason);
        }

        private void OnMembership(OutpostMembership membership)
        {
            if (!membership.IsJoined) pending = null;
        }

        private void Unsubscribe()
        {
            if (port == null) return;
            port.CommandCompleted -= OnCompleted;
            port.CommandAbandoned -= OnAbandoned;
            port.MembershipChanged -= OnMembership;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            port = null;
        }

        private void OnGUI()
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip,
                    normal = { textColor = new Color(.75f, .83f, .9f) } };
                button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            }
            Matrix4x4 previous = GUI.matrix;
            int previousDepth = GUI.depth;
            GUI.depth = drawDepth;
            float scale = Mathf.Min(Screen.width / canvasWidth, Screen.height / canvasHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - canvasWidth * scale) / 2, (Screen.height - canvasHeight * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));
            try { Draw(); }
            finally { GUI.matrix = previous; GUI.depth = previousDepth; GUI.enabled = true; GUI.color = Color.white; }
        }

        private void Draw()
        {
            var strip = new Rect(16, stripY, canvasWidth - 32, 34);
            float buttonY = stripY + 3;
            GUI.color = new Color(.055f, .09f, .15f, .92f);
            GUI.DrawTexture(strip, Texture2D.whiteTexture);
            GUI.color = confirmed ? new Color(.45f, .9f, .6f) : new Color(.95f, .7f, .35f);
            GUI.DrawTexture(new Rect(strip.x, strip.y, 6, strip.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string text = string.IsNullOrEmpty(feedback) ? status : feedback;
            GUI.Label(new Rect(30, stripY + 5, 760, 24), text, label);

            float x = strip.xMax - 8;
            bool joined = port != null && port.Membership.IsJoined;
            OutpostSessionSnapshot snapshot = null;
            bool hasState = joined && port.TryGetSnapshot(out snapshot) && snapshot != null;
            bool idle = pending == null;

            if (connectionState != null)
            {
                SessionConnectionState state = connectionState();
                bool offline = state == SessionConnectionState.Disconnected || state == SessionConnectionState.ConnectionLost;
                x -= 130;
                if (offline)
                {
                    GUI.enabled = connect != null;
                    if (GUI.Button(new Rect(x, buttonY, 126, 28), "Conectar", button)) connect?.Invoke();
                }
                else
                {
                    GUI.enabled = disconnect != null;
                    if (GUI.Button(new Rect(x, buttonY, 126, 28), "Desconectar", button)) disconnect?.Invoke();
                }
                GUI.enabled = true;
            }
            if (!hasState) return;

            x -= 130;
            GUI.enabled = idle;
            if (GUI.Button(new Rect(x, buttonY, 126, 28), "Reiniciar", button)) Request(OutpostAction.Restart);

            string primary = null;
            OutpostAction action = OutpostAction.Resume;
            switch (snapshot.State)
            {
                case SessionState.Ready: primary = "Iniciar misión"; action = OutpostAction.Resume; break;
                case SessionState.Running: primary = "Pausar"; action = OutpostAction.Pause; break;
                case SessionState.Paused: primary = "Reanudar"; action = OutpostAction.Resume; break;
            }
            if (primary != null)
            {
                x -= 150;
                if (GUI.Button(new Rect(x, buttonY, 146, 28), idle ? primary : "Esperando…", button)) Request(action);
            }
            GUI.enabled = true;
        }
    }
}
