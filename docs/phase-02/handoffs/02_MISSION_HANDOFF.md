# HANDOFF D2 — Tareas, misión y montaje de la escena (fase 2)

**Estado: escena `Phase02_InteriorBase` montada en integración (ver [Escena compartida](#escena-compartida-montada-en-integración)). La fase NO está cerrada: falta la partida en Unity con dos procesos.** La lógica de tareas, la
autoridad, el escenario y el bootstrap de autoridad están implementados y probados con dobles de prueba.
La escena compartida y la prueba con dos clientes reales dependen de entregas de D1, D3 y D4 que todavía no
existen en el repositorio (ver [Dependencias y bloqueos](#dependencias-y-bloqueos)).

- Rama: `feature/phase-02/d02-tareas`, creada desde la base común `95caa315527200bbbbcbb73fd654321d7992552e`.
- No se hizo ningún merge. No se tocaron archivos congelados, comunes ni de otros D.
- Fase 1 (`MissionSession`, `MissionOutcomeEvaluator`, `MissionBootstrap`, `MissionSceneComposition`,
  `SimulationRunner`, `ScenarioDefinition`, `Phase01_PowerCoop`) **no cambia**.

## Registro de archivos exclusivos

Registrado antes de editar. Todas las rutas son de D2 según `docs/phase-02/OWNERSHIP.md`. Prefijo omitido:
`Assets/_Project/`. Cada archivo nuevo lleva su `.meta`, igual que las carpetas nuevas.

| Área | Archivo |
| --- | --- |
| Application | `Runtime/Application/Outpost/OutpostMissionRules.cs` — `OutpostObjectiveRule`, `OutpostMissionRules` |
| Application | `Runtime/Application/Outpost/OutpostMissionEvaluator.cs` — fin de misión y comprobación previa de stock |
| Application | `Runtime/Application/Outpost/OutpostTaskBoard.cs` — libro de reservas estación↔persona |
| Application | `Runtime/Application/Outpost/OutpostAuthority.cs` — implementa `IOutpostAuthority` |
| Configuración | `Runtime/Unity/Configuration/Outpost/OutpostScenarioDefinition.cs` — ScriptableObject → `OutpostScenario` + reglas |
| Bootstrap | `Runtime/Unity/Bootstrap/Outpost/OutpostBootstrap.cs` — composición de la autoridad |
| Bootstrap | `Runtime/Unity/Bootstrap/Outpost/OutpostRunner.cs` — reloj único de la autoridad |
| Datos | `Data/Scenarios/Phase02/Phase02_InteriorBase_Synthetic.asset` — plano, tareas, recursos, misión (sintético) |
| Escena | `Scenes/Phase02_InteriorBase.unity` — **reservado, no creado**: se monta en integración |
| Pruebas | `Tests/EditMode/Application/Phase02/Editor/OutpostAuthorityTests.cs`, `OutpostTaskBoardAndRulesTests.cs`, `OutpostTestDoubles.cs` |
| Pruebas | `Tests/EditMode/Bootstrap/Editor/OutpostScenarioDefinitionTests.cs` |
| Documentación | `docs/phase-02/handoffs/02_MISSION_HANDOFF.md` |

`Runtime/Simulation/Tasks/**` queda vacío a propósito. Las tareas usan `StationId`, `PlayerId` y
`TaskDefinition`, que son tipos de Application, y Simulation no puede depender de Application. Por eso la
lógica de tareas vive en `Application/Outpost`.

## La misión interior (lo que juega el equipo)

Escenario `interior-base-synthetic`: un plano de 18 × 10 m con tres salas unidas por puertas de 3 m.

| Sala | Estación (`StationId`) | Tarea (`TaskTypeId`) | Tipo | Duración | Energía | Efecto al completar |
| --- | --- | --- | --- | --- | --- | --- |
| Hábitat (aparición) | `air-scrubber` (3, 8,6) | `check-cabin-air` | Soporte | 10 s | 40 W | ninguno (no mueve masa) |
| Planta de agua | `water-recycler` (9, 8,6) | `treat-water` | Soporte | 20 s | 150 W | −2,0 kg sin tratar, +1,8 kg potable, 0,2 kg de residuo sale del sistema |
| Laboratorio | `science-lab` (15, 8,6) | `run-experiment` | Ciencia | 30 s | canal científico de fase 1 | ninguno |
| Laboratorio | `sample-analyzer` (15, 1,4) | `analyze-sample` | Ciencia | 25 s | canal científico de fase 1 | ninguno |

- **Objetivos** (claves para los textos de D4):
  - `treat-water`: tratar agua 2 veces (obligatorio);
  - `check-cabin-air`: revisar el aire 1 vez (obligatorio);
  - `science`: 2 tareas científicas cualesquiera (opcional).
- **Fin**: la misión termina con `ObjectiveAchieved` en cuanto se cumplen los obligatorios. Si se agotan los
  240 s, termina con `TimeExpired`. Si la energía esencial deja de estar servida, con `EssentialServiceLost`.
- **Decisión para el equipo**: la ciencia opcional hay que hacerla antes de terminar el último obligatorio, y
  mientras corre ocupa a una persona, una estación y energía. Una persona hace una sola tarea a la vez.
- **Recursos**: el agua sin tratar inicial (6 kg) alcanza para 3 tratamientos si D1 no añade más. La comida
  (15 kg) no genera ninguna crisis artificial.
- **Valores**: todos son sintéticos y están rotulados en `provenance`. No son datos de la NASA. Con las cuatro
  estaciones activas la demanda supera la generación en 230 W. Cómo se reparte la energía lo decide el modelo
  de D1.
- **Mapeo a Unity**, decidido por D2 como pide el contrato: X del mundo = X del plano, Y del mundo = Y del
  plano, 1 unidad = 1 m, origen del plano = origen del mundo. Z solo sirve para el orden de dibujo.
- **Colisión**: los obstáculos del asset (paredes y consolas) son la única geometría de colisión. D4 dibuja de
  acuerdo con ella; los cambios de plano se piden a D2.

## Autoridad (`OutpostAuthority : IOutpostAuthority`)

Es dueña de la única `IOutpostSimulation` (D1) y del único `ICrewRoster` (D3), y es la única que los llama.
Decide la validación, las reservas, el progreso, el completado, `Apply` y el resultado. Nunca suma ni resta
stock y nunca mueve astronautas. Funciona en un solo hilo; el estado y el registro de comandos se confirman
antes de emitir eventos.

**Validación**: sigue exactamente el orden de `SESSION_CONTRACT.md`, y el primer fallo es el motivo:

1. `InvalidCommandId`: no se guarda.
2. Id repetido:
   - mismo remitente y mismo contenido: se reproduce el resultado guardado, sin publicar;
   - en cualquier otro caso: `CommandIdConflict`, que no se guarda.
3. Comprobaciones generales: `NotJoined`, `EpochMismatch`, `UnknownAction`, `InvalidActionValue`.
4. `Restart`: se acepta en cualquier estado.
5. `Pause`/`Resume`.
6. `StartTask`: `SessionEnded` → `InvalidState` → `UnknownStation` → `UnknownTaskType` →
   `TaskNotOfferedAtStation` → `StationUnavailable` → `PlayerBusy` → `StationOccupied` → `OutOfRange` →
   `InsufficientResources`.
7. `CancelTask`: `SessionEnded` → `InvalidState` (exige Running o Paused) → `UnknownStation` → `NoActiveTask`.

Detalles de la validación:

- **Proximidad**: siempre sobre la pose **confirmada** del roster (`IsWithinReach`). Los comandos no llevan
  coordenadas. Se comprueba al empezar y en cada paso.
- **Stock suficiente**: se comprueba contra el libro mayor **actual** del modelo (`IOutpostSimulation.Snapshot`).
  No reserva nada: al completar, `Apply` vuelve a decidir, todo o nada.
- **`StationUnavailable`**: ninguna regla de esta misión inutiliza una estación, así que nunca se emite. El
  único punto donde añadir una regla así es `IsStationAvailable`.

**Paso de la autoridad** (`Tick`): solo avanza en `Running`. Cada subpaso dura como máximo 0,25 s y se recorta
a la duración restante, al trabajo pendiente y a `SecondsUntilServiceChange`. En cada subpaso:

1. `roster.Advance(paso)`: los astronautas avanzan según su dirección sostenida.
2. Las tareas cuyo ejecutor quedó fuera del radio terminan con `PerformerMovedAway` y no consumen ese paso.
3. `simulation.Advance(paso, OutpostDemand(tripulantesPresentes, cienciaActiva, ΣW de soporte))`.
4. A cada tarea se le acreditan los segundos plenamente servidos:
   - ciencia: `LastFullyServedScienceSeconds`;
   - soporte con potencia: `LastFullyServedServiceSeconds`;
   - soporte de 0 W: el tiempo transcurrido.
   Una tarea no servida del todo no avanza y queda `Stalled`.
5. Una tarea que llega a su duración llama `Apply(ResourceTransfer(idDelStartTask, efecto))` una sola vez.
6. Se evalúa el fin de la misión: primero pérdida esencial, luego objetivos obligatorios, luego tiempo.

Cada llamada a `Tick` publica como máximo un snapshot. Las entradas de movimiento no publican por sí mismas:
el siguiente snapshot ya lleva la `LastInputSequence` nueva.

### Qué pasa al cancelar, alejarse, desconectarse o reiniciar

| Situación | Motivo (`LastEnd`) | Reserva estación/persona | Efecto de material | Energía gastada | Objetivo |
| --- | --- | --- | --- | --- | --- |
| Completa y `Apply` = `Applied` | `Completed` | liberada | aplicado una vez | consumida | +1 |
| Completa y `Apply` = `InsufficientStock`/`ExceedsCapacity` | `InsufficientResources` | liberada | nada (todo o nada) | no se devuelve | sin cambio |
| `CancelTask` del ejecutor (Running o Paused) | `CancelledByPerformer` | liberada | nada | no se devuelve | sin cambio |
| La posición confirmada sale del radio | `PerformerMovedAway` | liberada | nada | no se devuelve | sin cambio |
| `Leave` (desconexión): D3 lo llama al cerrar la conexión | `PerformerLeft` | liberada; el miembro sale de `Crew` y `CrewAboard` baja | nada | no se devuelve | sin cambio |
| La misión termina (objetivo, tiempo, pérdida esencial, fallo) | `MissionEnded` | liberada | nada | no se devuelve | sin cambio |
| `Restart` | se borra (sin `LastEnd`) | liberada | nada | el modelo hace `Reset` | vuelve a 0 |

Garantías frente a reservas huérfanas y cobros duplicados:

- `OutpostTaskBoard` ata estación y persona en una sola operación, y `End` libera las dos. Una segunda
  reserva o un segundo `End` lanzan una excepción.
- Tras `Leave` no queda demanda de servicio sin responsable: hay una prueba que lo verifica.
- El id de la transferencia es el del `StartTask`. Es único durante toda la vida de la autoridad, también
  tras `Restart`, así que ni D1 ni D2 pueden cobrar dos veces.
- Si `Apply` responde `AlreadyApplied` o `IdConflict` para una instancia nueva, el modelo y la autoridad no
  coinciden. Se trata como `SimulationFault`.
- **Fallo del modelo**: si `Advance`, `Reset` o el roster lanzan una excepción, la misión pasa a `Interrupted`
  con `SimulationFault`, las tareas terminan con `MissionEnded`, se publica el estado y la excepción se
  relanza. Desde ahí solo se acepta `Restart`. Repetir el mismo `Restart` no vuelve a reiniciar.

## Coordinación con D1 (recursos y energía)

D2 solo **declara** la demanda y **pide** aplicar efectos. Todo el cálculo de stock y energía es de D1.

- **Demanda por paso**: `CrewAboard` = miembros presentes; `ScienceRequested` = hay alguna tarea científica
  activa, aunque esté `Stalled`; `ServiceLoadWatts` = suma de W de las tareas de soporte activas.
- **Efectos vacíos**: D2 **no llama a `Apply`** con efectos vacíos (`ResourceEffect.None`), porque no mueven
  masa y no pueden fallar. Si D1 necesita verlos, se coordina; el contrato no lo exige.
- **Lo que D2 da por hecho de D1**, tal como lo describe el contrato. D5 debería comprobarlo:
  - `LastFullyServedServiceSeconds` = tiempo transcurrido cuando la carga de servicio pedida es 0;
  - `SecondsUntilServiceChange` = 0 si lo esencial ya está mal servido; infinito si el régimen es estable;
  - `Snapshot.Power.SimulatedTimeSeconds` avanza exactamente lo que dura el paso.

## Coordinación con D3 (jugadores y red)

- **Composición**:
  `OutpostBootstrap.Compose(scenario => simulaciónD1, scenario => rosterD3)`.
  - El roster debe construirse con `scenario.Layout` y `scenario.Movement`, y llegar vacío.
  - `Members` va en orden de `PlayerId`.
  - `Advance` solo se llama en `Running`.
- **Puente de host**: escucha `OutpostBootstrap.Composed`/`ShutDown` y usa `Authority`.
  - `Join()` al sincronizar una conexión y `Leave(id)` al cerrarla;
  - `Submit(id, comando)`;
  - `CommandCompleted(id, resultado)` va solo a ese remitente.
- **Cliente**: no compone nada. Recibe un `IOutpostSessionPort` del adaptador de red.
- `MovementInputDisposition` se devuelve tal cual del roster, para diagnóstico.

## Coordinación con D4 (base, astronautas y paneles)

- **Plano**: el arte se alinea con el plano del asset (coordenadas arriba). Ningún `Collider2D` decide
  posiciones.
- **Estados de estación**: solo `Available` u `Occupied` en esta misión. `ActiveTask.Fraction` da el progreso
  y `Stalled` indica falta de energía.
- **Mensaje de cada fin de tarea** (`LastEnd.Reason`): Completada, Cancelada, Se alejó, Se desconectó,
  Faltaron recursos, Fin de misión.
- **Textos**: los de los objetivos se buscan por clave (`treat-water`, `check-cabin-air`, `science`). Los de
  los rechazos, por `OutpostReasons`.
- **Prefabs**: D4 los entrega a D2. D4 no edita la escena.

## Coordinación con D5 (validación)

Propuesta de verificación (de D5, no es una regla nueva):

- puntos de aparición admisibles y estaciones alcanzables sobre el asset (D2 ya lo prueba);
- coherencia entre el plano y el arte de D4;
- regresión de los motivos de rechazo y de los fines de tarea con la autoridad real y la red real.

## Escena compartida (montada en integración)

Rama `feature/phase-02/d02-escena`, creada desde `origin/integration/phase-02` (`28472b8`, con D1–D5 ya
fusionados). **Montada y verificada fuera de Unity; la partida en Unity y entre dos procesos sigue pendiente.**

| Archivo (prefijo `Assets/_Project/`) | Qué hace |
| --- | --- |
| `Scenes/Phase02_InteriorBase.unity` | Cámara ortográfica existente (misma configuración que fase 1) + `LocalCameraFraming` (D4), luz global 2D, `OutpostComposition`, `OutpostAuthority` (inactivo: `OutpostRunner`, `OutpostBootstrap`, `OutpostNetworkHost`), `OutpostClient` (inactivo: `OutpostNetworkClient`), `CrewPresentation` (`CrewKeyboardInput`, `CrewAvatarSync` → `AstronautVisual`, padre `Crew` en el origen, sigue a la cámara) y las instancias de `BaseInterior` y `OutpostPanel` (D4). Sin `Collider2D`, `Rigidbody2D`, Canvas ni EventSystem. |
| `Runtime/Unity/Bootstrap/Outpost/OutpostSceneComposition.cs` | Rol con la misma regla y argumento que fase 1 (`MissionSceneComposition.ResolveRole`, `-trainer-role authority\|client`; WebGL siempre cliente). Autoridad: `OutpostBootstrap.Compose(OutpostBaseSimulation(SyntheticTraining), CrewRoster.For)` → `OutpostNetworkHost.Attach`; fuera de `-batchmode` el anfitrión también juega con `CreateLocalPort()`. Cliente: `OutpostNetworkClient.Prepare()`. El mismo puerto va a la entrada y avatares de D3, al panel/interior de D4 y a los controles de sesión. `[DefaultExecutionOrder(100)]`: si `Prepare` corriera antes que el `Start` del cliente de red, este ignoraría `-trainer-server/-trainer-connect`. |
| `Runtime/Unity/Bootstrap/Outpost/OutpostSessionControls.cs` | Franja IMGUI (fila libre y = 102–132 del lienzo 1280×720 del panel): **Iniciar misión** (`Resume` desde `Ready`), Pausar/Reanudar, Reiniciar y, en el cliente, Conectar/Desconectar. La autoridad empieza cada época en `Ready` y el panel de D4 solo cubre tareas: sin esto nadie podía empezar ni reintentar. Solo envía comandos con la época confirmada; una solicitud en vuelo. D4 puede sustituirla por botones diseñados. |
| `Runtime/Unity/World/Outpost/D3CrewVisualAdapter.cs` + `AstronautVisual.prefab` | Paso de integración que el HANDOFF de D4 asigna a D2 (menús *Install D3 visual adapter* y *Attach D3 adapter*), hecho a mano: copia byte a byte de `D3CrewVisualAdapter.cs.txt` y el componente añadido a la raíz del prefab. Repetir los menús en Unity es idempotente. |
| `Tests/EditMode/Bootstrap/Editor/Phase02InteriorMatchTests.cs` | Partida completa de dos jugadores sobre la pila real (ver abajo). |

`FakeOutpostSessionPort` y `OutpostFixtures` no aparecen en `Runtime/Unity`. `Phase01_PowerCoop` no cambia.
`ProjectSettings/EditorBuildSettings.asset` (común) no se tocó: las builds usan listas explícitas de escenas.

### Partida de dos jugadores comprobada (memoria, un proceso)

`Phase02InteriorMatch.Play` usa el asset real, la autoridad compuesta con las **mismas fábricas que la escena**,
el `OutpostCommandBridge`, el cable `'T' 'O'`, el controlador/adaptador de cliente y `CrewInputSender` reales de D3,
y el `LocalOutpostSessionPort` del anfitrión. Solo el transporte es en memoria (`InMemoryOutpostNetwork` de D3).
Recorre: unión con identidades distintas en sus spawns; tarea en `Ready` → `InvalidState`; iniciar; estación lejana →
`OutOfRange`; caminar por las puertas sin atravesar paredes y verse mutuamente; `StationOccupied` y `PlayerBusy`;
agua (−2,0/+1,8 kg una sola vez) dos veces y ciencia en paralelo; mismo estado en ambos jugadores; desconexión durante
una tarea → estación libre (`PerformerLeft`) y sin fantasma; reconexión con id nuevo en su hueco; alejarse →
`PerformerMovedAway` sin efecto; revisar el aire → `ObjectiveAchieved` antes de 240 s; `SessionEnded` tras el fin;
`Restart` por el cliente → época nueva, `Ready`, stocks y posiciones iniciales; el movimiento sigue en la época nueva.

Cómo jugarla de verdad (pendiente, necesita Unity):

```sh
# En el Editor: abrir Phase02_InteriorBase y pulsar Play = anfitrión que juega (puerto 7777).
# Build con la herramienta de D5 y dos procesos:
Unity -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.BuildFromCommandLine \
  -phase2Scenes Assets/_Project/Scenes/Phase02_InteriorBase.unity -logFile Logs/phase2-build.log
<build> -trainer-role authority -trainer-port 7777
<build> -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect
```

Son los argumentos por defecto de `Tools/Trainer/Phase 2/Launch last build twice` de D5. Rellenar la lista de
comprobación de `05_VALIDATION_HANDOFF.md` con dos personas.

Limitaciones conocidas:

- El arte de estaciones se genera en `Configure` al componer; en la escena guardada solo está la vista previa de D4,
  así que la auditoría «station drawn on plan» de D5 quedará Pending fuera de Play.
- La cámara sigue al astronauta local (D3) con altura visible de 9,5 m (D4); el plano mide 18 × 10 m.
- Los textos de tarea de `OutpostPanel` no traducen `analyze-sample` (muestra la clave); es de D4.

## Verificación ejecutada

Fuera de Unity, con el Roslyn 4.3.1 (C# 9) y el runtime .NET 6.0.21 que trae el Editor 6000.3.28f1, y con
el `nunit.framework.dll` de `com.unity.ext.nunit`. Las pruebas se ejecutaron con un runner por reflexión que
vive fuera del repositorio. Nada de esto equivale al Unity Test Runner.

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` compilados solo contra `netstandard.dll`, con `-warnaserror+` y `-doc` (CS1591/CS1573 excluidos, como en fase 1) | exit 0 |
| Pruebas nuevas de D2 (`Tests/EditMode/Application/Phase02/Editor/`) | 36/36 |
| Regresión pura: contratos de fase 2 (34) + `MissionSession` 21, `MissionOutcomeEvaluator` 7, `FakeSessionPort` 6, `PowerSystem` 30, `SimulationClock` 5 | 103/103 (139/139 con las de D2) |
| Sensibilidad: 9 mutantes sobre una copia de `OutpostAuthority` (sin `PlayerBusy`, sin proximidad continua, deduplicación que ignora al remitente, objetivo contado aunque `Apply` falle, `Leave` que no cancela, progreso sin servicio, id de transferencia aleatorio, `Restart` sin limpiar el tablero, orden `OutOfRange`/`StationOccupied`) | los 9 detectados |
| Código Unity de D2 (`Configuration/Outpost`, `Bootstrap/Outpost`) + `OutpostScenarioDefinitionTests`, compilados contra las DLL `UnityEngine*`/`UnityEditor*` del Editor con `UNITY_EDITOR`, `UNITY_WEBGL` y sin define | exit 0 en los tres. El único aviso es CS0649 en campos `[SerializeField]`, como en fase 1 |
| Archivos congelados (`git diff --stat 95caa31 -- <congelados>`) | vacío |

Una prueba encontró un fallo real, que se corrigió: la comprobación previa de `InsufficientResources` leía el
último snapshot publicado en lugar del stock actual del modelo.

**No ejecutado (bloqueado):** el Editor no arranca desde el entorno de esta sesión
(`error while loading shared libraries: libGL.so.1`). Por eso **no** se verificó nada de lo siguiente:

- la importación en Unity: AssetDatabase, `.meta` creados a mano y el YAML del asset;
- el Test Runner, incluidas las 5 pruebas de `OutpostScenarioDefinitionTests`, que cargan el asset real y
  juegan la misión completa;
- la regresión de fase 1 que depende de Unity;
- la build.

Para ejecutarlo desde una terminal normal:

```sh
~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity -batchmode -nographics -projectPath ~/Escritorio/nasa-space-app \
  -runTests -testPlatform EditMode -testResults /tmp/d2-editmode.xml -logFile /tmp/d2-editmode.log
```

### Verificación del montaje de la escena (2026-11-14)

Fuera de Unity, con el Roslyn y el .NET 6.0.21 del Editor 6000.3.28f1. Burst, Collections, Mathematics, Transport
2.7.4 e Input System 1.20.1 se compilaron como referencias desde los `.tgz` del Editor, en una carpeta temporal fuera
del repositorio. Nada de esto equivale a importar en Unity ni al Test Runner.

| Comprobación | Resultado |
| --- | --- |
| `Assembly-CSharp` completo (todo `Assets/` salvo carpetas `Editor/`) como player Linux, WebGL y con `UNITY_EDITOR` | exit 0 en los tres; 0 errores; los 51 avisos son CS0649/CS0414 de campos serializados |
| Integridad del YAML de la escena: referencias locales y anclas | 35 objetos, 0 referencias rotas, 0 anclas duplicadas |
| GUID de los `.meta` nuevos | únicos en `Assets/` |
| `Phase02InteriorMatch.Play` con el asset real (cargado por reflexión, sin `AssetDatabase`) | **pasa** |
| Sensibilidad: puente que no llama a `Leave` al desconectar; expectativa cambiada `StationOccupied`→`PlayerBusy` | ambos detectados |
| Contratos congelados, `Phase01_PowerCoop`, `ProjectSettings`, `Packages`, `Runtime/Application`, `Runtime/Simulation` | sin cambios |

**No ejecutado (bloqueado):** el Editor no arranca desde esta sesión: el sandbox impide acceder a `libGL.so.1`/GTK
y a la carpeta de licencia de Unity. Sin verificar: importación de la escena, prefab y `.meta`; Test Runner (incluida
la nueva prueba con `AssetDatabase`); `Assembly-CSharp-Editor`; render, teclado, cámara; build; sockets; dos procesos.

## Dependencias y bloqueos

- **Cierre de la fase bloqueado por Unity**, ya no por entregas: falta importar, ejecutar el Test Runner, abrir la
  escena, generar la build y jugar con dos procesos. Ni la escena YAML ni la partida en memoria lo demuestran.
- No se pidió ningún cambio de contrato. Las decisiones que el contrato deja a D2 están documentadas arriba:
  - no hay estaciones `Unavailable`;
  - no se llama a `Apply` con efectos vacíos;
  - la posición se comprueba tras moverse y antes de consumir energía;
  - la entrada de movimiento no publica sola.
- Los permisos de pausa y reinicio por rol siguen sin decidir, como en fase 1: cualquier miembro presente
  puede usarlos.
