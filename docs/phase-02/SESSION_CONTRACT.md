# Contratos de sesión — fase 2 (base común congelada)

Base común de la fase 2 (base interior 2D, vista cenital, astronautas que se mueven, estaciones y tareas cooperativas). **No es la jugabilidad**: define tipos, reglas y puertos para que D1–D5 construyan sus partes sin interfaces incompatibles. No existe todavía autoridad, movimiento, modelo de recursos ni transporte de fase 2.

C# puro, sin `UnityEngine` ni SDK de red. Compila contra `netstandard` sin referencias de Unity (ver `COMMON_BASE.md`). Todos los tipos publicados son inmutables: sin setters públicos y con colecciones copiadas y expuestas como solo lectura. Una prueba por reflexión lo comprueba.

| Archivo (bajo `Assets/_Project/`) | Namespace | Contenido |
| --- | --- | --- |
| `Runtime/Simulation/Contracts/LifeSupport/LifeSupportStocks.cs` | `Trainer.Simulation.LifeSupport` | `ResourceKind`, `WaterStock`, `FoodStock` |
| `…/LifeSupport/ResourceTransfer.cs` | ídem | `ResourceChange`, `ResourceEffect`, `ResourceTransfer`, `ResourceTransferStatus` |
| `…/LifeSupport/CabinAir.cs` | ídem | `CabinConfiguration`, `ReadingStatus`, `GasReading`, `CabinAirObservation` |
| `…/LifeSupport/OutpostSimulation.cs` | ídem | `LifeSupportConfiguration`, `OutpostConfiguration`, `OutpostDemand`, `LifeSupportSnapshot`, `OutpostSimulationSnapshot`, `IOutpostSimulation` |
| `Runtime/Application/Contracts/Outpost/Identifiers.cs` | `Trainer.Application.Outpost` | `OutpostLimits`, `StableKey`, `PlayerId`, `StationId`, `TaskTypeId` |
| `…/Outpost/Geometry.cs` | ídem | `BasePosition`, `BaseRect`, `MoveDirection` |
| `…/Outpost/Layout.cs` | ídem | `StationPlacement`, `CrewMovementSettings`, `OutpostLayout` |
| `…/Outpost/Tasks.cs` | ídem | `TaskCategory`, `TaskDefinition`, `TaskProgress`, `TaskEndReason`, `TaskEnd`, `StationAvailability`, `StationState` |
| `…/Outpost/Scenario.cs` | ídem | `OutpostScenario` |
| `…/Outpost/Crew.cs` | ídem | `CrewMotion`, `CrewPose`, `CrewMemberState`, `MembershipStatus`, `OutpostMembership` |
| `…/Outpost/Movement.cs` | ídem | `MovementInput`, `MovementInputDisposition`, `MovementInputPolicy` |
| `…/Outpost/Commands.cs` | ídem | `OutpostAction`, `OutpostCommand`, `OutpostReasons` |
| `…/Outpost/Snapshot.cs` | ídem | `OutpostOutcome`, `ProgressUnit`, `ObjectiveProgress`, `OutpostSessionSnapshot` |
| `…/Outpost/Ports.cs` | ídem | `IOutpostSessionPort`, `IOutpostAuthority`, `ICrewRoster` |
| `Runtime/Application/Contracts/Development/FakeOutpostSessionPort.cs` | `Trainer.Application.Development` | Fake de desarrollo del puerto |
| `…/Development/OutpostFixtures.cs` | ídem | Escenario sintético y constructores de snapshots |

Se reutilizan sin cambios, y con su mismo significado, tres tipos de fase 1: `PowerConfiguration`, `PowerSnapshot` (canales eléctrico esencial y científico) y `CommandResult` (`CommandId`, `Epoch`, `Accepted`, `Reason`). También `SessionState` (`Ready`, `Running`, `Paused`, `Completed`, `Interrupted`).

## Unidades y coordenadas

| Magnitud | Unidad | Tipo |
| --- | --- | --- |
| Posición, distancias, radios | metros | `double` (finito) |
| Tiempo de misión, duraciones, trabajo | segundos simulados (nunca FPS ni reloj de pared) | `double` |
| Velocidad | m/s simulados | `double` |
| Potencia | W (instantánea, último paso) | `double` |
| Energía | J | `double` (fase 1) |
| Agua, comida, efectos de masa | kg | `double` |
| Presiones parciales | kPa | `double?` (lectura) |
| Temperatura del gas | K | `double` |
| Volumen de gas libre | m³ | `double` |

El plano es cenital: `+X` hacia la derecha de la pantalla, `+Y` hacia arriba y el origen en el origen del plano (`OutpostLayout`). Mapeo recomendado a Unity, que confirma D2 en la escena: X del mundo = X, Y del mundo = Y, 1 unidad = 1 m y Z solo para el orden de dibujo. `BaseRect` incluye sus bordes y exige `Min < Max`.

`OutpostLimits` acota todo lo publicado: tripulación ≤ 8, estaciones ≤ 16, tipos de tarea ≤ 32, obstáculos ≤ 64, objetivos ≤ 8, claves ≤ 32 caracteres. Las claves estables (`StableKey`) usan `a-z`, `0-9` y `-` y empiezan por letra.

## Identidad y presencia

- `PlayerId` (`int` ≥ 1, `PlayerId.None` = 0) lo asigna **solo la autoridad** en `Join()`. Los comandos y las entradas no llevan identidad: el adaptador de host la toma de la conexión real. La autoridad nunca reutiliza un id; un cliente que vuelve a entrar recibe uno nuevo.
- `Slot` (0..`MaxCrew`-1) es el hueco libre más bajo. Elige el punto de aparición (`SpawnPoints[slot]`) y el color en D4. No es identidad: se reutiliza cuando su dueño sale. `MaxCrew` = número de puntos de aparición. Sin hueco libre, `Join()` devuelve `Rejected(SessionFull)`.
- `OutpostMembership` es la identidad del propio puerto: `NotJoined`, `Joined(PlayerId)`, `Rejected(reason)` o `Left(antiguoId, reason)`. **Personaje local** = `CrewMemberState.Player == port.Membership.Player`; el resto son remotos. `Joined` solo se anuncia cuando ya hay un snapshot que contiene a ese jugador.
- Al desconectar (`Leave`) no hay periodo de gracia ni astronauta fantasma. La tarea se cancela (`PerformerLeft`) sin efecto de finalización, la estación queda libre, el miembro desaparece de `Crew`, se olvida su estado de movimiento y `CrewAboard` baja.
- `Restart` conserva ids, huecos y secuencias. Cada miembro vuelve a su punto de aparición en reposo, mirando hacia abajo y sin dirección sostenida.
- Los astronautas no chocan entre sí: solo el plano bloquea.

## Movimiento

`MovementInput(long epoch, long sequence, MoveDirection direction)` expresa una **dirección sostenida**, no una posición. La autoridad nunca acepta posiciones del cliente.

- `MoveDirection`: componentes finitas en [-1, 1] y magnitud ≤ 1 (+1e-9). El constructor y `TryCreate` rechazan NaN, infinito y magnitudes mayores que 1, sin recortar nada. `FromAxes(x, y)` es para entrada cruda: rechaza NaN o infinito y escala a magnitud 1 lo que la supere (`(1,1)` → `(0.7071, 0.7071)`); un valor analógico menor que 1 se conserva. Velocidad = `Magnitude × MaxSpeedMetersPerSecond`, así que en diagonal no se va más rápido.
- **Orden** (`MovementInputPolicy.Evaluate(lastApplied, currentEpoch, input)`): se aplica solo si `input.Epoch == epoch actual` y `input.Sequence > última aplicada`. Lo viejo, duplicado o reordenado da `StaleSequence`; otra época da `EpochMismatch`; un remitente que no está presente da `NotJoined`. Puede haber huecos de secuencia. La secuencia sube durante toda la vida del `PlayerId` y no se reinicia con la época. Las entradas no tienen id, resultado ni caché: la autoridad guarda por miembro presente solo la última secuencia y la dirección sostenida. Tras un `Restart`, el cliente reenvía su dirección actual con la nueva época.
- **Confirmación**: el snapshot trae `CrewPose` (`Position`, `Facing` unitario de la última marcha confirmada, `Motion` `Idle`/`Walking`) y `LastInputSequence`. D4 representa la orientación y el reposo o marcha con esos datos. Esta base no incluye predicción, rollback ni migración de host.
- **Colisión**: `OutpostLayout.IsAdmissible(center, radius)` es la regla única. Admite un disco que esté dentro de `Bounds` sin solapar obstáculos (tocar sí está permitido); se evalúa como disco contra rectángulo, con esquinas redondeadas. Toda posición confirmada la cumple. Cómo se resuelve un bloqueo (parar o deslizar) lo decide D3, que además no puede mover más de velocidad × tiempo por paso. Las posiciones avanzan solo en `Running`; en `Ready` y `Paused` la entrada se registra igualmente como dirección sostenida.

## Estaciones y tareas

**Configuración** (fija en la build): `OutpostScenario` reúne `Id`, `Provenance`, `OutpostLayout` (con `Bounds`, `Obstacles`, `Stations: StationPlacement`, `SpawnPoints`), `CrewMovementSettings`, `Tasks: TaskDefinition` y `OutpostConfiguration`. El constructor rechaza ids duplicados, tareas en estaciones inexistentes y puntos de aparición no admisibles para el radio de la tripulación. Las reglas de misión (objetivo, duración) pertenecen a D2 y no forman parte de este tipo.

`TaskDefinition(id, station, category, durationSeconds, powerWatts, completionEffect)`: una persona en una estación.

- `Support`: `PowerWatts` se suma a `OutpostDemand.ServiceLoadWatts` mientras la tarea avanza.
- `Science`: pide el canal científico de fase 1 (`ScienceRequested`), así que su `PowerWatts` debe ser 0.

**Estado** (lo publica la autoridad): `StationState` (`Availability`, `ActiveTask: TaskProgress`, `LastEnd: TaskEnd`, `Occupant`). Está `Occupied` si y solo si hay tarea activa. `TaskProgress.WorkSeconds` ∈ [0, `DurationSeconds`]; `Stalled` indica que el último paso no la alimentó por completo.

**Exclusión.** Un snapshot que la viole no se puede construir:

- una estación tiene como máximo una tarea activa;
- un miembro tiene como máximo una tarea activa en todo el plano;
- quien ejecuta una tarea debe estar presente;
- no hay tareas activas en `Completed` ni en `Interrupted`.

**Proximidad.** `StationPlacement.IsWithinReach(posiciónConfirmada)`: distancia ≤ `InteractionRadiusMeters`, inclusiva, siempre sobre la posición **confirmada** por la autoridad. Se comprueba al empezar y en cada paso.

**Validación de comandos.** D2 la implementa en este orden; el primer fallo es el motivo:

1. `InvalidCommandId` (Guid vacío; no se guarda).
2. ID ya usado: si coinciden remitente y contenido (`HasSameContent`), se reproduce el resultado guardado, sin eventos de snapshot ni cambios; si no, `CommandIdConflict` (no se guarda).
3. `NotJoined`, `EpochMismatch`, `UnknownAction`, `InvalidActionValue` (`ShapeError()` es la comprobación estructural compartida).
4. `Restart`: se acepta en cualquier estado.
5. `Pause` y `Resume`: `SessionEnded` si la misión terminó; `InvalidState` según el estado (Pause exige `Running`; Resume, `Ready` o `Paused`).
6. `StartTask`: `SessionEnded`; `InvalidState` (exige `Running`); `UnknownStation`; `UnknownTaskType`; `TaskNotOfferedAtStation`; `StationUnavailable`; `PlayerBusy`; `StationOccupied`; `OutOfRange`; `InsufficientResources` (comprobación previa: el efecto no se podría aplicar con el stock actual).
7. `CancelTask`: `SessionEnded`; `InvalidState` (exige `Running` o `Paused`); `UnknownStation`; `NoActiveTask` (el remitente no tiene tarea en esa estación).

Los comandos se aplican en orden de llegada a la autoridad: si dos jugadores piden la misma estación, el primero la obtiene y el segundo recibe `StationOccupied`.

**Fin de una tarea** (`TaskEndReason`). Solo `Completed` aplica el efecto:

| Situación | Motivo | Efecto de material | Energía ya gastada |
| --- | --- | --- | --- |
| Progreso = duración y `Apply` responde `Applied` | `Completed` | aplicado una vez | consumida |
| Progreso = duración y `Apply` lo rechaza (stock o capacidad) | `InsufficientResources` | nada | no se devuelve |
| `CancelTask` del ejecutor | `CancelledByPerformer` | nada | no se devuelve |
| La posición confirmada sale del radio | `PerformerMovedAway` | nada | no se devuelve |
| El ejecutor sale o pierde el enlace | `PerformerLeft` | nada | no se devuelve |
| La misión pasa a `Completed` o `Interrupted` | `MissionEnded` | nada | no se devuelve |
| `Restart` | (se borra, sin `LastEnd`) | nada | reinicio del modelo |

## Costos: quién, cuándo y qué pasa al cancelar

| Costo | Lo aplica | Cuándo | Al cancelar |
| --- | --- | --- | --- |
| Demanda continua de la tripulación (aire, agua, comida) | **D1**, dentro de `IOutpostSimulation.Advance`, según `CrewAboard` | en cada paso, mientras corre la misión | no aplica |
| Energía de las tareas | **D1**, en `Advance`, con `ServiceLoadWatts` o `ScienceRequested` | durante el progreso | lo gastado no se devuelve |
| Efectos de material de una tarea | **D1**, en `Apply(ResourceTransfer)`, todo o nada | una vez, al completarse | nada se aplica |
| Decidir el progreso y llamar a `Apply` | **D2** (única autoridad de tareas) | progreso = segundos plenamente servidos (`LastFullyServedServiceSeconds` / `LastFullyServedScienceSeconds`; tiempo transcurrido si la tarea es Support con 0 W) | — |

D2 **nunca** suma ni resta stock. D1 **nunca** decide tareas. `ResourceTransfer.Id` = id del comando `StartTask` que creó la instancia; `Apply` acepta cada id una sola vez por `Reset` (si se repite, `AlreadyApplied`; mismo id con otro efecto, `IdConflict`). Así ni D1 ni D2 pueden descontar dos veces el mismo recurso. Si el servicio es parcial, la tarea no avanza (`Stalled`), igual que la ciencia en fase 1. El reparto entre esencial, servicio y ciencia lo documenta D1.

## Recursos y cabina

- Stocks (`WaterStock`: potable/sin tratar + capacidades; `FoodStock`: almacenada + capacidad). Son finitos, no negativos y nunca superan su capacidad. Se usan como configuración inicial y como libro mayor publicado. La humedad de la comida forma parte de `StoredFood` y no se cuenta como agua.
- `ResourceEffect(changes, boundaryOutflowKilograms)`: un cambio por tipo, deltas finitos distintos de cero y **balance explícito** (Σdeltas + salida por la frontera = 0, ±1e-9 kg). La masa nunca aparece ni desaparece en silencio. El tipo no implica ningún porcentaje de recuperación.
- Cabina: `CabinConfiguration` declara **una zona bien mezclada** (simplificación explícita) con volumen, temperatura y presiones parciales iniciales de O2 y CO2. `CabinAirObservation` lleva dos lecturas **independientes** (`GasReading`): `Valid` y `Stale` traen valor y hora de medida; `Unavailable` no trae ninguno de los dos (`null`). Una lectura ausente nunca es 0 ni "normal". El contrato no incluye umbrales clínicos, de alarma ni de seguridad, y la presión total y otros gases no se modelan aquí.
- `OutpostSimulationSnapshot` = `PowerSnapshot` (significado de fase 1) + `ServiceLoadRequestedWatts`/`ServiceLoadServedWatts` (servido ≤ pedido) + `LifeSupportSnapshot`. La carga de servicio es un campo nuevo: no se mezcla con los campos esencial o científico.
- Los valores de `OutpostFixtures` son **sintéticos** y están rotulados. No son datos de la NASA, de hardware ni tasas reutilizables.

```csharp
public interface IOutpostSimulation            // D1 implementa; solo la autoridad (D2) la llama
{
    void Reset(OutpostConfiguration configuration);
    OutpostSimulationSnapshot Snapshot { get; }
    void Advance(double elapsedSeconds, OutpostDemand demand);   // OutpostDemand(crewAboard, scienceRequested, serviceLoadWatts)
    double LastFullyServedServiceSeconds { get; }
    double LastFullyServedScienceSeconds { get; }
    double SecondsUntilServiceChange(OutpostDemand demand);
    ResourceTransferStatus Apply(ResourceTransfer transfer);     // Applied | AlreadyApplied | IdConflict | InsufficientStock | ExceedsCapacity
}
```

## Puertos

```csharp
public interface IOutpostSessionPort           // un participante: presentación, entrada, pruebas
{
    OutpostMembership Membership { get; }
    bool TryGetSnapshot(out OutpostSessionSnapshot snapshot);
    void Submit(OutpostCommand command);           // correlacionado: un CommandCompleted o un CommandAbandoned
    void SubmitMovement(MovementInput input);      // sin correlación ni resultado
    event Action<OutpostSessionSnapshot> SnapshotChanged;
    event Action<CommandResult> CommandCompleted;
    event Action<OutpostCommand, string> CommandAbandoned;   // NotConnected | ConnectionLost | LocalDisconnect
    event Action<OutpostMembership> MembershipChanged;
}

public interface IOutpostAuthority             // D2 implementa; lo usan los adaptadores de host de D3
{
    OutpostMembership Join();                      // Joined(nuevo id) o Rejected(SessionFull)
    bool Leave(PlayerId player);                   // idempotente
    void Submit(PlayerId sender, OutpostCommand command);
    MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);
    bool TryGetSnapshot(out OutpostSessionSnapshot snapshot);
    event Action<OutpostSessionSnapshot> SnapshotChanged;
    event Action<PlayerId, CommandResult> CommandCompleted;  // enviar solo al remitente
}

public interface ICrewRoster                   // D3 implementa; la autoridad de D2 es dueña de la única instancia
{
    IReadOnlyList<CrewMemberState> Members { get; }
    OutpostMembership Join();
    bool Leave(PlayerId player);
    MovementInputDisposition SubmitMovement(PlayerId player, MovementInput input, long currentEpoch);
    void Advance(double elapsedSeconds);           // única autoridad de colisión; resultado IsAdmissible
    void ResetForRestart();
}
```

`OutpostCommand(Guid id, long epoch, OutpostAction action, StationId station = default, TaskTypeId task = default)` admite `Pause`, `Resume`, `Restart`, `StartTask(station, task)` y `CancelTask(station)`. Hay atajos `OutpostCommand.StartTask(...)` y `CancelTask(...)`. La época negativa lanza excepción; el Guid vacío, una acción desconocida o un campo que falta no lanzan, para que la autoridad responda con un motivo.

Todos los puertos son de un solo hilo (hilo principal de Unity), con callbacks síncronos. Estado y deduplicación se confirman antes de emitir eventos, y las excepciones del suscriptor se propagan. `Submit` solo entrega la intención; hay que suscribirse antes de enviar.

## Correlación, épocas y revisiones

- **ID de comando**: único durante toda la vida de la autoridad, también tras `Restart`. Repetir exactamente un comando reproduce su resultado sin volver a aplicarlo. Otro contenido con el mismo id, o el mismo id desde otro remitente, da `CommandIdConflict`. Se guardan aceptados y rechazados (salvo `InvalidCommandId` y `CommandIdConflict`). Un id nuevo con una época distinta de la actual da `EpochMismatch`. El resultado conserva la época **del comando**; la nueva época de un `Restart` se ve en el snapshot.
- **Movimiento frente a comandos**: los comandos (clics, ritmo humano) se guardan para deduplicar; las entradas de movimiento (por frame) no, solo la última secuencia por miembro presente, que se borra en `Leave`. La caché no crece por frame.
- **Revisión**: cada cambio publicado (entrada o salida, comando aceptado, paso que cambia algo) suma 1. `Restart` sube la época y deja la revisión en 0. Un rechazo o una repetición no cambian la revisión. El orden es `(Epoch, Revision)` (`IsNewerThan`); los consumidores descartan lo viejo o igual.
- **Clientes que llegan tarde**: cada snapshot es estado **completo**, nunca un delta. Basta el último, y lo recibe al unirse antes de `Joined`.
- **Desconexión**: en el cliente, los pendientes se abandonan (`CommandAbandoned`, resultado desconocido: la autoridad pudo aplicarlo), `Membership` pasa a `Left` y el último snapshot queda como "último conocido", no vigente. No se reintenta con un id nuevo para "repetir". En la autoridad se aplica `Leave(player)` (ver presencia).
- **Reinicio**: los comandos pendientes de la época anterior reciben `EpochMismatch`; tareas y estaciones se borran sin efectos; el modelo vuelve a `Reset`.
- **Inmutabilidad**: los snapshots y sus listas son copias privadas de solo lectura. Modificar la lista de origen o intentar `IList.Add` no los cambia (`NotSupportedException`).

## Códigos (`OutpostReasons`)

| Grupo | Códigos |
| --- | --- |
| Iguales que en fase 1 | `Accepted`, `InvalidCommandId`, `CommandIdConflict`, `EpochMismatch`, `UnknownAction`, `InvalidActionValue`, `InvalidState`, `SessionEnded`, `SimulationFault` |
| Fase 2 | `NotJoined`, `UnknownStation`, `UnknownTaskType`, `TaskNotOfferedAtStation`, `StationUnavailable`, `PlayerBusy`, `StationOccupied`, `OutOfRange`, `NoActiveTask`, `InsufficientResources`, `SessionFull` |
| Solo cliente (nunca en un `CommandResult`) | `NotConnected`, `ConnectionLost`, `LocalDisconnect` |

`OutpostOutcome`: `InProgress` (Ready/Running/Paused), `ObjectiveAchieved` y `TimeExpired` (Completed), `EssentialServiceLost` y `SimulationFault` (Interrupted). Va en el snapshot, así que el cliente no tiene que evaluar nada por su cuenta. Es un enum propio de la fase 2, no `MissionOutcome`. `ObjectiveProgress(key, current, target, unit, required)` publica el avance de los objetivos que defina D2.

## Convivencia con fase 1

- `ISessionPort`, `MissionSession`, `PowerSystem`, el protocolo de cable v1, `FakeSessionPort` y la escena `Phase01_PowerCoop` **no cambian**. La build de fase 1 sigue usando `ISessionPort`.
- `IOutpostSessionPort` es otro puerto. Una escena usa uno u otro; no hay adaptador entre ambos y no hace falta para producción.
- D3 implementa sobre Unity Transport 2.7.4/WebSocket, como en fase 1 y sin paquetes nuevos:
  - el adaptador de cliente de red (`IOutpostSessionPort`);
  - el adaptador local de host (`IOutpostSessionPort` sobre `IOutpostAuthority` con su `PlayerId`);
  - el puente de host (`Join` al sincronizar una conexión, `Leave` al cerrarla, resultados solo al remitente);
  - un cable de fase 2 con versión o tipos nuevos, que conserva el v1 y respeta `OutpostLimits`.
- D2 implementa `IOutpostAuthority` y compone D1 (`IOutpostSimulation`) y D3 (`ICrewRoster`). Nada de esto está conectado a producción en esta base.

## Fake de desarrollo

`FakeOutpostSessionPort` (`Label` = "DESARROLLO · FakeOutpostSessionPort · sin red, sin simulación, sin autoridad") lo controla una fixture:

- `Publish(snapshot)` exige que sea más reciente y que, si el puerto está unido, siga incluyendo al jugador local.
- `Join(id)` exige un snapshot con ese id. `RejectJoin(reason)`.
- `Disconnect(reason)` abandona los pendientes y pasa a `Left`.
- `Accept(id)` y `Reject(id, reason)` completan, **en diferido**, la presentación pendiente más antigua con ese id. No inventan resultados para ids no enviados.
- `PendingCommands`, `LastMovementInput`, `MovementInputCount` y `DroppedMovementInputCount` permiten inspeccionar lo enviado.

Limitaciones explícitas del fake:

- no decide resultados ni deduplica;
- no valida proximidad, exclusión, stock, época ni permisos;
- no mueve a nadie ni simula;
- no es transporte.

`OutpostFixtures` provee `Scenario()` (dos salas sintéticas), `InitialResources`, `ExampleObjectives`, `Initial`, `Next`, `WithMember`, `WithStation` y `WithoutMember`. Solo arma los estados que la fixture elige.

## Ejemplos (código real, ejecutado en `OutpostContractExamplesTests`)

En los ejemplos, la fixture hace **a mano** el papel de la autoridad: decide los resultados y publica las consecuencias. Muestran el flujo del cliente y las reglas documentadas, no una autoridad implementada.

**1. Incorporación y estado inicial**

```csharp
var port = new FakeOutpostSessionPort();
port.Publish(OutpostFixtures.Initial(scenario, 0, Ana, Leo));   // autoridad: Ana hueco 0, Leo hueco 1
port.Join(Leo);
port.TryGetSnapshot(out OutpostSessionSnapshot state);
foreach (CrewMemberState member in state.Crew)
{
    bool local = member.Player == port.Membership.Player;        // Leo = local, Ana = remota
    // member.Pose.Position == scenario.Layout.SpawnPoints[member.Slot], Motion == Idle
}
```

**2. Movimiento solicitado y confirmado**

```csharp
long sequence = 1;
port.SubmitMovement(new MovementInput(before.Epoch, sequence, MoveDirection.FromAxes(1, 1)));
// Sin predicción: el snapshot no cambia hasta que la autoridad confirma.
var pose = new CrewPose(new BasePosition(start.X + step, start.Y + step), MoveDirection.FromAxes(1, 1), CrewMotion.Walking);
port.Publish(OutpostFixtures.Next(before, crew: OutpostFixtures.WithMember(before, Ana, pose, sequence)));
// ana.LastInputSequence == 1; recorrió 1 m en 0,5 s a 2 m/s, también en diagonal.
MovementInputPolicy.Evaluate(ana.LastInputSequence, after.Epoch,
    new MovementInput(after.Epoch, sequence, MoveDirection.Zero));   // StaleSequence
```

**3. Dos jugadores intentan ocupar la misma estación**

```csharp
OutpostCommand anaStart = OutpostCommand.StartTask(Guid.NewGuid(), s0.Epoch, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
OutpostCommand leoStart = OutpostCommand.StartTask(Guid.NewGuid(), s0.Epoch, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater);
anaPort.Submit(anaStart);
leoPort.Submit(leoStart);
// Autoridad: orden de llegada, Ana primero.
var occupied = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Occupied,
    new TaskProgress(OutpostFixtures.TreatWater, Ana, 0, 20, false), null);
OutpostSessionSnapshot s1 = OutpostFixtures.Next(s0, stations: OutpostFixtures.WithStation(s0, occupied));
anaPort.Publish(s1); leoPort.Publish(s1);
anaPort.Accept(anaStart.Id);
leoPort.Reject(leoStart.Id, OutpostReasons.StationOccupied);
// Ana en dos estaciones a la vez => ArgumentException al construir el snapshot.
```

**4. Tarea rechazada por distancia**

```csharp
scenario.Layout.TryGetStation(OutpostFixtures.ScienceBench, out StationPlacement bench);
bool reach = bench.IsWithinReach(ana.Pose.Position);           // false: unos 11 m desde (3, 3)
OutpostCommand start = OutpostCommand.StartTask(Guid.NewGuid(), s0.Epoch, OutpostFixtures.ScienceBench, OutpostFixtures.RunExperiment);
port.Submit(start);
port.Reject(start.Id, OutpostReasons.OutOfRange);             // sin cambio de estado ni de revisión
```

**5. Cancelación, reinicio y desconexión**

```csharp
OutpostCommand cancel = OutpostCommand.CancelTask(Guid.NewGuid(), s1.Epoch, OutpostFixtures.WaterProcessor);
port.Submit(cancel);
var cancelled = new StationState(OutpostFixtures.WaterProcessor, StationAvailability.Available, null,
    new TaskEnd(OutpostFixtures.TreatWater, Ana, TaskEndReason.CancelledByPerformer, s1.SimulatedTimeSeconds));
port.Publish(OutpostFixtures.Next(s1, stations: OutpostFixtures.WithStation(s1, cancelled)));
port.Accept(cancel.Id);

OutpostCommand restart = new OutpostCommand(Guid.NewGuid(), s2.Epoch, OutpostAction.Restart);
port.Submit(restart);
port.Publish(OutpostFixtures.Initial(scenario, s2.Epoch + 1, Ana));   // época 1, revisión 0
port.Accept(restart.Id);                                              // result.Epoch == 0
// Un comando tardío de la época 0 recibe EpochMismatch.

port.Submit(OutpostCommand.StartTask(Guid.NewGuid(), 1, OutpostFixtures.WaterProcessor, OutpostFixtures.TreatWater));
port.Disconnect(OutpostReasons.ConnectionLost);   // CommandAbandoned(ConnectionLost) y Membership = Left
```

**6. Presentación con el fake (D4)**

```csharp
var port = new FakeOutpostSessionPort();
var labels = new CrewLabels(port);   // la vista solo conoce IOutpostSessionPort
port.Publish(OutpostFixtures.Initial(scenario, 0, Ana, Leo));   // "Sin datos vigentes" (no unido)
port.Join(Ana);                                                 // "Tú: Idle", "Astronauta 2: Idle"
port.Disconnect(OutpostReasons.ConnectionLost);                 // "Sin datos vigentes"
```

`OutpostContractExamplesTests.HostAdapterSketch` muestra, solo como código compilado y nunca ejecutado, cómo un puente de host liga conexión ↔ `PlayerId` y devuelve cada resultado únicamente a su remitente.

## Qué garantiza esta base y qué no

| Garantía | Dónde | Estado |
| --- | --- | --- |
| Rechazo de NaN/∞, negativos, sobrecapacidad, ids inválidos, enums no definidos, balance de masa | constructores de tipos | implementado y probado |
| Inmutabilidad de snapshots y configuración | tipos + prueba por reflexión | implementado y probado |
| Exclusión persona/estación, ejecutor presente, sin tareas tras el fin, resultado coherente con el estado | constructor de `OutpostSessionSnapshot` | implementado y probado |
| Orden de movimiento (época/secuencia), forma de los comandos, regla de colisión y de alcance | `MovementInputPolicy`, `ShapeError`, `IsAdmissible`, `IsWithinReach` | implementado y probado |
| Diferido, sin resultados inventados, abandono al desconectar, snapshots solo hacia delante, `Joined` con estado | `FakeOutpostSessionPort` | implementado y probado (fake, no autoridad) |
| Deduplicación de ids, orden de validación, proximidad continua, costos, `Join`/`Leave`, revisiones | autoridad D2 + roster D3 | **pendiente**: regla documentada, sin implementar ni probar |
| Modelos de agua, comida, cabina, demanda y reparto eléctrico | D1 | **pendiente** |
| Cable de fase 2, adaptadores y entrada | D3 | **pendiente** |
| Permisos de pausa/reinicio por rol | — | **sin decidir** (igual que en fase 1: cualquier miembro presente) |

Cualquier cambio de estos archivos es una **nueva base coordinada** (ver `OWNERSHIP.md`).
