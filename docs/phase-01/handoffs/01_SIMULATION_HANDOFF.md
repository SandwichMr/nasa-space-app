# HANDOFF — Fase 1, Desarrollador 1: núcleo de simulación

## Contexto

- Rama: `phase1/simulation-core`, creada desde `main` en el commit base `4850e1d` (contiene los contratos congelados).
- Sin commits, merge ni push: los cambios quedan en el árbol de trabajo para revisión.
- Contratos usados sin modificar: `IPowerSimulation`, `PowerConfiguration`, `PowerSnapshot` (namespace `Trainer.Simulation`).

## Archivos creados (todos en la lista permitida)

- `Assets/_Project/Runtime/Simulation/Time/SimulationClock.cs`: reloj simulado público (`Reset`, `Advance`, `Validate`).
- `Assets/_Project/Runtime/Simulation/State/PowerState.cs`: estado mutable `internal` (energía, configuración, última petición de ciencia).
- `Assets/_Project/Runtime/Simulation/Systems/PowerSystem.cs`: `public sealed class PowerSystem : IPowerSimulation`.
- `Assets/_Project/Tests/EditMode/Simulation/SimulationClockTests.cs` y `PowerSystemTests.cs`: tests NUnit **desactivados con `#if TRAINER_SIMULATION_TESTS`** (ver bloqueo).
- Este HANDOFF.

No se modificó ningún otro archivo. Unity no se abrió, así que no hay `.meta` generados para los archivos nuevos; Unity los creará al importar.

## Modelo implementado

Unidades: segundos, joules y watts. C# puro, sin UnityEngine.

- Durante cada `Advance(dt, scienceRequested)` las potencias configuradas son constantes.
- Despacho: la generación cubre primero la carga esencial y después la ciencia (solo si se pide). La batería cubre el déficit hasta `MaxDischargeWatts` mientras tenga energía y absorbe el excedente hasta `MaxChargeWatts` mientras no esté llena.
- Si la batería se vacía o se llena dentro del intervalo, el intervalo se divide en ese instante exacto y se recalcula el despacho. El resultado no depende del tamaño de paso (salvo redondeo de coma flotante). La energía cae en 0 o en la capacidad exactamente en ese instante; no se esconde energía negativa con clamps.
- La generación que no se usa ni se almacena se recorta (no se guarda ni cuenta como servida). Carga y descarga sin pérdidas.
- Validación: `Snapshot`/`Advance` antes de `Reset` → `InvalidOperationException`; `Reset(null)` → `ArgumentNullException`; `dt` NaN, infinito, negativo o que desborde el tiempo → `ArgumentOutOfRangeException` sin cambiar el estado. `dt == 0` es no-op completo (ni siquiera actualiza la petición de ciencia; el snapshot sigue siendo la misma instancia).
- Cada cambio publica un `PowerSnapshot` nuevo e inmutable; los anteriores no cambian.

### Semántica del snapshot (decisión a validar por D2)

- Las potencias son el **punto de operación instantáneo en `SimulatedTimeSeconds`**, según la energía actual y la última petición de ciencia no nula. Si la batería se vació a mitad del intervalo, el snapshot muestra el servicio limitado posterior, no el servicio medio del intervalo.
- Tras `Reset`, se asume ciencia no pedida: `ScienceRequestedWatts = 0`.
- `GenerationWatts` es la generación disponible configurada, no la aprovechada.

## Limitaciones del contrato congelado (no resueltas añadiendo campos)

1. **Tiempo/energía servidos dentro del intervalo**: el snapshot solo da potencias instantáneas al final. D2 no puede derivar con exactitud `ScienceWorkSeconds` si la batería se agota a mitad de un `Advance` grande. Opciones: (a) D2 avanza en pasos pequeños; (b) cambio común del contrato para exponer energía servida a ciencia/esencial por intervalo.
2. **Generación recortada**: no se puede observar cuánta generación se desperdicia.
3. **Pérdidas de carga/descarga**: `PowerConfiguration` no tiene eficiencia; el modelo es sin pérdidas.
4. **Servicio parcial**: se representa con `ServedWatts < RequestedWatts`. No hay estado ni bandera de "apagado"; cualquier umbral de servicio mínimo debe decidirlo la capa de misión.

## Bloqueo: infraestructura de tests

Las rutas de test permitidas (`Tests/EditMode/Simulation/*.cs`) no están en una carpeta `Editor` ni tienen `.asmdef`. Unity las compila en `Assembly-CSharp`, que no referencia NUnit: un test activo ahí rompería la compilación del proyecto. Siguiendo el documento de fase, no creé `.asmdef` ni moví archivos. Por eso los tests están completos pero desactivados con `#if TRAINER_SIMULATION_TESTS`.

**Petición mínima (cambio común autorizado)**, a elegir una:

- Autorizar las rutas `Assets/_Project/Tests/EditMode/Simulation/Editor/{PowerSystemTests,SimulationClockTests}.cs` (misma convención que `FakeSessionPortTests`). Después basta con mover los archivos y quitar el `#if`/`#endif`. Opción recomendada: no requiere `.asmdef`.
- O bien añadir un `.asmdef` de tests EditMode, lo que implica `.asmdef` también para los runtime.

## Pruebas realmente ejecutadas

No se ejecutó Unity Test Runner (bloqueo anterior; además el proyecto no tiene `Library/` importado).

Verificación fuera de Unity:
- Compilador Roslyn incluido en Unity 6000.3.28f1 (`Editor/Data/DotNetSdkRoslyn/csc.dll`), `-langversion:9 -warnaserror+`, contra el runtime .NET 6.0.21 incluido en Unity.
- Se compilaron los 3 contratos reales, los 3 archivos runtime y el contenido exacto de ambos archivos de test (sin el guard).
- NUnit no está disponible sin `Library/`, así que se usó un shim mínimo de `NUnit.Framework` (`[Test]`, `Assert.That`, `Assert.Throws`, `Is.*`) con un runner por reflexión. El shim era temporal, fuera del repositorio, y ya se borró.

Resultado real:

```
csc_exit=0
Total: 23, Passed: 23, Failed: 0
exit=0
```

(18 de `PowerSystemTests` y 5 de `SimulationClockTests`.) Cubren:

- balance analítico de carga y descarga;
- límites de capacidad y de potencia de carga/descarga;
- llenado y agotamiento dentro del intervalo;
- prioridad de la carga esencial sobre la ciencia;
- ciencia no pedida sin consumo;
- avance inválido rechazado sin cambios y `dt = 0` como no-op;
- inmutabilidad de snapshots;
- reset sin mutar la configuración;
- repetibilidad;
- refinamiento de paso (1 × 30 s vs 3000 × 0,01 s vs pasos irregulares, tolerancia 1e-9 J: el modelo es exacto por tramos, así que la diferencia es solo redondeo);
- invariantes en 2000 pasos deterministas;
- capacidad cero.

Comprobación de sensibilidad: un mutante que dejaba a la ciencia usar el suministro ya consumido por la carga esencial **no superó el arnés**: la ejecución se colgó (la invariante `servido ≤ suministro` se rompe y el bucle de tramos no termina) y hubo que detenerla. Se detectó, pero no como fallo limpio de test. La implementación real mantiene esa invariante (`net ≥ −límite de descarga`), así que el bucle tiene como máximo dos tramos.

Son pruebas del modelo, no evidencia de rendimiento lunar. Todos los valores de los tests son sintéticos.

## Uso y conexión futura (D2 / integración)

```csharp
IPowerSimulation power = new Trainer.Simulation.PowerSystem();
power.Reset(new PowerConfiguration(initialJ, capacityJ, generationW, essentialW, scienceW, maxChargeW, maxDischargeW));
power.Advance(stepSeconds, scienceEnabled);   // solo en la autoridad de sesión
PowerSnapshot snapshot = power.Snapshot;      // copiar a MissionSnapshot.Power
```

Pasos para la integración autorizada:

1. La autoridad de sesión (D2) es la única dueña de la instancia. Llama a `Advance` desde su reloj de sesión mientras esté `Running`, nunca por frame en clientes.
2. En `Restart`, llamar a `Reset(config)` con la misma configuración.
3. Publicar `power.Snapshot` dentro de `MissionSnapshot`. D3/D4 consumen solo el puerto de sesión.
4. Resolver la limitación 1 antes de acumular `ScienceWorkSeconds`.
5. Resolver el bloqueo de tests y ejecutarlos en Unity Test Runner.

## Dependencias

Ninguna nueva. Solo `System` y los contratos de la base.
