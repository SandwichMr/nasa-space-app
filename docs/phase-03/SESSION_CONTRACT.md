# Contratos de sesión — fase 3 (base común: trabajo exterior, regreso y refugio)

Base común de la fase 3. **No es la jugabilidad**: fija tipos, reglas y puertos para que la simulación EVA (D1), la autoridad y la misión (D2), la red y las transiciones (D3), la presentación (D4) y la validación (D5) encajen sin inventar interfaces. Se apoya en la fase 2 **sin modificar sus contratos congelados** (ver [Compatibilidad](#compatibilidad-con-la-fase-2)).

C# puro, sin `UnityEngine` ni SDK de red, compilado contra `netstandard` 2.1. Todos los tipos publicados son inmutables (sin setters públicos, colecciones copiadas de solo lectura); una prueba por reflexión lo comprueba.

| Archivo (bajo `Assets/_Project/`) | Namespace | Contenido |
| --- | --- | --- |
| `Runtime/Simulation/Contracts/Eva/SuitModel.cs` | `Trainer.Simulation.Eva` | `SuitService`, `SuitActivity`, `SuitServiceModel`, `SuitModelConfiguration`, `SuitLevels`, `SuitDefinition`, `EvaSupplyConfiguration`, `EvaConfiguration` |
| `…/Eva/EvaSimulation.cs` | ídem | `SuitDemand`, `EvaDemand`, `EvaTransferKind`, `EvaTransfer`, `EvaTransferStatus`, `SuitReading`, `EvaSupplyReading`, `EvaSnapshot`, `EvaLedger`, `IEvaSimulation` |
| `…/Eva/ReturnMargin.cs` | ídem | `ReturnPlan`, `MarginStatus`, `ServiceMargin`, `ReturnMarginEstimate` |
| `Runtime/Application/Contracts/Expedition/World.cs` | `Trainer.Application.Expedition` | `ExpeditionLimits`, `CrewZone`, `CrewZones`, `ZoneRegion`, `AirlockState`, `AirlockConfiguration`, `AirlockStatus`, `ShelterConfiguration`, `ShelterStatus`, `ProtectionState`, `CommsConfiguration` |
| `…/Expedition/Suits.cs` | ídem | `ChecklistSteps`, `ChecklistStep`, `Checklist`, `SuitStatus`, `SuitRack`, `SuitState`, `TaskPurpose`, `SuitRequirement`, `ExpeditionTaskRule` |
| `…/Expedition/Advisories.cs` | ídem | `AdvisoryKind`, `AdvisoryMessage`, `HistoricalSource`, `ParticleEventEpisode`, `AdvisoryValidity`, `IssuedAdvisory`, `AdvisoryReception`, `InstructionState`, `ShelterInstructionStatus`, `EventDisclosure` |
| `…/Expedition/Mission.cs` | ídem | `ExpeditionPhase`, `ExpeditionOutcome`, `ExpeditionOutcomes`, `ObjectiveRole`, `ExpeditionObjectiveRule`, `ExpeditionMissionRules`, `ExpeditionObjective`, `ReturnDecision`, `ProtectionCategory`, `ProtectionAssessment`, `IncidentKind`, `ExpeditionIncident` |
| `…/Expedition/Scenario.cs` | ídem | `ExpeditionScenario` |
| `…/Expedition/Commands.cs` | ídem | `ExpeditionAction`, `ExpeditionCommand`, `ExpeditionReasons` |
| `…/Expedition/Snapshot.cs` | ídem | `ExpeditionCrewState`, `ExpeditionSessionSnapshot` |
| `…/Expedition/Ports.cs` | ídem | `IExpeditionSessionPort`, `IExpeditionAuthority`, `IZonedCrewRoster` |
| `…/Expedition/Presentation.cs` | ídem | `IExpeditionIntents`, `ExpeditionViewContext`, `IExpeditionView`, `ExpeditionCrewFrame`, `IExpeditionCrewView` (puntos de montaje de D4) |
| `…/Expedition/Development/FakeExpeditionSessionPort.cs` | `Trainer.Application.Development` | Fake de desarrollo del puerto |
| `…/Expedition/Development/ExpeditionFixtures.cs` | ídem | Escenario sintético compacto y `ExpeditionSnapshotBuilder` |

## Compatibilidad con la fase 2

Nada de lo congelado en `docs/phase-02/OWNERSHIP.md` cambia: ni `OutpostCommand`, ni `OutpostReasons`, ni `OutpostSessionSnapshot`, ni `IOutpostSessionPort`/`IOutpostAuthority`/`ICrewRoster`, ni los tipos de soporte vital. La fase 3 los **reutiliza** y añade tipos nuevos:

- **Plano, tareas y base**: `ExpeditionScenario.Outpost` es un `OutpostScenario` completo con su significado de fase 2. El plano contiene la base, el ala EVA, la cámara de la esclusa con las **puertas abiertas** y el exterior. Las tareas de preparación, del exterior y del refugio son `TaskDefinition` de fase 2; una `ExpeditionTaskRule` les añade dónde y cómo se hacen. El modelo de base sigue siendo `IOutpostSimulation`.
- **Snapshot**: `ExpeditionSessionSnapshot` **contiene** el `OutpostSessionSnapshot` de la misma época y revisión. Así las vistas de fase 2 (avatares, entrada, controles de sesión, interior) funcionan sin cambios.
- **Puerto**: `IExpeditionSessionPort : IOutpostSessionPort`. Un `Submit(OutpostCommand)` de fase 2 se reenvía como `ExpeditionCommand.FromOutpost` con la secuencia de zona del jugador local.
- **Autoridad**: `IExpeditionAuthority` **no** implementa `IOutpostAuthority`. Las herramientas de fase 2 (descubrimiento de D5, puente de host) no pueden confundirla con la autoridad interior. Tampoco se añaden implementaciones nuevas de `ICrewRoster` ni de `IOutpostSimulation` en el ensamblado de juego: el descubrimiento de D5 de fase 2 exige una sola de cada una.
- **Roster**: `IZonedCrewRoster : ICrewRoster` lo implementa el **mismo** `CrewRoster` de fase 2, que gana dos capacidades opcionales: escotillas cerradas como obstáculos superpuestos y velocidad reducida con el traje sellado. Sin esas llamadas se comporta exactamente igual. Sigue siendo el único que mueve y la única autoridad de colisión.
- **Por qué no se extienden los tipos congelados**: añadir valores a `OutpostAction`/`ResourceKind`/`OutpostOutcome` cambiaría el cable v1 y las validaciones (`Enum.IsDefined`) de la fase 2 y rompería su conformidad. Por eso los motivos nuevos van en `ExpeditionReasons` y el resultado nuevo en `ExpeditionOutcome`, con una sola correspondencia a los campos de fase 2 (`ExpeditionOutcomes`).

## Unidades

| Magnitud | Unidad | Nota |
| --- | --- | --- |
| Posición, distancias, alcance de radio | m | plano cenital de fase 2 (X derecha, Y arriba) |
| Tiempo, duraciones, márgenes | s simulados | nunca FPS ni reloj de pared |
| O2 del traje, agua de refrigeración, recarga de O2, gas de reposición | kg | |
| Batería del traje | J | carga de herramienta en W |
| Consumo por actividad | kg/s o W | estrictamente positivo |
| Potencias de la base (bomba, cargador, ventilación) | W | carga de servicio de fase 2 |

## Zonas

`CrewZone`: `Interior`, `Shelter` (base presurizada), `Airlock` (cámara) y `Exterior` (superficie). La zona la deriva **solo la autoridad** de la posición **confirmada**: es la primera `ZoneRegion` que contiene el centro del miembro, así que las regiones pequeñas van primero. Ningún cliente la envía. D5 valida que toda posición admisible tenga zona. Por convención, el vano de la escotilla interior es `Interior` y el de la exterior, `Exterior`.

Cada cambio de zona confirmado incrementa `ExpeditionCrewState.ZoneSequence`, también la vuelta al punto de aparición en `Restart`. La secuencia no se reinicia nunca para un `PlayerId`.

## Esclusa

`AirlockConfiguration`: cámara, dos rectángulos de escotilla, dos paneles (estaciones del plano alcanzables desde ambos lados), capacidad, duraciones, `PumpPowerWatts`, `MakeupGasPerCycleKilograms` y `ExteriorEntry` (destino de toda ruta de regreso).

| Estado | Escotilla interior | Escotilla exterior | Ocupantes |
| --- | --- | --- | --- |
| `PressurizedInnerOpen` | abierta | cerrada | sin traje sellado (pueden llevar traje) |
| `Depressurizing` | cerrada | cerrada | con suministro del traje |
| `DepressurizedOuterOpen` | cerrada | abierta | con suministro del traje |
| `Repressurizing` | cerrada | cerrada | con suministro del traje hasta que termina |

- Una escotilla **cerrada** bloquea el movimiento porque la autoridad añade su rectángulo a `IZonedCrewRoster.SetObstacleOverlay`. Una escotilla solo se cierra si ningún disco la toca (`IsClear`); si no, el ciclo se rechaza con `AirlockObstructed`. Nadie atraviesa una escotilla cerrada ni queda cortado por ella.
- `CycleAirlock` (desde un panel) inicia el ciclo siguiente. El sentido lo fija el estado, nunca el cliente. Al empezar una despresurización, todos los ocupantes pasan a `Sealed`, con suministro del traje. Al terminar una represurización, los ocupantes vuelven a `Worn` (consumen de la base) y su checklist vuelve a `Don`: otra salida exige recargar y revisar.
- Un ciclo avanza solo mientras la carga de servicio de la base está **plenamente servida**, igual que las tareas de soporte de fase 2. Si no, se publica `Stalled`. No hay aborto de ciclo en esta base.
- **Costes**: la bomba consume energía de la base durante el ciclo (D1 lo aplica en `Advance`). Cada **represurización** toma una sola vez, al empezar, `MakeupGasPerCycleKilograms` del suministro de reposición (`EvaTransfer UseMakeupGas`, con el id del comando del ciclo). Sin gas, la represurización se rechaza con `InsufficientResources` y el margen pasa a `ReturnUnavailable`.
- `AirlockStatus.CycleSequence` cuenta los ciclos iniciados en la época. `CycleAirlock` lleva el valor que vio el remitente: si empezó otro ciclo después, se rechaza con `StaleAirlockState`. Así una petición pensada para salir nunca ejecuta el ciclo de entrada.
- La capacidad limita el **ciclado**, no la entrada a pie.

## Trajes, checklist y servicios

Hay tres servicios, cada uno con su unidad: `Oxygen` (kg), `Battery` (J) y `Feedwater` (kg). Nunca se promedian ni se convierten entre sí. `SuitServiceModel` define la capacidad, la **reserva** (la parte que un plan no debe usar; valor de escenario, no estándar de seguridad) y el consumo por actividad: `Rest`, `Walk` y `Work`. La carga de herramienta de una tarea exterior se suma solo a la batería (W).

- **Quién consume dónde (sin doble conteo)**: un miembro con el traje `Sealed` consume del traje y **no** cuenta en `OutpostDemand.CrewAboard`; cualquier otro consume de la base. La autoridad decide la actividad con el estado confirmado: tarea exterior en curso = `Work`, caminata confirmada en el paso = `Walk` y el resto, `Rest`.
- **Checklist educativo** (`ChecklistSteps`): primero `Don`, después `ChargeBattery` y `FillConsumables` en cualquier orden, y `SealCheck` al final. No es un protocolo EVA real ni una receta de prerrespiración. Un traje está listo (`IsReady`) con los cuatro pasos y puesto (`Worn`).
- **Estados**: `Stowed` (en su soporte), `Worn` (puesto, sin suministro propio), `Sealed` (suministro propio) y `Abandoned` (su portador se desconectó estando sellado: nadie dentro, no consume, no se teletransporta ni se reutiliza hasta `Restart`).
- **Costes de preparación**:
  - `ChargeBattery` es una tarea de soporte con potencia de base. La base gasta la energía mientras progresa. Al completarse, el traje almacena `ChargeEfficiency × PowerWatts × Duration` J hasta su capacidad; lo que no cabe se registra como no almacenado (calor). Si se cancela, no hay devolución.
  - `FillConsumables`: al completarse, el O2 que falta sale del **suministro de recarga EVA** (`FillOxygen`; el almacén de O2 de cabina de fase 2 no es un stock publicado y no se toca). El agua que falta sale del agua potable de la base con una `ResourceTransfer` de fase 2 (−X kg potable, salida por frontera +X kg) y después entra al traje (`FillFeedwater`), con el mismo id.
- **Transferencias**: `EvaTransfer` es idempotente por id: una vez por `Reset`; si se repite, `AlreadyApplied`; mismo id con otro contenido, `IdConflict`. Es todo o nada, salvo la carga, cuyo balance es explícito: almacenado + no almacenado = entregado. **No existe** transferencia de O2 ni de batería entre trajes.

```csharp
public interface IEvaSimulation                  // D1 implementa; solo la autoridad la llama
{
    void Reset(EvaConfiguration configuration);
    EvaSnapshot Snapshot { get; }
    EvaLedger Ledger { get; }                    // diagnóstico: consumido, llenado, no almacenado, no servido
    void Advance(double elapsedSeconds, EvaDemand demand);   // solo consumen los trajes listados (sellados)
    double SecondsUntilServiceEvent(EvaDemand demand);       // cruce de reserva o cero: la autoridad corta su paso ahí
    EvaTransferStatus Apply(EvaTransfer transfer);
}
```

## Margen de regreso

Por cada miembro con suministro del traje, la autoridad construye un `ReturnPlan` conservador desde el estado confirmado:

- **Recorrido**: la ruta por el plano hasta `ExteriorEntry`, a la velocidad con traje.
- **Espera**: el tiempo hasta que la esclusa pueda abrir su escotilla exterior.
- **Ingreso**: la represurización, con el miembro todavía con suministro del traje.
- **Accesibilidad**: si la esclusa no puede completar un ingreso, el plan no es alcanzable.

D1 calcula el margen de cada servicio:

```
utilizable = nivel − reserva
necesario  = consumo_Walk × recorrido + consumo_Rest × (espera + ingreso)
margen     = (utilizable − necesario) / consumo_actual          (s; consumo_actual incluye la herramienta)
```

- **Margen del miembro**: el **menor** de los servicios. `LimitingService` es el primero que se queda corto; empata el orden declarado. Nunca es una media (el constructor lo rechaza).
- **Estado**: `Sufficient` si el margen es ≥ 0 (el límite cuenta como suficiente), `Insufficient` si es < 0, `ReturnUnavailable` si el plan no es alcanzable, y `NotApplicable` sin suministro del traje.
- **Equipo**: el **peor** margen válido entre los sellados (`TryGetTeamWorstMargin`). La salida no termina hasta que vuelven **todos** los que salieron, no solo el que lleva más recursos.
- **Validez**: el margen vale solo para el estado confirmado en `ComputedAtSeconds`. El cliente no lo extrapola.

## Avisos de partículas: evento, información, recepción y vigencia

| Concepto | Tipo | Se publica | Se usa para evaluar |
| --- | --- | --- | --- |
| **Evento** (verdad del escenario) | `ParticleEventEpisode.EventOnsetSeconds/EventEndSeconds` | solo al terminar la sesión (`EventDisclosure`) | **nunca** |
| **Información disponible** | `AdvisoryMessage`, emitidos en `IssuedAtSeconds` | solo cuando ya se han emitido (`IssuedAdvisory`) | sí |
| **Recepción** | `AdvisoryReception` por miembro y mensaje | sí | sí |
| **Vigencia** | `AdvisoryValidity`: `Current`, `Superseded` o `Expired` | sí | sí |
| **Decisión** | acciones: cancelar, `DeclareReturn`, ir al refugio | `ReturnDecision`, con lo que sabía quien declaró | sí |

- **Mensajes**:
  - `ShelterInstruction`: estar protegido antes de `ShelterBySeconds`.
  - `InstructionUpdate`: sustituye a otro mensaje; la instrucción sigue vigente.
  - `InstructionEnd`: fin **explícito** del periodo de refugio de este escenario. No es un "todo despejado" universal, y nunca se deduce del fin de una fulguración ni de una lectura.
- **Instrucción** (`ShelterInstructionStatus`):
  - `None`: no hay información. No significa "seguro".
  - `InForce`: el último mensaje está vigente.
  - `InForceWithoutCurrentInformation`: el último mensaje caducó sin sustituto. La instrucción **no** se levanta: la ausencia de noticias no es un fin.
  - `Ended`.
- **Recepción**: un mensaje emitido y vigente llega a quien esté dentro de la base o la esclusa, o fuera a menos de `CommsConfiguration.ExteriorRangeMeters` de la antena (distancia en línea recta; el relieve no se modela). Quien está fuera de cobertura lo recibe al entrar en ella, si sigue vigente. Si llega después de su `ShelterBy`, se marca `AfterShelterBy`. Quien se incorpora tarde recibe al unirse los mensajes vigentes.
- **Evaluación sin información futura**: `ProtectionAssessment` se calcula con los mensajes emitidos y la recepción de cada miembro. Quien no recibió la instrucción (`NotReceived`) no se juzga como si hubiera decidido mal.
- **Episodio por defecto**: autorado y **sintético**, reproducible y sin Internet. Uno histórico exige `HistoricalSource` (fuente, referencia, timestamps UTC, magnitud y unidad originales, lugar de observación y contexto). Ningún dato de clase X, pfu o detector orbital se convierte en dosis humana lunar, y un aviso **no** reduce la producción solar.

## Refugio y protección efectiva

El área del refugio es la zona `Shelter`. Un miembro está `Protected` solo si se cumplen tres condiciones reales a la vez:

1. Su centro confirmado está en `Shelter`.
2. El refugio está preparado: alguien completó `ShelterConfiguration.PrepareTask` en su panel.
3. La ventilación del refugio estuvo plenamente servida en el último paso. Son `VentilationWatts` de carga de servicio de la base mientras está preparado y ocupado.

Si falla alguna, el estado es `NotInShelter`, `ShelterNotPrepared` o `ShelterServiceUnavailable`. Sin instrucción vigente, `NotRequired`. No se modela ningún porcentaje de blindaje ni ninguna dosis.

## Misión, fin y resultado

`ExpeditionMissionRules` tiene una duración y objetivos:

- exactamente uno `Safety`: vuelven todos los que salieron;
- al menos uno `Primary`: ciencia cercana;
- y `Optional`: ciencia con un coste que obliga a valorar el regreso.

`ExpeditionPhase` pasa por `Preparing`, `Underway` (alguien estuvo sellado), `Returning` (después de `DeclareReturn`, no empieza más trabajo exterior) y `Closed`.

**Condiciones de fin** (la autoridad las evalúa tras cada paso, en este orden):

| # | Condición | `ExpeditionOutcome` | Estado / resultado de fase 2 |
| --- | --- | --- | --- |
| 1 | Carga esencial de la base no servida | `EssentialServiceLost` | Interrupted / EssentialServiceLost |
| 2 | Un servicio de un traje sellado llega a 0 | `SuitServiceDepleted` (sin consecuencias médicas: la simulación se detiene) | Interrupted / EssentialServiceLost |
| 3 | Se emitió el fin de una instrucción | `ShelteredInTime`, `ShelteredLate`, `ProtectionIncomplete` o `AdvisoryNotReceived` | Completed / ObjectiveAchieved (las dos primeras) o TimeExpired |
| 4 | Sin instrucción vigente, alguien salió, ya no queda nadie sellado y (`Primary` cumplido o regreso declarado) | `ScienceReturned` o `ReturnedWithoutScience` | Completed / ObjectiveAchieved |
| 5 | Fin de la duración | `TimeExpired` | Completed / TimeExpired |

Volver sin ciencia tras declarar el regreso, o refugiarse a tiempo, son **resultados correctos**, no derrotas. Quien vuelve sin declarar el regreso ni cumplir el primario puede volver a preparar el traje y salir otra vez.

## Comandos

`ExpeditionCommand(Guid id, long epoch, ExpeditionAction action, StationId station, TaskTypeId task, long zoneSequence = −1, long airlockCycle = −1)`. Las acciones son `Pause`, `Resume`, `Restart`, `StartTask`, `CancelTask`, `CycleAirlock(panel)` y `DeclareReturn`. Como en fase 2, no llevan identidad ni posición. Marcas de vigencia (−1 = no indicada):

- `ZoneSequence` va solo en `StartTask` y `CycleAirlock`. Si el remitente cambió de zona después, se rechaza con `StaleZone`.
- `AirlockCycle` va solo en `CycleAirlock`. Si empezó otro ciclo después, `StaleAirlockState`.

**Orden de validación** (el primer fallo es el motivo):

1. `InvalidCommandId`.
2. Id repetido: si coinciden remitente y contenido, se reproduce el resultado guardado; si no, `CommandIdConflict`.
3. `NotJoined`, `EpochMismatch`, `UnknownAction`, `InvalidActionValue` (`ShapeError`).
4. `Restart`: se acepta siempre.
5. `Pause` y `Resume`: como en fase 2.
6. `StartTask`: `SessionEnded` → `InvalidState` (exige `Running`) → `UnknownStation` → `UnknownTaskType` → `TaskNotOfferedAtStation` → `StationUnavailable` → `PlayerBusy` → `StationOccupied` → `StaleZone` → `OutOfRange` → `WrongZone` → reglas de traje y checklist (`SealedNotAllowed`, `AlreadySuited`, `SuitNotOnRack`, `SuitRequired`, `ChecklistOutOfOrder`, `ChecklistStepDone`) → `ReturnDeclared` (trabajo exterior tras declarar el regreso) → `InsufficientResources` (stock de la base, recarga EVA o gas).
7. `CancelTask`: como en fase 2.
8. `CycleAirlock`: `SessionEnded` → `InvalidState` (exige `Running`) → `UnknownStation` → `NotAnAirlockPanel` → `StaleZone` → `StaleAirlockState` → `OutOfRange` (respecto al panel) → `AirlockBusy` → `AirlockObstructed` → `AirlockOverCapacity` → al despresurizar con ocupantes: `SuitNotReady` → `EgressBlockedByInstruction` → al represurizar: `InsufficientResources` (gas de reposición).
9. `DeclareReturn`: `SessionEnded` → `InvalidState` (exige `Running` o `Paused`) → `NoExpeditionUnderway` (nadie sellado) → `ReturnAlreadyDeclared`.

Si dos jugadores piden la esclusa a la vez, la autoridad sigue el orden de llegada. El segundo recibe `StaleAirlockState` (si indicó el ciclo que vio) o `AirlockBusy`.

**Códigos**: se reutilizan los de `OutpostReasons` con su significado, y los nuevos están en `ExpeditionReasons`. Los códigos solo de cliente siguen siendo `NotConnected`, `ConnectionLost` y `LocalDisconnect`.

## Snapshot autoritativo

`ExpeditionSessionSnapshot` = `Outpost` (fase 2, misma época y revisión) + `Crew` (`ExpeditionCrewState` en el mismo orden) + `Suits` + `Airlock` + `Shelter` + `Supply` + `Advisories` + `Receptions` + `Instruction` + `Phase` + `Decision` + `Outcome` + `Objectives` + `Assessments` + `Incidents` + `Disclosure`. Siempre es estado **completo**, nunca un delta. Se ordena por `(Epoch, Revision)` de `Outpost`.

El constructor **rechaza**:

- listas de tripulación distintas de las de fase 2;
- un traje puesto por un ausente o por otro;
- suministro del traje fuera de sus reglas: fuera siempre sellado, en la base nunca;
- ocupantes de la esclusa que no están en la cámara;
- una esclusa sin presión con alguien sin suministro del traje;
- mensajes no emitidos todavía;
- recepciones de mensajes desconocidos o futuras;
- una revelación del evento antes del final;
- un resultado que no coincide con el estado de fase 2.

Los objetivos de fase 2 de `Outpost.Objectives` son una copia de compatibilidad de `Objectives`: `Required` = papel `Safety`.

## Puertos

```csharp
public interface IExpeditionSessionPort : IOutpostSessionPort      // un participante
{
    bool TryGetExpedition(out ExpeditionSessionSnapshot snapshot);  // TryGetSnapshot == Expedition.Outpost
    void Submit(ExpeditionCommand command);                         // resultado por CommandCompleted (fase 2)
    event Action<ExpeditionSessionSnapshot> ExpeditionChanged;      // después de SnapshotChanged, misma revisión
    event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
}

public interface IExpeditionAuthority                              // D2 implementa; adaptadores de host de D3
{
    ExpeditionScenario Scenario { get; }
    OutpostMembership Join();                                       // aparición dentro de la base, sin traje
    bool Leave(PlayerId player);                                    // sellado → traje Abandoned + incidente
    void Submit(PlayerId sender, ExpeditionCommand command);
    MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);
    bool TryGetSnapshot(out ExpeditionSessionSnapshot snapshot);
    event Action<ExpeditionSessionSnapshot> SnapshotChanged;
    event Action<PlayerId, CommandResult> CommandCompleted;        // solo al remitente
}

public interface IZonedCrewRoster : ICrewRoster                    // D3: el mismo CrewRoster de fase 2
{
    IReadOnlyList<BaseRect> ObstacleOverlay { get; }
    bool IsClear(BaseRect area);
    void SetObstacleOverlay(IEnumerable<BaseRect> rectangles);     // lanza si alguien dejaría de ser admisible
    bool SetSpeedFactor(PlayerId player, double factor);            // (0, 1]; 1 al unirse y tras Restart
    double SpeedFactorOf(PlayerId player);
}
```

Siguen las reglas de fase 2:

- un solo hilo; el estado se confirma antes de emitir eventos;
- la identidad solo viene de `Join`;
- los resultados van solo al remitente;
- los comandos se deduplican durante toda la vida de la autoridad;
- el movimiento es una dirección sostenida, sin posiciones del cliente;
- `Restart` sube la época;
- sin fantasmas ni periodo de gracia.

En la desconexión de un miembro sellado:

- su tarea termina con `PerformerLeft`;
- su traje queda `Abandoned` (no consume);
- se registra `DisconnectedWhileSealed`;
- deja de ocupar la esclusa.

Al reconectar recibe un `PlayerId` nuevo dentro de la base, sin traje.

## Costes: quién, cuándo y qué pasa al cancelar

| Coste | Lo aplica | Cuándo | Al cancelar o abandonar |
| --- | --- | --- | --- |
| Demanda de la tripulación en la base | D1 base (`Advance`, `CrewAboard` = no sellados) | cada paso | — |
| Consumo del traje sellado | D1 EVA (`Advance`, `EvaDemand` solo con sellados) | cada paso | — (lo consumido no vuelve) |
| Energía del cargador, la bomba, la ventilación y las tareas de base | D1 base (carga de servicio) | mientras progresa o funciona | no se devuelve |
| Carga de la batería del traje | D1 EVA (`ChargeBattery`) | una vez, al completarse | nada |
| O2 desde la recarga EVA | D1 EVA (`FillOxygen`) | una vez, al completarse | nada |
| Agua: potable de la base → traje | D1 base (`ResourceTransfer`), después D1 EVA (`FillFeedwater`), mismo id | una vez, al completarse | nada |
| Gas de reposición | D1 EVA (`UseMakeupGas`) | una vez, al empezar cada represurización | — (el ciclo no se aborta) |
| Decidir zona, sellado, actividad, progreso, ciclos, recepción, protección, fin | **D2** (única autoridad) | — | — |

D2 **nunca** suma ni resta stock. D1 **nunca** decide quién está fuera ni qué progresa. Cada id se aplica una sola vez por `Reset` en el modelo correspondiente.

## Red (D3)

D3 extiende el transporte existente: Unity Transport 2.7.4 sobre WebSocket, con los mismos `UnityTransportHost`/`UnityTransportClient`. Usa un cable de fase 3 propio, `'T' 'E'` v1, que reutiliza la codificación de fase 2 para movimiento, pertenencia, resultados y la parte `Outpost` del snapshot. Los cables `'T' 'S'` (fase 1) y `'T' 'O'` (fase 2) no cambian. Una escena usa uno solo.

## Conexión de D4

D4 se conecta **solo** a estos puntos, nunca a la autoridad ni a la red:

- `IExpeditionView` (HUD, superposiciones del mundo, pantalla de resultado): componentes bajo el montaje de presentación de la escena. D2 llama a `Bind(ExpeditionViewContext)` con el puerto, el escenario y `IExpeditionIntents`, y a `Unbind()` antes de cambiar de puerto.
- `IExpeditionIntents` (lo implementa D3): `Interact()` (la "E"), `RequestStart`, `RequestCancel`, `RequestCycle`, `RequestDeclareReturn`, `HasPending`, `LastReason` y `Answered`. Un panel que capture el teclado llama a `context.BlockMovement(this)` y después a `UnblockMovement(this)`.
- `IExpeditionCrewView` en el prefab del astronauta, junto al `ICrewAvatarView` de fase 2. Recibe `ExpeditionCrewFrame`: zona, traje, suministro, margen, protección, aviso recibido y estación de trabajo. Se construye con `ExpeditionCrewFrame.TryBuild`.
- **Geometría**: se dibuja desde `ExpeditionScenario.Layout`/`Zones`/`Airlock` en tiempo de ejecución, como en fase 2. Ningún `Collider2D`/`Rigidbody2D` decide posiciones.
- **Textos**: se buscan por claves estables: estaciones, tareas, trajes, `TextKey` de los mensajes, objetivos y motivos.
- **Desarrollo sin red**: `FakeExpeditionSessionPort` + `ExpeditionFixtures.Scenario()`/`Initial()`/`Edit()`. El fake no es autoridad: no valida nada, no mueve a nadie y no decide resultados.

Rutas, archivos y escena de cada D: `OWNERSHIP.md`. Plano del escenario y SHA de esta base: `COMMON_BASE.md`.

## Qué garantiza esta base y qué no

| Garantía | Dónde | Estado |
| --- | --- | --- |
| Rechazo de NaN/∞, negativos, capacidad, claves, enums; margen = mínimo (nunca media); estados de margen coherentes | constructores | implementado y probado |
| Snapshot coherente: suministro del traje, ocupantes, sin información futura, revelación solo al final, resultado ↔ fase 2 | constructor de `ExpeditionSessionSnapshot` | implementado y probado |
| Inmutabilidad de los tipos publicados | reflexión | implementado y probado |
| Fake: orden de eventos fase 2 → fase 3, `Joined` con estado, sin resultados inventados, abandono por tipo, marca de zona | `FakeExpeditionSessionPort` | implementado y probado (no es autoridad) |
| Modelo EVA, margen, autoridad, esclusa, avisos, refugio, red, escena | D1–D5 | **pendiente**: regla documentada, sin implementar en esta base |
