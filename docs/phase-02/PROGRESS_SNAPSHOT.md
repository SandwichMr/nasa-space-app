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
