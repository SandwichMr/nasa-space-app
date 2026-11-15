# HANDOFF — D5 fase 2: pruebas y aceptación del prototipo jugable

Fecha: 2026-11-14. Rama `feature/phase-02/d05-validacion`, creada desde la base común `95caa315527200bbbbcbb73fd654321d7992552e`. Sin merge ni push.

**Estado de aceptación: NO ACEPTADA.** No se ha generado ni ejecutado ninguna build y nadie ha jugado una partida con dos jugadores. Este entorno no puede ejecutar Unity (ver Bloqueos). Lo entregado son herramientas, pruebas y evidencia fuera de Unity. Nada de esto sustituye a la partida real.

## Criterio

### Actualización de integración tras auditoría (2026-11-15)

Working tree sobre `be207a3`, sin commit. Antes del arreglo, el controlador ejecutó Unity nativo:
importación/compilación exit 0; EditMode 282 casos, 281 Passed/1 Inconclusive, exit 2; checks D5 exit 1.
La inconclusa era `RealImplementationsConformToTheContracts`: dobles dentro de `Assembly-CSharp-Editor`
se mezclaban con D1/D3 y el constructor real de D2 no tenía binding. Aquella suite no quedó aceptada.

- Discovery ahora excluye el ensamblado Editor y dobles de pruebas; conserva los candidatos reales del
  ensamblado runtime predefinido. D2 se construye explícitamente con sus tipos reales y reglas rotuladas
  **solo para la fixture D5**, sin cambiar las reglas del asset de misión ni los contratos.
- El fixture comprueba que se descubren las implementaciones reales; no supone que el ensamblado del
  fake carece de producción. La aserción/inconclusa de la puerta no se desactivó.
- `RunAll` conecta la suite de dos puertos a D1+D2+D3 reales mediante el transporte en memoria existente
  de D3. La evidencia sigue siendo `session-two-ports`, en un proceso; rechaza puertos fake conectados.
- El escáner excluye segmentos `Editor`, manteniendo el rechazo del mismo uso de fake en fuentes player.
  La auditoría de estaciones ejecuta `BaseInteriorView.Configure` en un clon de una escena temporal usando
  las referencias asignadas en la composición. Exige estaciones y rectángulos reales y no guarda escenas.
  Esto verifica la proyección de geometría, no el arranque, render ni la experiencia jugable.

Regresiones, resultados managed y checks de tipos contra DLL ya importadas:
`Logs/phase02-fixes-og-20261115T060242Z/FIX_REPORT.md`. El controlador debe repetir importación, EditMode y
D5 sobre el árbol corregido. PlayMode/build dependen de esas puertas; la partida con personas continúa pendiente.

La fase se acepta solo con una experiencia de tipo Among Us: dos astronautas controlables dentro de la base, que se ven entre sí y hacen tareas al acercarse a las estaciones. Una UI conectada sin personajes ni navegación no cuenta, y tampoco basta con compilar ni con contar archivos.

## Archivos de D5 (exclusivos, según `OWNERSHIP.md`)

| Ruta (`Assets/_Project/…`) | Qué es |
| --- | --- |
| `Editor/Validation/Phase02/Phase02Check.cs` | Resultado tipado, tipos de evidencia separados y ejecutor de checks |
| `…/Phase02Fixtures.cs` | Escenario sintético propio de D5 (pared fina con puerta, 2 spawns, 3 estaciones) y variantes rotas |
| `…/OutpostScenarioValidator.cs` | Navegabilidad (flood fill sobre `IsAdmissible`), estaciones alcanzables, spawns conectados, trabajo en paralelo, efectos aplicables |
| `…/OutpostSnapshotStreamValidator.cs` | Reglas de estado y de transición: velocidad máxima, admisibilidad, ids nunca reutilizados, secuencias, fin de tarea, salida sin fantasma, masa con Δt = 0, reinicio |
| `…/CrewRosterConformance.cs` | 16 checks de `ICrewRoster` (D3) |
| `…/OutpostSimulationConformance.cs` | 10 checks de `IOutpostSimulation` (D1) |
| `…/OutpostAuthorityConformance.cs` | 12 checks de `IOutpostAuthority` (D2) con el espía de costos |
| `…/RecordingOutpostSimulation.cs` | Espía que reenvía las llamadas al modelo real y registra `Apply`/`Advance` |
| `…/TwoPortSessionScript.cs` | Flujo de aceptación con dos puertos reales (unión, movimiento visto por el otro, tareas distintas, contención, fin, reinicio, desconexión) |
| `…/Phase02ImplementationDiscovery.cs` | Encuentra las implementaciones reales por reflexión y excluye fakes, fixtures y dobles |
| `Editor/Build/Phase02/Phase02BuildChecks.cs` | Menú y CLI: checks, auditoría de escena, build de la escena de misión de fase 2 y lanzador host + cliente |
| `Editor/Build/PhaseOneBuildChecks.cs` | **Cambio de una línea**: la escena por defecto de fase 1 se limita a `Phase01_*` para que la escena de fase 2 no rompa la regresión |
| `Tests/EditMode/Validation/Phase02/Editor/Phase02ValidatorTests.cs` | NUnit: escenario (8) y flujo de snapshots (16), con un mutante por regla |
| `…/Phase02ConformanceTests.cs` | NUnit: autoprueba de las suites (12) y puerta sobre implementaciones reales (1) |
| `…/ValidationDoubles.cs` | Dobles de prueba **rotulados** (roster y modelo de referencia con defectos inyectables). No son D1 ni D3, y la discovery los excluye |
| `docs/phase-02/handoffs/05_VALIDATION_HANDOFF.md`, `d5-evidence/**` | Este documento, el arnés offline y las salidas reales |

Todos los `.meta` nuevos llevan GUID aleatorios sin duplicados en `Assets/`. No modifiqué paquetes, contratos, escenas, settings ni archivos de otros D.

## Comandos

```sh
# A. Offline (lo que SÍ se ejecutó aquí). Roslyn y .NET 6.0.21 del Editor + NUnit real; no es Unity.
OUT=$PWD/Logs/d5-phase2-offline docs/phase-02/handoffs/d5-evidence/offline/run_offline_checks.sh

# B. En Unity (pendiente; requiere licencia y el proyecto en un sistema de archivos sin distinción de mayúsculas).
Unity -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.RunChecksFromCommandLine -logFile Logs/phase2-checks.log
Unity -batchmode -runTests -testPlatform EditMode -projectPath <repo> -testResults Logs/phase2-editmode.xml
Unity -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.BuildFromCommandLine \
  -phase2Scenes "Assets/_Project/Scenes/Phase02_<Nombre>.unity" -logFile Logs/phase2-build.log
# Regresión de la build de fase 1 (herramienta existente):
Unity -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.BuildFromCommandLine \
  -phase1Scenes Assets/_Project/Scenes/Phase01_PowerCoop.unity -logFile Logs/phase1-build.log
```

La build usa una lista explícita de escenas y no modifica Build Settings ni el target. Rechaza las plantillas y la escena de fase 1. Escribe en `Builds/Phase2Mission/<target>/` y deja un `build-info.txt` con el commit, el Editor, las escenas y el resultado. Los menús están en `Tools/Trainer/Phase 2/…`.

## Resultados reales, por tipo (no se suman entre sí)

Los evaluados son el árbol de trabajo de esta rama sobre `95caa31` y, para pares, D1 `6aed43a` y D3 `a76ae73`.

| Tipo | Resultado | Evidencia |
| --- | --- | --- |
| Pureza: `Runtime/Simulation` + `Runtime/Application` contra `netstandard` 2.1, `-warnaserror` | exit 0. Silencio CS1591 y CS1573 porque `MissionSession.cs` (D2, fase 1) ya tenía avisos de doc antes | `offline/run_offline_checks.sh` |
| NUnit (runner por reflexión, **no** Unity Test Runner) | **139 pasan, 0 fallan, 1 inconclusa**. D5 fase 2: escenario 8, snapshots 16, autoprueba de suites 12. Contratos de la base 34. Regresión pura de fase 1: 69 (PowerSystem 30, Clock 5, MissionSession 21, Evaluator 7, FakeSessionPort 6) | `d5-evidence/offline-nunit-results.txt` |
| Inconclusa | `Phase02IntegrationGateTests`: en esta rama no hay roster, modelo ni autoridad reales. Correcto: nunca pasa con dobles | ídem |
| Sensibilidad | Las suites aceptan el doble correcto y detectan cada defecto inyectado: diagonal más rápida, efecto túnel, ids reutilizados, dirección conservada tras reiniciar, `Apply` doble, `Apply` parcial y masa que aparece de la nada. El validador de flujo tiene un mutante por regla | ídem |
| Conformidad de pares (copia aislada, driver de scratch) | **D3 `CrewRoster` 16/16. D1 `OutpostBaseSimulation` 10/10** (con `OutpostSimulationParameters.SyntheticTraining()`). Discovery: encuentra ambos; autoridad D2 Pending | `d5-evidence/peer-conformance-results.txt` |
| Comprobación de tipos de `Phase02BuildChecks.cs` contra las DLL reales de UnityEngine/UnityEditor 6000.3.28f1 y uGUI compilado desde el Editor, C# 9, `-warnaserror` | exit 0. Los tipos de fase 1 que referencia se sustituyen por un **shim solo de compilación** con las firmas copiadas | `d5-evidence/offline/EditorTypeCheckShim.cs.txt` |
| Unity batchmode | **No se ejecutó**: abortó antes de importar | `d5-evidence/unity-attempt-excerpt.txt` |
| Tests EditMode/PlayMode en Unity, auditoría de escena, regresión en el Editor, build, dos procesos | **No ejecutados** | — |

`PhaseOneBuildChecks.cs` (cambio de una línea) no se compiló aquí: el ensamblado Editor completo necesita Unity Transport, y ese paquete no está en el Editor ni hay `Library/`.

## Bloqueos de entorno (verificados, no supuestos)

1. **Sistema de archivos**: Unity aborta con «The project is on case sensitive file system». El repo está en ext4 de WSL y el Editor es el de Windows.
2. **Licencia**: «Found 0 entitlement groups», «'com.unity.editor.headless' was not found», «Access token is unavailable». Hace falta iniciar sesión o activar en Unity Hub.
3. Unity Transport no está en caché (`Library/` no existe), así que no se puede compilar `Assembly-CSharp` fuera del Editor.
4. No probé sockets ni render, y no los sustituí por mocks.

Para desbloquear: copiar o clonar el repo en una ruta de Windows (por ejemplo `C:\src\nasa-space-app`), activar la licencia en Unity Hub y ejecutar los comandos B. El intento creó dos archivos de sondeo vacíos (`CaseSensitiveTest`, `casesensitivetest`) en la raíz y ya los eliminé.

## Defectos y observaciones (responsable)

- **Ningún defecto funcional** en lo que pude ejecutar de D1 y D3.
- **D2 / coordinación**: `IOutpostAuthority` no expone un tick. La conformidad temporal (completar, alejarse, demanda, pausa) necesita un método público `Advance`, `Step` o `Tick(double)` en la autoridad, o un hook que se cablee en la integración. Sin él, esos checks quedan en Pending.
- **D2**: la herramienta espera que la escena de fase 2 se llame `Phase02_*.unity`; si no, hay que pasar `-phase2Scenes`. También espera un asset en `Data/Scenarios/` con un método público sin parámetros que devuelva `OutpostScenario`, para validarlo y compararlo con el arte.
- **D2/D4**: para auditar la coherencia entre el arte y el plano, los objetos de estación deben llevar en su nombre el id de la estación (por ejemplo `water-processor`). Si no, ese check queda en Pending.
- **Coordinador (base congelada, solo nota)**: `OutpostFixtures.Initial` pone `LastInputSequence = 0`. Como el reinicio conserva las secuencias (SESSION_CONTRACT), sirve como snapshot posterior a un reinicio solo si nadie había enviado movimiento. Mi validador lo detecta (`InputSequenceRegressed`).
- **D5 (corregido aquí)**: la resolución por defecto de la escena de fase 1 fallaría al añadir una segunda escena.

## Aceptación pendiente: partida con dos jugadores sobre la build

1. Generar la build con el comando B sobre el commit de `integration/phase-02`; guardar `build-info.txt`.
2. `Tools/Trainer/Phase 2/Launch last build twice` (o a mano) con los argumentos de rol que documente D3 para la fase 2 (`-phase2HostArgs`, `-phase2ClientArgs`). Por defecto usa la receta de fase 1. Los logs quedan en `Logs/phase2-two-players-<UTC>/`.
3. Lista de comprobación, a rellenar por dos personas y guardar en `d5-evidence/`:
   - cada jugador ve dos astronautas, distingue el suyo y aparecen en sus spawns;
   - ambos se mueven con teclado; en diagonal no van más rápido; no atraviesan paredes ni salen del mapa;
   - el movimiento de uno se ve en la pantalla del otro;
   - una estación fuera de alcance rechaza la tarea con un mensaje claro;
   - hacen tareas distintas a la vez;
   - si piden la misma estación, solo uno la obtiene;
   - cancelar y alejarse terminan la tarea sin aplicar su efecto;
   - los costos y recursos se ven igual en ambos y cada efecto se aplica una sola vez;
   - tras hacer clic en la UI, WASD y las flechas siguen moviendo (la UI no captura el movimiento);
   - completar la necesidad interior y, opcionalmente, la ciencia → resultado → reintento;
   - cerrar el cliente: no queda fantasma y la estación se libera;
   - la build de fase 1 sigue funcionando.
4. En el Editor de integración: cablear `TwoPortSessionScript` y `AuthorityHarness` con los adaptadores reales de D3 y la autoridad de D2, y ejecutar `RunChecksFromCommandLine` sin ningún Failed.

## Pendientes

Importación en Unity y GUID; Test Runner EditMode con los 37 casos nuevos más la regresión; checks de fase 2 en Unity; build de fase 2; regresión de la build de fase 1; partida con dos procesos (local y, si se decide, LAN/WebGL); conformidad de la autoridad D2 y del flujo de dos puertos reales; auditoría de escena (física, UI, EventSystem, arte frente al plano, sin fakes en producción).
