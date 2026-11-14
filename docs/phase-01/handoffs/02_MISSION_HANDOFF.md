# HANDOFF — Desarrollador 2: lógica de misión (fase 1)

## Base y rama

- Commit de base: `4850e1d128ca1c1666d6cdd13e0a4de05d4f6687` (`main`, "docs 2"), con los contratos de `SESSION_CONTRACT.md`.
- Rama: `phase1/mission-integration`. Cambios **sin commit**, pendientes de revisión. Sin merge, rebase ni push.
- Contratos protegidos, `FakeSessionPort`, escenas, paquetes, ajustes y archivos de otros roles: **sin cambios**.

## Archivos creados

| Archivo | Contenido |
|---|---|
| `Assets/_Project/Runtime/Application/Sessions/MissionSession.cs` | `MissionSession : ISessionPort`, la autoridad de sesión. |
| `Assets/_Project/Runtime/Application/Evaluation/MissionOutcomeEvaluator.cs` | `MissionRules`, `MissionOutcome`, `MissionOutcomeEvaluator` (estático y puro). |
| `Assets/_Project/Runtime/Unity/Configuration/ScenarioDefinition.cs` | `ScriptableObject` de authoring y conversión a objetos en memoria. |
| `Assets/_Project/Runtime/Unity/Bootstrap/SimulationRunner.cs` | El único reloj: `Update` llama a `MissionSession.Tick`. |
| `Assets/_Project/Runtime/Unity/Bootstrap/MissionBootstrap.cs` | Raíz de composición del lado autoridad. Sin conectar a ninguna escena. |
| `Assets/_Project/Tests/EditMode/Application/MissionSessionTests.cs` | 16 tests (protegidos por `#if`, ver abajo). |
| `Assets/_Project/Tests/EditMode/Application/MissionOutcomeEvaluatorTests.cs` | 5 tests (protegidos por `#if`). |
| `docs/phase-01/handoffs/02_MISSION_HANDOFF.md` | Este documento. |

No hay `.meta`: el proyecto nunca se abrió en Unity en esta máquina (no existe `Library/`). Unity los generará; versionar solo los de estos archivos.

## Comportamiento implementado

- **Comandos y transiciones:** la misma política que documenta el contrato y aplica el fake: Resume desde Ready/Paused, Pause solo desde Running, SetScienceEnabled en Ready/Running/Paused, y Completed/Interrupted solo aceptan Restart. Los motivos de rechazo son los mismos textos: `InvalidCommandId`, `EpochMismatch`, `UnknownAction`, `InvalidActionValue`, `InvalidState`, `SessionEnded`, `CommandIdConflict`; el éxito devuelve `Accepted`.
- **Resultados asíncronos:** se publican solo por `CommandCompleted`, con `CommandId` y la época original del comando. Si el comando se acepta, `SnapshotChanged` se emite antes que el resultado. Los consumidores no deben depender de ese orden.
- **Deduplicación:** cada ID guarda su comando y su resultado (también los rechazos) durante toda la vida de la instancia, incluso tras un Restart. Repetir el mismo comando devuelve el resultado guardado sin reaplicarlo y sin emitir un snapshot. Un contenido distinto con el mismo ID se rechaza.
- **Épocas:** un comando nuevo de una época anterior o futura se rechaza. Restart incrementa la época, pone la revisión a 0, llama a `IPowerSimulation.Reset(config)`, desactiva la ciencia, pone el trabajo a 0 y deja la sesión en Ready.
- **Reloj:** `Tick(segundosSimulados)` solo avanza en Running. Divide el tiempo en subpasos de como máximo `maxStepSeconds` (1 s por defecto), no se pasa de la duración de la misión, deja de avanzar en cuanto hay un resultado y publica **un** snapshot por Tick con `revision + 1`.
- **Trabajo científico servido:** por cada subpaso se acredita el tiempo simulado que avanzó la simulación (`after.SimulatedTimeSeconds - before.SimulatedTimeSeconds`), pero solo si la ciencia estaba solicitada y el snapshot posterior muestra `ScienceServedWatts >= ScienceRequestedWatts > 0`. Un servicio parcial no acredita nada. No se usa el tiempo transcurrido a ciegas.
- **Resultado** (`MissionOutcomeEvaluator.Evaluate`, por orden de prioridad):
  1. Carga esencial no servida por completo → `Interrupted` (`EssentialServiceLost`).
  2. Trabajo ≥ objetivo → `Completed` (`ObjectiveAchieved`).
  3. Tiempo simulado ≥ duración → `Completed` (`TimeExpired`).
- **Hilos y reentrada:** un solo hilo. El estado y la deduplicación se confirman antes de emitir eventos, así que un suscriptor puede volver a llamar a `Submit`. Las excepciones de los suscriptores y de la simulación se propagan.

## Uso / API de composición

```csharp
// Solo en la autoridad (host/servidor):
IPowerSimulation sim = /* implementación de D1 */;
MissionSession session = bootstrap.Compose(sim);   // crea la sesión desde ScenarioDefinition y arranca el runner
ISessionPort port = bootstrap.Port;                // para el adaptador de red de D3 y la UI local del host
// Sin MonoBehaviour (tests, otra raíz de composición):
var s = new MissionSession(sim, powerConfiguration, new MissionRules(targetSeconds, durationSeconds), maxStepSeconds);
s.Tick(simulatedSeconds);
```

`ScenarioDefinition` expone `ToPowerConfiguration()`, `ToMissionRules()`, `CreateSession(sim)`, `TimeScale` y `MaxStepSeconds`. La conversión crea objetos nuevos y nunca muta el asset. Sus valores por defecto (5000/10000 J, 100 W de generación, 60 W esenciales, 60 W de ciencia, 120 s de objetivo, 300 s de duración) son **ilustrativos**, no datos aprobados. No se creó ningún asset `.asset`.

## Pruebas realmente ejecutadas

Unity no se ejecutó: el proyecto nunca se ha importado aquí y no quise generar `Library/` ni `.meta` de archivos ajenos. En su lugar, con las herramientas que trae el propio Editor 6000.3.28f1 (Roslyn `csc.dll` y .NET 6.0.21 de `Editor/Data`):

1. **Tests**: compilé `Runtime/Simulation` + `Runtime/Application` + los dos archivos de tests + `FakeSessionPortTests` (con `-define:TRAINER_MISSION_TESTS`) usando un **shim mínimo de NUnit** (Test, SetUp, `Assert.That` con Is.True/False/EqualTo/SameAs, `Assert.Throws`) y un runner por reflection, todo en un scratchpad fuera del repo. Resultado: `Passed 27, Failed 0` (16 de sesión, 5 del evaluador y 6 del fake existente). **No es una ejecución con el Unity Test Runner ni con NUnit real.**
2. **Compilación Unity**: compilé todo `Assets/_Project/Runtime` contra `netstandard 2.1` + `UnityEngine.CoreModule.dll` del Editor. Compila; solo aparecen los avisos CS0649 propios de `[SerializeField]`.

No verificado: la compilación dentro del Editor, la serialización de `double` en el Inspector, el runner en Play Mode, la red ni las escenas.

## Bloqueos y decisiones pendientes

1. **Ubicación de los tests (requiere decisión).** Las rutas permitidas `Tests/EditMode/Application/*.cs` no están dentro de una carpeta `Editor/` y no hay `.asmdef` de tests, así que caerían en `Assembly-CSharp`, que no referencia NUnit, y **romperían la compilación**. Por eso están envueltas en `#if TRAINER_MISSION_TESTS` y Unity hoy no las compila ni las ejecuta. Opciones, ambas fuera de mi lista permitida:
   - (a) moverlas a `Tests/EditMode/Application/Editor/` y quitar el `#if`, como hizo la base con `FakeSessionPortTests`;
   - (b) crear asmdefs de tests.
2. **Autorización.** `ISessionPort.Submit` no lleva la identidad del remitente y la base no documenta una política de permisos. `MissionSession` **no autoriza**: confía en quien llama a `Submit`. El adaptador de red (D3) debe autenticar al remitente y aplicar la política antes de llamar a `Submit`. No fijé ninguna política: hace falta que el equipo decida quién puede pausar, reiniciar o activar la ciencia.
3. **El resultado no aparece en el snapshot.** `MissionSnapshot` no distingue `ObjectiveAchieved` de `TimeExpired` (ambos son `Completed`) ni incluye el objetivo en segundos. Los clientes no pueden mostrar "objetivo cumplido / no cumplido" ni una barra de progreso sin conocer el objetivo. En la autoridad está disponible en `MissionSession.Outcome` y `.Rules`. Propuesta mínima para un cambio común de contrato: añadir `ScienceWorkTargetSeconds` (o un resultado) a `MissionSnapshot`.
4. **Precisión del trabajo servido.** `PowerSnapshot` es instantáneo y no trae energía servida acumulada. Si el servicio cambia dentro de un subpaso, no se ve, y la evaluación usa el estado al final del subpaso (error acotado por `maxStepSeconds`). Lo mismo vale para detectar la pérdida de carga esencial. Propuesta: un acumulado de energía servida por carga en `PowerSnapshot`.
5. **Política de misión provisional.** Que la misión termine al alcanzar el objetivo, que se interrumpa ante cualquier déficit esencial y que el servicio parcial de ciencia no acredite trabajo son decisiones mías para que el flujo funcione. Las debe confirmar el equipo.
6. **Memoria.** Los IDs se guardan durante toda la vida de la instancia (lo exige el contrato), así que el consumo crece con el número de comandos.

## Pasos de conexión (integración posterior, no realizados)

1. **D1:** instanciar su `IPowerSimulation` **solo en la autoridad** y pasarla a `MissionBootstrap.Compose(sim)`. `MissionSession` llama a `Reset`; D1 no debe avanzarse desde ningún otro sitio.
2. **Escena (autoridad):** un GameObject con `MissionBootstrap` y `SimulationRunner`, y un `ScenarioDefinition` asset (crearlo con *Create → Trainer → Scenario Definition*) asignado. Debe existir un único runner por sesión compartida. Llamar a `Compose` cuando la red confirme el rol de autoridad y a `Shutdown` al terminar o al perder el host.
3. **D3 (adaptador de red):** en el host, envolver `bootstrap.Port`, reenviar los comandos remotos ya autenticados y autorizados a `Submit`, y difundir `SnapshotChanged`/`CommandCompleted`. Hacerlo en el hilo principal (`MissionSession` no es thread-safe). En los clientes, exponer un `ISessionPort` remoto: los clientes no crean `MissionSession` ni `SimulationRunner`. No duplicar la deduplicación: la de `MissionSession` es la autoritativa.
4. **D4 (prefab UI):** consumir solo `ISessionPort` (el del host o el remoto), sin referenciar `MissionSession`.
5. Resolver el bloqueo 1 para que los tests corran en el Unity Test Runner y ejecutarlos allí, junto con la prueba de dos clientes de D5.
