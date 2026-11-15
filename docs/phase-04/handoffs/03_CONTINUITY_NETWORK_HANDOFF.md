# HANDOFF D3 — Fase 4: red continua de la base viva

**Estado: entrega de D3 (red y sus pruebas propias). No es la aceptación de la fase.** La aceptación integral es de D5 y del
coordinador. La autoridad continua de D2 todavía no existe en esta base: todo lo que aquí cruza la red lo produce un
**doble de pruebas rotulado** (ver «Qué prueba y qué no»).

## Base y rama

| Dato | Valor |
| --- | --- |
| `main` actualizado (fast-forward, `origin/main`) | `1ca26923ad7d29cfe7ebdbb2969c331a8875c4e0` |
| Base común publicada por D2 (`setup/phase-04-shared-contracts`) | `9b96f80ee8102073152d9e51cad4beb5a353aeb5` |
| `git merge-base --is-ancestor main base` | sí |
| Rama | `feature/phase-04/d03-networking`, creada desde la base común (nombre del encargo; `COMMON_BASE.md` preveía `d03-d05-continuity`, de antes de separar D3 y D5) |
| Trabajo combinado D3+D5 previo | ninguno: no había ramas ni commits de fase 4 de D3/D5 en `origin` |

## Lista de escritura (registrada antes de escribir código)

Según las filas `D3` de la lista legible por máquina de `docs/phase-04/OWNERSHIP.md`. Prefijo `Assets/_Project/`:

| Ruta | Qué |
| --- | --- |
| `Runtime/Unity/Networking/BaseOperations/BaseOperationsWireProtocol.cs` | Cable `'T' 'B'` v1 (nuevo) |
| `…/BaseOperationsCommandBridge.cs` | Puente de host sobre `IBaseOperationsAuthority` |
| `…/BaseOperationsNetworkSessionAdapter.cs` | `IBaseOperationsSessionPort` remoto + `BaseOperationsConnectionController` |
| `…/LocalBaseOperationsSessionPort.cs` | Puerto del jugador dentro del proceso autoridad |
| `…/BaseOperationsNetworkHost.cs`, `…/BaseOperationsNetworkClient.cs` | MonoBehaviours de composición (mismo transporte y argumentos) |
| `Runtime/Application/Crew/BaseOperationsInteractionClient.cs` | `IBaseOperationsIntents` (C# puro) |
| `Runtime/Unity/Input/BaseOperations/BaseOperationsInteractionInput.cs` | Tecla E de fase 4 |
| `Runtime/Unity/Networking/Expedition/ExpeditionWireProtocol.cs` | **Solo visibilidad**: 6 métodos `private` → `internal` (`WriteExtension`, `ReadExtension`, `WriteOptional`, `ReadOptional`, `Count`, `Key`). Ni un byte ni una regla del `'T' 'E'` cambian |
| `Tests/EditMode/Phase04/Networking/Editor/*.cs` | Pruebas en memoria y del cliente de interacción |
| `Tests/PlayMode/Networking/Phase04/*.cs` | Pruebas del cable, doble de autoridad de pruebas y sockets reales |
| `docs/phase-04/handoffs/03_CONTINUITY_NETWORK_HANDOFF.md` | Este archivo (nombre de `OWNERSHIP.md`; el encargo lo llamaba `03_CONTINUOUS_NETWORK_HANDOFF.md`) |

Más los `.meta` de esos archivos y carpetas. No se tocó nada congelado, ni escenas, `Packages/**`, `ProjectSettings/**`,
renderer, otros roles ni las rutas de D5.

## Qué se entrega

### Transporte: el mismo

Unity Transport 2.7.4 sobre WebSocket, los mismos `UnityTransportHost`/`UnityTransportClient` y los mismos argumentos
(`-trainer-bind/-trainer-port/-trainer-path/-trainer-cert/-trainer-key` en el host; `-trainer-server/-trainer-port/-trainer-path/-trainer-secure/-trainer-server-name/-trainer-connect`
o la consulta WebGL `?server=&port=&path=&secure=&connect=1` en el cliente). Ni paquetes, ni servicios, ni puertos nuevos.
Una escena usa **un** cable: el host de fase 4 no convive con uno de fase 2/3 en el mismo puerto.

### Cable `'T' 'B'` v1 (little-endian)

| Tipo | Sentido | Cuerpo |
| --- | --- | --- |
| 1 Join | cliente→host | vacío |
| 2 Submit | cliente→host | `ExpeditionCommand`, el mismo cuerpo que `'T' 'E'` (pausa, reanudar, reiniciar, tareas, esclusa, regreso) |
| 3 Movement | cliente→host | igual que fases 2–3 (época, secuencia, dirección; **sin posiciones**) |
| **4 Operation** | cliente→host | `BaseOperationCommand`: Guid · época · acción (int32) · estación · elección (int32) · manifiesto · secuencia de pedido · secuencia de observación · secuencia de zona |
| 10 Result / 12 Membership | host→cliente | cuerpos de fase 2 sin cambios |
| 11 Snapshot | host→cliente | cuerpo de fase 2 **sin cambios** + extensión de fase 3 **sin cambios** (los codificadores del `'T' 'E'`) + extensión de fase 4 |
| 13 Chunk | host→cliente | troceado de fase 2 (≤ 1100 B por trama) bajo la cabecera `'T' 'B'` |

- **Extensión de fase 4**, en este orden: sesión (modo, segundos sin servicio esencial, ventana, causa, fin, secuencia de
  actividades, salidas terminadas) · 8 barras (tipo, lectura con estado/unidad/valor/hora, capacidad, tendencia, líneas,
  banda) · 2 máquinas (estado operativo, conocimiento, síntomas, secuencia de estado, última observación con hallazgos y
  ≤ 4 lecturas y su vigencia, trabajo de reparación) · tratamiento de agua · repuestos · operaciones (con su repuesto
  reservado) · pedidos (con lo que falta por descargar) · actividades · decisiones · informe.
- **Estado completo**, nunca un delta. El cliente lo ordena por `(Epoch, Revision)`.
- **Decodificación estricta**: cabecera o versión ajena (`BadMagic`/`UnsupportedVersion`), tipo desconocido o del otro
  sentido, bytes de menos o de más, cuentas por encima de los límites del contrato, bits de `Findings` inexistentes y todo lo
  que rechazan los constructores del contrato (NaN, rangos, bandas que no siguen `GaugeBands`, invariantes del snapshot) →
  código de red, nada parcial publicado. Acciones o elecciones desconocidas **sí** viajan: las responde la autoridad
  (`ShapeError`).
- **Límites**: cliente→host ≤ 256 B (una operación ocupa ≤ 126 B). Host→cliente: reensamblado ≤ **32 KB** propio de este
  cable, porque el peor caso analítico de un snapshot de fase 4 es 25 416 B (16 384 B, cota del `'T' 'E'` + 9 032 B de la
  extensión de fase 4 calculada desde `BaseOperationsLimits`) y no cabe en los 16 KB de los cables anteriores. Mismo diseño
  de troceado, sin tocar `OutpostWireProtocol.cs` (no es de D3 en esta fase).
- **Tamaños medidos** (pruebas): ver «Resultados».

### Host: `BaseOperationsCommandBridge` + `BaseOperationsNetworkHost`

- Identidad solo por `IBaseOperationsAuthority.Join()` sobre una conexión real. Ningún campo del cable elige el remitente.
- Quien se incorpora tarde recibe el **estado actual completo** (reloj, stocks, máquinas, repuestos y reservas, pedidos,
  operaciones, actividades) **antes** de su `Joined`. Nunca los valores iniciales.
- Resultados solo al remitente. Submit/Operation sin `Join` → `NotJoined` (sin llegar a la autoridad).
- Desconexión, cierre del socket, fallo de envío o `Dispose` → `Leave` **una vez** por miembro (`LeaveCount`). Lo que haga
  `Leave` con operaciones, reservas y pedidos lo decide la autoridad (D2), según `SESSION_CONTRACT.md`: la operación termina
  con `PerformerLeft`, los repuestos reservados se liberan, la energía no se devuelve y **los pedidos se conservan**.
- Snapshots coalescidos: el más nuevo, una vez por frame (`FlushSnapshots` en `LateUpdate`).
- `BaseOperationsNetworkHost.Attach(IBaseOperationsAuthority)`, `Detach`, `CreateLocalPort()`, `StartHost/StopHost`:
  la misma API que `ExpeditionNetworkHost`.

### Cliente: `BaseOperationsNetworkSessionAdapter` + `BaseOperationsConnectionController` + `BaseOperationsNetworkClient`

- `Joined` solo cuando hay un snapshot que contiene al jugador asignado.
- Por cada revisión guardada: `SnapshotChanged` (fase 2) → `ExpeditionChanged` (fase 3) → `BaseOperationsChanged` (fase 4).
- Snapshots no más nuevos → descartados (`DiscardedSnapshotCount`). Resultados emparejados por `(Id, Epoch)`: uno atrasado,
  repetido o ajeno no completa nada (`DiscardedResultCount`).
- **Fallo de envío** (Unity Transport lanza si la cola rechaza el mensaje): la petición se abandona como `ConnectionLost`
  (resultado desconocido, nunca éxito ni rechazo inventado), se emite `SendFailed` y se cierra el enlace. No se lanza la
  excepción al que llamó.
- Pérdida del enlace → `Left`, pendientes abandonados (`ConnectionLost`/`LocalDisconnect`); el último snapshot queda solo como
  último estado conocido.
- Nunca simula, ni avanza un reloj, ni guarda un inventario, ni mueve a nadie.

### Puerto local: `LocalBaseOperationsSessionPort`

El jugador que hospeda es un miembro normal de la misma autoridad (su propio `PlayerId`, las mismas validaciones). Solo se
salta la red.

### Interacción local: `BaseOperationsInteractionClient` (`IBaseOperationsIntents`) y `BaseOperationsInteractionInput`

- Sella cada intención con la **época**, la **secuencia de zona** confirmada y, para las decisiones (`RouteProduct`,
  `ServiceAir`, `Isolate`, `Intervene`, `Restore`), la **secuencia de la observación vigente** de esa máquina. Sin
  observación vigente no envía nada (`false`). La vista no inventa observaciones.
- `RequestCancelOperation` no lleva marca de zona (alejarse no debe impedir parar). Los pedidos sí.
- No envía nada mal formado (`ShapeError`), ni sin membresía o estado, ni con otra petición en vuelo.
- Una petición en vuelo. La libera solo su resultado `(Id, Epoch)` o un abandono, y también una membresía perdida.
- Ayudas de UI: `TryFindStationInReach` (solo agua, aire y logística), `TryGetOwnOperation`, `TryGetCurrentObservation`.
- **E** (`BaseOperationsInteractionInput.Activate`):
  1. cancela la operación propia en curso;
  2. si hay una estación de fase 4 al alcance, lanza `StationActivated(station)` para que el panel de D4 ofrezca sus
     decisiones (la tecla nunca elige por sí sola);
  3. si no, pasa a la interacción de fase 3 que se le dé en `Bind`.

  El movimiento sigue en `CrewKeyboardInput` (un solo controlador) y la R en `ExpeditionInteractionInput`.

## Para D2: cómo se monta

```csharp
// Autoridad (proceso host o servidor dedicado)
networkHost.Attach(authority);                        // BaseOperationsNetworkHost, IBaseOperationsAuthority de D2
LocalBaseOperationsSessionPort port = networkHost.CreateLocalPort(); port.Join();   // solo si el host también juega
// Cliente
BaseOperationsNetworkSessionAdapter port = networkClient.Prepare();                 // BaseOperationsNetworkClient
// Ambos
keyboardInput.Bind(port, scenario.Expedition.Outpost);  avatarSync.Bind(port);  crewPresenter.Bind(port, avatarSync);
ExpeditionInteractionClient p3 = expeditionInput.Bind(port, scenario.Expedition);    // con interactWithE = false
BaseOperationsInteractionClient intents = baseOperationsInput.Bind(port, scenario, p3);
new BaseOperationsViewContext(port, scenario, intents, p3, (owner, blocked) => { /* bloquear teclas y movimiento */ });
```

`IBaseOperationsSessionPort` hereda de `IExpeditionSessionPort`: las vistas y componentes de fases 2–3 se enlazan al mismo
puerto sin cambios.

## Para D5: receta de conexión (sin métodos de prueba)

La misma de fase 3, con la build de la escena de fase 4 (la herramienta de build es de D5; el rol lo lee la composición de D2
con `-trainer-role authority|client`, como en fase 3):

```sh
P=<build de Phase04_LivingBase>
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7791 -logFile Logs/authority.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7791 -trainer-connect -logFile Logs/a.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7791 -trainer-connect -logFile Logs/b.log
```

- El host escribe `[BaseOperationsNetworkHost] Listening on ws://… (cable 'T' 'B' v1)`.
- El cliente escribe `[BaseOperationsNetworkClient] Connected as P<n>`.
- Un piloto solo necesita lo público: `IBaseOperationsSessionPort` (`SubmitMovement` con direcciones sostenidas) e
  `IBaseOperationsIntents`/`IExpeditionIntents`. Nada cambia stocks ni posiciones por atajo.
- En proceso, `BaseOperationsConnectionController` sobre cualquier `ISessionClientTransport` y `BaseOperationsCommandBridge`
  sobre cualquier `ISessionHostTransport` (el `InMemoryOutpostNetwork` de las pruebas sirve).
- Contadores útiles: `LeaveCount`, `LargestSnapshotBytes`, `IgnoredMovementCount` (host); `DiscardedSnapshotCount`,
  `DiscardedResultCount`, `FailedSendCount`, `PendingCommandCount` (cliente).

## Defecto encontrado en la base congelada (para D2; no lo corrijo yo)

En `9b96f80`, `Runtime/Application/Contracts/BaseOperations/Session.cs`:

- **`ActivityRecord.EndedAtSeconds`** nunca se asigna en el constructor, que sí lo valida.
- **`BaseSessionStatus.EndedAtSeconds`** tampoco se asigna.

Una búsqueda de todas las propiedades `{ get; }` de los contratos de fase 4 no encontró otras.

Consecuencias:

1. Toda actividad cerrada y toda sesión terminada se publican sin hora de fin (`null`). D4/D5 no pueden mostrarla.
2. El constructor exige esa hora al reconstruirlas, así que **ningún cable puede volver a construir esos objetos** tal cual.

Lo que hace el cable mientras dure el defecto:

- Al arrancar, el decodificador **comprueba el contrato real**: construye un registro de prueba y mira si guarda la hora.
- Si la hora se pierde, rellena una hora provisional que el constructor valida y descarta:
  - actividad cerrada → su inicio;
  - sesión terminada → la hora del snapshot.

  El objeto del cliente queda **idéntico** al de la autoridad (la prueba lo compara campo a campo).
- Cuando D2 publique una base corregida, la comprobación da `false` y una hora ausente vuelve a ser `MalformedPayload`, sin
  tocar el cable.

**Petición a D2**: una base coordinada con `EndedAtSeconds = endedAtSeconds;` en ambos constructores, y una prueba de
contrato que lea esas propiedades.

## Qué prueba y qué no

| Nivel | Qué | Dónde |
| --- | --- | --- |
| Cable (bytes) | ida y vuelta exacta, cuerpos de fases 2–3 idénticos byte a byte, truncados, sobrantes, NaN, enums, bits, cuentas, ids negativos, cables ajenos, troceado y cotas | `Tests/PlayMode/Networking/Phase04/Phase04NetworkWireTests.cs` (12) |
| Cliente de interacción + fake de D2 | sellos, no-envíos, una petición en vuelo, respuestas, abandono, excepción | `Tests/EditMode/Phase04/Networking/Editor/Phase04NetworkInteractionTests.cs` (10) |
| Extremo a extremo **en memoria** (sin sockets) | ver lista abajo | `…/Phase04NetworkLoopbackTests.cs` (13) |
| **Sockets WebSocket reales**, un proceso, 127.0.0.1 | 3 clientes con el estado idéntico; troceado real; cierre de socket durante una intervención; incorporación tardía | `Tests/PlayMode/Networking/Phase04/Phase04NetworkSocketTests.cs` (4) |
| Procesos separados, equipos reales, WebGL, `wss`, LAN | **no probado por D3** | aceptación de D5 |
| La autoridad real de D2 | **no existe en esta base** | integración |

Casos de extremo a extremo en memoria:

- estado remoto idéntico campo a campo y orden de eventos 2 → 3 → 4;
- incorporación tardía con el estado actual;
- pausa (sin reloj de cliente; cancelar lo propio sigue permitido);
- dos clientes en la misma estación a la vez;
- decisión en vuelo cuya observación caduca → `StaleObservation`;
- reinicio con una petición en vuelo → `EpochMismatch`, y la época vieja reenviada entera no cambia nada;
- petición repetida o falsificada desde otra conexión → resultado guardado / `CommandIdConflict`, sin segundo cobro antes ni
  después de completar;
- resultados solo al remitente;
- desconexión durante una intervención → `Leave` una vez, repuesto libre y sin consumir, pedido conservado, reconexión con
  identidad nueva;
- alejarse caminando termina la operación;
- fallo de envío → `ConnectionLost` sin excepción y enlace cerrado;
- mensajes ajenos o corruptos en ambos sentidos;
- envío sin enlace.

**El doble `Phase04NetworkTestAuthority` NO es la autoridad.** Usa el `CrewRoster` real y aplica solo el subconjunto de
reglas de `SESSION_CONTRACT.md` §5 que la red necesita ejercitar: identidad, época, ids deduplicados, ocupación, alcance,
sellos de observación, reserva y liberación de repuestos, pedidos y reinicio.

- Sin modelo de equipo: no hay lotes, rutas, aislamiento ni avería.
- Los stocks son las lecturas iniciales del fixture.
- Fases 2/3: solo pausa, reanudar y reiniciar.

Lo que estas pruebas demuestran es la parte de la red. Las reglas de la base son de D2 y se prueban con su autoridad.

## Resultados

Ejecutados en el checkout de D3 (no la laptop del proyecto original), Unity 6000.3.28f1, sin otro Editor abierto sobre el
proyecto. Logs y XML en `Logs/phase04-d3/` (ignorado por git): `runs.txt`, `editmode-all.{xml,log}`, `playmode-all.{xml,log}`.

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` contra `netstandard` 2.1 solo, `-warnaserror` (CS1591/CS1573 excluidos), Roslyn del Editor | exit 0 |
| Compilación gestionada de `Assembly-CSharp` y `Assembly-CSharp-Editor` con las referencias del proyecto (Roslyn del Editor) | 0 errores |
| Runner gestionado por reflexión (.NET 6.0.21 del Editor, NUnit del proyecto), antes de Unity | cable 12/12; EditMode Phase04 54/54 (31 de D2 + 23 de D3); regresión `Phase03NetworkLoopbackTests` 7/7 y `OutpostNetworkLoopbackTests` 16/16 |
| **Unity nativo, EditMode completo** (`-runTests -testPlatform EditMode`) | **436/437**. Todo D3 en verde: `Phase04NetworkLoopbackTests` 13/13 y `Phase04NetworkInteractionTests` 10/10. Contratos de D2 31/31. Red de fases 2–3: `OutpostNetworkLoopbackTests` 19/19 y `Phase03NetworkLoopbackTests` 7/7. **1 fallo, de la base y no de D3** (abajo) |
| **Unity nativo, PlayMode completo** (`-runTests -testPlatform PlayMode`) | **75/75**: `Phase04NetworkWireTests` 12/12, `Phase04NetworkSocketTests` 4/4 (sockets WebSocket reales en 127.0.0.1), `Phase03TransportSocketTests` 3/3, `OutpostTransportSocketTests` 3/3, el resto de fases 1–3 en verde |
| Procesos separados / equipos reales / WebGL / `wss` | no ejecutados por D3 (aceptación de D5) |

Tamaños medidos:

- snapshot más grande sobre sockets, 3–4 clientes: **1 642 B** (2 tramas);
- estado cargado cerca de los límites (32 actividades, 16 decisiones, 8 pedidos): **6 294 B**, de los que 5 331 B son la
  extensión de fase 4;
- cota analítica del peor caso: 25 416 B, por debajo del límite de 32 768 B.

**Fallo de EditMode heredado de la base `9b96f80`.**

- Prueba: `Phase03SceneTests.TheShippedSceneIsWiredWithoutPhysicsFakesOrASecondCable` (línea 96).
- Causa: `Phase03BuildChecks.ScanProductionFakeUsage()` busca `ExpeditionFixtures`/`ExpeditionSnapshotBuilder` en
  `Assets/_Project/Runtime/**` y solo exime `/Contracts/Expedition/Development/`. El fixture de D2
  `Runtime/Application/Contracts/BaseOperations/Development/BaseOperationsFixtures.cs` los usa, y es el único archivo que
  coincide (comprobado con `grep`).
- Ningún archivo de D3 coincide: mi doble de pruebas está en `Tests/`, fuera del escaneo.
- D2 no lo vio porque Unity nativo quedó bloqueado en su preparación.
- Se arregla en una base coordinada (D2) o en la herramienta de D5. D3 no lo toca.

## Pendiente y decisiones abiertas

- **Integración con la autoridad real de D2**: el puente y los puertos están escritos contra `IBaseOperationsAuthority`. No
  se han ejecutado contra ella porque no está publicada. D2 los monta en la escena; D5 hace la aceptación.
- **Defecto de `EndedAtSeconds`** en la base congelada: espera una base coordinada de D2 (arriba).
- **Reconexión** = una incorporación nueva con otro `PlayerId`. No reanuda identidad, operación ni reserva (probado). No
  hay reconexión automática ni migración de host.
- Igual que en fases 2–3:
  - sin predicción local;
  - sin límite de tasa por conexión;
  - sin token de sala;
  - pausa y reinicio permitidos a cualquier miembro (regla de la autoridad).
- **Ancho de banda**: un snapshot completo por frame (30 fps en `-batchmode`) por cliente. Medido en pruebas (ver
  «Resultados»). Si hiciera falta, se puede espaciar el envío en el host sin cambiar el cable.
- No probado por D3: dos procesos o más, LAN entre equipos, WebGL en navegador, `wss`, Internet, crossplay. No se prometen.
