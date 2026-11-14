# D4 — NUEVA fase 1: sala cooperativa de energía

Entrega local del 2026-11-14. Rama: `feature/phase-01/d04-room-visuals`.
Base comprobada: `0f05e3874c16a5bd8700a6d9f95d2475ba83369f` (`main` al iniciar,
árbol limpio). Sin commits, merge ni push. Sustituye el handoff anterior de UI
por esta entrega de arte/sala; conserva su implementación funcional IMGUI.
D4 está listo para conectar. Esto **no** certifica la fase integrada ni multiplayer.

## Archivos de esta entrega

- `Assets/_Project/Runtime/Unity/Presentation/MissionHUD.cs`: binding previo,
  más aviso `PresentationChanged` para observadores visuales y ancho serializado.
- `Presentation/{ConnectionPanel,PowerPanel,ScienceControls,ResultPanel}.cs`:
  solo ajuste de líneas para mantener el texto visible en la columna del HUD.
- `Assets/_Project/Runtime/Unity/World/{PowerRoomView,PowerStationView}.cs`.
- `Assets/_Project/Runtime/Unity/Presentation/Editor/Phase01VisualAssets.cs`:
  importación y serialización explícita de los prefabs de D4, sin escenas.
- `Assets/_Project/Runtime/Unity/Presentation/Editor/Phase01VisualChecks.cs`:
  runner optativo de comprobaciones en PlayMode y capturas con el fake existente.
- `Assets/_Project/Prefabs/UI/MissionHUD.prefab`: mismo componente y GUID;
  ancho de columna 430 px. Sigue funcionando por sí solo, sin sala ni cámara.
- `Assets/_Project/Prefabs/World/{PowerRoom,BatteryStation,EssentialStation,ScienceStation}.prefab`.
- `Assets/_Project/Art/Sprites/Phase01/`: seis PNG, fuentes SVG editables,
  `Phase01Sprite.mat` y `ART_PROVENANCE.md`.
- `Assets/_Project/Art/Icons/Phase01/`: cinco PNG y sus fuentes SVG editables.
- Este documento, `d4-evidence/` y los `.meta` de Unity de los assets/scripts propios.

No se editaron escenas, contratos, fake compartido, simulación, aplicación,
networking, bootstrap, paquetes, settings ni tests ajenos. Unity generó metadatos
faltantes de otros scripts durante la importación; se retiraron exclusivamente
esos archivos nuevos creados por esta ejecución, sin borrar `.meta` preexistentes.
La base sigue necesitando que el equipo gestione sus propios metadatos al importar.
No seleccionar todo el árbol con `git add`.

## Arte y prefabs reales

Once PNG con contenido y transparencia, no placeholders vacíos: sala de 1200 × 800,
objetos de 256 × 256, selección de 280 × 280, relleno de 128 × 24 e iconos de 96 × 96.
Geometría original hecha para el proyecto; no incorpora material de terceros.
Procedencia, regeneración y condiciones de uso en
[ART_PROVENANCE.md](../../../Assets/_Project/Art/Sprites/Phase01/ART_PROVENANCE.md).

Importados realmente con Unity como Sprite/Single, 100 px/unidad, sin mipmaps ni
compresión, filtro bilineal y alpha. Fuentes SVG opcionales: la build usa los PNG,
sin requerir un importador SVG. El material propio usa el shader unlit del paquete
Universal 2D ya instalado. No requiere modificar el renderer ni añadir luces.
Todos los GUID nuevos y prefabs se generaron/serializaron con Unity.

`PowerRoom.prefab` contiene el fondo y tres instancias anidadas de estaciones.
Dimensiones nominales: 12 × 8 unidades en XY; estaciones en X = -3.62, 0, 3.62;
Y = 0.62 respecto de la raíz. Z = 0. Orden de sprites: sala -10, cuerpos 0,
relleno de batería 1 y selección 2. No contiene cámara, sesión, fake, runner,
Canvas, EventSystem ni física. No añade movimiento, astronautas o animaciones.
Los prefabs individuales son visuales reutilizables; la interacción conjunta la
coordina `PowerRoomView` en el prefab de sala.

Batería ámbar con lectura/relleno confirmado; servicio esencial turquesa con escudo;
instrumento violeta de óptica. La selección muestra marco y texto «Selección: …».
Nombres, J, W, ciencia activada/desactivada, solicitado/servido y estado pendiente
son texto: el significado no depende solo del color. El servicio sigue siendo
una carga eléctrica abstracta, sin afirmaciones de atmósfera ni fisiología.

## Conexión de D2

Instanciar `Prefabs/UI/MissionHUD.prefab` y `Prefabs/World/PowerRoom.prefab` en la
escena compartida **solo desde la integración de D2**. Asignar las referencias por
Inspector o mediante esta secuencia desde el compositor existente:

```csharp
using Trainer.Application;
using Trainer.Unity.Presentation;
using Trainer.Unity.World;

// hud y room: componentes de las instancias; sceneCamera: cámara elegida por D2.
// sessionPort: ISessionPort de la autoridad/adaptador existente, en hilo principal.
room.Connect(hud, sceneCamera);
hud.Bind(sessionPort);
hud.SetConnectionStatus(true, "Conexión confirmada por el adaptador");

// Ante pérdida de transporte: invalida el HUD y la sala inmediatamente.
hud.SetConnectionStatus(false, "Conexión perdida");
// Tras reconectar: Bind al puerto de la nueva conexión y confirmar su estado.
hud.Bind(reconnectedSessionPort);
hud.SetConnectionStatus(true, "Conexión restablecida");

// Al liberar o sustituir los visuales:
room.Connect(null, null);
hud.Bind(null);
```

`PowerRoomView` no se suscribe directamente a `ISessionPort`: observa el HUD y
reutiliza su snapshot filtrado, su correlación y su único comando pendiente.
Para selección programática: `room.SelectStation(PowerStationKind.Science)`;
para solicitar: `room.SetScienceEnabled(true/false)`. Solo la estación científica
seleccionada habilita esos controles. Los botones previos del HUD siguen disponibles.
Ambos caminos terminan en `MissionHUD.SetScienceEnabled` -> `ISessionPort.Submit`.
No hay descuento de recursos, reloj, evaluación de resultado ni toggle optimista.

La selección por ratón usa el sprite o su botón con nombre, mediante IMGUI;
no requiere cambiar el Input System. D2 debe encuadrar la sala a la derecha del
HUD y entre la cabecera y los controles inferiores. `hud.PanelRect` publica el
área reservada por el HUD. D4 no mueve/configura automáticamente la cámara.
Los botones de estaciones se proyectan con la cámara explícita y evitan el HUD.
Sin cámara no se puede seleccionar visualmente; aparece «Vista de sala sin conectar».

Referencia de encuadre **solo de la fixture de captura**, no decisión de cámara
final: sala en origen, cámara temporal XY ortográfica en Z=-10, tamaño 4.4,
viewport desde X=460 y Y=160 hasta 12 px del borde derecho y 130 px del superior,
aspecto del viewport. Para otras posiciones, resoluciones o cámara, ajustar desde
la integración. Las capturas reales son 1440 × 919; para revisar las tres columnas
se recomienda una vista Game de al menos 1280 × 800. En ventanas estrechas el HUD
con scroll sigue siendo utilizable; la sala completa requiere encuadre/espacio.
No se seleccionó target de build ni plataforma.

D3 debe informar pérdida de transporte mediante el método anterior y publicar
eventos en hilo principal. Tener un puerto no prueba conexión: sin confirmación
externa la UI mantiene «Transporte sin verificar». El contrato no notifica pérdida
ni timeout por sí mismo; no se implementa otro protocolo ni reconexión automática.

## Ciclo de vida y autoridad

El HUD conserva sus guardas de época/revisión, identidad de callback, ID de comando,
confirmación síncrona/diferida y resultado de Restart con época anterior. Publica
`PresentationChanged` al limpiar, recibir snapshot/resultado, cambiar conexión o
iniciar/fallar envío; no modifica el contrato compartido.

La sala retira su único listener al deshabilitar, destruir o cambiar HUD. El HUD
retira ambos listeners del puerto al deshabilitar, destruir o hacer Bind. Disable,
rebind y desconexión eliminan lecturas vigentes, pendientes, relleno y selección;
los sprites quedan atenuados y dicen «Sin datos vigentes». Enable recupera una
observación del puerto actual; no reutiliza una afirmación externa de conexión.

`FakeSessionPort` solo aparece en el runner **Editor**, no en los prefabs ni en
los componentes de presentación de producción. Para desarrollo se puede inyectar
explícitamente ese fake al HUD. Su fixture entrega ejemplos fijos, no consume
batería ni demuestra red o física. D2 debe inyectar el puerto real al integrar.
El wrapper de pruebas solo demora eventos del fake compartido, sin decidir
resultados ni sustituir su autoridad. No se modificó el fake.

## Comprobaciones ejecutadas

Unity real: **6000.3.28f1**. Versiones y dependencias inspeccionadas del proyecto,
sin cambios de paquetes/settings.

| Ejecución | Resultado real |
| --- | --- |
| Importación/serialización con `Phase01VisualAssets.Build`, batch sin gráficos | Salida 0; cuatro prefabs visuales, HUD preservado y once PNG importados |
| `Phase01VisualChecks.Run`, Editor con gráficos, `-d4-ci` | Salida 0; `D4_VISUAL_CHECKS_PASSED: 46`; viewport 1440 × 919 |
| Runner previo `MissionHUDTests.PrepareAndRun`, batch sin gráficos, sin editarlo | Salida 0; `MISSION_HUD_CHECKS_PASSED: 30` |
| Inspección de las cuatro capturas finales | Sala y textos visibles; pendiente sin cambio optimista; terminal sin controles; desconexión sin números retenidos |
| `git diff --check` y auditoría de rutas | Sin errores; cambios finales limitados a los archivos propios y esta entrega documental |

Las 46 comprobaciones nuevas incluyen referencias reales a sprites/material,
instancias de prefab, selección y restricciones por estación, batería derivada de
snapshot, un solo listener por evento del puerto, pendiente compartido entre HUD
y sala, resultado antes del snapshot, confirmación posterior, estado terminal,
Restart, disable/enable, rebind sin duplicar listeners, eventos tardíos tras
pérdida, nuevo puerto con otra época, fake síncrono y Destroy diferido. Incluyen
existencia/tamaño de capturas y ausencia de errores de Unity durante la fixture.
Los 30 checks previos cubren adicionalmente revisiones/épocas antiguas y fallos
de envío. Son runners de smoke checks, **no** una suite NUnit completa.

Capturas reales sin composición ni retoque, obtenidas de Game View con ejemplos
rotulados «DESARROLLO · FakeSessionPort · Sin red ni física» o entrega diferida:

- [Ciencia confirmada](d4-evidence/01-science-confirmed.png).
- [Solicitud pendiente](d4-evidence/02-request-pending.png).
- [Resultado completado](d4-evidence/03-mission-result.png).
- [Desconexión sin datos vigentes](d4-evidence/04-disconnected.png).

Logs locales finales: `/tmp/phase01-d4-import.log`,
`/tmp/phase01-d4-visual-interactive.log`, `/tmp/phase01-d4-hud-regression.log`.
Un primer intento batch con gráficos no produjo capturas y terminó con salida 1;
se resolvió usando Game View visible. Las imágenes finales también permitieron
corregir el material y el encuadre de la fixture antes del pase final.
Los mensajes iniciales de licencia no impidieron las ejecuciones finales.

## Repetir

Cerrar primero cualquier Editor que ya tenga este proyecto abierto. Para revisar
assets existentes no hace falta reconstruirlos. La reconstrucción explícita
reescribe únicamente los prefabs/ajustes de importación propios de D4; no usarla
para preservar posteriores ajustes artísticos manuales en esos mismos assets.

```sh
UNITY='/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity'
PROJECT='/home/leonardo/My project'

# Opcional: regenerar importación y prefabs de D4 con GUID administrados por Unity.
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod Trainer.Unity.Presentation.Editor.Phase01VisualAssets.Build \
  -quit -logFile /tmp/phase01-d4-import.log

# Gráficos/desktop disponibles: pruebas y capturas reales. -d4-ci cierra al terminar.
"$UNITY" -projectPath "$PROJECT" \
  -executeMethod Trainer.Unity.Presentation.Editor.Phase01VisualChecks.Run \
  -d4-ci -logFile /tmp/phase01-d4-visual-interactive.log

# Regresión anterior sin cambiar su runner. No añadir -quit: espera Play/Destroy.
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod Trainer.Tests.UI.MissionHUDTests.PrepareAndRun \
  -logFile /tmp/phase01-d4-hud-regression.log
```

En Editor: `Tools > Trainer > Phase 01 > Run D4 visual checks and captures`.
Desde el menú sale de PlayMode sin cerrar el Editor. Crea una vista Game temporal,
objetos/cámaras temporales y restaura cámaras existentes; no guarda escenas.
Las nuevas capturas quedan en `/tmp/phase01-d4-evidence/`. Sin gráficos se pueden
comprobar comportamientos mediante el mismo runner con `-batchmode -nographics`;
se omiten las capturas y su conteo de checks es menor. No presentar eso como prueba
visual. Si se desean conservar capturas en el repositorio, copiar solo las revisadas.

Pendiente para D2/D3/D5: cableado en escena compartida al puerto real, comprobación
de clics físicos y resoluciones objetivo, build y dos clientes reales. Los checks
llaman las mismas acciones públicas que los botones; no simulan ratón/mando.
No se ejecutaron build ni multiplayer en esta entrega, ni se certifica Internet.
