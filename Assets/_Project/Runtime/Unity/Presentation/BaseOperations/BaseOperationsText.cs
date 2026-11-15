using System.Globalization;
using System.Text;
using Trainer.Application.BaseOperations;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Unity.Presentation.BaseOperations
{
    /// <summary>Formatting of published readings. Never estimates stock, trends, bands or equipment truth.</summary>
    public static class BaseOperationsText
    {
        public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        public static string Unit(ReadingUnit unit)
        {
            switch (unit)
            {
                case ReadingUnit.Joules: return "J";
                case ReadingUnit.Watts: return "W";
                case ReadingUnit.Kilopascals: return "kPa";
                case ReadingUnit.MicrosiemensPerCentimeter: return "µS/cm";
                case ReadingUnit.Fraction: return "fracción (0–1)";
                default: return "kg";
            }
        }

        public static string Name(GaugeKind kind)
        {
            switch (kind)
            {
                case GaugeKind.BatteryEnergy: return "BATERÍA";
                case GaugeKind.PowerBalance: return "BALANCE ELÉCTRICO";
                case GaugeKind.PotableWater: return "AGUA POTABLE";
                case GaugeKind.UntreatedWater: return "AGUA SIN TRATAR";
                case GaugeKind.ProcessorHold: return "PRODUCTO EN ESPERA";
                case GaugeKind.StoredFood: return "COMIDA";
                case GaugeKind.CabinOxygen: return "O2 · CABINA";
                default: return "CO2 · CABINA";
            }
        }

        public static string Reading(InstrumentReading reading)
        {
            if (reading == null || reading.Status == ReadingStatus.Unavailable) return "SIN LECTURA";
            return Number(reading.Value.Value) + " " + Unit(reading.Unit)
                + (reading.Status == ReadingStatus.Stale ? " · DATO ANTIGUO" : "");
        }

        public static string Value(ResourceGauge gauge)
        {
            if (gauge == null || gauge.Reading.Status == ReadingStatus.Unavailable) return "SIN LECTURA";
            return Number(gauge.Reading.Value.Value)
                + (gauge.Capacity.HasValue ? " / " + Number(gauge.Capacity.Value) : "")
                + " " + Unit(gauge.Reading.Unit);
        }

        public static string Band(ResourceGauge gauge)
        {
            if (gauge == null || gauge.Reading.Status == ReadingStatus.Unavailable) return "? SIN DATOS";
            if (gauge.Reading.Status == ReadingStatus.Stale) return "? DATO ANTIGUO · t=" + Number(gauge.Reading.ObservedAtSeconds.Value) + " s";
            switch (gauge.Band)
            {
                case GaugeBand.Reserve: return "! RESERVA BAJA";
                case GaugeBand.Exhausted: return "! AGOTADO";
                case GaugeBand.AboveLimit: return "! SOBRE LA LÍNEA";
                case GaugeBand.Deficit: return "! DÉFICIT";
                case GaugeBand.Nominal: return "EN RANGO DEL ESCENARIO";
                default: return "? SIN DATOS";
            }
        }

        public static string Trend(ResourceGauge gauge)
        {
            if (gauge == null || gauge.Reading.Status != ReadingStatus.Valid || !gauge.TrendPerSecond.HasValue)
                return "Tendencia sin confirmar";
            double trend = gauge.TrendPerSecond.Value;
            return (trend > 0 ? "+ SUBE " : trend < 0 ? "− BAJA " : "= ESTABLE ") + Rate(System.Math.Abs(trend), gauge.Reading.Unit);
        }

        /// <summary>A published rate in a unit of time where it does not round to 0 (per second, minute or hour). Energy keeps
        /// J/s. A real change is never shown as "0" next to "BAJA"/"SUBE".</summary>
        public static string Rate(double perSecond, ReadingUnit unit)
        {
            string u = Unit(unit);
            if (perSecond == 0 || perSecond >= 0.01 || unit == ReadingUnit.Joules || unit == ReadingUnit.Watts) return Number(perSecond) + " " + u + "/s";
            if (perSecond * 60 >= 0.01) return Number(perSecond * 60) + " " + u + "/min";
            double perHour = perSecond * 3600;
            return (perHour >= 0.01 ? Number(perHour) : "< 0.01") + " " + u + "/h";
        }

        public static string Action(GaugeKind kind)
        {
            switch (kind)
            {
                case GaugeKind.BatteryEnergy:
                case GaugeKind.PowerBalance: return "Prioriza cargas y generación";
                case GaugeKind.PotableWater: return "Trata agua o pide suministros";
                case GaugeKind.StoredFood: return "Pide y descarga suministros";
                case GaugeKind.CabinOxygen: return "Revisa la lectura de cabina";
                case GaugeKind.CabinCarbonDioxide: return "Inspecciona la unidad de aire";
                case GaugeKind.UntreatedWater: return "Procesa un lote en el reciclador";
                default: return "Mide el producto y elige su ruta";
            }
        }

        public static string Equipment(EquipmentStatus equipment, OperationProgress operation = null)
        {
            if (equipment == null) return "? SIN DATOS";
            if (operation != null && operation.Action == BaseOperationAction.TestEquipment) return "EN PRUEBA";
            if (operation != null && operation.Action == BaseOperationAction.Intervene) return "EN REPARACIÓN";
            if (equipment.Operating == EquipmentOperatingState.Isolated) return "AISLADA · FUERA DE SERVICIO";
            if (equipment.LatestObservation != null && !equipment.LatestObservation.Current) return "? OBSERVACIÓN ANTIGUA";
            switch (equipment.Knowledge)
            {
                case EquipmentKnowledge.Nominal: return "NORMAL · EN LÍNEA";
                case EquipmentKnowledge.Degraded: return "! DEGRADADA";
                case EquipmentKnowledge.Failed: return "! AVERIADA";
                default: return "? INSPECCIÓN PENDIENTE";
            }
        }

        public static string FindingsText(Findings findings)
        {
            if (findings == Findings.None) return "No se observaron anomalías en esa inspección.";
            var text = new StringBuilder();
            if ((findings & Findings.ProductOffSpec) != 0) text.Append("Producto fuera de la línea. ");
            if ((findings & Findings.ProcessorFilterRestricted) != 0) text.Append("Filtro del reciclador restringido. ");
            if ((findings & Findings.ProcessorFailed) != 0) text.Append("Reciclador detenido por avería. ");
            if ((findings & Findings.ActiveBedSaturated) != 0) text.Append("Lecho activo cargado: retira menos CO2. ");
            if ((findings & Findings.StandbyBedLoaded) != 0) text.Append("Lecho de reserva cargado. ");
            if ((findings & Findings.InletFlowRestricted) != 0) text.Append("Flujo de entrada restringido. ");
            return text.ToString().TrimEnd();
        }

        public static string ReadingName(string key)
        {
            switch (key)
            {
                case "product-conductivity": return "Conductividad del producto";
                case "active-bed-loading": return "Carga del lecho activo";
                case "standby-bed-loading": return "Carga del lecho de reserva";
                case "inlet-flow": return "Flujo de entrada";
                default: return "Lectura " + key;
            }
        }

        public static string Operation(BaseOperationAction action, OperationChoice choice = OperationChoice.None)
        {
            switch (action)
            {
                case BaseOperationAction.Inspect: return "Inspeccionar";
                case BaseOperationAction.ProcessBatch: return "Procesar un lote";
                case BaseOperationAction.RouteProduct: return choice == OperationChoice.AcceptToPotable ? "Enviar a potable" : "Volver a tratar";
                case BaseOperationAction.RecheckPotable: return "Recomprobar el tanque";
                case BaseOperationAction.ServiceAir: return choice == OperationChoice.SwitchBed ? "Cambiar al lecho de reserva" : "Regenerar el lecho de reserva";
                case BaseOperationAction.Isolate: return "Aislar el equipo";
                case BaseOperationAction.Intervene: return "Cambiar filtro";
                case BaseOperationAction.TestEquipment: return "Probar el equipo";
                case BaseOperationAction.Restore: return "Restituir servicio";
                case BaseOperationAction.PlaceOrder: return "Pedir suministros";
                case BaseOperationAction.UnloadDelivery: return "Descargar entrega";
                default: return "Cancelar";
            }
        }

        public static string Reason(string reason)
        {
            switch (reason)
            {
                case null: return "";
                case "Accepted": return "Solicitud aceptada. Comprueba el estado confirmado del equipo y los recursos.";
                case "StationOccupied": return "Otra persona está usando esta estación. Espera o repartid el trabajo.";
                case "PlayerBusy": return "Ya tienes un trabajo activo. Termínalo o cancélalo.";
                case "InsufficientResources": return "Faltan recursos para empezar. Revisa agua sin tratar y energía.";
                case "SpareUnavailable": return "No hay un repuesto libre; puede estar reservado. Revisa logística.";
                case "ObservationRequired": return "Primero inspecciona el equipo.";
                case "StaleObservation": return "La observación cambió o caducó. Vuelve a inspeccionar.";
                case "OutOfRange": return "Acércate a la estación.";
                case "EquipmentIsolated": return "Equipo aislado: completa intervención, prueba y restitución.";
                case "EquipmentNotIsolated": return "Primero aísla el equipo tras diagnosticarlo.";
                case "EquipmentFailed": return "Reciclador averiado: inspecciona y repara antes de procesar.";
                case "RepairStepOutOfOrder": return "Sigue el orden: inspeccionar, aislar, cambiar filtro, probar y restituir.";
                case "NothingToRepair": return "La observación no indica una avería que reparar.";
                case "HoldNotEmpty": return "Hay producto esperando una decisión. Elige su ruta antes de otro lote.";
                case "HoldEmpty": return "No hay producto retenido. Procesa un lote primero.";
                case "PotableNotSuspect": return "El tanque no tiene masa pendiente de recomprobación.";
                case "OrderTooSoon": return "Espera el intervalo del escenario antes de otro pedido.";
                case "OrderLimitReached": return "Ya está ocupado el cupo de pedidos. Revisa el pedido del equipo.";
                case "OrderNotArrived": return "La entrega aún no ha llegado.";
                case "OrderNotCancellable": return "El pedido ya salió: terminó su ventana de cancelación.";
                case "NoStorageCapacity": return "Los almacenes están llenos. La entrega queda pendiente.";
                case "NotConnected":
                case "ConnectionLost":
                case "LocalDisconnect": return "Sin conexión. El resultado de la solicitud no está confirmado.";
                case "EpochMismatch": return "La sesión se reinició. Revisa el nuevo estado.";
                case "InvalidState": return "La sesión debe estar en marcha para empezar ese trabajo.";
                case "SessionEnded": return "La sesión se detuvo. Revisa la causa y reinicia explícitamente.";
                case "SealedNotAllowed": return "Este trabajo requiere dejar de usar el suministro del traje dentro de la base.";
                default: return "Solicitud sin confirmar (" + reason + "). Revisa el estado del equipo.";
            }
        }

        public static string Explanation(string key)
        {
            switch (key)
            {
                // D2 explanation keys (docs/phase-04/handoffs/02_LIVING_BASE_HANDOFF.md, «Claves»).
                case "accepted-off-spec-product": return "Producto fuera de línea enviado al tanque. Recomprobar lo devuelve a sin tratar.";
                case "accepted-within-limit": return "Producto dentro de la línea: el agua potable subió con ese lote.";
                case "reprocessed-off-spec-product": return "Producto fuera de línea devuelto a sin tratar: el tanque potable no se contaminó.";
                case "reprocessed-within-limit": return "El producto estaba dentro de la línea; volver a tratarlo gastó tiempo y energía.";
                case "bed-switch-not-needed": return "El lecho activo no estaba cargado: el cambio no mejoró la retirada de CO2.";
                case "switched-to-loaded-bed": return "El lecho de reserva estaba cargado: el CO2 se retira peor. Regenera la reserva.";
                case "bed-switch-restored-removal": return "Cambio de lecho confirmado. Revisa la nueva tendencia del CO2.";
                case "standby-bed-regenerated": return "Lecho de reserva limpio: listo para el próximo cambio.";
                case "standby-bed-already-clean": return "La reserva ya estaba limpia: se gastaron tiempo y energía.";
                case "isolated-for-repair": return "Equipo aislado para repararlo: su servicio está detenido hasta restituirlo.";
                case "processor-filter-replaced": return "Filtro del reciclador cambiado. Prueba el equipo antes de restituir.";
                case "processor-filter-not-indicated": return "Ese filtro no era la causa. Se gastaron tiempo y repuesto.";
                case "inlet-filter-replaced": return "Filtro de entrada cambiado. Prueba el flujo antes de restituir.";
                case "inlet-filter-not-indicated": return "Ese filtro no era la causa. Se gastaron tiempo y repuesto; revisa los lechos.";
                case "restored-after-passed-test": return "Servicio restituido tras una prueba aprobada. Lo ya perdido no se devuelve.";
                case "product-reprocessed": return "Producto devuelto a sin tratar; comprueba el tanque antes del siguiente lote.";
                case "processor-restored": return "Servicio restituido. El agua y la energía gastadas no se devuelven.";
                default: return "Resultado confirmado: " + key + ". Comprueba las lecturas y recursos actuales.";
            }
        }
    }
}
