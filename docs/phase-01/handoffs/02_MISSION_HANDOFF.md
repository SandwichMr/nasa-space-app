# HANDOFF — D2: misión y escena funcional (fase 1)

Fecha: 2026-11-14. Sustituye el handoff anterior de D2. La versión previa, sin escena, está en `5024bf4`.

## Base y rama

| Dato | Valor |
|---|---|
| Commit base | `0f05e38` (`main` = `origin/main`, "documentation-addon"). Es el mismo commit del que parten D1, D3 y D4 |
| Rama | `feature/phase-01/d02-mision`, creada desde la base. Sin merge ni push |
| Contratos, fake, paquetes, renderer, ProjectSettings y archivos de otros roles | **Sin cambios** |
| Ramas ajenas leídas, sin mezclar | `feature/phase-01/d01-simulacion` (`e1cc4d2`), `d03-multiplayer` (`a7a506c`), `d04-room-visuals` (`df0bb12`) |

## Qué se reutilizó y qué se cambió

Se reutilizaron `MissionSession`, `MissionOutcomeEvaluator`, `MissionBootstrap`, `SimulationRunner` y `ScenarioDefinition`. La lógica de comandos, épocas, deduplicación, reloj y trabajo servido **no cambió**: se verificó con tests nuevos sobre el `PowerSystem` real de D1.

| Archivo | Cambio |
|---|---|
| `Runtime/Application/Evaluation/MissionOutcomeEvaluator.cs` | `Classify(rules, snapshot)`: obtiene el resultado a partir de un snapshot publicado (ver «Resultado en clientes») |
| `Runtime/Unity/Configuration/ScenarioDefinition.cs` | Campos de rótulo `displayName` y `provenance` (+ propiedades) |
| `Runtime/Unity/Bootstrap/MissionBootstrap.cs` | Propiedad `Scenario` (solo lectura). Comentario actualizado |
| `Runtime/Unity/Bootstrap/MissionSceneComposition.cs` | **Nuevo.** La raíz de composición de la escena (ver abajo) |
| `Data/Scenarios/Phase01_PowerCoop_Synthetic.asset` | **Nuevo.** El escenario sintético rotulado |
| `Scenes/Phase01_PowerCoop.unity` | **Nueva.** La escena mínima de misión |
| `Tests/EditMode/Application/Editor/MissionSessionTests.cs`, `MissionOutcomeEvaluatorTests.cs` | Movidos desde `Tests/EditMode/Application/` y **sin** el `#if TRAINER_MISSION_TESTS`. Resuelve el bloqueo 1 del handoff anterior con el mismo patrón de la base y de D1 (carpeta `Editor/`, sin asmdef) |
| `Tests/EditMode/Application/Editor/MissionScenarioTests.cs` | **Nuevo.** El escenario de principio a fin con `PowerSystem` |
| `Tests/EditMode/Bootstrap/Editor/MissionSceneCompositionTests.cs` | **Nuevo.** Roles, composición, asset y escena (necesita Unity) |
| `.meta` de todos los archivos anteriores y de las carpetas `Tests/EditMode/Bootstrap{,/Editor}` | **Nuevos**, con GUID fijo. La escena y el asset referencian scripts por GUID y antes no había `.meta` versionados de estos scripts. Ninguna otra rama crea `.meta` para ellos (comprobado), así que no habrá conflictos |

## Misión: comportamiento verificado

- **Objetivo:** acumular `ScienceWorkTargetSeconds` de servicio científico **completo**. La duración máxima es `MissionDurationSeconds`.
- **Ejecución y pausa:** Resume desde Ready/Paused; Pause solo desde Running. Mientras la sesión está en pausa, el reloj no avanza ni publica nada. La ciencia se puede cambiar en Ready, Running y Paused.
- **Resultado** (por orden de prioridad):
  1. Pérdida de la carga esencial → `Interrupted`.
  2. Objetivo alcanzado → `Completed`.
  3. Duración agotada → `Completed`.
  4. Fallo de la simulación → `Interrupted`.
  Cuando hay resultado, solo se acepta Restart.
- **Reinicio:** abre una época nueva, pone la revisión a 0, reinicia la simulación y deja la ciencia apagada, el trabajo a 0 y la sesión en Ready. Si un Restart llega tarde con la época antigua, se rechaza con `EpochMismatch`. Si se reenvía el mismo Restart (mismo ID), se reproduce su resultado sin abrir otra época.
- **IDs y deduplicación:** dos participantes con intenciones opuestas se ordenan en la autoridad. Un duplicado no se reaplica.
- **Trabajo realmente servido:** con `PowerSystem` (`IIntegratedPowerSimulation`), la sesión acredita `LastFullyServedScienceSeconds` y corta el paso en cada cambio de despacho. El resultado no depende del frame (probado con frames de 1/60, 0,1, 0,7 y 5 s). Un servicio parcial no acredita nada.

## Escenario sintético (`Phase01_PowerCoop_Synthetic.asset`)

Rótulo en el asset: *«Fase 1 · Sala de energía (escenario sintético)»*. La procedencia dice explícitamente: «Valores ilustrativos… No representan hardware ni datos de la NASA». `MissionSceneComposition` registra ese texto en el log al componer.

| Parámetro | Valor | Parámetro | Valor |
|---|---|---|---|
| Energía inicial | 2400 J | Capacidad | 6000 J |
| Generación | 100 W | Carga esencial | 60 W |
| Ciencia | 80 W | Carga/descarga máx. | 100 / 200 W |
| Objetivo | 120 s de trabajo | Duración | 240 s simulados |
| `timeScale` | 2 (unos 2 min reales) | `maxStepSeconds` | 1 |

**Decisión comprobable (cálculo a mano, confirmado por los tests):**
- Con la ciencia encendida, la batería baja 40 W netos; con la ciencia apagada, se recarga a +40 W.
- Si se deja la ciencia siempre encendida, la batería se agota a los 60 s. Desde ahí la ciencia solo recibe 40 de 80 W y deja de progresar: el resultado es `TimeExpired` con 60 s de trabajo.
- Si se apaga la ciencia para recargar (60 s encendida, 60 s apagada, 60 s encendida), se logra el objetivo a t = 180 s.
- Si nunca se enciende la ciencia, la batería llega a 6000 J a los 90 s y el excedente se recorta: `TimeExpired` con 0 s de trabajo.
- La carga esencial (60 W) nunca supera a la generación, así que **siempre se sirve**: la prioridad se ve porque, con la batería vacía, se recorta la ciencia y no lo esencial. Este escenario no produce `Interrupted`, a propósito, para no mostrar «fallo de soporte vital» a niños de 7–12 años. El camino `Interrupted` sigue cubierto por tests con otras configuraciones.
- `PhaseOneScenarioValidator.ValidateConfiguration` (D5) da `OK` para esta configuración.

## Escena `Scenes/Phase01_PowerCoop.unity`

Escrita a mano en YAML a partir de `SampleScene` (cámara ortográfica y Global Light 2D de la plantilla, sin cambios). **Nunca se abrió en Unity** (ver «Pruebas»).

| Objeto | Activo al guardar | Contenido |
|---|---|---|
| `Main Camera`, `Global Light 2D` | sí | Copia literal de la plantilla. No se eligió encuadre ni cámara nueva |
| `MissionComposition` | sí | `MissionSceneComposition`: role = Authority, readCommandLine, bootstrap, hud, `authorityOnly = [MissionAuthority]`, `clientOnly = [MissionClient]` |
| `MissionAuthority` | **no** | `SimulationRunner` + `MissionBootstrap` (scenario = el asset, runner = el de este objeto). Es el **único** runner de la escena |
| `MissionClient` | **no** | Vacío: es el punto de conexión para el cliente de red de D3 |
| `MissionHUD` | sí | Instancia del prefab `Prefabs/UI/MissionHUD.prefab` (GUID `b7223d84…`, fileIDs 1000/1001/1002, que D4 conserva en su rama) |

**No se añadió a Build Settings**: `ProjectSettings/EditorBuildSettings.asset` es un ajuste global. Para que la build cargue esta escena hace falta aprobar ese cambio en la integración.

### `MissionSceneComposition`: una sola autoridad y un solo runner

- **Rol:** el valor del Inspector, que se puede sustituir con `-trainer-role authority|client` (también se aceptan `server` y `host`). Un player WebGL siempre es Client.
- En `Awake` desactiva los objetos del otro rol **antes** de activar los propios, así que un cliente nunca ejecuta el `Start` de los objetos de la autoridad (por ejemplo, el listener de red).
- **Authority:** en `Start` crea `new PowerSystem()`. Ese es el **único** lugar donde se instancia la simulación de D1. Después llama a `bootstrap.Compose(...)`, que vincula el único runner. Si `bindLocalHud` está activo, conecta el HUD al puerto local («Autoridad local: este proceso ejecuta la misión»). Ante `ShutDown`, libera el HUD.
- **Client:** no compone nada. `ComposeAuthority()` lanza una excepción y el runner queda sin vincular. `PresentRemoteSession(port, confirmed, detail)` conecta el HUD al confirmar el enlace, lo libera al perderlo y lo vuelve a conectar al reconectar.
- `Rules` y `DescribeOutcome(snapshot)`: en la autoridad, `DescribeOutcome` devuelve `MissionSession.Outcome`. En un cliente usa `Classify` con las reglas del asset de la build.

## Procedimiento de conexión final (en `integration/phase-01`, lo hace D2)

Solo D2 edita la escena. En la integración, el orden es: mezclar las ramas, reabrir la escena en Unity y aplicar estos pasos.

1. **D1:** no hay que hacer nada. La composición ya usa `PowerSystem` (está en la base; la rama de D1 solo mueve tests y actualiza su handoff). D1 debe seguir avanzándose solo desde `MissionSession`.
2. **D3, autoridad:** añadir `SessionNetworkHost` al objeto **`MissionAuthority`**, con `bootstrap` = el `MissionBootstrap` de ese mismo objeto. Sigue a `Composed`/`ShutDown` por sí solo y empieza a escuchar en su `Start`, que solo ocurre en la autoridad.
3. **D3, cliente:** añadir `SessionNetworkClient` al objeto **`MissionClient`**. En `MissionSceneComposition`, añadir `[SerializeField] SessionNetworkClient networkClient;` y, en el rol Client:
   ```csharp
   networkClient.ConnectionStatusChanged += (confirmed, detail) =>
       PresentRemoteSession(networkClient.Session, confirmed, detail);
   ```
   Para «unirse», activar `connectOnStart` o usar `-trainer-connect` / `?connect=1`, según la receta de D3. Hoy la UI no tiene botón de unirse; si se quiere uno, lo decide D4.
4. **D4:** instanciar `Prefabs/World/PowerRoom.prefab` y llamar a `room.Connect(hud, camera)` desde la composición: añadir `[SerializeField] PowerRoomView room; [SerializeField] Camera sceneCamera;` y hacer la llamada en `Awake`, para ambos roles. Encuadrar la sala a la derecha de `hud.PanelRect`. La posición y el tamaño de la cámara se fijan entonces, sin cambiar de tipo de cámara.
5. Añadir la escena a Build Settings (requiere aprobación).
6. Prueba de dos clientes (D5):
   - Una autoridad: `-batchmode -trainer-role authority` en Linux, o un host con HUD.
   - Dos clientes: `-trainer-role client` o WebGL.
   - Comprobar que ambos ven la misma época y revisión, que la ciencia gasta batería, el resultado y el Restart.
7. Ejecutar en el Test Runner los tests de EditMode (incluidos `MissionSceneCompositionTests` y `PhaseOneRegressionTests`).

## Coordinación pendiente (sin cambiar contratos)

1. **Con D1: el snapshot eléctrico no refleja un cambio de ciencia hasta el siguiente Advance.** `SetScienceEnabled` en Ready o Paused publica `ScienceEnabled` actualizado con el `PowerSnapshot` anterior. Ejemplo: en pausa, al apagar la ciencia se siguen viendo 80 W pedidos y servidos. `Advance(0, …)` no sirve, porque el contrato dice que cero no cambia nada. El validador de D5 lo marca como `ScienceRequestedWhileDisabled` (aviso, no error). Propuesta para una base coordinada: una consulta sin efectos en `IIntegratedPowerSimulation`, por ejemplo `PowerSnapshot Preview(bool scienceRequested)`, que la sesión publicaría al cambiar la ciencia. Mientras tanto, D4 podría rotular la potencia como «último paso medido».
2. **Contrato: el resultado no está en `MissionSnapshot`.** `Classify` lo deduce del estado, el trabajo y la potencia, pero necesita que el cliente y la autoridad usen el **mismo** asset de escenario. La misma build lo garantiza; builds distintas, no. Además, un Restart que falla queda ambiguo. Sigue pendiente la propuesta de añadir el resultado o el objetivo al snapshot. D4 puede usar `composition.DescribeOutcome(hud.Snapshot)` para mostrar «objetivo logrado / tiempo agotado» sin tocar el contrato.
3. **Permisos:** `MissionSession` no autoriza. Cualquier participante conectado puede pausar, reanudar, cambiar la ciencia o reiniciar. Falta la decisión del equipo; si hay política, la aplica D3 antes de `Submit`.
4. **Documentos comunes:** `AGENTS.md` y `SESSION_CONTRACT.md` siguen diciendo que no hay backend ni escena conectada. No los edité: son de coordinación.

## Pruebas realmente ejecutadas

Unity **no** se abrió ni se ejecutó (no hay `Library/`). Se usaron las herramientas que trae el propio Editor 6000.3.28f1: Roslyn `csc.dll`, .NET 6.0.21 y el `nunit.framework.dll` real de `com.unity.ext.nunit`. Todo se hizo en un scratchpad fuera del repo.

| Comprobación | Resultado |
|---|---|
| Compilar todo `Assets/_Project` no-Editor como Assembly-CSharp (player, `UNITY_EDITOR` y `UNITY_WEBGL`) con los módulos de UnityEngine/UnityEditor | Compila; solo los avisos CS0649 de `[SerializeField]` |
| Compilar las carpetas `Editor/` (validador de D5, tests) como Assembly-CSharp-Editor con NUnit real | Compila. Encontró y corregí una ambigüedad `SessionState` (UnityEditor) en mi test |
| Tests puros con NUnit real y un runner por reflexión: `MissionSessionTests` 21, `MissionOutcomeEvaluatorTests` 7, `MissionScenarioTests` 8, `FakeSessionPortTests` 6 | **Passed 42, Failed 0**. Una mutación deliberada en una copia hizo fallar el test esperado, así que el runner detecta fallos |
| Partida completa del escenario (encender, recargar, pausar, objetivo, Restart; 11 409 snapshots) pasada por `PhaseOneScenarioValidator` de D5 | 0 errores. 1 tipo de aviso: `ScienceRequestedWhileDisabled` (coordinación 1) |
| Coherencia del YAML de la escena y del asset: anclas únicas, referencias locales y GUIDs | Todo resuelve. Los 3 GUID externos son URP/builtin, igual que en la plantilla. No hay GUIDs duplicados en el repo |

**No verificado:**
- Importar la escena y el asset en Unity, la instancia del prefab, la serialización de `double`/`string` y el texto con acentos escapado.
- `MissionSceneCompositionTests` y `PhaseOneRegressionTests`: necesitan Unity.
- Play Mode, red, build y dos clientes.

Si al abrir la escena Unity informa de un problema de YAML, se recrea con los mismos objetos y referencias de la tabla de arriba. Los tests de composición fallarán si algo no coincide.
