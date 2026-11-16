using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Trainer.Unity.MainMenu
{
    public sealed class MainMenuView : MonoBehaviour
    {
        public MainMenuConfiguration configuration;
        [Tooltip("Optional integrator component implementing IMainMenuLaunchPort. No transport is created by the menu.")]
        public MonoBehaviour launchAdapter;
        public GameObject home, help, settings, credits, connection, multiplayer;
        public Button joinOnline, multiplayerBack;
        public Text endpointLabel;
        public Button play, helpButton, settingsButton, creditsButton, quit, retry, back, apply, cancel, confirm;
        public Button[] closeButtons;
        public Text connectionDetail, version, displayLabel, volumeLabel, confirmLabel;
        public Slider volume;
        public Toggle mute, fullscreen;
        public Dropdown resolution;
        private MenuLaunchFlow flow;
        private GameObject page;
        private float originalVolume;
        private bool originalMute;
        private int oldWidth, oldHeight;
        private FullScreenMode oldMode;
        private double confirmDeadline;
        private bool confirming, applied;
        private readonly Vector2Int[] sizes = { new Vector2Int(1024, 768), new Vector2Int(1280, 800), new Vector2Int(1280, 720), new Vector2Int(1920, 1080) };
        private void Awake()
        {
            if (UnityEngine.Application.isBatchMode) { gameObject.SetActive(false); return; }
            MenuPreferences.Load();
            if (!UnityEngine.Application.isEditor && UnityEngine.Application.platform != RuntimePlatform.WebGLPlayer) MenuPreferences.LoadDisplay();
            flow = new MenuLaunchFlow(launchAdapter as IMainMenuLaunchPort, configuration);
            flow.Updated += UpdateConnection;
            play.onClick.AddListener(() => Show(multiplayer));
            joinOnline.onClick.AddListener(Connect); retry.onClick.AddListener(Connect);
            multiplayerBack.onClick.AddListener(() => Show(home));
            endpointLabel.text = configuration != null && configuration.HasEndpoint ? configuration.Endpoint : "Servidor público pendiente de configurar";
            helpButton.onClick.AddListener(() => Show(help));
            creditsButton.onClick.AddListener(() => Show(credits));
            settingsButton.onClick.AddListener(OpenSettings);
            quit.onClick.AddListener(() => UnityEngine.Application.Quit());
            quit.gameObject.SetActive(!UnityEngine.Application.isEditor && UnityEngine.Application.platform != RuntimePlatform.WebGLPlayer);
            foreach (var button in closeButtons) button.onClick.AddListener(() => Show(home));
            back.onClick.AddListener(() => { flow.Leave(); Show(multiplayer); });
            apply.onClick.AddListener(Apply); cancel.onClick.AddListener(CancelSettings); confirm.onClick.AddListener(ConfirmDisplay);
            volume.onValueChanged.AddListener(v => { MenuPreferences.Set(v, mute.isOn, false); volumeLabel.text = "Volumen maestro · " + Mathf.RoundToInt(v * 100) + "%"; });
            mute.onValueChanged.AddListener(v => MenuPreferences.Set(volume.value, v, false));
            version.text = "BUILD " + UnityEngine.Application.version + "  /  " + UnityEngine.Application.unityVersion;
            Show(home);
        }
        private void Connect()
        {
            Show(connection);
            flow.Connect(Time.realtimeSinceStartupAsDouble);
            if (configuration != null && configuration.HasEndpoint) Debug.Log("[MainMenu] " + configuration.Endpoint + " / " + flow.Detail);
        }
        private void UpdateConnection()
        {
            connectionDetail.text = flow.Detail;
            retry.gameObject.SetActive(flow.State == MenuLaunchState.Failed);
            back.interactable = true;
            back.GetComponentInChildren<Text>().text = flow.State == MenuLaunchState.Joined ? "Desconectar y volver" : "Volver / Cancelar";
            if (page == connection && !flow.Busy) Select(retry.gameObject.activeSelf ? retry : back);
        }
        private void Update()
        {
            flow?.Tick(Time.realtimeSinceStartupAsDouble);
            if (confirming)
            {
                confirmLabel.text = "¿Conservar pantalla? " + Mathf.Max(0, (int)Math.Ceiling(confirmDeadline - Time.realtimeSinceStartupAsDouble)) + " s";
                if (Time.realtimeSinceStartupAsDouble >= confirmDeadline) RevertDisplay();
            }
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                var choices = page.GetComponentsInChildren<Selectable>().Where(s => s.IsActive() && s.IsInteractable()).ToArray();
                int current = Array.FindIndex(choices, s => s.gameObject == EventSystem.current?.currentSelectedGameObject);
                int step = Keyboard.current.shiftKey.isPressed ? -1 : 1;
                if (choices.Length > 0) choices[(current + step + choices.Length) % choices.Length].Select();
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (page == settings) CancelSettings();
                else if (page == connection) { flow.Leave(); Show(multiplayer); }
                else Show(home);
            }
        }
        private void Show(GameObject target)
        {
            foreach (var item in new[] { home, help, settings, credits, connection, multiplayer })
            {
                GameObject root = item == home ? item : item.transform.parent.gameObject;
                root.SetActive(item == target);
            }
            page = target;
            var first = target.GetComponentInChildren<Button>();
            if (first != null) Select(first);
        }
        private static void Select(Button b) { EventSystem.current?.SetSelectedGameObject(null); b.Select(); }
        private void OpenSettings()
        {
            originalVolume = MenuPreferences.Volume; originalMute = MenuPreferences.Muted;
            volume.value = originalVolume; mute.isOn = originalMute;
            oldWidth = Screen.width; oldHeight = Screen.height; oldMode = Screen.fullScreenMode;
            fullscreen.isOn = Screen.fullScreen;
            resolution.ClearOptions();
            var options = new System.Collections.Generic.List<string>();
            options.Add("Actual · " + oldWidth + " × " + oldHeight);
            foreach (var s in sizes) options.Add(s.x + " × " + s.y);
            resolution.AddOptions(options); resolution.value = 0;
            bool desktop = UnityEngine.Application.platform != RuntimePlatform.WebGLPlayer && !UnityEngine.Application.isEditor;
            resolution.interactable = desktop; fullscreen.interactable = desktop;
            displayLabel.text = desktop ? "Pantalla · confirma los cambios en 12 segundos" : "Pantalla administrada por el Editor o navegador";
            applied = false; confirm.gameObject.SetActive(false); apply.interactable = true;
            Show(settings);
        }
        private void Apply()
        {
            if (confirming) return;
            bool desktop = resolution.interactable;
            Vector2Int size = resolution.value == 0 ? new Vector2Int(oldWidth, oldHeight) : sizes[resolution.value - 1];
            FullScreenMode mode = fullscreen.isOn ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (desktop && (size.x != oldWidth || size.y != oldHeight || mode != oldMode))
            {
                Screen.SetResolution(size.x, size.y, mode);
                confirming = true; confirmDeadline = Time.realtimeSinceStartupAsDouble + 12;
                confirm.gameObject.SetActive(true); apply.interactable = false; Select(confirm);
            }
            else CommitSettings();
        }
        private void ConfirmDisplay() { if (!confirming) return; confirming = false; MenuPreferences.SaveDisplay(); CommitSettings(); }
        private void CommitSettings()
        {
            MenuPreferences.Set(volume.value, mute.isOn, true); applied = true; Show(home);
        }
        private void RevertDisplay()
        {
            Screen.SetResolution(oldWidth, oldHeight, oldMode); confirming = false;
            confirm.gameObject.SetActive(false); apply.interactable = true; resolution.value = 0; fullscreen.isOn = oldMode != FullScreenMode.Windowed;
            Select(apply);
        }
        private void CancelSettings()
        {
            if (confirming) RevertDisplay();
            MenuPreferences.Set(originalVolume, originalMute, false); Show(home);
        }
        private void OnDisable()
        {
            if (confirming) RevertDisplay();
            if (page == settings && !applied) MenuPreferences.Set(originalVolume, originalMute, false);
            flow?.Cancel();
        }
        private void OnDestroy() { if (flow != null) { flow.Updated -= UpdateConnection; flow.Dispose(); } }
    }
}
