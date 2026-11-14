# Contratos de sesión — fase 1

C# puro. Namespaces: `Trainer.Simulation`, `Trainer.Application` y, solo para desarrollo,
`Trainer.Application.Development`. Sin física implementada, transporte ni autorización real.
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
