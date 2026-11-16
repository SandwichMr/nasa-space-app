using UnityEngine;

namespace Trainer.Unity.MainMenu
{
    public static class MenuPreferences
    {
        private const string Prefix = "main-menu.";
        public static float Volume { get; private set; } = .7f;
        public static bool Muted { get; private set; }
        public static void Load()
        {
            float value = PlayerPrefs.GetFloat(Prefix + "volume", .7f);
            Volume = float.IsNaN(value) || float.IsInfinity(value) ? .7f : Mathf.Clamp01(value);
            Muted = PlayerPrefs.GetInt(Prefix + "muted", 0) == 1;
            ApplyAudio();
        }
        public static void Set(float volume, bool muted, bool save)
        {
            Volume = float.IsNaN(volume) || float.IsInfinity(volume) ? .7f : Mathf.Clamp01(volume);
            Muted = muted; ApplyAudio();
            if (!save) return;
            PlayerPrefs.SetFloat(Prefix + "volume", Volume);
            PlayerPrefs.SetInt(Prefix + "muted", Muted ? 1 : 0);
            PlayerPrefs.Save();
        }
        public static void ApplyAudio() => AudioListener.volume = Muted ? 0 : Volume;
        public static void SaveDisplay()
        {
            PlayerPrefs.SetInt(Prefix + "width", Screen.width);
            PlayerPrefs.SetInt(Prefix + "height", Screen.height);
            PlayerPrefs.SetInt(Prefix + "mode", (int)Screen.fullScreenMode);
            PlayerPrefs.Save();
        }
        public static void LoadDisplay()
        {
            int w = PlayerPrefs.GetInt(Prefix + "width", 0), h = PlayerPrefs.GetInt(Prefix + "height", 0);
            int mode = PlayerPrefs.GetInt(Prefix + "mode", (int)FullScreenMode.Windowed);
            if (w < 640 || h < 480 || w > Display.main.systemWidth || h > Display.main.systemHeight
                || (mode != (int)FullScreenMode.Windowed && mode != (int)FullScreenMode.FullScreenWindow)) return;
            Screen.SetResolution(w, h, (FullScreenMode)mode);
        }
    }
}
