using System;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Unity.Networking;
using Trainer.Unity.Presentation;
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
        private HudSkin skin;
        private bool embedded;
        private GUIStyle skinnedLabel, skinnedButton;

        /// <summary>Phase 2/3 layout: 1280×720 virtual canvas, strip at y 102.</summary>
        public const float DefaultCanvasWidth = 1280f, DefaultCanvasHeight = 720f, DefaultStripY = 102f;

        public OutpostCommand Pending => pending;
        public string Feedback => feedback;
        public string Status => status;
        public int DrawDepth => drawDepth;
        /// <summary>True when a dashboard draws these controls inside its own frame (see <see cref="EmbedIn"/>).</summary>
        public bool Embedded => embedded;

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

        /// <summary>Phase 4: the dashboard draws these controls in its own pass through <see cref="DrawDock"/> (status line plus a
        /// framed button dock), so they are part of its frame instead of a separate overlay. Phase 2/3 never call this.</summary>
        public void EmbedIn(HudSkin value)
        {
            skin = value != null && value.HasFrames ? value : null;
            embedded = true;
            skinnedLabel = skinnedButton = null;
        }

        /// <summary>Draws the session status in <paramref name="statusLine"/> and the session buttons right-aligned inside
        /// <paramref name="dock"/>. Call from the host dashboard's OnGUI, in its virtual canvas.</summary>
        public void DrawDock(Rect dock, Rect statusLine)
        {
            EnsureStyles();
            if (skinnedLabel == null)
            {
                skinnedLabel = new GUIStyle(label) { fontSize = 13 };
                skinnedLabel.normal.textColor = HudSkin.Label;
                skinnedButton = skin != null ? skin.Button(new GUIStyle(button) { fontSize = 14 }) : button;
            }
            string text = string.IsNullOrEmpty(feedback) ? status : feedback;
            HudSkin.Paint(new Rect(statusLine.x, statusLine.y + 3, 8, 14), confirmed ? HudSkin.Green : HudSkin.Amber);
            GUI.Label(new Rect(statusLine.x + 18, statusLine.y, statusLine.width - 18, statusLine.height), text, skinnedLabel);
            Color previousColor = GUI.color;
            try { Buttons(dock.xMax - 8, dock.y + (dock.height - 34) / 2, 34, DockStep, DockPrimaryStep, skinnedButton); }
            finally { GUI.enabled = true; GUI.color = previousColor; }
        }

        /// <summary>Horizontal space each dock button takes (button plus gap); the widest dock is connect + restart + primary.</summary>
        public const float DockStep = 132f, DockPrimaryStep = 152f, DockWidth = 2 * DockStep + DockPrimaryStep + 16f;

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

        private void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip,
                normal = { textColor = new Color(.75f, .83f, .9f) } };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        }

        private void OnGUI()
        {
            if (embedded) return; // The host dashboard draws us inside its own frame.
            EnsureStyles();
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
            GUI.color = new Color(.055f, .09f, .15f, .92f);
            GUI.DrawTexture(strip, Texture2D.whiteTexture);
            GUI.color = confirmed ? new Color(.45f, .9f, .6f) : new Color(.95f, .7f, .35f);
            GUI.DrawTexture(new Rect(strip.x, strip.y, 6, strip.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(30, stripY + 5, 760, 24), string.IsNullOrEmpty(feedback) ? status : feedback, label);
            Buttons(strip.xMax - 8, stripY + 3, 28, 130, 150, button);
        }

        /// <summary>Session buttons laid out right to left from <paramref name="right"/>: connect (clients), restart, primary.</summary>
        private void Buttons(float right, float buttonY, float buttonHeight, float step, float primaryStep, GUIStyle buttonStyle)
        {
            float x = right;
            bool joined = port != null && port.Membership.IsJoined;
            OutpostSessionSnapshot snapshot = null;
            bool hasState = joined && port.TryGetSnapshot(out snapshot) && snapshot != null;
            bool idle = pending == null;

            if (connectionState != null)
            {
                SessionConnectionState state = connectionState();
                bool offline = state == SessionConnectionState.Disconnected || state == SessionConnectionState.ConnectionLost;
                x -= step;
                if (offline)
                {
                    GUI.enabled = connect != null;
                    if (GUI.Button(new Rect(x, buttonY, step - 4, buttonHeight), "Conectar", buttonStyle)) connect?.Invoke();
                }
                else
                {
                    GUI.enabled = disconnect != null;
                    if (GUI.Button(new Rect(x, buttonY, step - 4, buttonHeight), "Desconectar", buttonStyle)) disconnect?.Invoke();
                }
                GUI.enabled = true;
            }
            if (!hasState) return;

            x -= step;
            GUI.enabled = idle;
            if (GUI.Button(new Rect(x, buttonY, step - 4, buttonHeight), "Reiniciar", buttonStyle)) Request(OutpostAction.Restart);

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
                x -= primaryStep;
                var primaryRect = new Rect(x, buttonY, primaryStep - 4, buttonHeight);
                string caption = idle ? primary : "Esperando…";
                // Starting the mission is the first thing a new crew must do: make it stand out in the dashboard skin.
                bool pressed = skin != null && snapshot.State == SessionState.Ready
                    ? skin.PrimaryButton(primaryRect, caption, buttonStyle)
                    : GUI.Button(primaryRect, caption, buttonStyle);
                if (pressed) Request(action);
            }
            GUI.enabled = true;
        }
    }
}
