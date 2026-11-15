using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.World.Outpost;
using UnityEngine;

namespace Trainer.Unity.Bootstrap.Provisional
{
    /// <summary>
    /// PROVISIONAL (D2 integration, not final art): world overlays drawn from the scenario at runtime, so the exterior,
    /// airlock and shelter are recognisable until D4's art arrives through the same <see cref="IExpeditionView"/> mount.
    /// Flat tints and text only, reusing the material and font of the phase 2 base view (no new art assets). It reads the
    /// confirmed snapshot: hatch open/closed, chamber pressure, shelter state and per-astronaut suit/margin badges. No
    /// collider, no movement, no state of its own. The exterior tint is grey regolith under black sky: no Earth atmosphere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProvisionalExpeditionWorld : MonoBehaviour, IExpeditionView
    {
        public const string Label = "PROVISIONAL · superposiciones de integración (D2) · se sustituyen por el arte de D4";

        [SerializeField] private BaseInteriorView baseView;
        [SerializeField] private CrewAvatarSync avatars;

        private ExpeditionViewContext context;
        private Transform root;
        private Sprite square;
        private Material material;
        private Font font;
        private SpriteRenderer innerHatch, outerHatch, chamber, shelterTint;
        private TextMesh shelterText, airlockText;
        private readonly Dictionary<PlayerId, TextMesh> badges = new Dictionary<PlayerId, TextMesh>();

        public void Bind(ExpeditionViewContext value)
        {
            Unbind();
            context = value;
            Build(context.Scenario);
            context.Port.ExpeditionChanged += OnExpedition;
            if (context.Port.TryGetExpedition(out ExpeditionSessionSnapshot current)) OnExpedition(current);
        }

        public void Unbind()
        {
            if (context != null) context.Port.ExpeditionChanged -= OnExpedition;
            context = null;
            foreach (TextMesh badge in badges.Values) if (badge != null) Destroy(badge.gameObject);
            badges.Clear();
            if (root != null) Destroy(root.gameObject);
            root = null;
        }

        private void OnDestroy() => Unbind();

        private void Build(ExpeditionScenario s)
        {
            root = new GameObject("PROVISIONAL phase 3 overlays (D2)").transform;
            root.SetParent(transform, false);
            var texture = new Texture2D(4, 4) { filterMode = FilterMode.Point };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            square = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4, 0, SpriteMeshType.FullRect);
            SpriteRenderer sample = baseView != null ? baseView.GetComponentInChildren<SpriteRenderer>(true) : null;
            material = sample != null ? sample.sharedMaterial : null;
            TextMesh sampleText = baseView != null ? baseView.GetComponentInChildren<TextMesh>(true) : null;
            font = sampleText != null ? sampleText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            foreach (ZoneRegion region in s.Zones)
            {
                if (region.Zone == CrewZone.Exterior) Rect("Exterior regolith", region.Area, new Color(.36f, .36f, .38f, .85f), -900);
                if (region.Zone == CrewZone.Shelter) shelterTint = Rect("Shelter area", region.Area, new Color(.95f, .65f, .2f, .18f), -890);
            }
            chamber = Rect("Airlock chamber", s.Airlock.Chamber, new Color(.3f, .6f, 1f, .25f), -880);
            innerHatch = Rect("Inner hatch", s.Airlock.InnerHatch, Color.green, -700);
            outerHatch = Rect("Outer hatch", s.Airlock.OuterHatch, Color.red, -700);
            Text("EXTERIOR · superficie lunar (sin atmósfera)", new Vector3(43, 15.2f), 0.22f, new Color(.85f, .85f, .9f));
            Text("ALA EVA · preparación de trajes", new Vector3(22.1f, 9.95f), 0.16f, Color.white);
            shelterText = Text("REFUGIO", new Vector3(22.1f, 4.3f), 0.18f, new Color(1f, .8f, .4f));
            airlockText = Text("ESCLUSA", new Vector3(27.65f, 9.5f), 0.16f, new Color(.6f, .85f, 1f));
            foreach (StationPlacement station in s.Layout.Stations)
            {
                string label = StationText(station.Id.Value);
                if (label != null) Text(label, new Vector3((float)station.Position.X, (float)station.Position.Y - 0.85f), 0.11f, new Color(.95f, .95f, .7f));
            }
            BasePosition antenna = s.Comms.Antenna;
            Text("ANTENA", new Vector3((float)antenna.X, (float)antenna.Y), 0.12f, Color.cyan);
            for (int i = 0; i < 72; i++)
            {
                float a = i * Mathf.PI * 2 / 72;
                var p = new Vector3((float)(antenna.X + s.Comms.ExteriorRangeMeters * Mathf.Cos(a)), (float)(antenna.Y + s.Comms.ExteriorRangeMeters * Mathf.Sin(a)));
                if (!s.TryGetZone(new BasePosition(p.x, p.y), out CrewZone z) || z != CrewZone.Exterior) continue;
                Rect("Radio range", new BaseRect(p.x - .06, p.y - .06, p.x + .06, p.y + .06), new Color(.3f, .9f, 1f, .6f), -650);
            }
        }

        private static string StationText(string id)
        {
            switch (id)
            {
                case "suit-rack-a": return "TRAJE A";
                case "suit-rack-b": return "TRAJE B";
                case "suit-charger": return "CARGAR BATERÍA";
                case "consumables-panel": return "O2 + AGUA";
                case "suit-check": return "REVISAR SELLOS";
                case "airlock-inner-panel": return "PANEL ESCLUSA";
                case "airlock-outer-panel": return "PANEL ESCLUSA";
                case "shelter-panel": return "PREPARAR REFUGIO";
                case "regolith-site": return "MUESTRA DE REGOLITO";
                case "crater-rim": return "BORDE DEL CRÁTER (opcional, sin radio)";
                default: return null;
            }
        }

        private void OnExpedition(ExpeditionSessionSnapshot s)
        {
            if (root == null) return;
            innerHatch.color = s.Airlock.InnerHatchOpen ? new Color(.2f, .9f, .3f, .9f) : new Color(.9f, .2f, .2f, .95f);
            outerHatch.color = s.Airlock.OuterHatchOpen ? new Color(.2f, .9f, .3f, .9f) : new Color(.9f, .2f, .2f, .95f);
            bool pressurized = s.Airlock.State == AirlockState.PressurizedInnerOpen;
            chamber.color = pressurized ? new Color(.3f, .6f, 1f, .25f) : new Color(.05f, .05f, .1f, .55f);
            airlockText.text = "ESCLUSA · " + (s.Airlock.IsCycling ? $"ciclando {s.Airlock.CycleFraction:P0}" : pressurized ? "con aire" : "sin aire");
            shelterText.text = "REFUGIO · " + (!s.Shelter.Prepared ? "sin preparar" : s.Shelter.ServiceAvailable ? "con servicio" : "preparado");
            shelterTint.color = s.Instruction.InForce ? new Color(1f, .55f, .1f, .35f) : new Color(.95f, .65f, .2f, .18f);
            UpdateBadges(s);
        }

        private void UpdateBadges(ExpeditionSessionSnapshot s)
        {
            if (avatars == null) return;
            var present = new HashSet<PlayerId>();
            foreach (ExpeditionCrewState c in s.Crew)
            {
                present.Add(c.Player);
                if (!avatars.TryGetAvatar(c.Player, out GameObject avatar)) continue;
                if (!badges.TryGetValue(c.Player, out TextMesh badge) || badge == null)
                {
                    badge = Text("", Vector3.zero, 0.09f, Color.white);
                    badge.transform.SetParent(avatar.transform, false);
                    badge.transform.localPosition = new Vector3(0, -0.75f, 0);
                    badges[c.Player] = badge;
                }
                string text = c.SuitIndex < 0 ? "" : c.OnSuitSupply ? "TRAJE SELLADO" : "con traje";
                if (c.Margin.Status == MarginStatus.Sufficient || c.Margin.Status == MarginStatus.Insufficient)
                    text += $"\nmargen {c.Margin.MarginSeconds:0} s ({ProvisionalExpeditionHud.ServiceText(c.Margin.LimitingService)})";
                if (c.Protection == ProtectionState.Protected) text += "\nPROTEGIDO";
                badge.text = text;
                badge.color = c.Margin.Status == MarginStatus.Insufficient || c.Margin.Status == MarginStatus.ReturnUnavailable
                    ? new Color(1f, .4f, .3f) : Color.white;
            }
            var gone = new List<PlayerId>();
            foreach (PlayerId id in badges.Keys) if (!present.Contains(id)) gone.Add(id);
            foreach (PlayerId id in gone)
            {
                if (badges[id] != null) Destroy(badges[id].gameObject);
                badges.Remove(id);
            }
        }

        private SpriteRenderer Rect(string name, BaseRect area, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3((float)((area.MinX + area.MaxX) / 2), (float)((area.MinY + area.MaxY) / 2), 0);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = square;
            if (material != null) renderer.sharedMaterial = material;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = new Vector2((float)area.Width, (float)area.Height);
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private TextMesh Text(string text, Vector3 at, float size, Color color)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 48;
            mesh.characterSize = size * 0.25f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (font != null) renderer.sharedMaterial = font.material;
            renderer.sortingOrder = 2500;
            return mesh;
        }
    }
}
