# Fase 4 — Integración (montaje D1–D5)

**Estado: montaje real hecho y compilado; puertas de prueba NO ejecutadas.** La escena productiva existe y está conectada
a la autoridad continua (D2), la red (D3), las vistas (D4) y el piloto (D5). A mitad del trabajo el usuario pidió **no
ejecutar más pruebas de Unity salvo lo imprescindible**. Por eso solo se ejecutó el builder, que compila todo el proyecto
y es la única forma de generar la escena. EditMode, validadores, PlayMode, build y partida entre procesos **no se
ejecutaron**. No se declara aceptada la fase.

Prefijo omitido en rutas: `Assets/_Project/`.

## Git

| Dato | Valor |
| --- | --- |
| Remoto | `git@github.com:SandwichMr/nasa-space-app.git` |
| Rama | `integration/phase-04-assembly` (rastrea `origin/integration/phase-04-assembly`) |
| Base publicada al empezar | `6a458ca21d89197361c02a16c60f30f346e07dc6` (fast-forward, sin divergencia) |
| D1 | `bdbce8778723376d4695f1fd3cdea056ce78a98f` (ancestro de HEAD) |
| D2 | `e6f5e16f2e175630b840d59312b8135c3a378ca0` (ancestro; incluye `69464b5`, arreglo de `EndedAtSeconds`, conservado) |
| D3 | `b532fdacd41cfd1af8f8396ef5aa9e21b3e8ce1d` (ancestro) |
| D4 | `70f3478245f3956cbafcc1b2be536d6232e91206` (ancestro) |
| D5 | `c9db51e7c83f439019c38f0693a3caac8f08cbe4` (ancestro) |
| `origin/main` | `b762675`, ya contenido en la rama |
| Commits posteriores en ramas D1–D5 | ninguno (`git rev-list --count integration..feature/phase-04/dNN` = 0 en las cinco) |
| Commit final publicado | ver el mensaje de entrega (último commit de la rama) |

Checkout usado: el de esta máquina (`/home/jhazeel/code/nasa-space-app`), no la laptop de Leo. Antes de empezar no había
operaciones Git pendientes, Editor abierto ni otro agente escribiendo. Había cambios locales que Unity reescribe:
`.vscode/settings.json`, `ProjectSettings/ProjectSettings.asset` y `ProjectSettings/SceneTemplateSettings.json` (sin
rastrear). Se conservaron sin subirlos (OWNERSHIP: rutas comunes).

## Cadena de montaje real

`Editor/Composition/Phase04/Phase04SceneBuilder.cs`. Menú: *Tools/Trainer/Phase 4/Build scenario asset and scene*.
CLI:

```
Unity -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.Composition.Phase04.Phase04SceneBuilder.BuildFromCommandLine -logFile Logs/phase04-integration/03-scene-builder.log
```

Genera `Scenes/Phase04_LivingBase.unity` y reutiliza `Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset` (GUID
conservado; no se regenera sin `-phase4ResetScenario`).

| Objeto | Componentes | Rol |
| --- | --- | --- |
| `Main Camera` | Camera ortográfica, `BaseOperationsCameraFraming` (D4) enlazado al panel | Encuadre; **sin** `LocalCameraFraming` |
| `Global Light 2D` | Light2D global | — |
| `BaseOperationsComposition` | `BaseOperationsSceneComposition` (D2), `BaseOperationsAutopilot` (D5, inerte sin `-trainer-autopilot continuity`) | Raíz |
| `BaseOperationsAuthority (authority only)` | `BaseOperationsRunner`, `BaseOperationsBootstrap` (D2), `BaseOperationsNetworkHost` (D3) | Un reloj, una autoridad, un host |
| `BaseOperationsClient (client only)` | `BaseOperationsNetworkClient` (D3) | Solo en el rol cliente |
| `CrewPresentation` | `CrewKeyboardInput` (E off), `CrewAvatarSync` (astronauta de fase 3), `ExpeditionInteractionInput` (E off, R on), `BaseOperationsInteractionInput` (D3, E), `ExpeditionCrewPresenter` | Un solo controlador de movimiento |
| `BaseInterior` | prefab de fase 2 | Plano |
| `SessionControls` | `OutpostSessionControls` en el lienzo 1440×900 de D4, fila `y=42` | Iniciar, pausar, reiniciar, conectar |
| `Phase03 Presentation Mount` | `ExpeditionWorld` (esclusa, refugio, marcos) | `ExpeditionPanel` retirado (receta D4) |
| `Phase04 Presentation Mount` | `BaseOperationsPanel`, `BaseOperationsWorld` (D4; `baseInterior` asignado) | Barras, estaciones, decisiones |

### Roles

- **Autoridad**: `Bootstrap.Compose` → `NetworkHost.Attach(authority)`.
  - Fuera de `-batchmode`, el anfitrión también juega con `host.CreateLocalPort()` (`LocalBaseOperationsSessionPort` de D3, miembro normal).
  - En `-batchmode` es servidor dedicado, sin jugador.
- **Cliente**: `NetworkClient.Prepare()`. El escenario solo se usa para conocer el plano.
  - Ni autoridad, ni runner, ni reloj, ni inventario: el objeto authority-only queda inactivo.
- Un único puerto va a movimiento, avatares, R, E de fase 4 (que cae a la interacción de fase 3 lejos de estaciones de
  fase 4), presentador, controles de sesión y todas las vistas `IExpeditionView` / `IBaseOperationsView`.
- **E en una estación de fase 4** abre el panel D4 de esa estación (`StationActivated` → `OpenNearbyStation`). La tecla
  nunca elige una decisión.
- **Bloqueo simétrico**: el mismo dueño bloquea movimiento, E/R de fase 3 y E de fase 4; `Unblock` libera los tres. No
  hay controlador nuevo.
- **Fakes**: ningún fake en el flujo productivo. `Provisional/` de D2 se borró: ya no lo referenciaba nada.

## Archivos modificados y por qué

| Archivo | Dueño | Cambio |
| --- | --- | --- |
| `Runtime/Unity/Bootstrap/BaseOperations/BaseOperationsSceneComposition.cs` | D2 | Receta D2 aplicada: host/cliente/puerto local/E de D3, vistas D4, bloqueo de las tres entradas, E → panel, estado del host |
| `Editor/Composition/Phase04/Phase04SceneBuilder.cs` | D2 | Monta la cadena de arriba; prefabs D4 obligatorios; sin vista provisional ni `ExpeditionPanel` |
| `Runtime/Unity/Bootstrap/BaseOperations/Provisional/**` | D2 | Borrado (paso 5 de la receta D2) |
| `Scenes/Phase04_LivingBase.unity` (+ `.meta`) | D2 | Generada por el builder |
| `Runtime/Unity/Bootstrap/Outpost/OutpostSessionControls.cs` | D2 de fase 2 (no congelado) | **Transversal**: `ConfigureCanvas(w, h, y)` opcional. Por defecto 1280×720, `y=102`, bit a bit igual que antes. Motivo: el HUD de D4 (bandas 0–193 en 1440×900, pintadas opacas a `GUI.depth -10`) tapaba la fila de sesión. Sin ella no se ve «Iniciar misión» ni «Reiniciar», y la autoridad empieza cada época en Ready |
| `Runtime/Unity/Presentation/BaseOperations/BaseOperationsPanel.cs` | D4 | Ver «Correcciones» |
| `Runtime/Unity/Presentation/BaseOperations/BaseOperationsText.cs` | D4 | Tendencias pequeñas por minuto u hora; textos de todas las claves de decisión de D2 |
| `Runtime/Unity/Networking/BaseOperations/BaseOperationsCommandBridge.cs` | D3 | `Leave` diferido e idempotente tras un fallo de envío |
| `Runtime/Application/BaseOperations/BaseOperationsAuthority.Operations.cs` | D2 | Precondición del lote en el equipo **antes** de descontar el agua sin tratar |
| `Editor/Build/Phase03/Phase03BuildChecks.cs` | D5 de fase 3 | Escáner de fakes: exime solo las dos carpetas de fixtures de contratos (`Contracts/Expedition/Development/`, `Contracts/BaseOperations/Development/`). El resto de `Runtime/**` se sigue escaneando |
| `Editor/Validation/Phase04/BaseOperationsAuthorityConformance.cs` | D5 | Un id reutilizado por otro remitente exige `CommandIdConflict`, y ninguna respuesta extra al remitente original |
| `Runtime/Unity/Validation/Phase04/BaseOperationsContinuityScript.cs` | D5 | El piloto falla si el reloj simulado lleva más de 15 s reales sin avanzar al final |
| `Tests/PlayMode/Integration/Phase04/Phase04SceneIntegrationTests.cs` | D5 | No compilaba (ver «Puerta 1») |
| `Tests/EditMode/Phase04/Networking/Editor/Phase04HostSendFailureTests.cs` | D3 (nuevo) | Regresión del arreglo del puente |

No se tocaron contratos ni documentos congelados, `Packages/`, `ProjectSettings/`, el renderer, escenas de fases 1–3 ni
`EditorBuildSettings`.

## Correcciones

### D2 / D1

1. **Transferencia base → equipo.** Al completar `ProcessBatch`, la autoridad descontaba el agua sin tratar y después
   pedía `ReceiveBatch`. Si el equipo rechazaba (retención no vacía, o producto por encima de la capacidad), el agua se
   perdía y la excepción de `Expect` dejaba la sesión en `SimulationFault`.
   - Ahora se comprueban antes las precondiciones de D1: retención vacía y `lote × recuperación ≤ capacidad`.
   - Si fallan, la operación termina `Failed / equipment-refused` sin mover masa.
   - Es endurecimiento: con el escenario enviado no hay ruta de producción que lo dispare, porque el inicio ya exige la
     retención vacía y 2 kg × 0,9 ≤ 2 kg. **No se escribió una prueba roja**, porque el escenario válido no la permite.
2. **CO2 y partición temporal: analizado, no cambiado.**
   - Hipótesis confirmada en el código: la autoridad aplica a todo el paso el factor publicado **al final** del paso de
     equipo (`SESSION_CONTRACT.md` §6, paso 2, congelado). Entre la rodilla y la saturación el factor cae de forma
     continua, y un evento de entrada al final del paso se aplica a todo el paso anterior.
   - Cota: la diferencia de CO2 retirado entre particiones es ≤ `MaxCO2Removal × (1 − SaturatedRemovalFactor) × maxStep`
     por tramo. Con `maxStep = 0,25 s` equivale a menos de 0,25 s de retirada máxima en un tramo de unos 100 s
     (2 tripulantes; rodilla 0,6 → 1 a 0,004/s).
   - El factor medio exacto del paso (trapecio entre cortes) eliminaría el efecto, pero cambia la regla congelada §6. Hace
     falta una base coordinada en un commit aparte: **no hecha**.
   - **Sin prueba acoplada ejecutada.**
3. **EVA: no verificado.** No se comprobó que las pruebas «EVA» ejerciten trajes y partición de población (falta de
   tiempo; pendiente).

### D3

- **Puente del host.** Un `SendToClient` que lanzaba solo quitaba la conexión de `connected` y llamaba a
  `DisconnectClient`. El `Leave` dependía de un `ClientDisconnected` posterior, que no llega si `DisconnectClient` lanza o
  si el transporte se libera antes de su siguiente `Update`. El resultado era un miembro fantasma (ocupación y reservas)
  hasta el `Dispose`.
  - Ahora la conexión fallida se apunta y se libera **una vez** en `FlushSnapshots`, fuera del callback de la autoridad.
  - Es idempotente con `OnClientDisconnected` y `Dispose`.
  - Regresión: `Phase04HostSendFailureTests.AFailedSendReleasesTheMemberOnceWithoutADisconnectEvent`. Con el código
    anterior es roja por diseño (`LeaveCount` 0). **No ejecutada.**
- **Revisado por lectura, sin cambio:**
  - el límite de trama al recibir (`PayloadTooLarge` > 256 B en el host, reensamblado ≤ 32 KB en el cliente);
  - la deduplicación por `(Id, Epoch)` en el cliente y por id/remitente en la autoridad.
  - D3 los midió en su rama: EditMode 436/437 y PlayMode 75/75. No se volvieron a ejecutar sobre el conjunto.

### D4

1. **Fila de sesión tapada.** Se describe en la tabla de archivos. El panel deja libre `y 42–76` y compacta las cinco
   tarjetas a 93 px (`y 80–173`). La banda superior sigue en 193, así que el encuadre de D4 no cambia.
2. **El aviso de refugio quedaba tapado.** El pie mostraba `Feedback → tooltip → aviso`, y una respuesta Accepted o un
   rechazo se quedaba para siempre encima del aviso.
   - Ahora un aviso **recibido** tiene su propia línea (ámbar) con el estado de protección.
   - La respuesta o el tooltip van debajo.
3. **Decisiones concurrentes.** El panel solo mostraba la última decisión global, y únicamente si era de la máquina
   abierta. Ahora busca la última decisión **de esa máquina** y muestra quién y cuándo.
4. **Tendencias pequeñas.** Antes se veía «− BAJA 0 kg/s». Ahora se usan `/min` o `/h` cuando `/s` redondea a 0; la
   energía sigue en J/s.
5. **Claves de D2.** Las 15 claves de explicación de D2 tienen texto, en lugar del genérico «Resultado confirmado: clave».
6. **Datos EVA al retirar `ExpeditionPanel`.**
   - El pie muestra margen y protección con traje.
   - «Recursos y actividad» añade objetivos, esclusa, refugio, tu margen y protección, y el estado del traje con sus
     servicios.
   - Los botones de `ExpeditionPanel` (interactuar, regreso) se sustituyen por E/R.

### D5

1. **No compilaba.** `Phase04SceneIntegrationTests` usaba `[UnityTest]`. Sin asmdef de pruebas, `Assembly-CSharp` no
   referencia `UnityEngine.TestRunner` (`autoReferenced: false`).
   - La prueba de prefabs D4 con lecturas desconocidas pasa a `[Test]` síncrono, con las mismas aserciones.
   - La de la escena productiva en Play Mode se retiró del archivo. Hay que ejecutarla contra la build (puertas 5–6),
     porque no puede esperar frames sin `[UnityTest]`. **Pendiente.**
2. **Id reutilizado por otro remitente.** Ahora se exige el rechazo contractual. Antes se aceptaba «Accepted» si había
   una sola respuesta.
3. **Piloto congelado.** El piloto ya no aprueba si al final el estado está congelado.
4. **Escáner de fase 3.** Clasificaba mal el fixture de fase 4. Se corrige de forma precisa (ver arriba); la prueba
   `Phase03SceneTests.TheShippedSceneIsWiredWithoutPhysicsFakesOrASecondCable` es la regresión (fallaba en la medición
   de D3). **No ejecutada.**
5. **Pendiente en el tooling D5:**
   - balances completos de energía y masa con trajes;
   - EVA dentro del modo continuo (sigue *Pending* en la conformidad);
   - lecturas Stale/Unavailable en pantalla;
   - separar con claridad la build diagnóstica de la aceptada (no se tocó `Phase04BuildChecks`).

## Puertas: qué se ejecutó

Unity 6000.3.28f1 (`~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity`). Módulos: Linux Standalone (IL2CPP), WebGL, Android y
visionOS. Ningún Editor abierto sobre el proyecto. Logs en `Logs/phase04-integration/` (ignorado por git), con
`runs.txt`.

| # | Puerta | Ejecutada | Resultado real |
| --- | --- | --- | --- |
| 1a | Importación y compilación de la base fusionada `6a458ca` (`-quit`) | sí | **exit 1**: 4 errores únicos `CS0246 UnityTest` en `Phase04SceneIntegrationTests.cs` (`01-import-baseline.log`). La base fusionada **no compilaba** |
| 1b | Builder tras las correcciones, intento 1 | sí | **exit 1**: `CS0234 SuitStatus` (namespace) en mi cambio del panel (`02-scene-builder.log`). Corregido |
| 1c | Builder tras las correcciones, intento 2 (compila `Assembly-CSharp`, `Assembly-CSharp-Editor` y las pruebas, y guarda la escena) | sí | **exit 0**, 0 líneas `error CS`. `[Phase04SceneBuilder] Saved Assets/_Project/Scenes/Phase04_LivingBase.unity …` (`03-scene-builder.log`). Escena 35 825 B, sha256 `1cacbbbd85c233be080dd17fbb698c3632189b33ded230e6ff4ec5b69b7558d6`. YAML revisado: todas las referencias de la composición asignadas, `interactWithE` = 1 solo en `BaseOperationsInteractionInput`, `baseInterior` del mundo D4 = interior de la escena, `authorityOnly`/`clientOnly` asignados. Compilar no es una prueba: no hay ni una aserción ejecutada |
| 2 | EditMode completo y regresión de fases 1–3 | **no** (instrucción del usuario) | — |
| 3 | Validadores `Phase04BuildChecks.RunChecksFromCommandLine` y los de fases 2–3 | **no** | — |
| 4 | PlayMode | **no** | — |
| 5 | Build de la escena productiva | **no** | sin artefacto ni hash |
| 6 | Autoridad y dos clientes de ESA build | **no** | — |
| 7 | Gráficos, teclado y personas; 30 min reales | **no** | — |

### Comandos pendientes, en orden

Desde la raíz, con el Editor cerrado:

```sh
U=~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity; L=Logs/phase04-integration
"$U" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults $L/editmode-all.xml -logFile $L/editmode-all.log
"$U" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testCategory Phase04Long -testResults $L/editmode-long.xml -logFile $L/editmode-long.log
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunChecksFromCommandLine -logFile $L/d5-checks.log   # 0 pasa, 1 falla, 2 pendiente
"$U" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults $L/playmode.xml -logFile $L/playmode.log
"$U" -batchmode -nographics -projectPath . -buildTarget Linux64 -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.BuildFromCommandLine -logFile $L/build.log
```

No usar `-phase4BuildAnyway` para certificar. Una build forzada solo es diagnóstica.

Receta de host y clientes para ESA build (argumentos de D3; `P` = el ejecutable que escriba `build-info.txt`):

```sh
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7795 -logFile $L/authority.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect -trainer-autopilot continuity -trainer-autopilot-role water -trainer-autopilot-minutes 30 -logFile $L/water.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect -trainer-autopilot continuity -trainer-autopilot-role air -trainer-autopilot-minutes 30 -logFile $L/air.log
```

Con personas: la misma autoridad y dos clientes con gráficos, sin `-trainer-autopilot`. El anfitrión con gráficos
también juega por su puerto local. Un piloto en `-nographics` no prueba comprensión, diversión ni teclado humano.

## Continuidad

**Nada ejecutado.** No hay tiempo real ni simulado medido, incidentes, recuperaciones ni reposiciones registradas.
Falta la meta del roadmap: al menos 30 minutos reales con dos clientes.

## Decisiones abiertas y bloqueos

- **Ejecución**: todas las puertas 2–7 están pendientes por instrucción explícita del usuario. La escena compila y se
  generó (puerta 1c), pero nada prueba que funcione en Play Mode.
- **Avisos de refugio**: el escenario de fase 4 tiene `keepPhase3Episode = false`, así que en la sesión continua **no se
  emite ningún aviso**. El panel los muestra si llegan, pero falta una decisión de escenario (D2) para ejercitarlos en
  fase 4.
- **CO2**: un factor medio exacto exige una base coordinada de `SESSION_CONTRACT.md` §6 (congelado).
- **Residuo (salmuera)**: no está publicado en el snapshot congelado (aviso de D4); hace falta una extensión coordinada
  si se quiere en la UI.
- **`AGENTS.md`** sigue diciendo «No networking backend is selected yet»; Unity Transport está aprobado desde
  2026-11-14. No se tocó, porque es una ruta común.
