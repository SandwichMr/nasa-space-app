# Handoff D3 — Adaptador multiplayer (fase 1)

> Componente listo para revisión ≠ juego conectado. No hay SDK de red aprobado. Ninguna prueba de este
> documento ejercita una red real, dos procesos ni Internet.

## Base y rama

- Rama: `phase01/multiplayer-session`. El plan dice `phase1/multiplayer-session`; el usuario renombró la
  rama a este prefijo. Un único commit de entrega, autorizado por el usuario; sin push ni merge.
- Aviso: el árbol de trabajo es compartido. Durante la verificación otro proceso cambió el checkout a
  `phase01/VALIDATION_AND_BUILDS` (mismo commit `4850e1d`). La entrega se confirmó después en
  `phase01/multiplayer-session` solo con estas 5 rutas, tras comprobar que el índice coincidía con lo verificado.
- Commit de base: `4850e1d` (`main` = `origin/main` en el momento del trabajo).
- Base comprobada en el proyecto real: `ISessionPort`, `SessionCommand`/`SessionAction`, `CommandResult`,
  `MissionSnapshot`, `SessionState`, `PowerSnapshot`, `FakeSessionPort` coinciden con
  `SESSION_CONTRACT.md`. No se modificaron. `MissionSession` (D2) no existe aún en esta base.

## Archivos creados (solo la lista permitida)

| Archivo | Contenido |
| --- | --- |
| `Assets/_Project/Runtime/Unity/Networking/NetworkCommandBridge.cs` | Host: `NetworkCommandBridge`, seam `ISessionHostTransport`, códigos `SessionNetworkCodes`, protocolo binario interno `SessionWireProtocol` |
| `Assets/_Project/Runtime/Unity/Networking/NetworkSessionAdapter.cs` | Cliente: `NetworkSessionAdapter : ISessionPort` |
| `Assets/_Project/Runtime/Unity/Networking/SessionConnectionController.cs` | Cliente: ciclo de conexión, seam `ISessionClientTransport`, `SessionConnectionState` |
| `Assets/_Project/Tests/PlayMode/Networking/NetworkSessionAdapterTests.cs` | 31 tests NUnit con transporte loopback en memoria + `FakeSessionPort` (y una autoridad guionizada interna) |
| `docs/phase-01/handoffs/03_NETWORKING_HANDOFF.md` | Este documento |

Sin `.meta`: la base versiona `.cs` sin `.meta` ("Unity genera los .meta"); se sigue esa convención.
Namespace `Trainer.Unity.Networking`. Todo es C# puro (sin `UnityEngine`), un solo hilo.

## Dependencia bloqueante: SDK/transporte

Inventario real: `Packages/manifest.json` solo contiene `com.unity.multiplayer.center` 1.0.1 (asistente
de recomendación, sin transporte). No hay Netcode for GameObjects, Unity Transport, Mirror, FishNet,
Photon ni nada en `Assets/ThirdParty/`. `AGENTS.md`: "No networking backend is selected yet".
Por eso no hay conexión real. Lo compatible se entregó detrás de dos interfaces neutrales
(`ISessionHostTransport`, `ISessionClientTransport`) que un binding futuro implementará.

**Petición al equipo:** aprobar SDK + transporte + plataforma objetivo. El módulo WebGL está instalado en
el Editor local; si el objetivo es Web, el transporte debe ser WebSocket/WebRTC (sin UDP directo).

## Arquitectura

```
Host (única autoridad)                          Cliente
ISessionPort autoridad (D2; hoy FakeSessionPort)  UI (D4) ──> ISessionPort = controller.Session
        ▲ Submit        │ SnapshotChanged/                         │ Submit
        │               ▼ CommandCompleted                         ▼
NetworkCommandBridge ── ISessionHostTransport ~~ red ~~ ISessionClientTransport ── SessionConnectionController
                                                                                      └─ NetworkSessionAdapter
```

- El host ejecuta la misión una sola vez. El jugador local del host usa el puerto de autoridad
  directamente, no el bridge. Los clientes no simulan batería ni predicen estado.
- `Submit` solo envía intención. Aceptación/rechazo llegan por `CommandCompleted` de forma asíncrona.

## Reglas implementadas

- **Remitente auténtico:** el `connectionId` lo asigna el transporte. El cable no lleva identidad ni
  flag de host. Un cliente que envíe tipos de host (snapshot/resultado) se descarta con
  `UnexpectedMessageKind`. Los mensajes de conexiones no registradas se descartan con `UnknownConnection`.
- **Propiedad de IDs:** el bridge liga cada `CommandId` a su primer remitente durante su vida. Otro
  remitente con el mismo ID recibe `CommandIdConflict` sin llegar a la autoridad. `Guid.Empty` recibe
  `InvalidCommandId` en el bridge porque no se puede enrutar; son los mismos códigos del contrato.
  El mismo remitente que reenvía el mismo comando llega a la autoridad, que decide (la deduplicación es
  de D2/autoridad, no se duplica).
- **Resultados:** solo al remitente. Correlación por `(CommandId, Epoch)`: un resultado por cada
  `Submit` enviado; duplicados o no solicitados se descartan (`DiscardedResultCount`).
- **Snapshots:** difundidos a todos. Se acepta solo época mayor, o misma época con revisión mayor; los
  viejos o duplicados se descartan (`DiscardedSnapshotCount`). Restart (época+1, revisión 0) se acepta.
- **Estado inicial:** al subir el enlace, el cliente pide estado (`SyncRequest`) y el host responde con
  `TryGetSnapshot` de la autoridad. Hasta entonces `TryGetSnapshot` devuelve `false` y el estado de
  conexión es `Synchronizing`. Si la autoridad aún no tiene estado, llega con el primer `SnapshotChanged`.
- **Conexión ≠ misión:** `SessionConnectionState { Disconnected, Connecting, Synchronizing, Connected,
  ConnectionLost }`. Nunca se fabrica un `MissionSnapshot` (p. ej. `Interrupted`) en el cliente.
- **Sin resultados inventados:** si no hay enlace, `Submit` emite `CommandAbandoned(cmd, NotConnected)`,
  no un `CommandResult`. Al perder el enlace, los pendientes salen con `ConnectionLost`/`LocalDisconnect`:
  su resultado es **desconocido** (la autoridad pudo aplicarlos); manda el próximo snapshot.
- **Host perdido:** interrupción controlada → `ConnectionLost` + `LastDisconnectReason`. Se conserva el
  último snapshot autoritativo como "último conocido". Sin migración ni reconexión automática.
  `Connect()` posterior es una incorporación nueva (nuevo `connectionId`; IDs antiguos no se reintentan).
  Un `Disconnect()`/`Dispose()` tras la pérdida conserva el motivo real.
- **Intentos con identidad (`link`):** cada `Connect()` usa un id nuevo que el binding repite en sus
  eventos. Un `Disconnected` tardío o mensajes de un intento anterior se ignoran; un `Connected` de un
  intento cancelado se cierra de inmediato (sin conexión fantasma en el host).
- **Fallos del transporte:** en el host, una excepción de `SendToClient` se reporta como `SendFailed` y
  no interrumpe la entrega de eventos de la autoridad ni a otros clientes. En el cliente, las llamadas del
  usuario propagan la excepción dejando estado coherente: `Submit` no deja pendiente fantasma; `Connect`
  pasa a `ConnectionLost` (`ConnectFailed`) y admite reintento; `Disconnect` cierra la sesión igualmente.
  Si falla el `SyncRequest`, el enlace se da por perdido (`SendFailed`) y se cierra.
- **Buffers:** cada `SendToClient`/`SendToHost` recibe su propio `byte[]`; el transporte puede retenerlo.

## Protocolo de cable v1 (interno, little-endian)

Cabecera de 4 bytes: `'T' 'S' versión(1) tipo`. Máx. 2048 bytes por mensaje; `Reason` ≤ 1024 bytes
UTF-8. Un `Reason` más largo de la autoridad se corta en límite de carácter (el host reporta
`ReasonTruncated`) y el UTF-16 inválido se codifica como U+FFFD: ID, época y aceptación siempre llegan.
Los motivos del contrato son códigos cortos; el texto para niños debería generarlo la UI a partir del código.

| Tipo | Dirección | Cuerpo |
| --- | --- | --- |
| 1 Submit | cliente→host | Guid(16) · epoch i64 · action i32 · ciencia u8 (0 null, 1 false, 2 true) |
| 2 SyncRequest | cliente→host | vacío |
| 3 CommandResult | host→cliente | Guid(16) · epoch i64 · accepted u8 · len u16 · reason UTF-8 |
| 4 Snapshot | host→cliente | epoch i64 · revision i64 · state i32 · 8×f64 de `PowerSnapshot` en orden de propiedades · scienceWork f64 · scienceEnabled u8 |

Rechazo estricto: magia/versión/tipo desconocidos, longitud corta o sobrante, bools ≠ 0/1, flag de
ciencia > 2, `SessionState` no definido, UTF-8 inválido. `SessionAction` desconocida **pasa** a la
autoridad, que responde `UnknownAction`. Los errores se reportan por `ProtocolError` (host:
`(connectionId, código)`; cliente: código). No desconectan automáticamente.

## API para componer

```csharp
// Host: un único proceso/rol autoridad.
ISessionPort authority = /* MissionSession de D2; FakeSessionPort solo en desarrollo */;
ISessionHostTransport hostTransport = /* binding del SDK aprobado, ya escuchando */;
var bridge = new NetworkCommandBridge(authority, hostTransport);   // Dispose() al cerrar la sesión
bridge.ProtocolError += (connectionId, code) => { /* log/diagnóstico */ };

// Cliente:
ISessionClientTransport clientTransport = /* binding del SDK aprobado */;
var connection = new SessionConnectionController(clientTransport);  // Dispose() al salir
ISessionPort port = connection.Session;          // lo que recibe la UI de D4
connection.StateChanged += state => { /* panel de conexión de D4 */ };
connection.Session.CommandAbandoned += (cmd, code) => { /* quitar "pendiente" en UI */ };
connection.Connect();
```

Requisitos para el binding del transporte:

- Ambos lados: entrega fiable y ordenada por conexión; callbacks en el hilo principal de Unity; el
  `byte[]` recibido en un envío pasa a ser del transporte.
- Host (`ISessionHostTransport`): `connectionId` real del SDK; `ClientConnected(id)` antes de cualquier
  `MessageReceived(id, …)`; `SendToClient` a una conexión cerrada no debería lanzar.
- Cliente (`ISessionClientTransport`): `Connect(link)` / `Disconnect(link)` / `SendToHost(link, …)` y
  eventos `Connected(link)`, `Disconnected(link, motivo)`, `MessageReceived(link, bytes)` con el mismo
  `link`. `Disconnect(link)` cancela un intento pendiente o cierra el enlace; es idempotente e ignora
  ids desconocidos. Puede emitir eventos tardíos de ese `link`: el controller los ignora.
- Las excepciones de la autoridad o de sus suscriptores se propagan, igual que en el fake.

## Pruebas realmente ejecutadas

Editor Unity 6000.3.28f1 (el de `ProjectVersion.txt`), batchmode, sobre una **copia temporal** del
proyecto sin `.git`; el repositorio real no se abrió en Unity. Ejecución final, tras aplicar las
correcciones de `/code-review`: 2026-11-14 18:47–18:50 UTC; los archivos entregados son idénticos a los verificados.

| Comprobación | Comando | Resultado |
| --- | --- | --- |
| EditMode | `Unity -batchmode -nographics -projectPath <copia> -runTests -testPlatform EditMode` | 6/6 passed (`FakeSessionPortTests` existentes) |
| PlayMode | `… -runTests -testPlatform PlayMode` | 31/31 passed (`NetworkSessionAdapterTests`) |
| Scripts de player | `PlayerBuildInterface.CompilePlayerScripts` (script de editor temporal, solo en la copia) | Linux64 y WebGL compilan; `Assembly-CSharp.dll` del player contiene bridge/adapter/controller y **no** contiene la clase de tests ni referencia NUnit |
| Warnings/errores en `Networking/` | grep de los logs | ninguno |

Montaje de los tests: sin `.asmdef`, el archivo compila en `Assembly-CSharp`. En el Editor ese
ensamblado tiene `-define:UNITY_INCLUDE_TESTS` y referencia `nunit.framework.dll` (verificado en el
`.rsp` de Bee). Por eso el archivo está bajo `#if UNITY_INCLUDE_TESTS`: corre en el Test Runner
(PlayMode) y queda fuera de builds normales. No se tocó `ProjectSettings` ni se crearon asmdef.
Si el equipo introduce asmdefs de runtime/tests, este archivo deberá pasar al asmdef de PlayMode
(los asmdef no pueden referenciar `Assembly-CSharp`).

Efecto observado al abrir con este Editor: en la copia, Unity reescribió `ProjectSettings/ProjectSettings.asset`
(añade iconos `VisionOS`) y `.vscode/settings.json`. Quien abra el repo verá esos cambios; no forman parte
de esta entrega y no deben versionarse sin revisión.

Regresión de `/code-review` (10 tests nuevos; el loopback simula un SDK imperfecto): `Disconnected`
tardío tras reincorporación rápida; mensajes de un `link` anterior; `Connect` cancelado que completa
(sin fantasma en el host); motivo real conservado tras pérdida; `Connect`, `SendToHost`, `SyncRequest`
y `Disconnect` que lanzan; `Reason` largo/inválido sin romper los eventos de la autoridad; fallo de
envío a un cliente sin afectar a otros; buffer propio por cliente.

Los 21 tests originales cubren: estado inicial al unirse y para quien llega tarde; envío asíncrono sin predicción;
motivos de rechazo; conversión de todos los campos; Restart (época del resultado vs. snapshot); reenvío
del mismo comando; descarte de snapshots viejos, duplicados y de épocas anteriores; resultado a lo sumo una vez y
no solicitado; dos clientes con una autoridad y resultados solo al remitente; ID ajeno rechazado antes de la
autoridad; `Guid.Empty`; suplantación de mensajes host/cliente; conexión desconocida; payloads
malformados en ambos lados; envío sin conexión; host perdido con resultado desconocido; reincorporación;
desconexión local; bridge liberado.

## Lo que NO está probado

- Ninguna red real: el transporte de los tests es un loopback en memoria. `FakeSessionPort` no es
  autoridad de producción. No hay prueba con dos procesos o equipos, Internet, latencia, NAT, WebSocket ni build ejecutado.
- No se integró con `MissionSession` (no existe en la base) ni con la UI de D4.
- Rendimiento con carga, límites de tasa por cliente y permisos por rol (no definidos en fase 1).

## Peticiones y pendientes

1. **Equipo:** elegir SDK/transporte/plataforma. Después, el binding implementa las dos interfaces en
   archivos nuevos autorizados (sin tocar contratos). Si el SDK impone mensajes tipados, puede
   transportar los `byte[]` de este protocolo o pedir un cambio común.
2. **D2:** documentar si `MissionSession.TryGetSnapshot` puede devolver `false` y en qué hilo emite
   eventos. El bridge requiere hilo principal. D2 decide dónde vive la autoridad y crea el bridge solo en el host.
3. **D4:** el panel de conexión puede leer `SessionConnectionController.State/StateChanged/
   LastDisconnectReason` y `CommandAbandoned`. Si se quiere desacoplar mediante un contrato compartido
   de conexión, debe pedirse como cambio común; no lo añadí a la base.
4. **Contrato (propuesta, no aplicada):** `ISessionPort` no tiene señal de "resultado imposible"
   (desconexión). Hoy se cubre con `CommandAbandoned` específico del adaptador.
5. **Permisos:** `SESSION_CONTRACT.md` dice que identidad/permisos se validan externamente. El bridge
   ya conoce el `connectionId` del remitente; una política de roles se añadiría ahí cuando exista.
6. Memoria: el mapa ID→remitente crece durante la vida del bridge (igual que el historial del fake).

### Huecos de diseño detectados en `/code-review` (documentados, no corregidos)

Los errores concretos de esa revisión se corrigieron con tests de regresión (ver arriba). Estos quedan
pendientes porque requieren decisiones del equipo, un SDK o un cambio de contrato:

1. **Rechazos del host sin aviso al cliente:** si el host descarta un mensaje (`UnsupportedVersion`,
   `UnknownConnection`…), solo lo reporta en el host. Un cliente con otra versión se queda en
   `Synchronizing` y sus comandos pendientes no terminan. Propuesta: timeout de sincronización en el
   bootstrap de Unity (requiere reloj; llamar `Disconnect()` al vencer) y/o un mensaje de rechazo en v2.
2. **Propiedad de IDs ligada a `connectionId`:** tras reconectar, reenviar un comando abandonado recibe
   `CommandIdConflict` aunque la autoridad lo hubiera aplicado. Hace falta identidad estable del jugador
   (autenticación del SDK). Mientras tanto: no reenviar tras reconectar; el snapshot dice la verdad.
3. **Sin límites por cliente:** el mapa de IDs no tiene tope y no hay rate limit de `Submit`/`SyncRequest`.
4. **Sin señal en `ISessionPort`:** el abandono solo existe como `CommandAbandoned` del adaptador
   (petición 4). Una UI que use solo `ISessionPort` puede esperar indefinidamente.
5. **Reconexión silenciosa:** al abrir un enlace nuevo, `TryGetSnapshot` pasa a `false` sin evento; la UI
   debe atenuar el último estado según `SessionConnectionController.State`.
6. **Asignaciones por mensaje** (stream, writer, closure, copia): solo relevante si D2 publica un snapshot
   por tick; optimizable con buffers de tamaño fijo cuando se mida.
7. `linkOpen` del adaptador duplica parte del estado del controller; ambos se actualizan juntos y antes
   de cualquier callback.

## Pasos de conexión posterior (tarea de unificación autorizada)

1. Aprobar SDK y añadirlo al manifest en un cambio común.
2. Implementar `ISessionHostTransport` / `ISessionClientTransport` sobre ese SDK.
3. D2: en el bootstrap del host, crear la autoridad única y `new NetworkCommandBridge(authority, host)`.
   En clientes, crear `SessionConnectionController` y entregar `connection.Session` a la UI.
4. D4: conectar el panel al controller; sustituir el fake por `connection.Session` en la escena (solo en la unificación).
5. Ejecutar la prueba de aceptación con dos clientes reales sobre el build objetivo: misma batería,
   carga esencial, ciencia opcional, resultado/reinicio coherentes, pérdida de host controlada.
