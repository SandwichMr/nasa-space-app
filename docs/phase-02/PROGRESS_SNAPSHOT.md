# Fase 2 — instantánea de avance (no aceptada como versión terminada)

Publicación de progreso autorizada por el coordinador. Base previa a las correcciones: `be207a3a56292b5b1962b0ecc3c8b241ec86e49a`.

Incluye las cinco entregas, el montaje de la escena `Assets/_Project/Scenes/Phase02_InteriorBase.unity` y las correcciones de auditoría en la autoridad, entrada, interacciones, cierre/red y validadores.

## Última verificación nativa (Unity 6000.3.28f1)

- Importación y compilación: aprobadas, exit 0.
- EditMode: 300 aprobadas, 2 fallidas, 0 inconclusas, 0 omitidas; 302 casos. El proceso terminó con exit 2.
- Checks D5: exit 0, sin fallos. Conservan pendientes reales de pruebas entre procesos y aceptación por personas.
- PlayMode y build: NO ejecutados en esta ronda, porque EditMode no pasó.
- Partida entre procesos/equipos, render y jugabilidad completa: no certificados.

## Pruebas EditMode todavía fallidas

- `Phase02InteriorMatchTests.ConfiguredGeometryAuditCleansPreviewAfterGeometryFailure`: Expected: True   But was:  False
- `Phase02InteriorMatchTests.ConfiguredGeometryAuditInstantiatesTheActualPlanWithoutChangingThePrefab`: Expected: True   But was:  False

Ambas son regresiones de la auditoría de geometría con escenas temporales/preview. No se borraron ni desactivaron para publicar. Falta investigar sus aserciones y repetir las puertas nativas antes de aceptar la fase.

Los logs/XML permanecen en `Logs/phase02-native-recheck-20261115T065741Z/` del equipo que ejecutó las pruebas; Logs está ignorado y no se publica. Esta nota registra los resultados, no sustituye los artefactos ni promete una build.

## Re-verificación nativa 2026-11-15 (entrada de la fase 3, rama `feature/phase-03/technical-core`)

Sobre `7d6b914`, Unity 6000.3.28f1 en Linux, logs en `Logs/phase03-stage0/` (ignorado):

- Reproducido el estado publicado: EditMode 302 casos, 300 aprobadas, 2 fallidas (las dos `ConfiguredGeometryAudit*`), exit 2.
- Causa: el ayudante `WithUntitledScene` exigía `SceneManager.SetActiveScene(untitled) == true`, y Unity devuelve
  `false` cuando esa escena **ya** es la activa (en batch hay una sola escena sin título y ya está activa). La
  auditoría ni llegaba a ejecutarse. Corrección mínima: solo se exige `true` si hay que cambiar de escena, y se sigue
  exigiendo que la escena activa sea la sin título. No se borró ni se relajó ninguna aserción de la auditoría.
- Tras la corrección: EditMode **302/302** (exit 0); checks D5 exit 0, sin `Failed` (siguen `Pending` las pruebas entre
  procesos y la de dos personas); PlayMode **56/56** (exit 0); build Linux de `Phase02_InteriorBase` **Succeeded**.
- Defecto encontrado al probar dos procesos: el cliente arrancado con `-trainer-connect` nunca abría conexión
  (`ss`: solo LISTEN). `OutpostSceneComposition.Start` llama a `Prepare()` antes de `OutpostNetworkClient.Start`, que
  entonces ignoraba los argumentos. Corregido en `OutpostNetworkClient` (lee los argumentos una vez, lo que ocurra
  antes) y añadido un log por cambio de estado. Recomprobado con la build: host + 2 clientes en 127.0.0.1, dos enlaces
  ESTAB y `Connected as P1` / `Connected as P2`.

**La fase 2 sigue sin aceptarse**: nadie ha jugado la partida con dos personas y la lista de comprobación de
`05_VALIDATION_HANDOFF.md` sigue pendiente. La prueba entre procesos solo demuestra conexión y unión, no la jugabilidad.
