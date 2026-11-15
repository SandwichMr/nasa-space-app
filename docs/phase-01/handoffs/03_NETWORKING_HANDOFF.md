# Handoff D3 — Sesión multiplayer (fase 1: sala cooperativa de energía)

> Binding real entregado (Unity Transport sobre WebSocket). Probado con sockets reales en una sola máquina:
> tests en un proceso y una partida de 3 procesos (servidor + 2 clientes Linux) por `127.0.0.1`.
> **No** probado: cliente WebGL en navegador, TLS/wss, dos equipos (LAN), Internet ni escenas de D2/D4.
> El resto de pruebas queda para la integración de las 5 D, como pidió el usuario.

> Actualización de integración, 2026-11-14: la escena `Phase01_PowerCoop` ya incluye host y cliente reales. `MissionSceneComposition` conecta el estado/puerto remoto al HUD y retira listeners al deshabilitar/destruir. Conexión por argumentos o botón; reintento manual, sin autoridad local en Client. El HUD conserva motivos de conexión. El log del host distingue ws de wss. Esta corrección compiló C# fuera de Unity; no ejecutó sockets, players ni navegador. Las pruebas de sockets/3 procesos que se describen más abajo son evidencia HISTÓRICA de D3, no de esta corrección.
>
> Receta actual para host con jugador local: `./Phase1Check.x86_64 -trainer-role authority -trainer-port 7777`.
> Cliente desktop: `./Phase1Check.x86_64 -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect`.
> Web por HTTP, ws explícito: `?server=IP&port=7777&path=/&secure=0&connect=1`.
> Web por HTTPS, wss explícito: `?server=dominio&port=443&path=/trainer&secure=1&connect=1`; requiere ese endpoint TLS/path configurado y accesible. No se configuró ningún servicio. El puerto público puede diferir del 7777 interno tras un proxy.
>
> `connectOnStart=0` en la escena: sin argumento/query el usuario usa Conectar/reintentar. El binding y UTP 2.7.4 ya existen; los pendientes actuales son ejecución del wiring, build, dos procesos y navegador/TLS. No añadir otra suscripción manual al HUD encima de la composición.

## Base y rama de la entrega original (registro histórico)

- Rama `feature/phase-01/d03-multiplayer`, creada desde `main` = `0f05e38` (`origin/main`). Cambios **sin commit**,
  sin push ni merge.
- Revisadas `origin/feature/phase-01/d01-simulacion` y `d04-room-visuals`: ninguna añade SDK ni toca `Packages/`.
- Se conservan bridge, adapter y controller. `NetworkSessionAdapter.cs` no cambia; bridge y controller solo
  reciben comentarios, códigos nuevos y `AbortLink`. Protocolo de cable v1 sin cambios.

## Decisión de red (tomada en esta tarea)

El usuario (coordinación) indicó: debe funcionar también en **WebGL**, hay **un servidor propio** y que se elija
lo que más convenga. Elegido:

- **Unity Transport `com.unity.transport` 2.7.4, interfaz WebSocket en todas las plataformas.** Un navegador no
  abre UDP, y un solo tipo de socket permite que clientes desktop y WebGL entren al mismo servidor.
- **Servidor = player no-WebGL** (Linux, `-batchmode -nographics`) con la única autoridad. Un navegador no puede
  escuchar conexiones.
- Sin Netcode for GameObjects: impondría `NetworkManager`/`NetworkObject` en las escenas de D2 y su propio modelo
  de autoridad. El protocolo propio ya encaja en `ISessionHostTransport`/`ISessionClientTransport`.
- Sin Relay, Lobby ni servicios UGS. No se configuró ningún servidor ni servicio.
- 2.7.4 es la versión que el Editor 6000.3.28f1 trae empaquetada (`mustBeBundled` en su manifest interno).
  Sus dependencias (collections 2.7.1, burst 1.8.31, mathematics 1.3.3) ya estaban en `packages-lock.json`
  por los paquetes 2D.

**Cambio común (único fuera de Networking):** `Packages/manifest.json` +1 línea y `Packages/packages-lock.json`
(generado por Unity: entrada `com.unity.transport`; `com.unity.burst` pasa de depth 2 a 1). Debe entrar una
sola vez en la integración. **No toqué** `AGENTS.md` ("No networking backend is selected yet") ni
`SESSION_CONTRACT.md` ("No hay SDK seleccionado"); coordinación debe actualizarlos.

## Archivos

| Archivo (bajo `Assets/_Project/` salvo los de `Packages/` y `docs/`) | Estado | Contenido |
| --- | --- | --- |
| `Packages/manifest.json`, `Packages/packages-lock.json` | modificado (común) | Unity Transport 2.7.4 |
| `Runtime/Unity/Networking/NetworkCommandBridge.cs` | modificado | Doc del seam; códigos `SyncTimeout`, `AuthorityUnavailable`, `AuthorityReplaced`, `ProtocolViolation`, `HostStopped` |
| `Runtime/Unity/Networking/SessionConnectionController.cs` | modificado | Doc del seam; `AbortLink(reason)` |
| `Runtime/Unity/Networking/SessionNetworkHost.cs` (+`.meta`) | nuevo | MonoBehaviour del servidor: transporte + bridge + autoridad |
| `Runtime/Unity/Networking/SessionNetworkClient.cs` (+`.meta`) | nuevo | MonoBehaviour del cliente; `SessionConnectionStatus` (textos); `SessionNetworkArguments` (internal) |
| `Runtime/Unity/Networking/Transport.meta` | nuevo | Carpeta |
| `Runtime/Unity/Networking/Transport/UnityTransportHost.cs` (+`.meta`) | nuevo | `ISessionHostTransport` sobre UTP WebSocket |
| `Runtime/Unity/Networking/Transport/UnityTransportClient.cs` (+`.meta`) | nuevo | `ISessionClientTransport` sobre UTP WebSocket (desktop y WebGL) |
| `Runtime/Unity/Networking/Transport/WebSocketTransportOptions.cs` (+`.meta`) | nuevo | `WebSocketHostOptions`, `WebSocketClientOptions` serializables |
| `Tests/PlayMode/Networking/UnityTransportSessionTests.cs` (+`.meta`) | nuevo | 19 tests con sockets reales |
| `docs/phase-01/handoffs/03_NETWORKING_HANDOFF.md` | reescrito | Este documento |

Los `.meta` de los archivos nuevos se versionan porque D2 referenciará los MonoBehaviours desde escenas
(GUID `SessionNetworkHost` = `9e68e7c20471cbcfb9f2300dae797809`, `SessionNetworkClient` =
`c04961e40a6872ce8b4723fb7ded236d`). Los tres `.cs` previos de Networking siguen sin `.meta` (clases puras).
Namespace `Trainer.Unity.Networking`, todo en `Assembly-CSharp` (sin asmdef). Tests bajo `#if UNITY_INCLUDE_TESTS`.

## Arquitectura

```
Servidor (player Linux, única autoridad)                     Cliente (desktop o WebGL)
MissionBootstrap ─Compose→ MissionSession ◄─ SimulationRunner     UI (D4) ──► client.Session (ISessionPort)
                               ▲ Submit │ SnapshotChanged/CommandCompleted              │ Submit
SessionNetworkHost ─ NetworkCommandBridge ─ UnityTransportHost ═WebSocket═ UnityTransportClient
                                                                   SessionConnectionController ─ NetworkSessionAdapter
                                                                   SessionNetworkClient (pump + timeout + textos)
```

- Los clientes no crean `MissionSession` ni `SimulationRunner`, no predicen y no calculan batería.
- `Submit` es intención; aceptación o rechazo llegan solo por `CommandCompleted` y solo al remitente.
- Todo corre en el hilo principal: los MonoBehaviours llaman `Update()` (recibir) y `Flush()` en `LateUpdate`
  (enviar en el mismo frame). Fiabilidad y orden vienen de TCP, así que se usa `NetworkPipeline.Null`. El TCP
  de UTP hace back-pressure, no descarta. Si alguien cambia a UDP, necesitará un pipeline fiable.

## Reglas implementadas

Siguen vigentes las del handoff anterior: remitente auténtico, IDs ligados a su conexión, resultados solo al
remitente, filtro época/revisión, `SyncRequest` inicial, conexión ≠ misión, `CommandAbandoned` sin resultados
inventados, intentos con `link`. Nuevas con el binding:

- **IDs de conexión:** los asigna `UnityTransportHost` de forma monotónica y nunca se reutilizan. Ningún byte del
  cliente los elige ni los cambia. El cable sigue sin identidad, flag de host ni permisos.
- **Una autoridad por puerto:** UTP abre su socket con reutilización de dirección. Observado: un segundo `Listen`
  en el mismo puerto **no falla**, y dos servidores se repartirían clientes. `Listen` sondea antes conectándose
  al puerto y lanza `InvalidOperationException` si alguien acepta. Una sonda por `bind` no sirve: Mono activa
  `SO_REUSEPORT`, y sin reutilización bloquearía reinicios con `TIME_WAIT`. Queda una carrera si dos servidores
  arrancan en el mismo instante.
- **Cambio de autoridad:** si `MissionBootstrap` recompone (`ShutDown` + `Composed`) o se llama
  `Attach(otra)`/`Detach()`, se cierran todas las conexiones. Una sesión nueva reinicia épocas en 0 y los clientes
  deben reincorporarse. Sin autoridad, `AcceptClients = false` cierra las conexiones nuevas al aceptarlas.
- **Violación de protocolo:** con `disconnectOnProtocolError` (activado por defecto), el host cierra la conexión
  que envía magia, versión, tipo o formato inválidos. `ReasonTruncated`, `SendFailed` y `UnknownConnection` no
  cuentan como culpa del cliente.
- **Timeout de sincronización:** si el enlace sube pero no llega estado en `synchronizationTimeoutSeconds`
  (10 s), el cliente cierra con `SyncTimeout` mediante `AbortLink`. Cierra el hueco "cliente de otra versión
  queda en Synchronizing".
- **Intento de conexión:** por defecto 10 intentos × 1000 ms, frente a los 60 s de UTP. Termina en
  `ConnectionLost` con `MaxConnectionAttempts`. Host o cliente callado: `disconnectTimeoutMS` 30000 (UTP).
- **`Application.runInBackground = true`** en ambos componentes (configurable), porque
  `ProjectSettings.runInBackground: 0` dejaría de bombear heartbeats con la ventana sin foco. En WebGL, el
  navegador limita igualmente las pestañas en segundo plano.
- **Servidor headless:** en `-batchmode`, `targetFrameRate = 30` (configurable). `MissionSession` publica un
  snapshot por `Tick` y un bucle sin tope inundaría a los clientes.
- **Excepciones de suscriptores** en `Update()`: no pierden los eventos siguientes del frame; la primera se
  relanza al final.

## Composición para D2

Servidor (escena o prefab de autoridad, solo en el proceso servidor):

```csharp
// GameObject con MissionBootstrap (scenario + runner asignados), SimulationRunner y SessionNetworkHost
// (campo bootstrap asignado). SessionNetworkHost.Start() escucha y se adjunta a bootstrap.Session;
// después sigue Composed/ShutDown por eventos. Da igual si Compose ocurre antes o después.
bootstrap.Compose(new PowerSystem());
host.Diagnostic += (connectionId, code) => Debug.Log($"[red] {connectionId}: {code}");
// Sin MissionBootstrap: listenOnStart=false; host.Configure(opts); host.StartHost(); host.Attach(authority);
```

Cliente (ejemplo de composición manual para otra escena; la escena entregada ya lo hace, no duplicar):

```csharp
// GameObject con SessionNetworkClient. Configure(...) solo antes del primer Connect (antes de su Start).
client.ConnectionStatusChanged += (confirmed, detail) =>
{
    if (confirmed) hud.Bind(client.Session);   // en cada (re)incorporación confirmada
    hud.SetConnectionStatus(confirmed, detail); // false => HUD.Bind(null): borra datos y pendiente
};
room.Connect(hud, sceneCamera);                 // D4
client.Connect();   // o connectOnStart, -trainer-connect, ?connect=1. Reintento: Connect() otra vez.
```

`ConnectionStatusChanged(true)` llega cuando el estado inicial ya está guardado: `hud.Bind` lo lee con
`TryGetSnapshot` y el `SnapshotChanged` duplicado lo descarta el HUD por revisión. Esto cumple el
`SetConnectionStatus` actual de `main` y de `d04-room-visuals` (false ⇒ `Bind(null)`).

Opciones: Inspector (`WebSocketHostOptions`/`WebSocketClientOptions`) o argumentos de arranque:

| Lado | Argumentos |
| --- | --- |
| Servidor | `-trainer-bind 0.0.0.0` · `-trainer-port 7777` · `-trainer-path /` · `-trainer-cert fullchain.pem -trainer-key privkey.pem` (wss directo) |
| Cliente desktop | `-trainer-role client` · `-trainer-server HOST` · `-trainer-port N` · `-trainer-path /p` · `-trainer-secure` · `-trainer-server-name NOMBRE` · `-trainer-connect` |
| Cliente WebGL (query de la página) | `?server=HOST&port=N&path=/p&secure=1&serverName=NOMBRE&connect=1` |

En WebGL, con `Address` **vacío** en el Inspector se usa el host que sirvió la página y `wss` si es HTTPS. El
valor por defecto es `127.0.0.1`, así que para la build web servida desde el mismo servidor hay que dejarlo vacío.
El navegador construye `ws(s)://<dirección>:<puerto><path>` (puerto siempre explícito).

Modo "host con jugador local" (servidor y jugador en un proceso desktop): el jugador local usa `bootstrap.Port`
directamente y D2 marca su HUD como confirmado. No está en el alcance probado.

## Estados de conexión y abandono para D4

| `SessionConnectionState` | `IsConfirmed` | Texto por defecto (`SessionConnectionStatus.Describe`) |
| --- | --- | --- |
| Disconnected | no | Sin conexión con la base |
| Connecting | no | Conectando con la base... |
| Synchronizing | no | Recibiendo el estado de la misión... |
| Connected | **sí** | Conectado: datos confirmados por la base |
| ConnectionLost | no | Se perdió la conexión + motivo |

Motivos en `LastDisconnectReason`: `MaxConnectionAttempts` (no se encontró la base), `Timeout`, `ClosedByRemote`,
`ProtocolError` (la comunicación se cortó; observado al matar el servidor), `AuthenticationFailure` (wss),
`SyncTimeout`, `ConnectFailed`, `SendFailed`, `LocalDisconnect`. Los textos son una propuesta para niños; D4
puede sustituirlos usando los códigos.

Abandono de solicitudes: `client.Session.CommandAbandoned(cmd, code)`. `NotConnected` = no se envió.
`ConnectionLost`/`LocalDisconnect` = resultado **desconocido** (la autoridad pudo aplicarlo). El HUD ya suelta su
pendiente con `SetConnectionStatus(false)`; no revertir nada y fiarse del próximo snapshot. Tras reincorporarse,
no reenviar con el mismo ID: el bridge responde `CommandIdConflict` porque la conexión es otra.

## Receta de despliegue en el servidor (no ejecutada en un servidor real)

1. Build **Linux Standalone** (no WebGL) con la escena de autoridad de D2. Ejecutar
   `./Juego.x86_64 -batchmode -nographics -trainer-port 7777 -logFile server.log`.
   El módulo "Linux Dedicated Server" no está instalado en este Editor; se usó el player normal en batchmode.
2. Abrir el puerto TCP en el firewall. Un solo proceso servidor por puerto.
3. WebGL servida por **HTTPS ⇒ wss obligatorio**. Dos opciones:
   a. Certificado de una CA pública en el propio juego: `-trainer-cert`/`-trainer-key` (`WithSecureServerParameters`).
   b. Proxy inverso con TLS que reenvía `wss://dominio:443/<path>` a `ws://127.0.0.1:7777/<path>`, con
      servidor `-trainer-bind 127.0.0.1`. El `Path` debe coincidir: el servidor solo acepta su path.
   Por HTTP sin TLS (p. ej. LAN): `ws://IP:7777` con `?server=IP`.
4. La build WebGL del proyecto usa compresión Brotli (`webGLCompressionFormat: 0`). El servidor web debe enviar
   `Content-Encoding: br` (Brotli exige HTTPS en navegadores), o el equipo cambia el ajuste común.
   No lo modifiqué.

## Protocolo de cable v1 (sin cambios)

Cabecera de 4 bytes: `'T' 'S' versión(1) tipo`. Máx. 2048 bytes por mensaje (el mayor real ≈ 1055, por debajo
de los 1400 de UTP); `Reason` ≤ 1024 bytes UTF-8.

| Tipo | Dirección | Cuerpo |
| --- | --- | --- |
| 1 Submit | cliente→host | Guid(16) · epoch i64 · action i32 · ciencia u8 (0 null, 1 false, 2 true) |
| 2 SyncRequest | cliente→host | vacío |
| 3 CommandResult | host→cliente | Guid(16) · epoch i64 · accepted u8 · len u16 · reason UTF-8 |
| 4 Snapshot | host→cliente | epoch i64 · revision i64 · state i32 · 8×f64 de `PowerSnapshot` · scienceWork f64 · scienceEnabled u8 |

Rechazo estricto de magia, versión o tipo desconocidos, longitudes incorrectas, bools ≠ 0/1, estado no definido,
números no finitos o negativos y UTF-8 inválido. Una `SessionAction` desconocida pasa a la autoridad
(`UnknownAction`).

## Pruebas realmente ejecutadas

Editor Unity 6000.3.28f1, batchmode, sobre una **copia temporal** del proyecto sin `.git`; el repositorio no se
abrió en Unity. Topología de todas: **una sola máquina, procesos locales, `127.0.0.1`**. No hubo LAN ni Internet.

| Hora (UTC, 2026-11-14) | Comprobación | Resultado |
| --- | --- | --- |
| 21:32 | PlayMode (`-runTests -testPlatform PlayMode`) | **53/53**: `NetworkSessionAdapterTests` 34 (loopback en memoria) + `UnityTransportSessionTests` 19 (sockets WebSocket reales en un proceso, `MissionSession` + `PowerSystem` reales) |
| 21:32 | EditMode | 23/23 (tests existentes de otros roles; regresión) |
| 21:33–21:42 | Build Linux64 con arnés temporal | Succeeded; sin `error CS` ni warnings en `Networking/` |
| 21:43 | Partida de **3 procesos** Linux players `-batchmode -nographics`: servidor + cliente A + cliente B | Ver abajo |
| 21:43–21:52 | Build WebGL con arnés temporal | `Assembly-CSharp.dll` del player WebGL **compiló sin errores**; IL2CPP/nativo **interrumpido a petición del usuario**. No hay player WebGL ni prueba en navegador |

Ejecuciones previas de PlayMode: 51/53 a las 21:28 y 52/53 a las 21:30. Corregidos: `Dispose` ahora ejecuta un
update para que el par reciba el cierre (antes veía `ProtocolError`), y la sonda de puerto pasó de `bind` a
conexión. El test de cierre del host acepta `ClosedByRemote` o `Timeout`; no registré cuál de los dos salió.

Tests nuevos (sockets reales): estado inicial a 2 clientes con IDs asignados por el host; quien llega tarde
recibe el estado actual; intención sin predicción y resultado solo al remitente; misma energía y trabajo en ambos
clientes calculados solo por la autoridad; motivo de rechazo de la autoridad; Restart → época 1 en todos y
comando viejo `EpochMismatch`; ID ajeno → `CommandIdConflict` sin llegar a la autoridad; un cliente que envía
snapshot o resultado forjado, o bytes extra de "identidad", recibe errores atribuidos a su conexión real y nada
se reenvía; host cierra un cliente → pendiente abandonado `ConnectionLost`, motivo `ClosedByRemote`, último
estado conservado; reincorporación con ID nuevo; apagado del host → `ConnectionLost` en ambos; desconexión local
vista por el host; servidor inexistente → `MaxConnectionAttempts`; host sin autoridad cierra; `AbortLink(SyncTimeout)`;
opciones inválidas y puerto ocupado; argumentos y URL; textos de estado.

Partida de 3 procesos. Arnés temporal, **no entregado**: monta en runtime `MissionBootstrap` + `SimulationRunner`
+ `SessionNetworkHost` (campos privados por reflexión, `timeScale` 20 solo en el arnés) y clientes
`SessionNetworkClient` con guion. Observado en los logs:

- A y B: `Connecting → Synchronizing → Connected` y estado inicial `e=0 r=0 Ready E=5000`.
- A activa ciencia con el ID fijo `a1a1a1a1-…` (aceptado) y reanuda. B reenvía **el mismo ID y contenido** →
  `CommandIdConflict`; el log de la autoridad no muestra ese comando.
- B pausa (`r=49 Paused t=30.67 E=4386.60`, idéntico en servidor, A y B) y reanuda a los 1,5 s.
- Fin: **`e=0 r=183 Completed t=120 E=2600 work=120`**, idéntico en los tres logs. B envía Restart y los tres
  ven `e=1 r=0 Ready E=5000`.
- `kill -9` del servidor → A y B pasan a `ConnectionLost`, motivo `ProtocolError`, y su panel muestra
  "Se perdió la conexión: la comunicación con la base se cortó".

**Cambios posteriores a la última ejecución (no compilados ni ejecutados):** caso de texto `ProtocolError` en
`SessionConnectionStatus` con su aserción en el test, y saltos de línea en comentarios y en una sentencia de
`SessionNetworkHost`.

## Lo que NO está probado

- Cliente WebGL en un navegador (build interrumpida), wss/TLS (certificado o proxy), compresión y hosting web.
- Dos equipos (LAN), Internet, NAT, firewall, latencia o pérdida real.
- Escenas de D2 y HUD/sala de D4 con el cliente real: el arnés registraba eventos en vez de la UI. Los
  MonoBehaviours solo se ejercitaron mediante el arnés, sin Inspector ni escena.
- Timeout de sincronización con reloj (el `AbortLink` sí se probó), `disconnectOnProtocolError` en el host
  (solo su clasificación), recomposición vía `MissionBootstrap.Shutdown`/`Compose` con clientes conectados.
- Carga, muchos clientes, límites de tasa.

## Hallazgos para el equipo

1. UTP permite dos servidores en el mismo puerto (mitigado con la sonda; ver reglas).
2. Al abrir la copia, Unity modificó `ProjectSettings/ProjectSettings.asset` (iconos VisionOS,
   `preloadedAssets` con las acciones de Input, `m_BuildTargetBatching`), `Assets/Settings/*.asset` (URP),
   creó `Assets/Resources/PerformanceTestRun*.json` y `ProjectSettings/SceneTemplateSettings.json`. El arnés puso
   la compresión WebGL en Disabled. **Nada de eso está en la entrega**; revisar si aparece al abrir el repo.
3. `MissionBootstrap.cs`, `SimulationRunner.cs` y `ScenarioDefinition.cs` (D2) no tienen `.meta` versionado. Si
   D2 crea la escena con GUIDs locales, las referencias se rompen en otras máquinas. Conviene versionarlos.

## Peticiones y pendientes

1. **Coordinación:** aceptar el cambio común de paquetes una sola vez en la integración y actualizar
   `AGENTS.md`/`SESSION_CONTRACT.md` con la decisión de red.
2. **Permisos de pausa/reinicio (decisión del roadmap, sin tomar):** hoy cualquier cliente puede Pause, Resume,
   Restart y SetScience; la autoridad solo valida estado y época. Si se define un rol, el sitio es el bridge, que
   conoce el `connectionId` real. Propuesta, no implementada.
3. **Acceso a un servidor público:** cualquiera que alcance el puerto entra. Propuesta: token de sala en el
   payload de conexión de UTP (`Connect(endpoint, payload)`/`Accept(out payload)`), dentro del binding y sin
   cambiar contratos, o limitar por proxy/firewall. No implementado.
4. **D5:** `PhaseOneIntegrationTests.ConnectedClientsFactory` necesita bombear el transporte. Los helpers
   `PumpUntil` de `UnityTransportSessionTests` sirven de modelo; no se creó un helper compartido.
5. Huecos que siguen abiertos: mapa ID→remitente sin tope y sin rate limit; `CommandAbandoned` fuera de
   `ISessionPort` (cambio de contrato propuesto, no aplicado); asignaciones por mensaje.

## Receta de prueba para la integración

1. Abrir con Unity 6000.3.28f1. En la copia, `com.unity.transport` 2.7.4 se resolvió sin errores.
2. Test Runner → PlayMode: `UnityTransportSessionTests` (19) y `NetworkSessionAdapterTests` (34).
3. Build Linux del servidor y ejecutarlo como en la receta de despliegue. Cliente desktop:
   `-trainer-role client -trainer-server <host> -trainer-port 7777 -trainer-connect`. Cliente WebGL por HTTP/ws:
   `…/index.html?server=<host>&port=7777&secure=0&connect=1`. Por HTTPS/wss:
   `…/index.html?server=<dominio>&port=443&path=/trainer&secure=1&connect=1` (endpoint TLS/path configurado).
4. Comprobar con dos clientes: misma batería, revisión y resultado; ambos pueden actuar; el resultado solo le
   llega al remitente; Restart lleva a ambos a época+1; matar el servidor ⇒ "Se perdió la conexión" en ambos;
   la reincorporación funciona.
5. Registrar la topología probada: procesos locales, LAN o Internet, y si el cliente WebGL usó ws o wss.
