using Trainer.Application.Outpost;
using UnityEngine;

namespace Trainer.Unity.World.Outpost
{
    /// <summary>One physical station. All labels/progress come from the current confirmed snapshot.</summary>
    public sealed class OutpostStationView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private SpriteRenderer selection;
        [SerializeField] private SpriteRenderer progress;
        [SerializeField] private TextMesh title;
        [SerializeField] private TextMesh status;
        public StationPlacement Placement { get; private set; }
        public string DisplayName { get; private set; }
        public string StatusText { get; private set; } = "Sin datos vigentes";
        public double ConfirmedFraction { get; private set; }
        public float CaptionSize => title != null ? title.characterSize : 0;

        /// <summary>Optional presentation skin. Does not change placement, station identity or task state.</summary>
        public void SetCaption(string label, float size)
        {
            DisplayName = label;
            if (title != null) { title.text = label; title.characterSize = size; }
        }

        public void Configure(StationPlacement placement, string label, Sprite sprite, Color color)
        {
            Placement = placement;
            DisplayName = label;
            transform.localPosition = new Vector3((float)placement.Position.X, (float)placement.Position.Y, 0);
            if (body != null)
            {
                body.sprite = sprite;
                body.sortingOrder = 1000 - Mathf.RoundToInt((float)placement.Position.Y * 20);
            }
            if (title != null) { title.text = label; title.color = color; }
            if (selection != null) selection.color = color;
            Clear();
        }

        public void ApplyLunarLabelStyle()
        {
            if(title!=null){title.characterSize=.055f;title.anchor=TextAnchor.MiddleCenter;title.transform.localPosition=new Vector3(0,1.02f,0);}
            if(status!=null){status.characterSize=.04f;status.anchor=TextAnchor.MiddleCenter;status.transform.localPosition=new Vector3(0,-1.03f,0);}
        }

        public void Present(StationState state, OutpostSessionSnapshot snapshot, PlayerId local, bool nearby)
        {
            ConfirmedFraction = state.ActiveTask == null ? 0 : state.ActiveTask.WorkSeconds / state.ActiveTask.DurationSeconds;
            if (state.ActiveTask != null)
            {
                snapshot.TryGetCrew(state.Occupant, out CrewMemberState crew);
                string who = crew == null ? state.Occupant.ToString() : "Astronauta " + (crew.Slot + 1);
                StatusText = who + (state.Occupant == local ? " · TÚ" : "") + "\n" +
                    state.ActiveTask.WorkSeconds.ToString("0.0") + " / " + state.ActiveTask.DurationSeconds.ToString("0.#") +
                    " s" + (state.ActiveTask.Stalled ? " · SIN SERVICIO" : "");
            }
            else StatusText = state.Availability == StationAvailability.Available ? "LIBRE" : "NO DISPONIBLE";
            if (status != null) status.text = StatusText;
            if (selection != null) selection.enabled = nearby;
            if (body != null) body.color = Color.white;
            if (progress != null)
            {
                progress.enabled = state.ActiveTask != null;
                progress.transform.localScale = new Vector3((float)ConfirmedFraction * 1.2f, .07f, 1);
                progress.transform.localPosition = new Vector3(-.6f + (float)ConfirmedFraction * .6f, -.52f, 0);
                progress.color = state.ActiveTask != null && state.ActiveTask.Stalled ? new Color(1,.7f,.25f) : new Color(.4f,.95f,.78f);
            }
        }

        public void Clear()
        {
            ConfirmedFraction = 0;
            StatusText = "Sin datos vigentes";
            if (status != null) status.text = StatusText;
            if (selection != null) selection.enabled = false;
            if (progress != null) progress.enabled = false;
            if (body != null) body.color = new Color(.45f,.5f,.55f);
        }
    }
}
