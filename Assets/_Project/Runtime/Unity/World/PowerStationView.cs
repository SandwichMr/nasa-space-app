using System.Globalization;
using Trainer.Application;
using UnityEngine;

namespace Trainer.Unity.World
{
    public enum PowerStationKind { Battery, Essential, Science }

    /// <summary>Visual observation only. Never owns a port, clock or mutable resource state.</summary>
    [DisallowMultipleComponent]
    public sealed class PowerStationView : MonoBehaviour
    {
        [SerializeField] private PowerStationKind kind;
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private SpriteRenderer selection;
        [SerializeField] private SpriteRenderer energyFill;
        [SerializeField] private Texture2D icon;

        public PowerStationKind Kind => kind;
        public Texture2D Icon => icon;
        public string Title => kind == PowerStationKind.Battery ? "BATERÍA" :
            kind == PowerStationKind.Essential ? "SERVICIO ESENCIAL" : "INSTRUMENTO CIENTÍFICO";
        public string StatusText { get; private set; } = "Sin datos vigentes";
        public bool HasCurrentData { get; private set; }
        public bool IsSelected => selection != null && selection.enabled;

        internal void Show(MissionSnapshot snapshot, bool selected)
        {
            HasCurrentData = snapshot != null;
            if (selection != null) selection.enabled = selected;
            if (body != null) body.color = snapshot == null ? new Color(.48f, .52f, .59f) : Color.white;
            if (energyFill != null) energyFill.enabled = snapshot != null;
            if (snapshot == null) { StatusText = "Sin datos vigentes"; return; }
            var p = snapshot.Power;
            if (kind == PowerStationKind.Battery)
            {
                StatusText = Format("{0:0.##} / {1:0.##} J", p.EnergyJoules, p.CapacityJoules);
                if (energyFill != null)
                {
                    float fraction = p.CapacityJoules > 0 ? Mathf.Clamp01((float)(p.EnergyJoules / p.CapacityJoules)) : 0;
                    energyFill.transform.localScale = new Vector3(fraction, 1, 1);
                    energyFill.transform.localPosition = new Vector3(-.64f + .64f * fraction, .20f, 0);
                }
            }
            else if (kind == PowerStationKind.Essential)
                StatusText = Format("Último paso medido\nSolicitado {0:0.##} W · Servido {1:0.##} W\nPrioridad esencial",
                    p.EssentialRequestedWatts, p.EssentialServedWatts);
            else
            {
                StatusText = (snapshot.ScienceEnabled ? "Activada" : "Desactivada") + " · Confirmado\n" +
                    Format("Último paso medido\nSolicitado {0:0.##} W · Servido {1:0.##} W", p.ScienceRequestedWatts, p.ScienceServedWatts);
                if (body != null && !snapshot.ScienceEnabled) body.color = new Color(.63f, .66f, .75f);
            }
        }

        private static string Format(string text, params object[] values) =>
            string.Format(CultureInfo.InvariantCulture, text, values);
    }
}
