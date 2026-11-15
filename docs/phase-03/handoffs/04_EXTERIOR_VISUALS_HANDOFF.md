# D4 — Exterior y presentación

Lista de escritura registrada antes de editar. Rama existente `feature/phase-03/d04-exterior`, base `35d97ce` (SHA completo se registra en evidencia); contratos comparados sin diferencias con técnico `1aec99cd2a0ecd9ff588e4c8c6346bbd587417a3`. Trabajo aislado en `/home/leonardo/phase03-d04`.

Se usa `Art/Phase03/Sprites` porque es la ruta exclusiva publicada en OWNERSHIP; prevalece sobre la ruta propuesta en el prompt.

Archivos previstos (incluyen .meta generados por Unity):
- `Art/Phase03/Sprites/{regolith,rock,route,hatch-open,hatch-closed,hatch-cycle,shelter,science,optional,antenna,warning}.png` y `Source/*.svg`, `ART_PROVENANCE.md`.
- `Prefabs/World/Phase03/{ExpeditionWorld,ScienceVisual,OptionalVisual,ShelterVisual,AirlockVisual}.prefab`.
- `Prefabs/UI/Phase03/ExpeditionPanel.prefab`.
- `Prefabs/Crew/Phase03/ExpeditionAstronaut.prefab` (variante del astronauta real).
- `Runtime/Unity/Presentation/Expedition/{ExpeditionPanel,ExpeditionBoundView}.cs`.
- `Runtime/Unity/World/Expedition/{ExpeditionWorldView,ExpeditionCrewBadge}.cs`.
- `Runtime/Unity/Presentation/Expedition/Editor/{Phase03VisualAssets,Phase03VisualChecks}.cs`.
- Este handoff y `d4-evidence/{ASSET_MANIFEST.tsv,verification.txt,*.png}`.

No se edita escena, contratos, astronauta original, settings ni código técnico.

## Entrega y montaje para D2

Base completa: `35d97ceccb879142e8e60723b9558ca9c6b32f1f`. Se reutilizan los contratos de esa base, comparados sin diferencias con el SHA técnico citado. No se incorporó código técnico ni se hicieron merges. La carpeta original `/home/leonardo/My project` sigue en su rama técnica sin modificaciones de D4; esta entrega está sin commit en el worktree D4.

1. En la integración que ya tiene D1/D2/D3/D5, retirar las dos vistas `ProvisionalExpeditionWorld` y `ProvisionalExpeditionHud` del montaje de presentación (también pueden deshabilitarse). Conservar la misma composición, runner, entrada, controles de sesión, base y cámara.
2. Instanciar `Prefabs/World/Phase03/ExpeditionWorld.prefab` y `Prefabs/UI/Phase03/ExpeditionPanel.prefab` bajo **Phase03 Presentation Mount**. Ambos en origen, rotación cero, escala uno. En `ExpeditionWorldView.baseView`, asignar el `BaseInteriorView` que D2 configura con `built.Outpost`. Este debe estar configurado **antes** del Bind de D4. Los otros campos están serializados: sprites nuevos, material unlit de fase 1, ScienceVisual y OptionalVisual.
3. Asignar `Prefabs/Crew/Phase03/ExpeditionAstronaut.prefab` a `CrewAvatarSync.avatarPrefab`. Es variante del astronauta Phase02 con su Animator y `D3CrewVisualAdapter` existentes; añade solo `ExpeditionCrewBadge` y su TextMesh. `ExpeditionCrewPresenter` de D3 debe seguir enlazado al mismo puerto y sincronizador: entrega `ExpeditionCrewFrame` por el contrato real. Ningún componente D4 coloca raíces de astronautas en producción.
4. `ExpeditionSceneComposition` descubre `IExpeditionView` bajo el montaje y llama a `Bind(new ExpeditionViewContext(port, built, intents, SetMovementBlocked))`. No añadir otro puerto, controlador ni loop. Al cambiar de puerto, llama `Unbind`. Tras deshabilitar/rehabilitar una vista, requiere Bind explícito de la composición: no revive datos ni delegados anteriores automáticamente.
5. Cámara: usar la misma cámara ortográfica y seguimiento local de D3 ya aprobados. No se suministra ni crea cámara de producción. El HUD usa lienzo lógico 1280×720 con bandas y ajuste proporcional; reservar aproximadamente 12% arriba y 14% abajo al encuadrar con la configuración existente. Las capturas usan **solo** una cámara temporal del runner, tamaño ortográfico 7 y vista 1440×899; no son una propuesta de cambio de cámara productiva.

`ScienceVisual` y `OptionalVisual` son variantes reutilizables de StationVisual y la vista de mundo las instancia solo en `regolith-site` / `crater-rim`. `ShelterVisual` y `AirlockVisual` son señales independientes disponibles para autoría; el prefab World ya consume esos mismos sprites y dibuja sus equivalentes según el plano, por lo que no añadir señales duplicadas en la misma posición. No colocar colliders a estos prefabs.

## Geometría y apariencia

- XY, 1 unidad = 1 m, pivotes centrales, PNG de 128×128, 128 PPU. Bilinear, FullRect, sin compresión/mipmaps, alpha real. SVG y procedencia en `Art/Phase03/Sprites/`; no imágenes IA ni texto incrustado.
- `regolith` se repite a 1 m por baldosa en **cada región Exterior publicada**; no es un terreno planetario. Rocas en los rectángulos de obstáculos exteriores del escenario, sin colisión añadida; dibujo estirado conforme a la huella real. Cámara, geometría, paquetes y renderer global no cambian.
- `ExpeditionWorldView` lee `Scenario.Zones`, `Layout.Obstacles`, `Layout.Stations`, `Airlock.Chamber/InnerHatch/OuterHatch/ExteriorEntry`, `Comms.Antenna`. No duplica las coordenadas de D2. Sirve tanto para la fixture compacta como para el plano técnico descrito en COMMON_BASE.
- Sorting en **Default** existente: suelo -950, cámara -940, rocas -798 (sobre pared genérica -800), flechas -780, señales -790, escotillas 1500, texto 3000. Estaciones conservan `1000-round(20*y)` y barra 2500; astronautas conservan el sorting y pivote `(0.5,0.18)` de Phase02. Material original Phase01Sprite, sin nuevas luces/materiales/layers.
- La ruta señalizada es el tramo corto de entrada a la esclusa, solo en puntos admisibles según el radio real del escenario. La dirección al refugio es una indicación cardinal explícita «sigue los pasillos», **no** una ruta navegable ni una línea recta autorizada a través de paredes. D4 no inventa waypoints de autoridad ausentes del contrato.
- La vista sustituye temporalmente los visuales de dos estaciones exteriores y ambos paneles de esclusa de BaseInteriorView para evitar duplicaciones; los restaura en Clear/Unbind. BaseInteriorView sigue dibujando los demás interiores y conserva su propiedad de geometría.

## Estados y callbacks

- Esclusa: sprite abierto/cerrado/ciclo por el estado confirmado, texto para cada puerta, plazas, progreso y falta de servicio. No se declara que una puerta se abrió por aceptar un comando.
- Estaciones: ocupante, progreso y estancamiento por OutpostStationView reutilizado; debajo, último resultado confirmado `COMPLETADA`, `CANCELADA · sin reembolso` o `INTERRUMPIDA`.
- Refugio: señal de escudo/casa; estado preparado y servicio. El escudo **no** afirma protección de nadie. «PROTEGIDO» / «PROTECCIÓN CONFIRMADA» solo cuando `ProtectionState.Protected` viene del snapshot/frame.
- Compañeros: mantienen colores, números, localidad y marcha del astronauta original; badge adicional de zona, traje sellado, protección y tarea. Solo el local lleva detalle de margen para limitar el ruido visual.
- HUD: energía y agua de base, aviso recibido, margen local y servicio limitante; detalles opcionales con objetivos, equipo, traje/checklist, reservas, resultados e incidentes. Margen calculado con timestamp y rótulos ACTUAL/DATO ANTERIOR, sin extrapolar; ausente = desconocido; no aplicable y regreso imposible tienen textos distintos. La fuente temporal es siempre el snapshot, no Time.deltaTime.
- Avisos: únicamente mensajes con `TryGetReception` del jugador local; emisión, recepción, plazo y vigencia explícitos. Un mensaje global todavía no recibido no revela el contenido al exterior. Un aviso vencido conserva texto de mantener la instrucción, nunca «seguro» por ausencia de noticias. Sin dosis/umbrales clínicos ni conversión de partículas.
- Botones: `IExpeditionIntents.Interact()` y `RequestDeclareReturn()`. Proximidad, tarea elegible y petición compartida pertenecen al cliente de interacción aprobado. Se muestra `HasPending`; las respuestas no simulan progreso. E/R siguen siendo de D3. No hay suscripción nueva a teclado en D4.
- Detalles abiertos: `Context.BlockMovement(this)`; cierre, reinicio, disable, Unbind y desconexión liberan con `UnblockMovement`. Se retiran eventos de puerto y `Answered`. El pendiente de comando es exclusivamente de D3; D4 no crea ni reenvía pendientes propios.

## Comprobación y límites

Unity 6000.3.28f1 sobre el worktree aislado. Prefabs y .meta generados realmente mediante Unity, sin GUID inventados. No se abrió otra instancia sobre el proyecto original ni se cerró un Editor del usuario.

Comandos reproducibles, **en copia aislada**:

```sh
/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity \
  -batchmode -nographics -projectPath /ruta/aislada \
  -executeMethod Trainer.Unity.Presentation.Expedition.Editor.Phase03VisualAssets.Build \
  -quit -logFile /tmp/d4-phase03-import.log

/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity \
  -projectPath /ruta/aislada \
  -executeMethod Trainer.Unity.Presentation.Expedition.Editor.Phase03VisualChecks.Run \
  -d4-ci -logFile /tmp/d4-phase03-visual.log
```

El segundo runner crea objetos temporales en PlayMode, no guarda ni entrega una escena de demostración. Con `-d4-ci` cierra solo su instancia de prueba. Las capturas son ejecución **FAKE COMÚN**: posiciones/estados guionizados; no prueban movimiento, ciencia, presupuesto, autoridad o networking reales. El callback de intenciones del runner es inerte y no certifica clics físicos. La fixture es más pequeña que el plano técnico; falta inspección del layout productivo y resoluciones finales tras integrar.

La excepción conocida `ArgumentOutOfRangeException` de `UnityEditor.Search.SearchDatabase.EnumerateAll / SearchInit.IndexationOnStartup` volvió a aparecer durante el arranque del Editor. Se separa de las comprobaciones D4, no se presenta el Editor como libre de errores. Evidencia resumida y resultados finales en `d4-evidence/verification.txt`; manifiesto con hashes en `ASSET_MANIFEST.tsv`.

Pendiente de integración D2/D5: montar estos prefabs en la escena productiva, probar controles reales, revisar encuadre y visuales en el plano completo, verificar rutas y avisos con dos clientes reales y obtener aceptación de la fase. **Esta entrega no declara completada la fase 3.** No commit, push, publicación ni merge.

Resultados finales: importación/prefabs **exit 0**; runner PlayMode **exit 0, 47 comprobaciones**, ocho capturas reales de 1440×899. Auditoría estática: 81 archivos de assets/fuentes/metadatos nuevos, metadata presente, GUIDs únicos y referencias de prefabs resueltas; firmas PNG válidas. Excepción de indexación de Unity documentada aparte.
