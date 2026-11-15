# HANDOFF — Fase 2, D1: recursos, cabina y consumos

## Contexto

- Fase: `docs/roadmap/02_BASE_MANAGEMENT_PROTOTYPE.md`, sección D1. Contratos: `docs/phase-02/SESSION_CONTRACT.md`.
- Base: `95caa315527200bbbbcbb73fd654321d7992552e` (base común de fase 2). Rama: `feature/phase-02/d01-recursos`.
- Sin merge ni push. Fase **no** completada: falta la integración en `integration/phase-02`.
- Contratos congelados usados **sin modificar**: `IOutpostSimulation`, `OutpostConfiguration`, `OutpostDemand`, `OutpostSimulationSnapshot`, `LifeSupportSnapshot`, `WaterStock`, `FoodStock`, `CabinConfiguration`, `CabinAirObservation`, `GasReading`, `ResourceEffect`, `ResourceTransfer`, `ResourceTransferStatus`, `PowerConfiguration`, `PowerSnapshot`.
- `PowerSystem`, `PowerState`, `SimulationClock` y sus pruebas de fase 1: sin cambios.

## Archivos (lista registrada antes de editar, todos en rutas D1)

| Archivo (bajo `Assets/_Project/`) | Contenido |
| --- | --- |
| `Runtime/Simulation/LifeSupport/OutpostBaseSimulation.cs` | Implementación de `IOutpostSimulation` |
| `Runtime/Simulation/LifeSupport/OutpostPowerDispatch.cs` | Reparto eléctrico esencial → servicio → ciencia (`internal`) |
| `Runtime/Simulation/LifeSupport/CabinGas.cs` | Conversión masa ↔ kPa (gas ideal, una zona) |
| `Runtime/Simulation/LifeSupport/AirRevitalizationParameters.cs` | Suministro de O2 y retirada de CO2 |
| `Runtime/Simulation/LifeSupport/OutpostSimulationParameters.cs` | Parámetros del modelo + conjunto `SyntheticTraining()` |
| `Runtime/Simulation/LifeSupport/LifeSupportLedger.cs` | Libro mayor de masas, para diagnóstico |
| `Runtime/Simulation/Crew/CrewMetabolicDemand.cs` | Demanda por tripulante |
| `Tests/EditMode/Simulation/Phase02/Editor/*.cs` | Fixture analítica + 29 pruebas (energía 7, recursos 12, cabina 10) |
| `.meta` de los anteriores y de las carpetas `LifeSupport`, `Phase02`, `Phase02/Editor` | GUID aleatorios, sin duplicados en `Assets/` |

## Unidades

s simulados, J, W, kg, kPa, m³, K. Tasas de masa en kg/s; `CrewMetabolicDemand.FromPerCrewDay` convierte de kg/(tripulante·día) con 86 400 s/día.

## Modelo

**Frontera.** El sistema seguido incluye agua potable, agua sin tratar, comida almacenada, O2 y CO2 de la cabina y el depósito de O2. La tripulación es frontera: lo que toma (O2, agua potable, comida) sale del sistema y lo que devuelve (CO2 a la cabina, agua a "sin tratar") entra. El CO2 retirado sale por la frontera, igual que `BoundaryOutflowKilograms` de los efectos aplicados. Cierre comprobado en las pruebas:

```
seguido(t) = seguido(0) − O2 respirado − agua bebida − comida ingerida
           + CO2 exhalado + agua devuelta a sin tratar − CO2 retirado − salida de transferencias
```

**Cabina.** Es **una zona bien mezclada** (simplificación declarada), con V y T constantes de `CabinConfiguration` y p = (m/M)·R·T/V. R = 8,314462618 J/(mol·K), M(O2) = 0,0319988 kg/mol y M(CO2) = 0,0440095 kg/mol. No se modelan presión total, otros gases, humedad, fugas ni temperatura variable.

- **Suministro de O2** (dispositivo propio): aporta hasta `MaxOxygenSupplyKilogramsPerSecond` desde un depósito finito (`OxygenStoreKilograms`) mientras p(O2) esté por debajo de su valor **inicial declarado**. Al alcanzarlo, sigue el consumo hasta ese máximo.
- **Retirada de CO2** (otro dispositivo): extrae hasta `MaxCarbonDioxideRemovalKilogramsPerSecond` mientras p(CO2) esté por encima de su valor inicial. Al alcanzarlo, sigue la producción. Nunca baja de ese valor.
- Suministrar O2 no retira CO2 y retirar CO2 no aporta O2; las pruebas lo verifican en los dos sentidos.
- La única consigna es la composición inicial del escenario. **No hay umbrales de alarma, clínicos ni de seguridad.**
- Ambos servicios y los instrumentos forman parte de la **carga esencial de fase 1** (sin vatios nuevos). Solo funcionan mientras la carga esencial esté plenamente servida.
- Si la cabina se queda sin O2, la respiración se limita a lo que entra y la exhalación de CO2 baja en la misma proporción. Es contabilidad de masa, no fisiología.
- **Lecturas.** Son `Valid` (valor y hora actuales) mientras la carga esencial esté servida en el punto publicado. Sin energía pasan a `Stale`, con el último valor y su hora, o a `Unavailable` si nunca hubo medida. Una lectura ausente nunca es 0. Un 0 medido es `Valid`.

**Agua y comida.**

- Cada tripulante bebe agua potable y come comida a tasa constante. Con el stock a 0, el consumo se detiene y la demanda no servida se acumula en el libro mayor (`UnservedPotableWaterKilograms`, `UnservedFoodKilograms`). Los stocks nunca son negativos.
- La devolución a "sin tratar" solo ocurre mientras se bebe y su tasa **no puede superar** la de agua bebida: el constructor lo rechaza, así que no se crea agua de la nada. Si "sin tratar" está llena, lo que no cabe se registra (`UntreatedReturnNotStoredKilograms`) y no se almacena.
- **No hay conversión de sin tratar a potable dentro de `Advance`.** Solo ocurre con el efecto de una tarea completada, mediante `Apply`, con su balance explícito.
- La humedad de la comida forma parte de `StoredFood` y no se cuenta como agua.

**Energía.** Se aplican las reglas de `PowerSystem` (generación, batería con límites de carga y descarga, recorte, sin pérdidas) con un canal más. Prioridad: **1) esencial, 2) servicio (`ServiceLoadWatts`), 3) ciencia.** Sin carga de servicio, el resultado es idéntico a `PowerSystem`; una prueba lo compara paso a paso con 5 configuraciones.

**Paso exacto.** Dentro de `Advance` la demanda es constante y cada tasa es constante por tramos. El intervalo se parte en el instante exacto de cada evento: batería vacía o llena, stock vacío o lleno, depósito de O2 vacío, gas que alcanza su consigna o llega a 0. El resultado no depende del tamaño del paso (probado con 1×300 s, 300×1 s y pasos irregulares). Se trabaja sobre una copia y se confirma al final, así que una entrada inválida lanza la excepción sin cambiar el estado.

## Costos: una sola aplicación (conforme al contrato, sin cambios)

| Costo | Lo aplica | Dónde |
| --- | --- | --- |
| Demanda continua de la tripulación (O2, CO2, agua, comida) | D1 | `Advance`, con `demand.CrewAboard` |
| Energía de las tareas (servicio y ciencia) y carga esencial | D1 | `Advance`, con `ServiceLoadWatts` y `ScienceRequested` |
| Efecto de material de una tarea | D1 | `Apply`, todo o nada, una vez por id y por `Reset` |
| Progreso, completado, cuándo llamar a `Apply`, cancelaciones | D2 | autoridad de tareas |

D2 no descuenta stock, energía ni tiempo de persona por su cuenta. D1 no decide tareas ni ocupación. Cancelar no devuelve la energía ya gastada; esa energía nunca se cobra aparte, porque se consume en `Advance` mientras la tarea pide carga. `Apply` con un id ya aplicado devuelve `AlreadyApplied` (con el mismo efecto) o `IdConflict` (con otro), y no cambia nada. Un rechazo (`InsufficientStock` o `ExceedsCapacity`) no registra el id. Si coinciden ambos fallos, el orden de comprobación es: stock y después capacidad.

## API para D2 (integración)

```csharp
var simulation = new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()); // solo en la autoridad
simulation.Reset(scenario.Simulation);                    // inicio y Restart
var demand = new OutpostDemand(crewAboard: presentes, scienceRequested: hayCiencia, serviceLoadWatts: ΣW Support);
double step = Math.Min(remaining, simulation.SecondsUntilServiceChange(demand)); // si es 0: esencial perdido
simulation.Advance(step, demand);                          // solo en Running; en pausa no se llama
double supportWork = simulation.LastFullyServedServiceSeconds;   // progreso de Support (con 0 W: tiempo transcurrido)
double scienceWork = simulation.LastFullyServedScienceSeconds;
ResourceTransferStatus status = simulation.Apply(new ResourceTransfer(startTaskCommandId, task.CompletionEffect));
OutpostSimulationSnapshot snapshot = simulation.Snapshot;  // → OutpostSessionSnapshot
```

- `SecondsUntilServiceChange` solo considera eventos eléctricos, que son los que cambian el servicio de las tareas. Los eventos de agua, comida y cabina se resuelven dentro de `Advance` y no obligan a D2 a partir el paso.
- La comprobación previa `InsufficientResources` de `StartTask` se hace sobre `Snapshot.LifeSupport`. Como la tripulación consume entretanto, `Apply` puede fallar al completar → `TaskEndReason.InsufficientResources`. Es lo previsto por el contrato.
- `Ledger`, `CabinOxygenKilograms` y `CabinCarbonDioxideKilograms` son de la clase concreta: diagnóstico para la autoridad y para D5. **No** forman parte del snapshot publicado; D4 no debe depender de ellos.
- Excepciones: si se usa antes de `Reset`, `InvalidOperationException`; argumentos nulos, `ArgumentNullException`; un `dt` NaN, infinito, negativo o que desborde el reloj, `ArgumentOutOfRangeException`, sin cambios de estado. `Advance(0, …)` no hace nada, tampoco registra la demanda.

## Supuestos y procedencia

- **Todos los valores numéricos son sintéticos.** `SyntheticTraining()` usa, por tripulante y día, 0,8 kg de O2, 1,0 kg de CO2, 2,5 kg de agua potable, 1,5 kg de comida y 1,5 kg de devolución a "sin tratar". Aire: depósito de 20 kg, suministro de hasta 6,4 kg/día y retirada de hasta 8 kg/día. Llevan la etiqueta `SINTÉTICO`; **no** se verificaron contra BVAD ni contra otra fuente. Solo se eligieron con un orden de magnitud plausible.
- Las fixtures de prueba (`LifeSupportAnalyticFixtures`) usan g/s redondos para calcular resultados exactos. No representan un escenario.
- No hay ninguna tasa de recuperación genérica (ni 98 % ni otra), ningún umbral médico ni ninguna consecuencia clínica.
- **Misión corta, sin crisis alimentaria.** Con `SyntheticTraining`, 4 tripulantes durante 30 min simulados consumen 0,125 kg de los 15 kg de comida, más del 99 % restante (probado). El interés de juego del agua viene de las tareas, por ejemplo tratar agua, no del consumo continuo.

## Pruebas realmente ejecutadas

**No se ejecutó Unity** (ni importación ni Test Runner). Fuera de Unity se usaron el Roslyn 4.3.1 y el runtime .NET 6.0.21 del Editor 6000.3.28f1, el NUnit real de `com.unity.ext.nunit` y un runner por reflexión temporal, fuera del repositorio.

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` (38 fuentes) **solo** contra `netstandard.dll`, `-warnaserror+`, `-doc` | exit 0. Se silenciaron CS1591 (falta doc XML) y CS1573, que ya existían en código de fase 1 (`PowerSnapshot`, `MissionSession`) |
| Pruebas nuevas D1 (`Phase02/Editor`) | 29/29 (energía 7, recursos 12, cabina 10) |
| Regresión pura: `PowerSystemTests` 30, `SimulationClockTests` 5, contratos de fase 2 34, `MissionSessionTests` 21, `MissionOutcomeEvaluatorTests` 7, `FakeSessionPortTests` 6 | 103/103 (total con las nuevas: 132/132) |
| Sensibilidad: 8 mutantes sobre una copia (deduplicación de `Apply`, prioridad servicio/ciencia, aire sin energía, devolución sin tope, CO2 bajo la consigna, servicio parcial contado como pleno, beber de un stock vacío, sin evento de depósito de O2 vacío) | los 8 detectados (1–3 fallos cada uno) |

No se ejecutaron `PhaseOneRegressionTests` ni `MissionScenarioTests` (dependen de UnityEngine) ni la compilación completa de `Assembly-CSharp`. Mis archivos no usan UnityEngine ni los referencia ningún código Unity.

**Pendiente en Unity:** importar y aceptar los `.meta`, ejecutar el Test Runner EditMode sobre `Tests/EditMode/Simulation/**` y la regresión completa.

## Necesidades de contrato (no bloqueantes; no se creó ninguna API alternativa)

1. **Parámetros del modelo fuera de `OutpostConfiguration`.** La demanda de la tripulación y los límites de aire entran por el constructor de `OutpostBaseSimulation`, porque la configuración congelada no los incluye. Si el equipo quiere que vengan del escenario (`Data/Scenarios`, de D2), hace falta una actualización común que los añada.
2. **El depósito de O2 no se publica.** El snapshot solo lleva lecturas de O2 y CO2. Si la UI debe mostrar "O2 de reserva", hay que ampliar el contrato.
3. **Ninguna tarea puede afectar al aire.** `ResourceKind` solo tiene agua y comida, así que tareas como "cambiar cartucho de CO2" o "rellenar O2" no son expresables sin ampliar el contrato.

## Pendientes

- Sustituir `SyntheticTraining()` por valores con fuente contextualizada (BVAD Rev. 2 u otra), con contexto y validez, o mantener la etiqueta de sintético en la UI.
- Decisión de equipo (D2 y escenario): con tasas reales y misiones de minutos, el consumo continuo apenas se nota. Cualquier compresión temporal debe ser explícita y aplicarse a todo el modelo, no solo a la biología.
- Integración (D2): componer `OutpostBaseSimulation` en `IOutpostAuthority`, `Restart` → `Reset`, `CrewAboard` = miembros presentes, y retirar el fake del flujo real.
- D5: validar el cierre de masa y las lecturas `Stale`/`Unavailable` en la build integrada.
