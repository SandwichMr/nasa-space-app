# HANDOFF D2 — Fase 3: autoridad única, expedición, refugio, escenario y escena

Rama `feature/phase-03/technical-core`. Base común `35d97ceccb879142e8e60723b9558ca9c6b32f1f`. Commits de D2: `ad5135b` (autoridad) y el commit de integración de la escena (ver `git log`).

**La fase 3 no está cerrada**: falta la entrega visual de D4 y la partida con personas (ver [Pendiente](#pendiente)). Los resultados medidos están en `05_VALIDATION_HANDOFF.md`.

## Archivos

| Archivo (`Assets/_Project/…`) | Qué es |
| --- | --- |
| `Runtime/Application/Expedition/ExpeditionAuthority.cs` | `IExpeditionAuthority`: la ÚNICA autoridad, para la base y el exterior |
| `…/Expedition/AdvisoryTimeline.cs` | Lo que la base sabe del episodio: emisión, vigencia, recepción por cobertura, instrucción |
| `…/Expedition/ProtectionTracker.cs` | Evaluación de protección solo con lo recibido |
| `…/Expedition/ReturnRouteField.cs` | Distancia de regreso (Dijkstra, malla de 0,25 m) a la entrada de la esclusa |
| `Runtime/Unity/Configuration/Expedition/ExpeditionScenarioDefinition.cs` + `Data/Scenarios/Phase03/Phase03_EvaAndShelter_Synthetic.asset` | Datos del escenario. Los valores por defecto SON el escenario sintético entregado |
| `Runtime/Unity/Bootstrap/Expedition/{ExpeditionBootstrap,ExpeditionRunner,ExpeditionSceneComposition}.cs` | Composición, reloj único y roles |
| `Runtime/Unity/Bootstrap/Expedition/Provisional/{ProvisionalExpeditionHud,ProvisionalExpeditionWorld}.cs` | Vistas **PROVISIONALES** (rotuladas) hasta que llegue D4 |
| `Editor/Composition/Phase03/Phase03SceneBuilder.cs` + `Scenes/Phase03_EvaAndShelter.unity` | Montaje reproducible de la escena real |
| `Tests/EditMode/Phase03/Application/Editor/*.cs` (6) | Arnés sobre la pila real y 33 pruebas |
| `Tests/EditMode/Phase03/Bootstrap/Editor/Phase03SceneTests.cs` | Escena, asset y composición |

## Autoridad única

`ExpeditionAuthority` es dueña del **único** roster (el `CrewRoster` de fase 2, que ahora implementa `IZonedCrewRoster`), del único modelo de base (`OutpostBaseSimulation`) y del único modelo EVA (`EvaSuitSimulation`), y es la única que los llama. No implementa `IOutpostAuthority`: las herramientas de fase 2 no pueden confundirla.

**Paso** (`Tick`; los pasos se cortan exactamente en emisiones o caducidades de avisos, plazos, fin de ciclo y eventos de reserva):

1. El roster mueve. Las escotillas cerradas son superposición de obstáculos; un traje sellado camina a ×0,6.
2. Se confirman las zonas; si cambia, sube `ZoneSequence`.
3. Terminan las tareas cuyo ejecutor salió del alcance o de su zona (`PerformerMovedAway`).
4. Se deriva la actividad: tarea exterior = `Work`, caminata confirmada = `Walk`, resto = `Rest`.
5. La base avanza con la tripulación **no sellada** y la carga de servicio: tareas, cargador, bomba de la esclusa y ventilación del refugio. El modelo EVA avanza solo con los trajes sellados.
6. Se acredita a las tareas y al ciclo de la esclusa lo plenamente servido. Si el servicio no está completo, quedan `Stalled`.
7. Las tareas completadas aplican sus efectos una sola vez: checklist, O2 y agua, ciencia, refugio.
8. Se emiten y reparten los avisos (cobertura de radio), se observa la protección y se registra el uso de la reserva.
9. Se evalúa el fin, en el orden del contrato.

**Validación**: el orden exacto de `SESSION_CONTRACT.md`, incluidas las marcas `StaleZone`/`StaleAirlockState`. Si dos jugadores piden la esclusa a la vez, gana el primero; el otro recibe `StaleAirlockState`.

**Esclusa**:

- `CycleAirlock` desde un panel. Nunca cierra una escotilla sobre una persona (`AirlockObstructed`).
- Capacidad 2 para ciclar. Despresurizar exige trajes listos (`SuitNotReady`) y que no haya instrucción vigente con ocupantes (`EgressBlockedByInstruction`); un ciclo vacío para recoger a alguien está permitido.
- La bomba consume 120 W de servicio y se detiene sin energía. Cada represurización toma una vez 0,30 kg de gas de reposición.
- Al despresurizar sella a los ocupantes; al represurizar los desella y vuelve su checklist a `Don`.

**Refugio**: protección efectiva = zona `Shelter` + refugio preparado (`prepare-shelter`, 6 s, 30 W) + ventilación (40 W) plenamente servida en el último paso. La ventilación arranca el paso siguiente a la preparación, así que hay una latencia de un subpaso (≤ 0,25 s).

**Avisos**:

- Solo existe lo emitido. `Expired` significa "sin información", nunca "seguro"; la instrucción sigue vigente hasta un `InstructionEnd` explícito.
- La recepción depende de la cobertura: base, esclusa, o exterior a ≤ 20 m de la antena. Quien se une tarde recibe al unirse los mensajes vigentes.
- La evaluación usa solo emisiones y recepciones. Una prueba comprueba que cambiar los tiempos ocultos del evento no cambia el resultado.

**Fin**:

- En este orden: servicio esencial; traje agotado (sin consecuencias clínicas); fin de la instrucción; salida cerrada (nadie sellado y primario cumplido o regreso declarado); tiempo.
- La represurización que desella al último es el ingreso, como fija el contrato.
- Volver sin ciencia tras `DeclareReturn` da `ReturnedWithoutScience`, un resultado correcto.

**Desconexión de un sellado**: tarea `PerformerLeft`, traje `Abandoned` (no consume, no se teletransporta), incidente `DisconnectedWhileSealed`, sin fantasma. Al reconectar entra con id nuevo dentro de la base. Si alguien con traje puesto pero sin sellar se va, el traje vuelve a su soporte.

## Escenario entregado (sintético)

Es el plano publicado en `COMMON_BASE.md`, sin cambios.

- **Plano**: la base de fase 2 intacta; el ala EVA (preparación con 2 soportes, cargador, O2 + agua y revisión; refugio con su panel); esclusa de 3 × 3 m; exterior de 29 × 22 m con rocas, ciencia cercana `regolith-site` y opcional `crater-rim` (fuera de radio).
- **Episodio SINTÉTICO**: evento oculto de 140 a 220 s; aviso a 150 s (refugiarse antes de 210 s, vigente hasta 280 s); actualización a 195 s; fin explícito a 235 s.
- **Misión**: 360 s.
- **Proporción de los recorridos**: A se completa hacia los 100–110 s, antes del aviso. Si se va al opcional o se tarda, el aviso los encuentra fuera.

## Escena `Phase03_EvaAndShelter.unity` (montada, no reservada)

La genera `Phase03SceneBuilder`: menú `Tools/Trainer/Phase 3/Build scenario asset and scene` o el CLI del handoff de D5.

| Objeto | Contenido |
| --- | --- |
| Main Camera | Ortográfica (como fase 2), `LocalCameraFraming` (D4 fase 2) y URP |
| Global Light 2D | Global |
| ExpeditionComposition | `ExpeditionSceneComposition` (roles `-trainer-role`), `ExpeditionAutopilot` (D5, inerte sin argumentos) |
| ExpeditionAuthority (inactivo, solo autoridad) | `ExpeditionRunner`, `ExpeditionBootstrap`, `ExpeditionNetworkHost` |
| ExpeditionClient (inactivo, solo cliente) | `ExpeditionNetworkClient` |
| CrewPresentation | `CrewKeyboardInput` de fase 2 (único control de movimiento, con su E desactivada), `CrewAvatarSync` (prefab `AstronautVisual` de D4), `ExpeditionInteractionInput` (E/R), `ExpeditionCrewPresenter` |
| BaseInterior | Prefab de D4 de fase 2, configurado en tiempo de ejecución con el plano de fase 3 |
| SessionControls | `OutpostSessionControls` de fase 2: Iniciar, Pausa, Reiniciar, Conectar |
| Phase03 Presentation Mount | Vistas PROVISIONALES: HUD IMGUI y superposiciones del mundo |

Sin `Collider2D` ni `Rigidbody2D`. Sin fakes. Un solo cable (fase 3).

## Conexión de D4 (puntos reales)

1. **Vistas**: componentes `IExpeditionView` en prefabs que D2 coloca bajo `Phase03 Presentation Mount`. Cuando llegue la vista equivalente de D4, D2 retira la provisional.
2. **Astronauta**: componentes `IExpeditionCrewView` en `Prefabs/Crew/Phase02/AstronautVisual.prefab` (o el prefab de fase 3 que D4 entregue y D2 asigne a `CrewAvatarSync`).
3. **Mundo**: se dibuja desde `context.Scenario` (`Layout`, `Zones`, `Airlock`, `Comms`) en tiempo de ejecución. Las escotillas se colorean con `Airlock.InnerHatchOpen/OuterHatchOpen`.
4. **Intenciones**: `context.Intents`. D4 nunca crea comandos.
5. **Textos**: por claves, como en `ProvisionalExpeditionHud` (motivos, resultados, mensajes `particle-warning`, `particle-update`, `shelter-period-end`).

## Decisiones tomadas en esta tarea

- La regla 4 de fin (cierre al desellar al último) se mantiene tal como está congelada.
- No se modela abortar un ciclo. No hay transferencia entre trajes. Una salida completa cierra la misión (no hay segunda salida tras completar el primario).
- Los objetivos de fase 2 del snapshot son una copia de compatibilidad (`crew-safe` es el requerido).

## Pendiente

- Entrega de D4 (arte del exterior, esclusa, refugio, indicadores) y su montaje en `integration/phase-03` por el coordinador.
- Partida con dos personas sobre la build (lista en `05_VALIDATION_HANDOFF.md`).
