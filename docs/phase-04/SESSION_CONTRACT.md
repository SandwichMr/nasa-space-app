# Contratos de sesión — fase 4 (base viva: recursos, mantenimiento y sesión continua)

Base común de la fase 4. **No es la jugabilidad**: fija tipos, reglas y puertos para que los balances y el equipo (D1), la sesión continua y las acciones (D2), la red (D3), la presentación (D4) y la validación (D5) encajen sin inventar interfaces. Se apoya en las fases 2 y 3 **sin modificar sus contratos congelados**.

C# puro, sin `UnityEngine` ni SDK de red, compilado solo contra `netstandard` 2.1 con `-warnaserror`. Todos los tipos publicados son inmutables; una prueba por reflexión lo comprueba.

| Archivo (bajo `Assets/_Project/`) | Namespace | Contenido |
| --- | --- | --- |
| `Runtime/Simulation/Contracts/BaseOperations/EquipmentModel.cs` | `Trainer.Simulation.BaseOperations` | `EquipmentKind`, `EquipmentCondition`, `EquipmentOperatingState`, `EquipmentFault`, `SorbentBed`, `ReadingUnit`, `InstrumentReading`, `WaterProcessorConfiguration`, `AirRevitalizationConfiguration`, `SpareStock`, `BaseEquipmentConfiguration`, `EquipmentDemand`, `WaterProcessorState`, `AirRevitalizationState`, `EquipmentLedger`, `BaseEquipmentSnapshot`, `BaseEquipmentLimits` |
| `…/BaseOperations/EquipmentSimulation.cs` | ídem | `EquipmentEffectKind`, `EquipmentEffect`, `EquipmentEffectStatus`, `IBaseEquipmentModel`, `IAirServiceCapacity` |
| `Runtime/Application/Contracts/BaseOperations/Session.cs` | `Trainer.Application.BaseOperations` | `BaseOperationsLimits`, `BaseSessionMode`, `IrreversibleCause`, `IrreversibleRule`, `BaseSessionStatus`, `ActivityKind`, `ActivityStatus`, `ActivityRecord`, `OperationsReport` |
| `…/BaseOperations/Gauges.cs` | ídem | `GaugeKind`, `GaugeBand`, `GaugeRule`, `GaugeBands` (regla única), `ResourceGauge` |
| `…/BaseOperations/Maintenance.cs` | ídem | `Findings`, `EquipmentKnowledge`, `ObservedReading`, `EquipmentObservation`, `RepairSteps`, `RepairSequence` (regla única), `RepairJob`, `EquipmentStatus`, `PotableQuality`, `WaterTreatmentStatus`, `DecisionAssessment`, `DecisionRecord`, `OperationProgress` |
| `…/BaseOperations/Logistics.cs` | ídem | `ManifestItem`, `ResupplyManifest`, `LogisticsRules`, `OrderState`, `ResupplyOrder` |
| `…/BaseOperations/Commands.cs` | ídem | `BaseOperationAction`, `OperationChoice`, `BaseOperationCommand`, `BaseOperationReasons` |
| `…/BaseOperations/Scenario.cs` | ídem | `OperationDefinition`, `ScienceOpportunity`, `BaseOperationsScenario` |
| `…/BaseOperations/Snapshot.cs` | ídem | `BaseOperationsSnapshot` |
| `…/BaseOperations/Ports.cs` | ídem | `IBaseOperationsSessionPort`, `IBaseOperationsAuthority` |
| `…/BaseOperations/Presentation.cs` | ídem | `IBaseOperationsIntents`, `BaseOperationsViewContext`, `IBaseOperationsView` (montaje de D4) |
| `…/BaseOperations/Development/FakeBaseOperationsSessionPort.cs` | `Trainer.Application.Development` | Fake de desarrollo del puerto |
| `…/BaseOperations/Development/BaseOperationsFixtures.cs` | ídem | Escenario sintético y `BaseOperationsSnapshotBuilder` |

## Compatibilidad con las fases 2 y 3

Nada de lo congelado en `docs/phase-02/OWNERSHIP.md` ni en `docs/phase-03/OWNERSHIP.md` cambia. La fase 4 los **contiene**:

- **Escenario**: `BaseOperationsScenario.Expedition` es un `ExpeditionScenario` completo (plano, zonas, esclusa, trajes, tareas). El reciclador es la estación existente `water-recycler`, el aire es `air-scrubber` y se añade una estación `logistics-console`. Una estación de fase 4 **no** ofrece tareas de fase 2: así cada estación tiene un solo significado (en el escenario de fase 4 desaparecen `treat-water` y `check-cabin-air`).
- **Snapshot**: `BaseOperationsSnapshot.Expedition` es el `ExpeditionSessionSnapshot` de la misma época y revisión, que a su vez contiene el `OutpostSessionSnapshot`. Las vistas de fase 2 y 3 funcionan sin cambios sobre el mismo puerto.
- **Puerto**: `IBaseOperationsSessionPort : IExpeditionSessionPort`. Eventos de una revisión: fase 2 → fase 3 → fase 4.
- **Autoridad**: `IBaseOperationsAuthority` no implementa `IOutpostAuthority` ni `IExpeditionAuthority`, para que las herramientas y validadores anteriores no la confundan con la suya. Tampoco se añade otra implementación de `IOutpostSimulation`, `ICrewRoster` o `IEvaSimulation`: el descubrimiento de D5 de fase 2 exige una sola de cada una.
- **Modelo base**: el `IOutpostSimulation` existente sigue siendo el único dueño de energía, agua potable/sin tratar, comida y aire de cabina. El modelo nuevo `IBaseEquipmentModel` solo lleva lo que no cabe en los tipos congelados: la retención del reciclador, los lechos, el flujo de entrada, los repuestos y la avería programada. `IAirServiceCapacity` acopla los lechos al CO2 del modelo base, en el **mismo** objeto (D1 lo implementa en `OutpostBaseSimulation`; factor 1 = comportamiento de fase 2 idéntico).
- **Por qué no se extienden los tipos congelados**: añadir valores a `ResourceKind`, `OutpostAction` u `OutpostOutcome` cambiaría los cables v1 y las validaciones `Enum.IsDefined` de las fases 2 y 3. Por eso las acciones nuevas van en `BaseOperationCommand` y los motivos nuevos en `BaseOperationReasons`.

## 1. Ciclo global de la sesión y ciclo de las actividades

| Concepto | Dónde | Regla |
| --- | --- | --- |
| Sesión global | `OutpostSessionSnapshot.State` + `BaseSessionStatus` | `Ready → Running ⇄ Paused`. En modo `Continuous` **nunca** pasa a `Completed`. Solo pasa a `Interrupted` por una regla irreversible. |
| Pausa | `ExpeditionCommand Pause/Resume` (fase 2/3) | Autoritativa: con la sesión en pausa no avanza el reloj ni hay consumo. |
| Salida voluntaria de un jugador | `Leave` | La base sigue para los demás. Su tarea u operación termina con `PerformerLeft`, sus repuestos reservados se liberan, la energía consumida no se devuelve y los pedidos se conservan. |
| Host apagado | — | Por defecto no hay consumo: el mundo solo progresa mientras la autoridad se ejecuta. No hay servidor persistente (eso es fase 6). |
| Fin irreversible | `IrreversibleRule`, `IrreversibleCause` | `EssentialPowerLost`: el canal esencial está sin servir del todo durante `EssentialUnservedGraceSeconds` **seguidos**. Cada paso servido pone el contador a 0, y `SecondsBeforeIrreversible` se publica como ventana de recuperación. También `SuitServiceDepleted` (regla de fase 3) y `SimulationFault`. Fase 2: `Interrupted` + `EssentialServiceLost` o `SimulationFault`. Solo se acepta `Restart`. |
| Reinicio | `ExpeditionCommand Restart` | Nueva época; todo vuelve al escenario (reloj, stocks, equipo, repuestos, pedidos). Es explícito y claro. |
| Actividades | `ActivityRecord` | `Expedition`, `ScienceAssignment`, `WaterBatch`, `AirService`, `Repair` y `Resupply`, cada una `Active → Completed / Cancelled / Failed`. Completar una **no** cierra la sesión. |
| Salida (sortie) | fase 3 `ExpeditionPhase` | `Preparing → Underway → Returning → Preparing`. Al volver todos o cancelar, el `ActivityRecord` se cierra y la fase 3 vuelve a `Preparing`, sin decisión. `Closed` solo existe con la sesión terminada. Stocks, reloj, tripulación y equipo se conservan entre salidas. Los objetivos de fase 3 publican la salida en curso. |
| Informes | `OperationsReport` | Cada `ReportIntervalSeconds`, un resumen **no terminal**. |
| Entrenamiento | `BaseSessionMode.Training` | Reglas finitas de fase 3. Las escenas `Phase02_InteriorBase` y `Phase03_EvaAndShelter` conservan sus propias autoridades y su regresión. |

La duración de la misión de fase 3 (`ExpeditionMissionRules.DurationSeconds`) **no** se aplica en modo continuo.

## 2. Lecturas, stocks y bandas

Ocho barras (`GaugeKind`), cada una con unidad (`GaugeBands.UnitOf`), causa, consecuencia y acción:

| Barra | Unidad | Origen | Acción del jugador |
| --- | --- | --- | --- |
| `BatteryEnergy` | J | `PowerSnapshot.EnergyJoules` | reducir cargas, esperar generación |
| `PowerBalance` | W | generación − (esencial + ciencia + servicio solicitados) | no arrancar operaciones caras en déficit |
| `PotableWater` | kg | stock de fase 2 | procesar y enrutar lotes, reponer |
| `UntreatedWater` | kg | stock de fase 2 (detalle) | procesar |
| `ProcessorHold` | kg | retención del reciclador (detalle) | decidir la ruta |
| `StoredFood` | kg | stock de fase 2 | reponer (no hay cultivos en fase 4) |
| `CabinOxygen` | kPa | lectura de instrumento de fase 2 | — |
| `CabinCarbonDioxide` | kPa | lectura de instrumento de fase 2 | servicio de aire |

- **Calidad**: `InstrumentReading` usa el mismo `ReadingStatus` que fase 2. `Unavailable` no tiene valor y su banda es `Unknown`. `Stale` conserva su último valor y su hora. **Desconocido o antiguo nunca es cero ni normal.**
- **Bandas** (`GaugeBands.Classify`, regla única): `Deficit` (potencia negativa) → `AboveLimit` (CO2 por encima de la línea) → `Exhausted` (stock en 0) → `Reserve` (en o bajo la línea de reserva) → `Nominal`. `ResourceGauge` rechaza una banda que no siga la regla.
- **Distinción obligatoria**: `Reserve` y `Exhausted` describen un **stock bajo**. Que una máquina esté degradada o fallada es `EquipmentKnowledge` (lo que se sabe) y `EquipmentCondition` (la verdad del modelo, no publicada). Nunca se mezclan.
- Las líneas (`GaugeRule`) son valores **sintéticos de escenario**, no umbrales clínicos.
- **Tendencia**: `TrendPerSecond` es la variación del último paso de la autoridad, o null. El cliente no la extrapola.

## 3. Equipo, observaciones y reparación

**Reciclador** (`water-recycler`):

1. `ProcessBatch` (con tiempo y potencia). **Al completarse**, la base quita `BatchKilograms` de agua sin tratar con una `ResourceTransfer` de fase 2 (−X sin tratar, salida por frontera +X). Después `EquipmentEffect.ReceiveBatch` con el **mismo id**. Producto = X × recuperación de la condición actual; el resto es salmuera que sale de la frontera y no vuelve nunca.
2. Al completarse se publica la conductividad del producto (µS/cm, `Valid`) y una observación nueva.
3. `RouteProduct` decide con esa observación vigente:
   - `AcceptToPotable`: `ReleaseToPotable`, y después la base añade +X potable (entrada por frontera), mismo id.
   - `Reprocess`: `ReleaseForReprocess`, y después +X sin tratar.
4. `ProductSpecLimitMicrosiemens` es la línea de aceptación del escenario, **no** un índice de salud.
5. **Consecuencia acotada**: aceptar producto fuera de línea deja el tanque `Suspect` con `SuspectKilograms`. `RecheckPotable` (con tiempo y potencia) devuelve esa masa (o lo que quede) de potable a sin tratar con una `ResourceTransfer` de fase 2 de balance cero. No se modela ningún daño médico.

**Unidad de aire** (`air-scrubber`):

- Tiene dos lechos absorbentes. El activo se carga con la tripulación a bordo; por encima de la rodilla, su factor cae linealmente (`AirRevitalizationConfiguration.BedFactor`).
- Factor aplicado al modelo base = factor del lecho × flujo de entrada. Aislada, el factor es 0.
- `ServiceAir`:
  - `SwitchBed` intercambia los lechos.
  - `RegenerateStandbyBed` (energía y tiempo) deja el lecho en reserva a 0.
- `Intervene ReplaceInletFilter` (con repuesto) restablece el flujo y exige la unidad aislada: mientras tanto el CO2 sube.

**Avería sintética reproducible**:

- En `DegradeAtSeconds` el filtro del reciclador queda restringido: condición `Degraded`, menor recuperación y producto fuera de línea.
- Sin reparar, tras `FailAfterDegradedSeconds` pasa a `Failed` y no admite lotes.
- No hay MTBF ni estadística.

**Observaciones** (`EquipmentObservation`):

- `Inspect` (con tiempo) produce una observación con hallazgos (`Findings`) y lecturas: `product-conductivity`, `active-bed-loading`, `standby-bed-loading`, `inlet-flow`.
- Es `Current` mientras no cambie el `StateSequence` de la máquina y no pase `ObservationValiditySeconds`.
- Las decisiones (`RouteProduct`, `ServiceAir`, `Isolate`, `Intervene`, `Restore`) llevan la secuencia que vieron. Si deja de ser vigente: `StaleObservation`. Sin observación: `ObservationRequired`.

**Reparación** (`RepairSequence`, regla única): Inspect → Isolate → Intervene → Test → Restore.

| Paso | Efecto | Exige |
| --- | --- | --- |
| `Inspect` | observación | máquina no aislada |
| `Isolate` (inmediato) | `SetOperatingState Isolated`; abre `RepairJob` | observación vigente con un hallazgo que lo justifique (si no, `NothingToRepair`) |
| `Intervene` (tiempo, repuesto **reservado** al empezar y **consumido** al completar) | `ReplaceProcessorFilter` o `ReplaceInletFilter` | aislada; repuesto libre (`SpareUnavailable`); intervención de esa máquina (`InterventionNotForEquipment`) |
| `TestEquipment` (tiempo) | `SampleProduct` y observación nueva; `LastTestPassed` | después de Intervene |
| `Restore` (inmediato) | `SetOperatingState Online`; cierra el trabajo | test aprobado y su observación vigente |

- Un test fallido permite intervenir otra vez.
- Cancelar o abandonar una intervención libera la reserva; la energía no se devuelve.
- Nada cura una máquina desde otro sitio ni devuelve agua o energía perdidas.

**Decisiones**:

- Una decisión aceptada se registra en `DecisionRecord`, con `Appropriate`, `NotIndicated` o `Harmful` y una clave de explicación (por ejemplo `accepted-off-spec-product`, `inlet-filter-not-indicated`).
- La autoridad la juzga con la verdad del momento. Una decisión incorrecta tiene explicación y una consecuencia acotada y recuperable.

## 4. Pedidos y logística

- `ResupplyManifest`: líneas de masa (potable o comida, en kg) y de repuestos (unidades), `LeadSeconds` y `CancelWindowSeconds`.
- `LogisticsRules`: consola, `MaxOrdersInFlight` y `MinSecondsBetweenOrders`.
- **Coste**: plazo, cupo, intervalo y tiempo de tripulación para descargar. La comida no aparece por pulsar un botón.
- **Estados**:
  - `Pending`: cancelable sin coste hasta `CommitAtSeconds`.
  - `InTransit`.
  - `Arrived`: espera en la consola.
  - `Unloaded`.
  - `Cancelled`: solo desde `Pending`.
- **Efecto único de D1**:
  - `UnloadDelivery` (con tiempo) aplica una `ResourceTransfer` de fase 2 con entrada por frontera (delta positivo, outflow negativo) y `EquipmentEffect.ReceiveSpares`, con ids derivados del comando.
  - Lo que no cabe queda en `Remaining`. Si no cabe nada: `NoStorageCapacity`.
  - Cada id se aplica una sola vez por `Reset`.
- La desconexión de quien hizo el pedido **no** lo cancela. Una descarga interrumpida no aplica nada.

## 5. Interacciones significativas (agua y aire) y validación autoritativa

`BaseOperationCommand(Guid id, long epoch, BaseOperationAction action, StationId station, OperationChoice choice, string manifestKey, long orderSequence, long observationSequence, long zoneSequence)`.

- Sin identidad, posición, stock ni «gané el minijuego».
- `ShapeError` comprueba: estación obligatoria; elección según la acción (`IsChoiceAllowed`); manifiesto solo en `PlaceOrder`; secuencia de pedido solo en `CancelOrder` y `UnloadDelivery`; observación exactamente en las decisiones (`NeedsObservation`).

**Orden de validación** (el primer fallo es el motivo):

1. `InvalidCommandId`.
2. Id repetido: si coinciden remitente y contenido, se reproduce el resultado; si no, `CommandIdConflict`. Comparten el espacio de ids con `ExpeditionCommand`.
3. `NotJoined` → `EpochMismatch` → `ShapeError` (`UnknownAction` / `InvalidActionValue`).
4. `SessionEnded` → `InvalidState` (exige `Running`; `CancelOperation` y `CancelOrder` también en `Paused`).
5. `UnknownStation` → `WrongStationForAction`.
6. `CancelOperation`: `NoActiveTask` si el remitente no tiene operación en esa estación.
7. `PlayerBusy` (tarea de fase 2 u operación propia) → `StationOccupied` → `StaleZone` → `OutOfRange` → `SealedNotAllowed` (con suministro del traje).
8. Reglas de la acción:
   - máquina: `EquipmentIsolated` / `EquipmentNotIsolated` / `EquipmentFailed`;
   - observación: `ObservationRequired` → `StaleObservation`;
   - reparación: `RepairStepOutOfOrder` / `NothingToRepair` / `InterventionNotForEquipment`;
   - agua: `HoldNotEmpty` / `HoldEmpty` / `PotableNotSuspect`;
   - repuestos: `SpareUnavailable`;
   - pedidos: `UnknownManifest` / `OrderLimitReached` / `OrderTooSoon` / `UnknownOrder` / `OrderNotCancellable` / `OrderNotArrived` / `NoStorageCapacity`.
9. `InsufficientResources`: el stock del modelo base no admite la transferencia al empezar (por ejemplo, falta agua sin tratar para un lote).

**Progreso confirmado**:

- Una operación con tiempo es una `OperationProgress`. Ocupa a su autor y su estación igual que una tarea de fase 2, y la estación se publica `Unavailable` en fase 2.
- Avanza solo mientras su potencia esté plenamente servida (`Stalled` si no).
- Termina si su autor se aleja (`PerformerMovedAway`), se va (`PerformerLeft`) o cancela.
- Las operaciones inmediatas (`Isolate`, `Restore`, `PlaceOrder` si su duración es 0) se aplican al aceptar.
- Dos jugadores en la misma estación: gana el primero en llegar y el segundo recibe `StationOccupied`.

## 6. Snapshot, identidad, red y montaje

`BaseOperationsSnapshot` = `Expedition` (fase 3 ⊃ fase 2) + `Session` + `Gauges` (una por tipo, en orden) + `Equipment` (una por máquina, en orden) + `Water` + `Spares` + `Operations` + `Orders` + `Activities` (ventana acotada) + `Decisions` (ventana acotada) + `LatestReport`.

- Siempre es estado **completo**, nunca un delta. Se ordena por `(Epoch, Revision)`.
- El constructor **rechaza**:
  - una sesión continua `Completed`;
  - una causa irreversible sin `Interrupted` (o al revés);
  - barras o máquinas que no sean una por tipo;
  - operaciones de ausentes, dos por miembro o por estación, o donde hay una tarea de fase 2;
  - una estación de fase 2 que no esté `Unavailable`;
  - operaciones después del fin;
  - más reservas que repuestos;
  - pedidos o informes del futuro;
  - actividades fuera de orden o más allá de `ActivitySequence`.
- **Identidad, épocas y secuencias**: iguales que en fases 2 y 3. Identidad solo desde `Join`, `Restart` sube la época, deduplicación durante toda la vida de la autoridad, `ZoneSequence` de fase 3 y, además, `ObservationSequence`. Quien se incorpora tarde recibe el estado actual, nunca los valores iniciales.
- **Red (D3)**: extiende el transporte existente (Unity Transport 2.7.4 sobre WebSocket, los mismos `UnityTransportHost`/`UnityTransportClient`) con un cable propio `'T' 'B'` v1, que reutiliza la codificación de fase 3 para la parte `Expedition`. Los cables `'T' 'S'`, `'T' 'O'` y `'T' 'E'` no cambian. Una escena usa uno solo.

```csharp
public interface IBaseOperationsSessionPort : IExpeditionSessionPort      // un participante
{
    bool TryGetBaseOperations(out BaseOperationsSnapshot snapshot);         // TryGetExpedition == BaseOperations.Expedition
    void Submit(BaseOperationCommand command);                              // resultado por CommandCompleted (fase 2)
    event Action<BaseOperationsSnapshot> BaseOperationsChanged;             // después de ExpeditionChanged, misma revisión
    event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
}

public interface IBaseOperationsAuthority                                  // D2 implementa; adaptadores de host de D3
{
    BaseOperationsScenario Scenario { get; }
    OutpostMembership Join();
    bool Leave(PlayerId player);
    void Submit(PlayerId sender, ExpeditionCommand command);                // pausa, tareas, esclusa, regreso
    void Submit(PlayerId sender, BaseOperationCommand command);             // operaciones de fase 4
    MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);
    bool TryGetSnapshot(out BaseOperationsSnapshot snapshot);
    event Action<BaseOperationsSnapshot> SnapshotChanged;
    event Action<PlayerId, CommandResult> CommandCompleted;                 // solo al remitente
}

public interface IBaseEquipmentModel                                        // D1 implementa; solo la autoridad la llama
{
    void Reset(BaseEquipmentConfiguration configuration);
    BaseEquipmentSnapshot Snapshot { get; }
    void Advance(double elapsedSeconds, EquipmentDemand demand);
    double SecondsUntilEquipmentEvent(EquipmentDemand demand);              // la autoridad corta su paso ahí
    EquipmentEffectStatus Apply(EquipmentEffect effect);                    // idempotente por id
}

public interface IAirServiceCapacity                                        // D1, en el MISMO objeto que IOutpostSimulation
{
    void SetCarbonDioxideRemovalFactor(double factor);                      // [0, 1]; 1 = fase 2
    double CarbonDioxideRemovalFactor { get; }
}
```

**Orden de un paso de la autoridad** (un solo reloj):

1. `equipment.Advance(dt, EquipmentDemand(crewAboard))`.
2. `airCapacity.SetCarbonDioxideRemovalFactor(equipment.Snapshot.Air.RemovalFactor)`.
3. Paso de fase 3: modelo base, EVA, roster y esclusa.
4. Progreso de las operaciones.
5. Pedidos (`Pending → InTransit → Arrived` por tiempo).
6. Regla irreversible.
7. Publicación.

El paso se corta en `SecondsUntilEquipmentEvent`, en `SecondsUntilServiceChange` (fase 2) y en `SecondsUntilServiceEvent` (fase 3).

## Costes: quién, cuándo y qué pasa al cancelar

| Coste | Lo aplica | Cuándo | Al cancelar o abandonar |
| --- | --- | --- | --- |
| Demanda de la tripulación, energía, CO2 | D1 base (`Advance`) | cada paso | — |
| Carga de los lechos | D1 equipo (`Advance`) | cada paso con la unidad en línea | — |
| Potencia de las operaciones | D1 base (carga de servicio) | mientras progresan | no se devuelve |
| Lote: sin tratar → reciclador | D1 base (`ResourceTransfer`) y después D1 equipo (`ReceiveBatch`), mismo id | una vez, al **completar** `ProcessBatch` (al empezar solo se valida que haya agua sin tratar y la retención esté vacía) | nada (la energía no se devuelve) |
| Producto → potable o sin tratar | D1 equipo (`Release*`) + D1 base, mismo id | al completar `RouteProduct` | nada |
| Recomprobación | D1 base (`ResourceTransfer` de balance cero) | al completar | nada |
| Repuesto | reservado por D2 al empezar; consumido por D1 (`Replace*`) al completar | — | se libera la reserva |
| Descarga de un pedido | D1 base (entrada por frontera) + D1 equipo (`ReceiveSpares`) | al completar `UnloadDelivery` | nada |
| Decidir zona, observación, vigencia, pasos, progreso, pedidos, fin | **D2** (única autoridad) | — | — |

D2 **nunca** suma ni resta stock. D1 **nunca** decide quién actúa ni qué progresa.

## Conexión de D4

D4 se conecta **solo** a estos puntos, nunca a la autoridad ni a la red:

- **`IBaseOperationsView`**: barras, paneles de estación, explicación de decisiones e informes. Va bajo el montaje `Phase04 Presentation Mount` de la escena. D2 llama a `Bind(BaseOperationsViewContext)` y a `Unbind()`.
- **`IBaseOperationsIntents`** (lo implementa D3): `RequestOperation(station, action, choice)`, `RequestPlaceOrder`, `RequestCancelOrder`, `RequestUnload`, `RequestCancelOperation`, `HasPending`, `LastReason` y `Answered`. El cliente pone la observación vigente; la vista no la inventa.
- **Fase 3**: las vistas `IExpeditionView` e `IExpeditionCrewView` siguen montándose con su contexto, construido sobre el mismo puerto.
- **Geometría**: se dibuja desde `BaseOperationsScenario.Layout` en tiempo de ejecución, como en fases 2 y 3. Ningún collider decide posiciones.
- **Textos**: se buscan por claves estables (`GaugeKind`, `Findings`, claves de lecturas, `ExplanationKey`, `ResultKey`, motivos, manifiestos). Cada barra se muestra con texto o icono además de color.
- **Desarrollo sin red**: `FakeBaseOperationsSessionPort` + `BaseOperationsFixtures.Scenario()`/`Initial()`/`Edit()`. El fake no es autoridad: no valida, no mueve y no decide.

## Qué garantiza esta base y qué no

| Garantía | Dónde | Estado |
| --- | --- | --- |
| Rechazo de NaN/∞, negativos, capacidades, claves, enums; configuración observable (producto nominal dentro de línea, degradado fuera) | constructores | implementado y probado |
| Bandas únicas (`GaugeBands`), orden de reparación (`RepairSequence`), forma de comandos | reglas estáticas | implementado y probado |
| Snapshot coherente: sesión continua sin `Completed`, causa ↔ `Interrupted`, operaciones ↔ estaciones de fase 2, reservas ≤ repuestos, nada del futuro | constructor de `BaseOperationsSnapshot` | implementado y probado |
| Inmutabilidad de los tipos publicados | reflexión | implementado y probado |
| Fake: orden de eventos fase 2 → 3 → 4, `Joined` con estado, sin resultados inventados, abandono por tipo | `FakeBaseOperationsSessionPort` | implementado y probado (no es autoridad) |
| Modelo de equipo, acoplamiento del aire, autoridad continua, red, vistas, escena | D1–D5 | **pendiente**: regla documentada, sin implementar en esta base |

Rutas y dueños: `OWNERSHIP.md`. Plano, SHA y forma de incorporarse: `COMMON_BASE.md`.
