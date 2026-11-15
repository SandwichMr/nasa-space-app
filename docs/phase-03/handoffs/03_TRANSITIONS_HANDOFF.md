# HANDOFF D3 — Fase 3: roster por zonas, transiciones, red e interacción

Rama `feature/phase-03/technical-core`. Base común `35d97ceccb879142e8e60723b9558ca9c6b32f1f`. Commits de D3: `9bda487` (roster), `974d721` (cable, adaptadores e interacción) y `3748635` (prueba de sockets).

## Qué se extendió (sin sustituir nada)

- **Transporte**: el mismo Unity Transport 2.7.4 sobre WebSocket (`UnityTransportHost`/`UnityTransportClient`) y los mismos argumentos (`-trainer-bind/-trainer-port/-trainer-server/-trainer-connect`, consulta WebGL). Ni paquetes ni servicios nuevos.
- **Roster**: el mismo `CrewRoster` de fase 2 implementa `IZonedCrewRoster`. Sigue siendo el único que mueve y la única autoridad de colisión.
  - Escotillas cerradas como obstáculos superpuestos. Se rechaza el cambio si dejaría a alguien solapado.
  - Velocidad ×(0, 1] por miembro.
  - Sin esas llamadas, el comportamiento de fase 2 no cambia (conformidad de D5 de fase 2: 16/16).
- **Cable de fase 3** `'T' 'E'` v1: mismos tipos de mensaje y troceado (≤ 1100 B por trama, ≤ 16 KB por mensaje) que el `'T' 'O'`. Los ayudantes de fase 2 se hicieron `internal` y el troceado se parametrizó por cabecera; los bytes y el comportamiento de fase 2 **no cambian** (pruebas de fase 2 en verde). Los cables `'T' 'S'` y `'T' 'O'` siguen intactos y cada decodificador rechaza los otros como `BadMagic`.

| Tipo | Sentido | Cuerpo |
| --- | --- | --- |
| 1 Join | cliente→host | vacío |
| 2 Submit | cliente→host | Guid · epoch · acción · estación · tarea · `ZoneSequence` · `AirlockCycle` |
| 3 Movement | cliente→host | igual que fase 2 (sin posiciones) |
| 10 Result / 12 Membership / 13 Chunk | host→cliente | igual que fase 2 |
| 11 Snapshot | host→cliente | cuerpo de snapshot de fase 2 **sin cambios** + extensión de fase 3: tripulación, trajes, esclusa, refugio, suministros, avisos emitidos, recepciones, instrucción, fase, decisión, resultado, objetivos, evaluaciones, incidentes y revelación |

La decodificación es estricta: todo pasa por los constructores del contrato y, si falla, queda `MalformedPayload`.

## Archivos

| Archivo (`Assets/_Project/…`) | Qué es |
| --- | --- |
| `Runtime/Unity/Networking/Expedition/ExpeditionWireProtocol.cs` | Cable `'T' 'E'` |
| `…/ExpeditionCommandBridge.cs` | Puente de host: identidad solo por `Join`, estado completo antes de `Joined`, resultados solo al remitente, `Leave` una vez al cerrar |
| `…/ExpeditionNetworkSessionAdapter.cs` | `IExpeditionSessionPort` remoto + `ExpeditionConnectionController` |
| `…/LocalExpeditionSessionPort.cs` | Puerto del jugador dentro del proceso autoridad |
| `…/ExpeditionNetworkHost.cs`, `…/ExpeditionNetworkClient.cs` | MonoBehaviours. El cliente lee los argumentos una vez, lo que ocurra antes entre `Prepare` y `Start` |
| `…/ExpeditionCrewPresenter.cs` | Pasa `ExpeditionCrewFrame` a los `IExpeditionCrewView` de los avatares de fase 2 |
| `Runtime/Application/Crew/ExpeditionInteractionClient.cs` | `IExpeditionIntents`: la "E" y las marcas de zona y de ciclo |
| `Runtime/Unity/Input/Expedition/ExpeditionInteractionInput.cs` | Teclas E (interactuar) y R (declarar regreso) |
| Mantenimiento de fase 2 | `CrewRoster.cs`, `OutpostWireProtocol.cs` (visibilidad), `CrewAvatarSync.cs` (`TryGetAvatar`, solo lectura), `OutpostNetworkClient.cs` (corrección de la etapa 0) |

## Reglas de transición comprobadas

- **Una sola autoridad** decide la posición, la zona, la salida y entrada, la ocupación y el ciclo de la esclusa, las tareas, el traje, los recursos y la protección. Los clientes no simulan ni consumen nada, así que no hay copia que pueda divergir.
- **Marcas**: las intenciones dependientes del lugar llevan la `ZoneSequence` propia y el `AirlockCycle` vistos al enviarlas. Si cambiaste de zona o empezó otro ciclo, se rechazan (`StaleZone`/`StaleAirlockState`). Los comandos de fase 2 (controles de sesión) se marcan en el puerto con la zona confirmada.
- **Movimiento**: el `CrewKeyboardInput`/`CrewInputSender` de fase 2, sin un segundo controlador. En la escena de fase 3, su E está desactivada y la E/R son de `ExpeditionInteractionInput`.

## Pruebas reales

| Prueba | Topología | Resultado |
| --- | --- | --- |
| `Phase03NetworkLoopbackTests` (7): estado remoto idéntico campo a campo, incorporación tardía con estado completo, dos peticiones simultáneas a la esclusa (una aceptada, otra `StaleAirlockState`), intención en vuelo durante un cambio de zona (`StaleZone`), desconexión fuera (sin fantasma ni reservas, traje abandonado, nadie sigue consumiendo), reconexión con id nuevo, pausa y reinicio limpios por la red, cables ajenos o corruptos rechazados en ambos lados | autoridad real + transporte en memoria | 7/7 en Unity nativo |
| `Phase03TransportSocketTests` (3): mismo estado autoritativo en dos clientes, snapshots completos troceados (> 1100 B), cierre de socket = un `Leave` | **sockets WebSocket reales** en 127.0.0.1, un proceso | 3/3 (PlayMode 59/59 en total) |
| Partida entre **procesos separados** con la build | autoridad + 2 clientes | ver `05_VALIDATION_HANDOFF.md` |

## Pendiente y decisiones abiertas

- Igual que en fase 2: sin predicción local, sin límite de tasa por conexión, sin token de sala y con pausa y reinicio permitidos a cualquier miembro.
- No probado: WebGL en navegador, `wss`, LAN entre dos equipos.
