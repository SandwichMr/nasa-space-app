# HANDOFF — D3 fase 2: jugadores, movimiento, entrada y red

## Contexto

- Rama `feature/phase-02/d03-jugadores`, creada desde el commit común `95caa315527200bbbbcbb73fd654321d7992552e` (`main`). **Sin commit, merge ni push.**
- Uso los contratos congelados de `docs/phase-02/SESSION_CONTRACT.md` sin modificarlos. No toqué escenas, arte, prefabs, paquetes, ProjectSettings ni archivos de fase 1.
- Se mantiene el SDK: Unity Transport 2.7.4 sobre WebSocket, con los mismos `UnityTransportHost` y `UnityTransportClient` de fase 1, sin cambios. El cable de fase 1 (`'T' 'S'`) tampoco cambia.

## Archivos exclusivos de D3 (todos nuevos, con `.meta`)

Todos están dentro de las rutas de D3 en `OWNERSHIP.md`. Prefijo: `Assets/_Project/`.

| Archivo | Tipo | Contenido |
| --- | --- | --- |
| `Runtime/Application/Crew/CrewRoster.cs` | C# puro | `ICrewRoster`: identidad, huecos, presencia y movimiento confirmado |
| `Runtime/Application/Crew/CrewMotionSolver.cs` | C# puro | Única resolución de colisiones: disco contra plano, con deslizamiento |
| `Runtime/Application/Crew/CrewInputSender.cs` | C# puro | Ejes → `MovementInput` del jugador local: secuencia, sin inundar la red, bloqueo |
| `Runtime/Application/Crew/CrewInterpolator.cs` | C# puro | Suavizado de poses confirmadas (solo presentación) |
| `Runtime/Application/Crew/StationInteractionClient.cs` | C# puro | Solicitudes StartTask/CancelTask hacia la autoridad de D2 |
| `Runtime/Unity/Networking/Outpost/OutpostWireProtocol.cs` | interno | Cable de fase 2 `'T' 'O'` v1, con troceado |
| `Runtime/Unity/Networking/Outpost/OutpostCommandBridge.cs` | sin UnityEngine | Puente de host: conexión ↔ `PlayerId`, `Join`/`Leave` y enrutado |
| `Runtime/Unity/Networking/Outpost/OutpostNetworkSessionAdapter.cs` | sin UnityEngine | `IOutpostSessionPort` de cliente, `OutpostConnectionController` y `OutpostConnectionStatus` |
| `Runtime/Unity/Networking/Outpost/LocalOutpostSessionPort.cs` | sin UnityEngine | `IOutpostSessionPort` para el jugador del proceso host |
| `Runtime/Unity/Networking/Outpost/OutpostNetworkHost.cs` | MonoBehaviour | Servidor: transporte + puente + autoridad de D2 |
| `Runtime/Unity/Networking/Outpost/OutpostNetworkClient.cs` | MonoBehaviour | Cliente desktop/WebGL |
| `Runtime/Unity/Networking/Outpost/CrewAvatarSync.cs` | MonoBehaviour | Un avatar por miembro, suavizado y cámara solo para el local. Define `ICrewAvatarView` y `CrewAvatarFrame` para D4 |
| `Runtime/Unity/Input/CrewKeyboardInput.cs` | MonoBehaviour | WASD/flechas y E con el Input System (manejador activo del proyecto). Namespace `Trainer.Unity.Controls`, para no tapar `UnityEngine.Input` |
| `Tests/EditMode/Crew/Editor/*.cs` (6) | pruebas | Roster, ayudantes del cliente y red en memoria, más dos dobles de prueba rotulados |
| `Tests/PlayMode/Networking/OutpostTransportSocketTests.cs` | pruebas | Sockets reales, bajo `UNITY_INCLUDE_TESTS` como en fase 1 |
| `docs/phase-02/handoffs/03_NETWORKING_HANDOFF.md` | doc | Este documento |

GUID de los MonoBehaviour, para que D2 los referencie desde la escena:

| Componente | GUID |
| --- | --- |
| `OutpostNetworkHost` | `1f65aea2a1ad42c884e599c5e407c0cc` |
| `OutpostNetworkClient` | `2d5f7ddf89fd4e6697eb8e46d0ed2f51` |
| `CrewAvatarSync` | `1c5347ada6294a948a5d216ad4ecbc6b` |
| `CrewKeyboardInput` | `ebf0037a07a74edaa04c7a64b43f6484` |

No hay prefab técnico: `Prefabs/**` es de D4. D2 añade estos cuatro componentes a la escena, o a un prefab que pida a D4.

## Arquitectura

```
Proceso autoridad (servidor Linux o host con jugador)            Cliente (desktop o WebGL)
 D2 OutpostSession : IOutpostAuthority ── D3 CrewRoster           CrewKeyboardInput ─► CrewInputSender ─┐
   (dueño único; llama Roster.Advance en su tick)                    E ─► StationInteractionClient ──────┤ Submit/SubmitMovement
   ▲ Join/Leave/Submit/SubmitMovement │ SnapshotChanged/CommandCompleted                              ▼
 OutpostNetworkHost ─ OutpostCommandBridge ─ UnityTransportHost ═WS═ UnityTransportClient ─ OutpostConnectionController
   └ CreateLocalPort() ─► LocalOutpostSessionPort (jugador del host)                         └ OutpostNetworkSessionAdapter
                                                                   CrewAvatarSync ◄─ snapshots confirmados (prefab de D4)
```

## Reglas implementadas

**Movimiento.**

- WASD o flechas se convierten en `MoveDirection.FromAxes`, de modo que la diagonal queda normalizada: el astronauta no va √2 más rápido.
- El cliente envía solo la **dirección sostenida**, y solo cuando cambia. Mantener una tecla pulsada no genera envíos por frame. El cliente nunca envía posiciones y no predice.
- La autoridad mueve a cada miembro a `|dir| × MaxSpeed` con `CrewRoster.Advance`, que D2 llama en su tick mientras la misión está en Running.
- Colisión (`CrewMotionSolver`):
  - avanza en subpasos de ≤ 0,25 × radio, así que un frame largo no atraviesa una pared;
  - si un paso queda bloqueado, se aproxima por bisección hasta tocar el obstáculo y desliza por el eje libre con la longitud restante;
  - el recorrido nunca supera velocidad × tiempo;
  - toda posición confirmada cumple `OutpostLayout.IsAdmissible`. Lo verifica un paseo aleatorio de 4000 pasos con 3 miembros, con `dt` de hasta 3 s.
- `Facing` es la última dirección sostenida aplicada, normalizada; se mantiene al pararse. `Motion` es `Walking` solo si el paso cambió la posición. Empujando contra una pared, el astronauta mira hacia ella y queda `Idle`.
- **Sin teletransporte posible**: el cable no tiene ningún mensaje con posición enviada por un cliente. Las direcciones con NaN, infinito o magnitud > 1 se rechazan al decodificar (`MalformedPayload`) y nunca llegan a la autoridad.

**Identidad, aparición y desaparición.**

- Un `PlayerId` solo se obtiene con `IOutpostAuthority.Join()`, cuando una conexión envía Join. El puente lo liga a esa conexión real y ningún byte del payload lo elige ni lo cambia; una membresía reenviada por otro cliente se rechaza (`UnexpectedMessageKind`).
- Repetir Join en la misma conexión reenvía su estado, sin crear un miembro duplicado.
- Si la base está llena (4 huecos en la fixture), el cliente queda en `ConnectionLost` con motivo `SessionFull` y texto "La base está completa"; no se crea ningún fantasma.
- Al desconectar (cliente, host, envío fallido o cierre por violación de protocolo), el puente llama a `Leave` exactamente una vez. Al soltar el puente (`OutpostNetworkHost.Attach`/`Detach`/`StopHost`) llama a `Leave` de todos. Así no quedan astronautas fantasma ni reservas huérfanas; la limpieza de tareas la hace la autoridad de D2 en `Leave`.
- Reconectar es un Join nuevo: `PlayerId` nuevo, sin duplicados, y reutiliza el hueco libre más bajo.
- `Restart`: `ResetForRestart` lleva a todos a su punto de aparición. El cliente reenvía la dirección sostenida con la época nueva (`CrewInputSender`) y las entradas de la época vieja se ignoran.

**Personaje local y remotos.** Solo `CrewKeyboardInput` emite entrada, y únicamente con la identidad de su puerto. `CrewAvatarSync` marca `IsLocal` y la cámara sigue solo al avatar local. El jugador del host usa `LocalOutpostSessionPort`: es un miembro normal, con su propio `PlayerId`, que no pasa por la red.

**Presentación suave** (`CrewInterpolator`, solo visual):

- interpolación de snapshots con un retardo fijo de 0,1 s: cada miembro se dibuja en `ahora − 0,1 s`, entre las dos muestras confirmadas que rodean ese instante;
- pasada la última muestra, se queda en ella, sin extrapolar;
- un salto mayor de 1,5 m o una época nueva colocan al miembro directamente en su sitio, en vez de deslizarlo a través de paredes;
- el jugador local usa la misma regla, sin predicción; su retraso es el viaje de ida y vuelta más 0,1 s, aceptable para este prototipo;
- sin membresía vigente se borran todos los avatares.

**Estaciones.** `StationInteractionClient` envía StartTask y CancelTask a la autoridad de D2 y mantiene una sola solicitud en vuelo. La E cancela la tarea propia, si existe, y si no empieza la primera tarea de la estación más cercana al alcance. La estación elegida es solo una pista: la autoridad vuelve a comprobar el alcance con su posición confirmada. Ocupación, progreso y recursos llegan en el snapshot completo de fase 2. Si se abandona una solicitud, se limpia sin inventar ningún resultado.

**Foco.** Un panel de tarea que tome el teclado llama a `CrewKeyboardInput.Block(panel)` al abrirse y a `Unblock(panel)` al cerrarse. Mientras haya algún bloqueador se envía un solo cero y se ignoran movimiento y E. Al perder el foco de la aplicación, el astronauta también se detiene.

**Envío de snapshots.** El puente agrupa: `FlushSnapshots()` en `LateUpdate` envía solo el más reciente, una vez por frame (30 Hz en `-batchmode`). Los resultados salen al momento y solo a su remitente. Quien llega tarde recibe el estado completo antes que su `Joined`, y el adaptador no anuncia `Joined` hasta tener un snapshot que lo incluya.

## Cable de fase 2 (`'T' 'O'`, versión 1, little-endian)

| Tipo | Dirección | Cuerpo |
| --- | --- | --- |
| 1 Join | cliente→host | vacío |
| 2 Submit | cliente→host | Guid · epoch i64 · action i32 · clave estación · clave tarea (u8 longitud + ASCII; 0 = ninguna) |
| 3 Movement | cliente→host | epoch i64 · seq i64 · x f64 · y f64 |
| 10 Result | host→cliente | Guid · epoch i64 · accepted u8 · razón u16 + UTF-8 (≤ 256 B) |
| 11 Snapshot | host→cliente | estado completo del contrato: tripulación, estaciones, recursos y objetivos |
| 12 Membership | host→cliente | estado u8 · player i32 · razón |
| 13 Chunk | host→cliente | índice u16 · total u16 · trozo. Se usa cuando un mensaje supera 1100 B |

Unity Transport admite unos 1400 B por mensaje y el mayor snapshot válido según `OutpostLimits` ronda los 3,4 KB, así que el host lo trocea. TCP conserva el orden de los trozos. Cada cliente rearma los mensajes, con un máximo de 16 KB; un hueco, una repetición o un trozo huérfano se descartan como `MalformedPayload`. Un cliente no puede enviar trozos, y sus mensajes están limitados a 256 B.

Decodificación estricta: se rechazan magia o versión ajenas, tipos inesperados, bytes de más, booleanos ≠ 0/1, claves inválidas, NaN e infinito, y todo lo que rechacen los constructores del contrato. Con `disconnectOnProtocolError` activado, el host cierra la conexión que viola el protocolo y eso también dispara `Leave`.

## Conexión para D2 (composición)

```csharp
// Proceso autoridad. D2 crea su autoridad, dueña única del roster de D3:
var roster = Trainer.Application.Crew.CrewRoster.For(scenario);   // en el tick de D2: roster.Advance(dt) si Running
IOutpostAuthority authority = /* D2: new OutpostSession(..., roster, ...) */;
outpostNetworkHost.Attach(authority);                // antes o después de StartHost (listenOnStart=true)
var local = outpostNetworkHost.CreateLocalPort();    // solo si el host también juega
local.Join();
crewAvatarSync.Bind(local);
crewKeyboardInput.Bind(local, scenario);
// Al cerrar: local.Dispose() (Leave) y outpostNetworkHost.StopHost() (Leave de todos los remotos).

// Proceso cliente (desktop/WebGL). Sin autoridad, roster ni simulación locales:
var port = outpostNetworkClient.Prepare();           // el mismo adaptador en todas las reconexiones
crewAvatarSync.Bind(port);
crewKeyboardInput.Bind(port, scenario);              // el mismo OutpostScenario de la build
outpostNetworkClient.ConnectionStatusChanged += (confirmed, detail) => { /* panel de conexión de D4 */ };
outpostNetworkClient.Connect();                      // o connectOnStart, -trainer-connect o ?connect=1
```

Argumentos de arranque iguales a los de fase 1: servidor `-trainer-bind`, `-trainer-port`, `-trainer-path`, `-trainer-cert` y `-trainer-key`; cliente `-trainer-server`, `-trainer-port`, `-trainer-secure`, `-trainer-connect`, o `?server=&port=&secure=&connect=1` en WebGL. Una escena usa el host y el cliente de fase 2 **o** los de fase 1, nunca los dos en el mismo puerto.

## Coordinación con D4

- **Montaje**: `CrewAvatarSync` instancia el prefab de astronauta que D2 le asigne (de D4) bajo `avatarParent` y coloca su **raíz** en X = x m, Y = y m, Z = `avatarZ`, a 1 unidad = 1 m. El centro de la raíz es el centro del disco de colisión, de radio `CrewRadiusMeters` (0,35 m en la fixture), así que sprite y sombra deben ir centrados ahí.
- **Vista**: cualquier componente del prefab que implemente `ICrewAvatarView.Present(CrewAvatarFrame)` recibe en cada snapshot `Player`, `Slot` (color y nombre), `IsLocal` (marcador "Tú"), `Facing`, `Motion` (animación de caminar) y `WorkingAt`. La vista no se mueve ni guarda inventario: solo dibuja.
- **Geometría de colisión**: la dueña es D2, en `OutpostLayout`; D3 la hace cumplir. Las habitaciones y paredes de D4 deben coincidir con `Bounds` y `Obstacles`. Ningún `Collider2D` ni `Rigidbody2D` debe decidir posiciones, y los cambios de plano se piden a D2.
- **Panel de tarea**: debe llamar a `Block` y `Unblock`. Para los botones, D4 puede usar `crewKeyboardInput.Interaction` (`RequestStart`, `RequestCancel`, `TasksAt`, `TryFindStationInReach`) en lugar de crear sus propios comandos.

## Pruebas realmente ejecutadas (2026-11-14, esta máquina)

No abrí Unity: no es Test Runner, ni build, ni player. Usé el Roslyn y el .NET 6.0.21 que trae el Editor 6000.3.28f1, el NUnit real de `com.unity.ext.nunit` y un runner por reflexión. Unity.Mathematics, Burst, Collections, Transport 2.7.4 e Input System 1.20.1 se compilaron como referencias managed a partir de los `.tgz` del Editor, extraídos a una carpeta temporal. Del Input System quedó fuera el plugin InputForUI, que no uso. No instalé nada.

| Comprobación | Topología | Resultado |
| --- | --- | --- |
| `Runtime/Simulation` + `Runtime/Application` (incluido `Crew/`) solo contra `netstandard`, avisos como errores | — | exit 0, 0 avisos: D3 puro sin UnityEngine |
| `Assembly-CSharp` en player Linux, `UNITY_EDITOR` y WebGL, más `Assembly-CSharp-Editor` | — | exit 0 en los cuatro; avisos solo CS0649 de campos serializados |
| `Assembly-CSharp` con `UNITY_INCLUDE_TESTS` (PlayMode de fase 1 y de fase 2) | — | exit 0 (solo compilación) |
| NUnit D3: `CrewRosterTests` 8, `CrewClientHelpersTests` 6, `OutpostNetworkLoopbackTests` 15 | **memoria**, 1 proceso, hasta 8 clientes simulados | 29/29 |
| NUnit de la base común (34) y regresión de fase 1 (PowerSystem 30, Clock 5, MissionSession 21, Evaluator 7, FakeSessionPort 6, MissionScenario 19) | — | 122/122 |
| **Total** | | **151/151** |
| Sensibilidad: 12 mutantes en una copia (sin Leave, resultados a todos, Join duplicado, Joined antes del estado, cable que acepta magnitud > 1, velocidad Manhattan, ids reutilizados, sin colisión, inundar la red, no reenviar tras Restart, extrapolar, pendiente huérfano) | — | los 12 detectados. Tres sobrevivían a la primera versión de las pruebas y añadí pruebas al nivel del cable |
| Propiedad (`OWNERSHIP.md`), GUID y congelados | — | sin solapes ni huecos; GUID únicos; `git diff 95caa31` vacío en congelados, escenas, prefabs, arte, paquetes y settings |

Las pruebas con dos clientes en **memoria** cubren:

- identidades distintas y cada astronauta visible para ambos;
- A camina 1,0 m en 0,5 s en diagonal y B lo ve en la misma posición confirmada, mientras B no se mueve;
- quien llega tarde recibe el estado actual antes de `Joined`;
- movimiento falsificado (×5, NaN), membresía o snapshot inyectados por un cliente: rechazados y atribuidos a la conexión real;
- B no puede cancelar la tarea de A y el resultado de B no viaja por el enlace de A;
- dos jugadores piden la misma estación: gana el primero y el otro recibe `StationOccupied`;
- alejarse de la estación da `PerformerMovedAway`;
- desconexión: un solo `Leave`, estación libre (`PerformerLeft`) y pendiente abandonado;
- reconexión con id nuevo y sin duplicados; cierre desde el host;
- Restart y reenvío de la dirección sostenida;
- entrada bloqueada;
- base llena;
- 8 clientes con 16 estaciones en trozos de ≤ 1100 B;
- tramas corruptas;
- jugador local del host;
- sustitución del puente.

**No ejecutado**:

- `OutpostTransportSocketTests` (3 pruebas con **sockets** WebSocket reales en un proceso): solo compilada, porque necesita el runtime nativo de Unity;
- partidas de **dos o más procesos**, **dos equipos** (LAN o Internet), navegador WebGL, ws/wss;
- importación en Unity, Test Runner, render, teclado físico y `CrewAvatarSync`/`CrewKeyboardInput` en una escena.

No lancé Unity, según la indicación de no hacer builds ni ejecuciones de Unity si no se piden.

## Dependencias y pendientes

1. **D2**: falta la autoridad real (`IOutpostAuthority`) y el wiring de escena; sin ellos no hay partida jugable. Las pruebas usan dobles rotulados (`CrewTestAuthority`, `MovementOnlyAuthority`) que no son la autoridad de misión. La autoridad debe:
   - ser dueña de un único `CrewRoster`;
   - llamar a `Advance` solo en Running;
   - publicar una revisión nueva cuando cambie la referencia de `roster.Members`;
   - en `Leave`, cancelar la tarea del miembro antes de llamar a `roster.Leave`.
2. **D4**: prefab de astronauta con `ICrewAvatarView` y panel de tarea que use `Block` y `Unblock`.
3. **D5**: ejecutar en Unity `OutpostTransportSocketTests` y las EditMode de `Tests/EditMode/Crew/Editor/`, y después la prueba de 2 o 3 procesos con la receta de fase 1. Registrar la topología: memoria, sockets en un proceso, procesos locales o equipos.
4. Decisiones que siguen abiertas:
   - no hay límite de tasa por conexión;
   - no hay token de sala: cualquiera que alcance el puerto entra;
   - permisos de Pause y Restart: cualquier miembro;
   - el local no tiene predicción; si el retraso molesta en LAN o Internet, D3 puede proponer una predicción simple que la autoridad corrija.
