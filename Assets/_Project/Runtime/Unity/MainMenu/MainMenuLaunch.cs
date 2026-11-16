using System;
using UnityEngine;

namespace Trainer.Unity.MainMenu
{
    public enum MenuLaunchState { Ready, Connecting, Synchronizing, Loading, Joined, Failed }

    // The integrator owns the one transport and gameplay lifetime. Every callback carries its attempt id.
    public interface IMainMenuLaunchPort
    {
        event Action<int, MenuLaunchState, string> Changed;
        void Connect(int attempt, MainMenuConfiguration configuration);
        void Cancel(int attempt);
        void Leave();
    }

    public sealed class MenuLaunchFlow : IDisposable
    {
        private readonly IMainMenuLaunchPort port;
        private readonly MainMenuConfiguration config;
        private int attempt;
        private double deadline;
        public MenuLaunchState State { get; private set; }
        public string Detail { get; private set; } = "";
        public bool Busy => State == MenuLaunchState.Connecting || State == MenuLaunchState.Synchronizing || State == MenuLaunchState.Loading;
        public event Action Updated;
        public MenuLaunchFlow(IMainMenuLaunchPort port, MainMenuConfiguration config)
        {
            this.port = port; this.config = config;
            if (port != null) port.Changed += OnChanged;
        }
        public void Connect(double now)
        {
            if (Busy || State == MenuLaunchState.Joined) return;
            Cancel();
            if (config == null || !config.HasEndpoint) { Set(MenuLaunchState.Failed, "Falta configurar el servidor público. Integración online pendiente."); return; }
            if (port == null) { Set(MenuLaunchState.Failed, "Falta conectar el menú con el arranque de la partida. Integración pendiente."); return; }
            deadline = now + (float.IsNaN(config.timeoutSeconds) ? 15 : Mathf.Clamp(config.timeoutSeconds, 3, 60));
            Set(MenuLaunchState.Connecting, "Conectando con el servidor…");
            try { port.Connect(attempt, config); }
            catch (Exception e) { Fail("No se pudo iniciar la conexión."); Debug.LogException(e); }
        }
        public void Tick(double now) { if (Busy && now >= deadline) Fail("El servidor no respondió a tiempo. Puedes reintentar."); }
        private void OnChanged(int id, MenuLaunchState state, string detail)
        {
            if (id != attempt || (!Busy && State != MenuLaunchState.Joined)) return;
            if (state == MenuLaunchState.Failed) { Fail(detail); return; }
            // Joined must mean admission plus initial snapshot, never just an open socket.
            if (state == MenuLaunchState.Connecting || state == MenuLaunchState.Synchronizing || state == MenuLaunchState.Loading || state == MenuLaunchState.Joined)
                Set(state, detail);
        }
        private void Fail(string detail)
        {
            int previous = attempt++; // Invalidate before cancellation can emit synchronous callbacks.
            Set(MenuLaunchState.Failed, detail);
            port?.Cancel(previous);
        }
        public void Cancel()
        {
            int previous = attempt++;
            bool wasBusy = Busy;
            Set(MenuLaunchState.Ready, "");
            if (wasBusy) port?.Cancel(previous);
        }
        public void Leave() { Cancel(); port?.Leave(); }
        private void Set(MenuLaunchState state, string detail) { State = state; Detail = detail ?? ""; Updated?.Invoke(); }
        public void Dispose() { if (port != null) port.Changed -= OnChanged; Cancel(); }
    }
}
