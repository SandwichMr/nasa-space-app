using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Unity.Presentation.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.World.BaseOperations
{
    /// <summary>Visual only. Placement and service/progress come exclusively from the scenario and snapshot.</summary>
    public sealed class BaseEquipmentView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer body, pad, progress;
        [SerializeField] private TextMesh title, status;
        public StationId Station { get; private set; }
        public string StatusText { get; private set; }
        public double ConfirmedFraction { get; private set; }

        public void Configure(StationPlacement placement, string caption)
        {
            Station = placement.Id;
            transform.localPosition = new Vector3((float)placement.Position.X, (float)placement.Position.Y, 0);
            body.sortingOrder = 1000 - Mathf.RoundToInt((float)placement.Position.Y * 20);
            title.text = caption;
            Clear();
        }
        /// <summary>Optional presentation skin; placement, identity and state are untouched.</summary>
        public void SetBodySprite(Sprite sprite)
        {
            if (body == null || sprite == null) return;
            body.sprite = sprite;
            // Large opaque lunar artwork: sort by its back edge so the operator in reach stays visible in front of it.
            body.sortingOrder = 1000 - Mathf.RoundToInt((transform.localPosition.y + .5f) * 20);
        }
        public void Present(BaseOperationsSnapshot snapshot, EquipmentKind? kind, PlayerId local, bool nearby)
        {
            snapshot.TryGetOperationAt(Station, out var operation);
            string condition = kind.HasValue ? BaseOperationsText.Equipment(snapshot.EquipmentOf(kind.Value), operation) : "LOGÍSTICA";
            Color color = new Color(.40f, .86f, .94f);
            if (kind.HasValue)
            {
                var equipment = snapshot.EquipmentOf(kind.Value);
                if (equipment.Operating == EquipmentOperatingState.Isolated || equipment.Knowledge == EquipmentKnowledge.Degraded || equipment.Knowledge == EquipmentKnowledge.Failed)
                    color = new Color(1, .76f, .35f);
            }
            ConfirmedFraction = operation?.Fraction ?? 0;
            StatusText = condition;
            if (operation != null)
            {
                snapshot.Outpost.TryGetCrew(operation.Performer, out var performer);
                StatusText += "\n" + (performer == null ? "Compañero" : "P" + (performer.Slot + 1))
                    + (operation.Performer == local ? " · TÚ" : " · OCUPADA")
                    + $" · {operation.WorkSeconds:0.#}/{operation.DurationSeconds:0.#} s"
                    + (operation.Stalled ? "\nESPERA: SIN POTENCIA" : "");
            }
            else StatusText += "\n" + (nearby ? "CERCA" : "LIBRE");
            if (kind == EquipmentKind.WaterProcessor && operation == null)
                StatusText += $"\nProducto en espera: {snapshot.Water.HoldKilograms:0.##} kg";
            status.text = StatusText; status.color = color;
            body.color = Color.white; pad.color = new Color(color.r, color.g, color.b, nearby ? .65f : .23f);
            progress.enabled = operation != null;
            progress.transform.localScale = new Vector3((float)ConfirmedFraction * 1.2f, .055f, 1);
            progress.transform.localPosition = new Vector3(-.6f + (float)ConfirmedFraction * .6f, -.68f, 0);
            progress.color = operation != null && operation.Stalled ? new Color(1, .76f, .35f) : color;
        }
        public void Clear()
        {
            StatusText = "? SIN DATOS VIGENTES"; ConfirmedFraction = 0;
            if (status != null) status.text = StatusText;
            if (body != null) body.color = new Color(.5f, .6f, .65f);
            if (pad != null) pad.color = new Color(.45f, .55f, .6f, .2f);
            if (progress != null) progress.enabled = false;
        }
    }
}
