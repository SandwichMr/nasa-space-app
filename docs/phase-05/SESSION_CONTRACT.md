# Contratos de sesión — fase 5 (contratiempos, datos NASA y rejugabilidad)

Base común de la fase 5. **No es la jugabilidad**: fija tipos, reglas y puertos para que los efectos físicos (D1), el director, las actividades y el montaje (D2), los datos y la red (D3), la presentación (D4) y la validación (D5) encajen sin inventar interfaces. Se apoya en las fases 2–4 **sin modificar sus contratos congelados**: ningún enum serializado previo cambia.

C# puro, sin `UnityEngine` ni SDK de red, compilado solo contra `netstandard` 2.1 con `-warnaserror` (CS1591/CS1573 excluidos, como en fases anteriores). Todos los tipos publicados son inmutables; una prueba por reflexión lo comprueba.

Prefijo omitido: `Assets/_Project/`.

| Archivo | Namespace | Contenido |
| --- | --- | --- |
| `Runtime/Simulation/Contracts/Incidents/IncidentEffects.cs` | `Trainer.Simulation.Incidents` | `IncidentEffectLimits`, `PowerModifierTarget`, `PowerModifier`, `PowerModifierStatus`, `PowerCapacityState` (regla única de composición), `IPowerCapacityModifiers`, `IEquipmentIncidentTarget` |
| `Runtime/Application/Contracts/Incidents/DataProvenance.cs` | `Trainer.Application.Incidents` | `IncidentLimits`, `DataMode`, `DataAvailability`, `SourceRecordKind`, `ValueClassification`, `SourceMeasurement`, `SourceRecord`, `DatasetManifest` |
| `…/Incidents/SpaceWeather.cs` | ídem | `EpisodeMessageKind`, `MessageBasis`, `EpisodeMessage`, `SpaceWeatherEpisodeDefinition` |
| `…/Incidents/Scenario.cs` | ídem | `IncidentType`, `IncidentDefinition`, `DirectorRules`, `PowerOperationRules`, `AssignmentTemplate`, `AssignmentRules`, `UpgradeDefinition`, `SyntheticEpisodeRules`, `LivingBaseScenario`, `LivingBaseSetup` |
| `…/Incidents/State.cs` | ídem | `IncidentPhase`, `IncidentRecord`, `DirectorPhase`, `DirectorStatus`, `DirectorDecisionRecord`, `PowerFindings`, `PowerObservation`, `PowerEquipmentStatus`, `EpisodeMessageView`, `MemberShelterState`, `MemberProtection`, `SpaceWeatherStatus`, `AssignmentState`, `AssignmentRecord`, `UpgradeStatus`, `IncidentOperationProgress`, `IncidentFeedback`, `DataStatus`, `LivingBaseSnapshot` |
| `…/Incidents/Commands.cs` | ídem | `IncidentAction`, `IncidentCommand`, `IncidentReasons` |
| `…/Incidents/Ports.cs` | ídem | `ILivingBaseSessionPort`, `ILivingBaseAuthority`, `ILivingBaseIntents`, `LivingBaseViewContext`, `ILivingBaseView` |
| `…/Incidents/Development/FakeLivingBaseSessionPort.cs` | `Trainer.Application.Development` | Fake de desarrollo (no autoridad) |
| `…/Incidents/Development/LivingBaseFixtures.cs` | ídem | Escenario de fixture, episodio histórico **de fixture** y `LivingBaseSnapshotBuilder` |

## Compatibilidad con las fases 2–4

- **Contiene, no reemplaza.** `LivingBaseScenario.BaseOperations` es el `BaseOperationsScenario` completo de fase 4; `LivingBaseSnapshot.BaseOperations` es el `BaseOperationsSnapshot` de la misma época y revisión (que contiene fase 3 ⊃ fase 2). `ILivingBaseSessionPort : IBaseOperationsSessionPort`: las vistas de fases 2–4 siguen funcionando sobre el mismo puerto. Eventos de una revisión: 2 → 3 → 4 → 5.
- **Autoridad.** `ILivingBaseAuthority` no implementa ninguna interfaz de autoridad anterior. Compone la **única** `BaseOperationsAuthority` (que a su vez compone la única `ExpeditionAuthority`, roster, modelo base, EVA, equipo y reloj). No se crea otra implementación de `IOutpostSimulation`, `ICrewRoster`, `IEvaSimulation` ni `IBaseEquipmentModel`.
- **Nuevos enums** (`IncidentType`, `IncidentAction`, …) en lugar de ampliar `BaseOperationAction`, `EquipmentEffectKind`, `ResourceKind` u `OutpostOutcome`. Los efectos nuevos de D1 son entradas idempotentes separadas (`IPowerCapacityModifiers`, `IEquipmentIncidentTarget`) que reutilizan `EquipmentEffectStatus` como resultado.
- **Avería heredada.** `LivingBaseScenario` rechaza un `BaseOperationsScenario` con `DegradeAtSeconds`: el director es el único que degrada el reciclador. La reparación de fase 4 (Inspect → Isolate → Intervene → Test → Restore) es la recuperación y no se duplica.
- El episodio de fase 3 sigue desactivado en el escenario de fase 4 (`keepPhase3Episode = false`); fase 5 crea **su propia** línea de avisos por episodio y reutiliza las clases públicas `AdvisoryTimeline` y `ProtectionTracker` de fase 3 con un `ParticleEventEpisode` desplazado al reloj de la sesión.

## 1. Identidad, épocas y reproducibilidad

| Concepto | Regla |
| --- | --- |
| Época / revisión | Las de fase 2 (`LivingBaseSnapshot.Epoch/Revision`). `Restart` sube la época y conserva `LivingBaseSetup` (modo de datos y semilla). |
| Semilla y PRNG | `LivingBaseSetup.Seed` (ulong). PRNG versionado `DirectorStatus.CurrentPrngVersion = 1`. El estado del PRNG se reinicia en cada época desde `Seed` y el número de época (misma semilla + misma época + mismas entradas ⇒ mismas decisiones). |
| Reproducción | Semilla + versión del PRNG + `DirectorStatus.ConfigurationHash` (SHA-256 del texto canónico del catálogo/reglas) + manifiesto del dataset + entradas ordenadas por tiempo simulado. La semilla sola no basta si cambian las decisiones de los jugadores. |
| Incidentes | `IncidentRecord.Id` (Guid derivado de semilla, época y secuencia) y `Sequence` creciente por época. |
| Efectos | Cada efecto D1 lleva un id derivado del incidente/comando; aplicar dos veces es `AlreadyApplied`, mismo id con otro contenido `IdConflict`. Eliminar es idempotente. |
| Comandos | `IncidentCommand` comparte el espacio de ids de fases 2–4 durante toda la vida de la autoridad: duplicado con el mismo contenido y remitente ⇒ mismo resultado; otro contenido o tipo ⇒ `CommandIdConflict`. |
| Encargos | `AssignmentRecord.Sequence`; el premio se aplica una vez (`Rewarded`), con mensajes duplicados o dos jugadores. |

## 2. Ciclo de un incidente y del director

`IncidentPhase`: `Warning → Active → Intervention → Recovery → Resolved`, o `Cancelled`. No todos pasan por todos los estados. **Resolver un incidente nunca cierra la sesión** (la sesión continua de fase 4 sigue sin `Completed`).

| Tipo P0 | Warning (preparación) | Active (efecto D1) | Intervention | Recovery / Resolved |
| --- | --- | --- | --- | --- |
| `ProcessorDegradation` | síntoma visible durante `WarningSeconds` | `DegradeProcessor` (con temporizador de fallo opcional) | trabajo de reparación de fase 4 abierto | máquina Nominal + Online durante `RecoveryObservationSeconds` |
| `PowerConverterTrip` | parpadeo visible | `PowerModifier GenerationFactor = PowerFactor` | — | `ResetConverter` (tras inspección vigente) retira el modificador |
| `PowerControllerFault` | parpadeo visible | `PowerModifier DischargeFactor = PowerFactor` | `IsolatePower`: el modificador pasa a `IsolatedFactor` | `ReplacePowerPart` (consume repuesto) → `RestorePower` retira todos |
| `SpaceWeather` | mensaje `Watch` emitido | `ShelterInstruction` emitida | instrucción vigente; protección observada | `InstructionEnd` (decisión del escenario, nunca all-clear NASA) → evaluaciones |

**Director (`DirectorRules`, parámetros sintéticos de juego):**

- Evalúa solo en múltiplos de `EvaluationIntervalSeconds` de tiempo simulado en `Running`; nunca por frame. En `Ready`, `Paused` o `Interrupted` su fase publicada es `Halted` y no despacha.
- `Grace` durante `InitialGraceSeconds` de cada época; `Rest` durante `RestAfterResolutionSeconds` tras cada resolución; `Busy` con `MaxSimultaneous` incidentes abiertos.
- En `Ready`, una tirada con probabilidad `PressurePerEvaluation` propone un candidato ponderado por `Weight`. Se rechaza (y se registra el motivo en `DirectorDecisionRecord`) si: misma clave que el último; tipo ya abierto; en enfriamiento (`CooldownSeconds`); **inviable** (reciclador no nominal; batería bajo `MinBatteryFractionForPowerIncident`; avería eléctrica abierta y clima espacial, porque el refugio necesita servicio eléctrico; repuesto del controlador agotado sin pedido que lo traiga).
- Nunca resta stock por su cuenta: toda consecuencia física pasa por D1. El agua baja por consumo es emergente, no un incidente.
- No garantiza la victoria ante cualquier decisión humana; garantiza que cada incidente admitido tiene una respuesta disponible en ese momento.

## 3. Datos: modos, procedencia y tiempos

Tres modos claramente distintos (`DataMode`):

1. **Historical** (obligatorio): episodio NASA/CCMC DONKI real, congelado en `Assets/StreamingAssets/ScientificData/Phase05/`, con manifiesto (consulta exacta, fecha UTC de descarga, SHA-256, esquema, términos). Jugable sin Internet.
2. **Synthetic** (obligatorio): generador con semilla, rotulado ENTRENAMIENTO. Es también el fallback.
3. **Current**: **no implementado en fase 5**. Los constructores lo rechazan y el selector no lo ofrece.

`DataStatus` distingue `Loaded`, `FallbackSynthetic` (con `ProblemKey`: dataset ausente, corrupto, hash o esquema incompatible) y `NotRequested`. Un fallo de carga **nunca** significa «sin peligro»: el director sigue generando episodios sintéticos rotulados.

**Tiempos que no se mezclan:** tiempo del fenómeno (`SourceRecord.EventTimeUtc`), primera disponibilidad demostrable (`IssuedUtc`: hora de emisión de la notificación o `submissionTime` del catálogo; no se presenta como primera publicación universal), descarga (`RetrievedUtc`) y reloj simulado (segundos de la sesión). Los desconocidos son `null`, nunca cero.

**Cronología didáctica:** `EpisodeMessage.OffsetSeconds` comprime horas reales en segundos de juego según `TimelineNote`. Es una decisión didáctica rotulada, no una reconstrucción operativa. El inicio/fin didáctico del episodio es verdad oculta: no se publica.

**Lo que puede ver un cliente:** solo los mensajes **emitidos**, los registros que esos mensajes citan (`SpaceWeatherStatus.VisibleRecords`, comprobado por el constructor), quién recibió qué, la instrucción en vigor y el estado cualitativo de cada miembro. Nunca picos, finales, revisiones futuras ni asociaciones posteriores.

**Ciencia, no inventada:** flare (FLR), CME y SEP son fenómenos distintos y se citan por separado. No se convierte clase X, pfu ni Kp en dosis lunar; un aviso de partículas no reduce la generación solar; sin modelo de transporte, la respuesta es cualitativa (preparar, volver, refugiarse). `MessageBasis.DidacticDecision` marca las instrucciones y el fin del periodo de refugio como decisiones del escenario.

## 4. Protección y refugio

`MemberShelterState` se calcula en la autoridad con el estado confirmado de fase 3: `Protected` = zona `Shelter` + refugio preparado (tarea `prepare-shelter`) + servicio del refugio disponible + sin suministro de traje. Estar dentro de la base **no** basta. La recepción depende de la cobertura de radio de fase 3. La evaluación por miembro (`ProtectionAssessment` de fase 3: InTime, Late, NotProtected, Interrupted, NotReceived) se publica al terminar el periodo. Sin dosis, sin porcentajes de blindaje, sin consecuencias clínicas.

## 5. Encargos renovables y mejora acotada

- `AssignmentTemplate` se cumple con una **actividad real** de fase 4 (`ActivityRecord` de tipo `ScienceAssignment` con esa clave, completada después de aceptar). El coste es el real de esa actividad (energía, tiempo de tripulación, consumibles de traje si es exterior).
- Ciclo: `Offered → Accepted → Completed / Cancelled / Expired`, o `Offered → Postponed → (vuelve)`. Al cerrar, el tablero vuelve a ofertar otro **sin Reset ni Completed global**. Variedad: plantillas distintas, no repetición inmediata, enfriamiento por plantilla (`RenewCooldownSeconds`). Las plantillas exteriores no se ofertan con una instrucción de refugio en vigor.
- Premio único y acotado (créditos científicos, tope `IncidentLimits.MaxCredits`). Cancelar no devuelve recursos usados.
- **Mejora** (`UpgradeDefinition`, una sola, una vez por época): sube la recuperación nominal del reciclador (≤ `MaxUpgradedRecoveryFraction`) a cambio de créditos + un repuesto y de una **carga esencial permanente** (`AddedEssentialWatts`). No elimina el mantenimiento ni la salmuera.

## 6. Comandos y validación

`IncidentCommand(Guid id, long epoch, IncidentAction action, StationId station, long assignmentSequence, long observationSequence, long zoneSequence)`. Sin identidad, posición, cantidades, premio, severidad, época elegida ni id de fuente.

Orden de validación (el primer fallo es el motivo): `InvalidCommandId` → id repetido (mismo contenido y remitente: resultado anterior; si no, `CommandIdConflict`) → `NotJoined` → `EpochMismatch` → `ShapeError` → `SessionEnded` → `InvalidState` (exige `Running`; `CancelOperation`, `PostponeAssignment` y `CancelAssignment` también en `Paused`) → estación (`UnknownStation`, `WrongStationForAction`) → ocupación (`PlayerBusy` con tarea de fase 2, operación de fase 4 u operación de fase 5; `StationOccupied`) → `StaleZone` → `OutOfRange` → `SealedNotAllowed` → reglas de la acción (`ObservationRequired`, `StaleObservation`, `NothingToRepair`, `PowerNotIsolated`, `PowerAlreadyIsolated`, `PartNotReplaced`, `SpareUnavailable`, `UnknownAssignment`, `AssignmentNotOffered`, `AssignmentNotAccepted`, `AssignmentLimitReached`, `UpgradeInstalled`, `InsufficientCredits`).

Una decisión aceptada que no corresponde (p. ej. reiniciar un controlador dañado) se registra en `IncidentFeedback` como `NotIndicated` con su explicación; no se aplica ningún efecto.

## 7. Orden de un paso de la autoridad (un solo reloj)

La autoridad de fase 5 avanza la de fase 4 **por tramos** que terminan en el próximo evento propio (evaluación del director, fin de una ventana de aviso, emisión/vencimiento de un mensaje, fin de una operación de fase 5). Tras cada tramo:

1. Observaciones de la verdad (equipo, potencia, posiciones confirmadas).
2. Progreso de operaciones de fase 5 (alcance, traje, abandono).
3. Mensajes del episodio emitidos, entregados por cobertura, protección observada.
4. Transiciones de incidentes y aplicación/eliminación de efectos D1 (en el instante exacto del tramo).
5. Evaluación del director (si toca).
6. Encargos (vencimientos, cumplimiento por actividades nuevas, renovación).
7. Publicación (una revisión de fase 2 con todo lo anterior).

Pausa: el reloj de fase 2 no avanza, el director no evalúa y ninguna ventana corre. Recibir datos externos no avanza la sesión.

## 8. Snapshot, red y montaje

`LivingBaseSnapshot` = `BaseOperations` + `Setup` + `Data` + `Director` + `Incidents` (ventana acotada) + `Power` + `SpaceWeather` (o null) + `Assignments` + créditos + `Upgrade` + `Operations` + `Feedback` + `Decisions`. Siempre completo, nunca delta. El constructor rechaza: incidentes o encargos del futuro, ids duplicados, créditos por encima de lo ganado, registros no citados por mensajes emitidos, conocimiento eléctrico sin observación vigente, más de una operación por miembro o estación.

- **Red (D3):** cable propio `'T' 'L'` v1 sobre el mismo Unity Transport/WebSocket, que reutiliza la codificación de `'T' 'B'` para la parte de fase 4. Los cables `'T' 'S'`, `'T' 'O'`, `'T' 'E'` y `'T' 'B'` no cambian. Una escena usa uno solo. Una versión desconocida o `DataMode.Current` se rechazan explícitamente.
- **D4** se conecta solo a `ILivingBaseView` (montaje `Phase05 Presentation Mount`), `ILivingBaseIntents` y los contextos de fases 3–4 sobre el mismo puerto. Textos por claves estables. Sin fakes en producción.
- **Desarrollo sin red:** `FakeLivingBaseSessionPort` + `LivingBaseFixtures`. El fake no valida, no mueve ni decide.

## Qué garantiza esta base y qué no

| Garantía | Estado |
| --- | --- |
| Rechazo de NaN/∞, rangos, claves, enums, modo `Current`; composición única de modificadores eléctricos | implementado y probado (runner gestionado fuera de Unity) |
| Estructura de episodios (watch → una cadena → un fin), fin nunca citado como all-clear, sintético sin fuentes, histórico con dataset y hash | implementado y probado |
| Snapshot: nada del futuro, registros visibles ⊂ citados por mensajes emitidos, créditos ≤ ganados, conocimiento ↔ observación vigente | implementado y probado |
| Inmutabilidad por reflexión; fake: orden 2→3→4→5, sin resultados inventados, abandono al desconectar | implementado y probado |
| Efectos físicos, director, encargos, red, vistas, escena y build | **pendiente** en esta base: regla documentada, sin implementar |

Rutas y dueños: `OWNERSHIP.md`. Plano, SHA y verificaciones: `COMMON_BASE.md`.
