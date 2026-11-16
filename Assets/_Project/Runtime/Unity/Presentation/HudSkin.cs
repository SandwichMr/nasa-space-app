using System;
using UnityEngine;

namespace Trainer.Unity.Presentation
{
    /// <summary>Optional pixel-art skin for the IMGUI dashboards. Purely visual: every missing texture falls back to a flat
    /// colour, and a component without a skin keeps its previous look. Textures come from Art/Icons/LunarHud.</summary>
    [Serializable]
    public sealed class HudSkin
    {
        [Header("9-slice frames")]
        public Texture2D cardFrame;
        public Texture2D panelFrame;
        public Texture2D button, buttonHover, buttonActive;
        [Tooltip("Highlighted call to action, e.g. starting the mission.")] public Texture2D buttonPrimary, buttonPrimaryHover;
        [Header("Bars (9-slice)")]
        public Texture2D barTrack;
        public Texture2D barBlue, barCyan, barGreen, barAmber, barRed;
        [Header("Status symbols")]
        public Texture2D statusOk, statusWarn, statusCritical, statusNone;
        [Header("Resource icons")]
        public Texture2D iconBattery, iconWater, iconFood, iconOxygen, iconCarbonDioxide;
        [Header("Trend arrows and badge")]
        public Texture2D trendUp, trendDown, trendFlat, badge;

        public const int FrameBorder = 12, ButtonBorder = 6, BarBorder = 6;

        public static readonly Color Ink = new Color(.024f, .047f, .106f, .96f);
        public static readonly Color CardInk = new Color(.043f, .082f, .172f, .94f);
        public static readonly Color Line = new Color(.18f, .36f, .80f, 1);
        public static readonly Color Title = new Color(.94f, .97f, 1f, 1);
        public static readonly Color Label = new Color(.62f, .76f, 1f, 1);
        public static readonly Color Dim = new Color(.52f, .60f, .74f, 1);
        public static readonly Color Cyan = new Color(.27f, .86f, .95f, 1);
        public static readonly Color Green = new Color(.33f, .86f, .42f, 1);
        public static readonly Color Amber = new Color(1f, .70f, .20f, 1);
        public static readonly Color Red = new Color(1f, .33f, .30f, 1);

        public bool HasFrames => cardFrame != null && panelFrame != null && button != null;

        [NonSerialized] private GUIStyle cardStyle, panelStyle, trackStyle, buttonStyle, primaryStyle;
        [NonSerialized] private GUIStyle[] fillStyles;

        /// <summary>Button style over <paramref name="baseStyle"/>; the base style when the skin has no button art.</summary>
        public GUIStyle Button(GUIStyle baseStyle)
        {
            if (button == null) return baseStyle;
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(baseStyle)
                {
                    border = new RectOffset(ButtonBorder, ButtonBorder, ButtonBorder, ButtonBorder),
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 4, 4)
                };
                buttonStyle.normal.background = button;
                buttonStyle.hover.background = buttonHover != null ? buttonHover : button;
                buttonStyle.active.background = buttonActive != null ? buttonActive : button;
                buttonStyle.focused.background = button;
                buttonStyle.onNormal.background = buttonActive != null ? buttonActive : button;
                buttonStyle.normal.textColor = buttonStyle.focused.textColor = new Color(.80f, .88f, 1f);
                buttonStyle.hover.textColor = Color.white;
                buttonStyle.active.textColor = buttonStyle.onNormal.textColor = new Color(.75f, 1f, 1f);
            }
            return buttonStyle;
        }

        /// <summary>Highlighted button with a soft pulsing glow; falls back to a green-tinted <paramref name="baseStyle"/>.</summary>
        public bool PrimaryButton(Rect rect, string caption, GUIStyle baseStyle)
        {
            if (GUI.enabled)
            {
                float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f);
                Paint(new Rect(rect.x - 4, rect.y - 4, rect.width + 8, rect.height + 8), new Color(Green.r, Green.g, Green.b, .16f + .22f * pulse));
                Paint(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(Green.r, Green.g, Green.b, .20f + .25f * pulse));
            }
            if (buttonPrimary == null)
            {
                Color old = GUI.backgroundColor; GUI.backgroundColor = Green;
                try { return GUI.Button(rect, caption, baseStyle); } finally { GUI.backgroundColor = old; }
            }
            if (primaryStyle == null)
            {
                primaryStyle = new GUIStyle(Button(baseStyle)) { fontSize = baseStyle.fontSize + 1 };
                primaryStyle.normal.background = primaryStyle.focused.background = buttonPrimary;
                primaryStyle.hover.background = buttonPrimaryHover != null ? buttonPrimaryHover : buttonPrimary;
                primaryStyle.active.background = buttonActive != null ? buttonActive : buttonPrimary;
                primaryStyle.normal.textColor = primaryStyle.focused.textColor = primaryStyle.hover.textColor = Color.white;
            }
            return GUI.Button(rect, caption, primaryStyle);
        }

        public void Card(Rect rect) => Frame(rect, cardFrame, ref cardStyle, CardInk);
        public void Panel(Rect rect) => Frame(rect, panelFrame, ref panelStyle, Ink);

        /// <summary>Track plus a fill clipped to <paramref name="fraction"/>, so the rounded pill end is never squashed.</summary>
        public void Bar(Rect rect, float fraction, Texture2D fill, Color fallback)
        {
            fraction = Mathf.Clamp01(fraction);
            Frame(rect, barTrack, ref trackStyle, new Color(.02f, .04f, .09f, 1), BarBorder);
            if (fraction <= 0) return;
            var fillRect = new Rect(0, 0, rect.width, rect.height);
            GUI.BeginGroup(new Rect(rect.x, rect.y, Mathf.Max(2f, rect.width * fraction), rect.height));
            try
            {
                if (fill == null) Paint(new Rect(1, 1, rect.width - 2, rect.height - 2), fallback);
                else if (Event.current.type == EventType.Repaint) FillStyle(fill).Draw(fillRect, false, false, false, false);
            }
            finally { GUI.EndGroup(); }
        }

        public static void Icon(Rect rect, Texture2D texture)
        {
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
        }

        public static void Paint(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }

        private static void Frame(Rect rect, Texture2D texture, ref GUIStyle style, Color fallback, int border = FrameBorder)
        {
            if (texture == null) { Paint(rect, fallback); return; }
            if (style == null || style.normal.background != texture)
                style = new GUIStyle { border = new RectOffset(border, border, border, border), normal = { background = texture } };
            if (Event.current.type == EventType.Repaint) style.Draw(rect, false, false, false, false);
        }

        private GUIStyle FillStyle(Texture2D texture)
        {
            fillStyles ??= new GUIStyle[0];
            foreach (var s in fillStyles) if (s.normal.background == texture) return s;
            var style = new GUIStyle { border = new RectOffset(BarBorder, BarBorder, BarBorder, BarBorder), normal = { background = texture } };
            Array.Resize(ref fillStyles, fillStyles.Length + 1);
            fillStyles[fillStyles.Length - 1] = style;
            return style;
        }
    }
}
