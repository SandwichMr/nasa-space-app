using System.Globalization;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;

namespace Trainer.Unity.Presentation.Incidents
{
    /// <summary>
    /// Spanish texts of the phase 5 views (D4), written for players aged 7–12. They describe only what the authority published: no
    /// prediction, no dose, no clinical threshold, and an expired message is never presented as an all-clear. Unknown keys fall back
    /// to a neutral text, never to "safe".
    /// </summary>
    public static class LivingBaseText
    {
        public static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        public static string DataBadge(DataStatus d)
        {
            if (d == null) return "DATOS: sin información";
            switch (d.Availability)
            {
                case DataAvailability.Loaded:
                    return "DATOS: HISTÓRICOS · NASA CCMC DONKI (copia congelada, sin conexión)";
                case DataAvailability.FallbackSynthetic:
                    return "DATOS: SINTÉTICOS DE RESPALDO · el episodio histórico no se pudo cargar (" + Problem(d.ProblemKey) + ")";
                default:
                    return "DATOS: SINTÉTICOS DE ENTRENAMIENTO · no son observaciones reales";
            }
        }

        public static string Problem(string key)
        {
            switch (key)
            {
                case "dataset-missing": return "faltan archivos";
                case "dataset-hash-mismatch": return "la huella SHA-256 no coincide";
                case "dataset-manifest-invalid": return "manifiesto inválido";
                case "dataset-schema-unsupported": return "versión no compatible";
                case "dataset-invalid": return "contenido inválido";
                default: return key ?? "motivo desconocido";
            }
        }

        public static string Kind(IncidentType kind)
        {
            switch (kind)
            {
                case IncidentType.ProcessorDegradation: return "Reciclador de agua: filtro obstruido";
                case IncidentType.PowerConverterTrip: return "Energía: convertidor solar desconectado";
                case IncidentType.PowerControllerFault: return "Energía: controlador de batería dañado";
                case IncidentType.SpaceWeather: return "Clima espacial";
                default: return "Contratiempo";
            }
        }

        public static string Phase(IncidentPhase phase)
        {
            switch (phase)
            {
                case IncidentPhase.Warning: return "AVISO";
                case IncidentPhase.Active: return "ACTIVO";
                case IncidentPhase.Intervention: return "EN REPARACIÓN";
                case IncidentPhase.Recovery: return "RECUPERÁNDOSE";
                case IncidentPhase.Resolved: return "RESUELTO";
                default: return "CANCELADO";
            }
        }

        public static string Hint(IncidentType kind)
        {
            switch (kind)
            {
                case IncidentType.ProcessorDegradation:
                    return "Ve al reciclador (E): inspecciona, aísla, cambia el filtro, prueba y restituye. Reparar gasta un repuesto.";
                case IncidentType.PowerConverterTrip:
                    return "Ve al panel eléctrico (E): inspecciona y luego reinicia el convertidor. Mientras, hay menos generación.";
                case IncidentType.PowerControllerFault:
                    return "Panel eléctrico (E): inspecciona, aísla, cambia el relé, restituye. Un reinicio NO repara el daño.";
                default:
                    return "Lee los avisos. Si hay instrucción de refugio, quien esté fuera debe volver y todos ir al refugio preparado.";
            }
        }

        public static string Message(string textKey)
        {
            switch (textKey)
            {
                case "sw-hist-watch-flare": return "VIGILANCIA: se detectó una fulguración solar intensa. Prepárate por si hay que volver.";
                case "sw-hist-instruction":
                    return "INSTRUCCIÓN: aumentan las partículas energéticas solares. Quien esté fuera, vuelva; todos al refugio preparado.";
                case "sw-hist-update-10mev": return "ACTUALIZACIÓN: las partículas siguen aumentando. Mantén el refugio.";
                case "sw-hist-update-summary": return "ACTUALIZACIÓN: resumen de las partículas observadas. Mantén el refugio.";
                case "sw-hist-update-ongoing": return "ACTUALIZACIÓN: el evento de partículas continúa. Mantén el refugio.";
                case "sw-synthetic-watch": return "VIGILANCIA (entrenamiento): podría llegar un evento de partículas. Planifica el regreso.";
                case "sw-synthetic-instruction": return "INSTRUCCIÓN (entrenamiento): vuelve y ve al refugio preparado antes del plazo.";
                case "sw-synthetic-update": return "ACTUALIZACIÓN (entrenamiento): el evento continúa. Mantén el refugio.";
                case "sw-shelter-period-ended":
                    return "FIN DEL PERIODO DE REFUGIO del escenario (decisión didáctica, no un aviso de la NASA de que todo terminó).";
                default: return "Mensaje del escenario.";
            }
        }

        public static string Basis(MessageBasis basis)
        {
            switch (basis)
            {
                case MessageBasis.SourceNotification: return "basado en una notificación de DONKI";
                case MessageBasis.DidacticDecision: return "decisión didáctica del escenario";
                default: return "entrenamiento sintético";
            }
        }

        public static string Validity(AdvisoryValidity validity)
        {
            switch (validity)
            {
                case AdvisoryValidity.Current: return "vigente";
                case AdvisoryValidity.Superseded: return "reemplazado";
                default: return "caducado: SIN información vigente (no significa que haya pasado)";
            }
        }

        public static string Instruction(InstructionState state)
        {
            switch (state)
            {
                case InstructionState.None: return "Sin instrucción de refugio.";
                case InstructionState.InForce: return "INSTRUCCIÓN DE REFUGIO EN VIGOR.";
                case InstructionState.InForceWithoutCurrentInformation:
                    return "INSTRUCCIÓN EN VIGOR SIN INFORMACIÓN VIGENTE: el último mensaje caducó; sigue en el refugio.";
                default: return "Periodo de refugio terminado (según el escenario).";
            }
        }

        public static string Shelter(MemberShelterState state)
        {
            switch (state)
            {
                case MemberShelterState.NotRequired: return "no se requiere refugio";
                case MemberShelterState.Protected: return "en el refugio preparado";
                case MemberShelterState.NotInShelter: return "fuera del refugio";
                case MemberShelterState.OnSuitSupply: return "con el traje (fuera o en la esclusa)";
                case MemberShelterState.ShelterNotPrepared: return "en el refugio, pero NO está preparado";
                default: return "en el refugio, pero sin ventilación (falta energía)";
            }
        }

        public static string Protection(ProtectionCategory category)
        {
            switch (category)
            {
                case ProtectionCategory.InTime: return "a tiempo";
                case ProtectionCategory.Late: return "tarde";
                case ProtectionCategory.Interrupted: return "salió antes del fin";
                case ProtectionCategory.NotProtected: return "no llegó al refugio";
                default: return "no recibió la instrucción (sin radio)";
            }
        }

        public static string RecordKind(SourceRecordKind kind)
        {
            switch (kind)
            {
                case SourceRecordKind.Flare: return "Fulguración (FLR)";
                case SourceRecordKind.SolarEnergeticParticles: return "Partículas energéticas solares (SEP)";
                case SourceRecordKind.Notification: return "Notificación";
                default: return kind.ToString();
            }
        }

        public static string Knowledge(EquipmentKnowledge knowledge)
        {
            switch (knowledge)
            {
                case EquipmentKnowledge.Nominal: return "normal (según la última inspección)";
                case EquipmentKnowledge.Degraded: return "con fallo (según la última inspección)";
                case EquipmentKnowledge.Failed: return "averiado (según la última inspección)";
                default: return "desconocido: inspecciona para saberlo";
            }
        }

        public static string Findings(PowerFindings f)
        {
            if (f == PowerFindings.None) return "sin hallazgos";
            string s = "";
            if ((f & PowerFindings.GenerationLow) != 0) s += "generación baja · ";
            if ((f & PowerFindings.ConverterTripped) != 0) s += "convertidor desconectado · ";
            if ((f & PowerFindings.DischargeLimited) != 0) s += "descarga limitada · ";
            if ((f & PowerFindings.ControllerDamaged) != 0) s += "controlador dañado · ";
            return s.TrimEnd(' ', '·');
        }

        public static string Action(IncidentAction action)
        {
            switch (action)
            {
                case IncidentAction.InspectPower: return "Inspeccionar";
                case IncidentAction.ResetConverter: return "Reiniciar convertidor";
                case IncidentAction.IsolatePower: return "Aislar";
                case IncidentAction.ReplacePowerPart: return "Cambiar relé";
                case IncidentAction.RestorePower: return "Restituir";
                case IncidentAction.InstallUpgrade: return "Instalar mejora";
                case IncidentAction.AcceptAssignment: return "Aceptar";
                case IncidentAction.PostponeAssignment: return "Posponer";
                case IncidentAction.CancelAssignment: return "Cancelar";
                default: return "Cancelar trabajo";
            }
        }

        public static string Assignment(AssignmentState state)
        {
            switch (state)
            {
                case AssignmentState.Offered: return "ofrecida";
                case AssignmentState.Accepted: return "aceptada";
                case AssignmentState.Completed: return "completada";
                case AssignmentState.Cancelled: return "cancelada";
                case AssignmentState.Expired: return "caducada";
                default: return "pospuesta";
            }
        }

        public static string Activity(string key)
        {
            switch (key)
            {
                case "regolith-sample": return "Muestra de regolito (exterior)";
                case "crater-core": return "Testigo del borde del cráter (exterior)";
                default: return key;
            }
        }

        public static string Feedback(IncidentFeedback f)
        {
            string head = f.Assessment == DecisionAssessment.Appropriate ? "Bien: " : f.Assessment == DecisionAssessment.NotIndicated ? "No hacía falta: " : "Cuidado: ";
            switch (f.ExplanationKey)
            {
                case "converter-reset": return head + "el convertidor volvió a conectarse.";
                case "reset-does-not-repair-damage": return head + "reiniciar no repara un controlador dañado; hay que aislar y cambiar el relé.";
                case "controller-isolated": return head + "controlador aislado; ahora cambia el relé.";
                case "part-replaced": return head + "relé cambiado; falta restituir.";
                case "power-restored": return head + "energía restituida.";
                case "upgrade-installed": return head + "mejora instalada: recupera más agua y gasta algo más de energía.";
                case "not-applicable": return head + "esa acción no correspondía ahora.";
                default: return head + f.ExplanationKey;
            }
        }

        public static string Reason(string reason)
        {
            switch (reason)
            {
                case null: return "";
                case "WrongStationForAction": return "Esa acción no se hace en esta estación.";
                case "StaleZone": return "Tu posición cambió; vuelve a intentarlo.";
                case "SealedNotAllowed": return "Con el traje puesto no puedes hacer esto dentro.";
                case "ObservationRequired": return "Primero inspecciona el panel eléctrico.";
                case "StaleObservation": return "La inspección caducó o el estado cambió: vuelve a inspeccionar.";
                case "NothingToRepair": return "La inspección no muestra nada que reparar.";
                case "PowerNotIsolated": return "Primero aísla el controlador.";
                case "PowerAlreadyIsolated": return "Ya está aislado.";
                case "PartNotReplaced": return "Primero cambia el relé.";
                case "SpareUnavailable": return "No queda repuesto libre.";
                case "UnknownAssignment": return "Esa tarea ya no existe.";
                case "AssignmentNotOffered": return "Esa tarea ya no está ofrecida.";
                case "AssignmentNotAccepted": return "Esa tarea no está aceptada.";
                case "AssignmentLimitReached": return "Ya tenéis una tarea aceptada; complétala primero.";
                case "UpgradeInstalled": return "La mejora ya está instalada.";
                case "InsufficientCredits": return "Faltan créditos de ciencia. Completa tareas para ganarlos.";
                case "CurrentDataNotAvailable": return "El modo de observación actual no está disponible.";
                default: return Trainer.Unity.Presentation.BaseOperations.BaseOperationsText.Reason(reason);
            }
        }
    }
}
