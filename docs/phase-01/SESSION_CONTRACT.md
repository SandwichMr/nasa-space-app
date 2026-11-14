# Contratos de sesión — fase 1

C# puro. Namespaces: `Trainer.Simulation`, `Trainer.Application` y, solo para desarrollo,
`Trainer.Application.Development`. Los contratos base no implementan física, transporte ni autorización real.
Los snapshots son clases selladas con propiedades de solo lectura y sin colecciones mutables.

## Simulación

- `PowerConfiguration(initialEnergyJoules, capacityJoules, generationWatts,
  essentialConsumptionWatts, scienceConsumptionWatts, maxChargeWatts, maxDischargeWatts)`:
  magnitudes `double`, finitas y no negativas; energía inicial ≤ capacidad.
- `IPowerSimulation.Reset(PowerConfiguration)` inicializa o restablece tiempo a cero y energía inicial.
- `PowerSnapshot IPowerSimulation.Snapshot { get; }` consulta el estado después de Reset.
- `void Advance(double elapsedSeconds, bool scienceRequested)` requiere Reset y segundos finitos
  no negativos; cero no cambia el estado. La ciencia solicitada no garantiza servicio.
- `PowerSnapshot`: tiempo simulado en segundos, energía y capacidad en joules, generación y
  potencia solicitada/servida de carga esencial y científica en watts instantáneos.
  El futuro implementador debe mantener energía dentro de capacidad y servicio dentro de demanda.

### Servicio integrado (extensión compatible)

`PowerSystem` implementa también `IIntegratedPowerSimulation`, sin cambiar `IPowerSimulation`
ni los snapshots base. `LastFullyServedScienceSeconds` indica segundos de servicio científico
completo del último Advance no nulo (cero tras Reset). `SecondsUntilServiceChange(bool)` consulta,
sin mutar estado, los segundos hasta el siguiente cambio de despacho para esa intención; infinito
significa régimen constante y cero indica servicio esencial ya insuficiente.

`MissionSession` recorta el paso al evento, acumula el servicio integrado y evalúa allí el fin.
En un empate conserva la prioridad existente: pérdida esencial, objetivo, duración. Una
implementación que solo exponga IPowerSimulation conserva la evaluación aproximada al final del
subpaso por compatibilidad; para contabilidad exacta debe implementar la extensión.

## Sesión

- `SessionCommand(Guid id, long epoch, SessionAction action, bool? requestedScienceEnabled = null)`.
  ID no vacío y único durante la vida de la sesión; época no negativa suministrada por autoridad.
  Solo `SetScienceEnabled` requiere valor bool; Pause, Resume y Restart no llevan valor.
  No existe Toggle ni campo para declararse host. Identidad y permisos se validarán externamente.
- `MissionSnapshot`: `long Epoch`, `long Revision`, `SessionState State`, `PowerSnapshot Power`,
  `double ScienceWorkSeconds`, `bool ScienceEnabled`. Trabajo científico en segundos acumulados;
  política de acumulación pendiente de implementación. La revisión ordena cambios dentro de una época.
- `CommandResult`: `Guid CommandId`, `long Epoch` original del comando, `bool Accepted`,
  `string Reason`. La nueva época de Restart se obtiene del snapshot, no del resultado.

```csharp
void Submit(SessionCommand command);
bool TryGetSnapshot(out MissionSnapshot snapshot);
event Action<MissionSnapshot> SnapshotChanged;
event Action<CommandResult> CommandCompleted;
```

Submit solo entrega intención: no indica aceptación. Suscribirse antes de enviar, pues el resultado
puede llegar inmediatamente o después. TryGetSnapshot puede devolver false antes del primer estado
con un transporte futuro. Los consumidores correlacionan resultados por ID y época, y snapshots por
época/revisión; no deben depender del orden entre ambos eventos.

## Fake exclusivo de desarrollo

`FakeSessionPort` entrega siempre estado inicial Ready (época/revisión 0), valores eléctricos fijos
ilustrativos y cero trabajo. No avanza tiempo ni calcula energía. `PublishExample(state, seconds)`
permite mostrar Running, Paused, Completed o Interrupted y trabajo arbitrario de ejemplo.
Cambiar ciencia solo selecciona valores de ejemplo de demanda/servicio.

Resume acepta Ready o Paused; Pause exige Running. SetScienceEnabled acepta Ready/Running/Paused.
Completed e Interrupted solo aceptan Restart. Restart incrementa época, reinicia revisión a cero,
ciencia y trabajo. Otros comandos aceptados incrementan revisión incluso si expresan el mismo valor.

Cada ID conserva comando y resultado durante la vida de esta instancia, incluso tras Restart.
Repetir exactamente un comando reproduce su resultado sin eventos de snapshot ni reaplicar cambios;
esto incluye el resultado histórico de un Restart. Otro contenido con el mismo ID se rechaza con
CommandIdConflict. Un ID nuevo de época antigua o futura se rechaza con EpochMismatch.
También se conservan rechazos. Validaciones adicionales: InvalidCommandId, UnknownAction,
InvalidActionValue, InvalidState, SessionEnded. Éxito: Accepted.

Fake de un solo hilo, callbacks síncronos y sin validación de permisos: no prueba networking.
Un comando null provoca ArgumentNullException; errores del suscriptor se propagan.
No usarlo como autoridad de producción. No implementa IPowerSimulation.

```csharp
ISessionPort port = new Trainer.Application.Development.FakeSessionPort();
port.CommandCompleted += result => { /* correlacionar ID y época; mostrar aceptación/rechazo */ };
port.SnapshotChanged += snapshot => { /* presentar snapshot de solo lectura */ };
if (port.TryGetSnapshot(out MissionSnapshot current))
    port.Submit(new SessionCommand(Guid.NewGuid(), current.Epoch,
        SessionAction.SetScienceEnabled, true));
```

Los cinco desarrolladores pueden consumir estos contratos para simulación, autoridad de sesión,
presentación, integración y pruebas, sin implementar esas capas en esta fase.
Las pruebas NUnit están en Tests/EditMode/Application/Editor para usar el ensamblado Editor
predefinido existente sin crear asmdef. Unity genera los .meta.

## Recuperación de transporte y reloj

`ISessionHostTransport.DisconnectClient(connectionId, reason)` es obligatorio para el binding:
debe cerrar idempotentemente la conexión y notificar pérdida de enlace al cliente (además de
ClientDisconnected al host). Ante SendToClient fallido, el bridge elimina esa conexión del broadcast,
solicita su cierre con SendFailed y publica el diagnóstico. El cliente abandona pendientes como
resultado desconocido; el snapshot de la próxima conexión es la referencia. No reintentar con ID nuevo
para asumir que un comando no se aplicó. No hay SDK seleccionado ni escena conectada.

El HUD libera el pendiente ante excepción de Submit sin inventar CommandResult ni revertir snapshots.
La composición aún debe notificar desconexión/reconexión al HUD. Cada MissionSession admite un solo
SimulationRunner vinculado; Unbind/Destroy liberan esa propiedad para volver a componer.
