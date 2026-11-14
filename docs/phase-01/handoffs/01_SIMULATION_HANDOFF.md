# HANDOFF — Nueva fase 1 (sala cooperativa de energía), D1: simulación eléctrica

## Contexto

- Fase: `docs/roadmap/01_POWER_COOP_PROTOTYPE.md`, sección D1.
- Commit base: `0f05e38` (`main`, "documentation-addon"). Rama local: `feature/phase-01/d01-simulacion`.
- Sin commit, merge ni push: los cambios quedan en el árbol de trabajo para revisión.
- Contratos usados **sin modificar**: `IPowerSimulation`, `IIntegratedPowerSimulation`, `PowerConfiguration`, `PowerSnapshot` (namespace `Trainer.Simulation`).

## Qué ya funcionaba (auditado, no reescrito)

`SimulationClock`, `PowerState` y `PowerSystem` (incluida la API integrada añadida en `6247318`) se revisaron línea a línea. No se encontró ningún defecto, así que **el código runtime no cambia**. Verificado:

- Generación → carga esencial → ciencia (solo si se pide). La batería cubre el déficit hasta `MaxDischargeWatts` y absorbe el excedente hasta `MaxChargeWatts`; el resto de la generación se recorta.
- Agotamiento y llenado dentro del paso: el intervalo se parte en el instante exacto; la energía cae justo en 0 o en la capacidad (máximo dos tramos por `Advance`). Tras agotarse, `net` es exactamente 0 en coma flotante (misma resta), sin oscilaciones.
- Límites de potencia y energía; capacidad cero; reset sin mutar la configuración; avance inválido rechazado sin cambios; `dt = 0` como no-op.
- `SecondsUntilServiceChange` usa la misma fórmula que `Advance`, así que avanzar exactamente ese tiempo cae en el límite sin error de redondeo.

## Archivos modificados

- `Assets/_Project/Tests/EditMode/Simulation/PowerSystemTests.cs` → movido a `.../Simulation/Editor/PowerSystemTests.cs`; guard `#if TRAINER_SIMULATION_TESTS` eliminado; **+12 tests** de la API integrada.
- `Assets/_Project/Tests/EditMode/Simulation/SimulationClockTests.cs` → movido a `.../Simulation/Editor/SimulationClockTests.cs`; guard eliminado.
- Este HANDOFF.

**Motivo del traslado:** sin `.asmdef`, los tests fuera de una carpeta `Editor` se compilan en `Assembly-CSharp`, que no referencia NUnit, y por eso estaban desactivados. Una subcarpeta `Editor` dentro de la propia carpeta de tests de simulación sigue la convención existente (`Tests/EditMode/Application/Editor/`), no requiere `.asmdef`, paquetes ni settings y deja los tests activos en Unity. Los archivos nunca tuvieron `.meta` (no se habían importado), así que no se pierde ningún GUID; Unity generará el `.meta` de la carpeta `Editor` y de ambos archivos al importar. **Si integración prefiere otra ubicación, solo hay que mover los dos archivos.**

## Semántica de `PowerSnapshot` (aclarada)

- **Potencias = punto de operación instantáneo final** en `SimulatedTimeSeconds`, con la energía final y la última petición de ciencia no nula. No son medias del intervalo. Si la batería se agotó a mitad de un `Advance`, el snapshot muestra el servicio limitado posterior.
- `GenerationWatts` es la generación disponible configurada, no la aprovechada.
- Tras `Reset`: ciencia no pedida (`ScienceRequestedWatts = 0`).
- **El servicio durante el intervalo** no está en el snapshot. Se obtiene con `IIntegratedPowerSimulation`:
  - `LastFullyServedScienceSeconds`: segundos con ciencia **plenamente** alimentada dentro del último `Advance` no nulo. Se pone a 0 en cada `Advance` no nulo y en `Reset`; `dt = 0` y los avances rechazados lo conservan.
  - `SecondsUntilServiceChange(science)`: tiempo hasta que cambia el despacho para esa intención (batería vacía o llena); `∞` si es estable; `0` si la carga esencial ya no está cubierta. No muta el modelo.

## Coordinación con D2: trabajo científico efectivo

Comprobado en el código real de `MissionSession.Tick` (sin modificarlo):

1. Si la simulación implementa `IIntegratedPowerSimulation`, recorta cada sub-paso a `SecondsUntilServiceChange(science)` y suma `LastFullyServedScienceSeconds`. El tiempo sin alimentación nunca se acumula, sea cual sea el tamaño del paso. Los tests `IntegratedServiceIsIndependentOfStepSize` e `IntegratedServiceSplitsAcrossBoundariesLikeTheSessionLoop` reproducen ese bucle.
2. Con `boundary == 0` evalúa el resultado antes de avanzar: la pérdida de servicio esencial se detecta en el instante exacto del agotamiento.
3. El servicio parcial (`Served < Requested`, p. ej. por límite de descarga) **no** suma trabajo. Coincide con `MissionOutcomeEvaluator.ServedScienceSeconds`.

No hace falta ningún cambio de contrato para la fase 1.

## API de uso

```csharp
var power = new Trainer.Simulation.PowerSystem();            // IIntegratedPowerSimulation
power.Reset(new PowerConfiguration(initialJ, capacityJ, generationW,
    essentialW, scienceW, maxChargeW, maxDischargeW));        // solo en la autoridad
double step = Math.Min(remaining, power.SecondsUntilServiceChange(scienceOn)); // si > 0
power.Advance(step, scienceOn);
double workGained = power.LastFullyServedScienceSeconds;      // servicio pleno dentro del paso
PowerSnapshot snap = power.Snapshot;                          // estado final inmutable → MissionSnapshot.Power
```

Excepciones: `Snapshot`/`Advance`/`SecondsUntilServiceChange` antes de `Reset` → `InvalidOperationException`; `Reset(null)` → `ArgumentNullException`; `dt` NaN, infinito, negativo o que desborde el reloj → `ArgumentOutOfRangeException`, sin cambiar el estado.

## Limitaciones (sin añadir campos)

1. Sin pérdidas de carga/descarga: `PowerConfiguration` no tiene eficiencia.
2. Generación recortada no observable.
3. La energía servida a la carga esencial durante el intervalo no se expone. No es necesario en la fase 1 porque la sesión se detiene en cada cambio de servicio.
4. `SecondsUntilServiceChange` también cuenta el llenado de batería como cambio de despacho, aunque el servicio no cambie. Solo provoca un sub-paso extra en la sesión, sin efecto en los resultados.
5. Con `ScienceConsumptionWatts = 0`, la ciencia nunca cuenta como servida (`ScienceRequestedWatts = 0`). `ScenarioDefinition` ya exige `> 0`.
6. Todos los valores de test son sintéticos: no son datos de hardware lunar.

## Requisitos de integración

1. La autoridad de sesión es la única dueña de `PowerSystem`; los clientes nunca llaman a `Advance`. `Restart` → `Reset(config)`.
2. Pasar a `MissionSession` la instancia concreta (o como `IIntegratedPowerSimulation`): si se envuelve en un adaptador que solo implemente `IPowerSimulation`, la sesión cae en la vía heredada aproximada (`MinimumWorkStepSeconds`).
3. Abrir el proyecto en Unity para generar los `.meta` de `Tests/EditMode/Simulation/Editor/` y confirmarlos en el mismo commit que el traslado.
4. Ejecutar Unity Test Runner (EditMode) en la build de integración; ver la sección de pruebas.

## Pruebas realmente ejecutadas

**No se ejecutó Unity Test Runner** en esta tarea: no se abrió el Editor (el proyecto no tiene `Library/`; el import y la licencia quedan para integración). Sí se hizo lo siguiente:

- Compilador: Roslyn incluido en Unity 6000.3.28f1 (`Editor/Data/DotNetSdkRoslyn/csc.dll`), `-langversion:9 -warnaserror+`, runtime .NET 6.0.21 del Editor.
- **NUnit real** del Editor (`com.unity.ext.nunit/net40/unity-custom/nunit.framework.dll`), con un runner mínimo por reflexión (`[Test]` + `[SetUp]`), temporal y fuera del repositorio.

Resultados:

```
# Simulación: 7 archivos runtime + 2 de tests
csc_exit=0
Total: 35, Passed: 35, Failed: 0      (30 PowerSystemTests + 5 SimulationClockTests)

# Compatibilidad: Runtime/Simulation + Runtime/Application completos
# + tests EditMode de misión de D2 (-define:TRAINER_MISSION_TESTS, solo lectura)
csc_exit=0
Total: 62, Passed: 62, Failed: 0
```

`PhaseOneRegressionTests` (usa `UnityEngine`) no se pudo ejecutar fuera de Unity.

Sensibilidad (mutantes aplicados solo a la copia del harness):

| Mutante | Tests fallidos |
|---|---|
| Contar servicio parcial de ciencia como pleno | 1 |
| No reiniciar `LastFullyServedScienceSeconds` en cada `Advance` | 3 |
| Acreditar el intervalo entero en vez de hasta el límite | 3 |
| `SecondsUntilServiceChange` ignora la intención de ciencia | 3 |

## Dependencias

Ninguna nueva. Solo `System` y los contratos existentes.
