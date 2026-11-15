# D4 · Base interior, astronautas y presentación de fase 2

Entrega en `feature/phase-02/d04-base-astronautas`, desde el commit común
`95caa315527200bbbbcbb73fd654321d7992552e`. Sin commit, merge ni push.
Lista exclusiva de archivos y SHA-256 en [ASSET_MANIFEST.tsv](d4-evidence/ASSET_MANIFEST.tsv).
No se editaron escenas compartidas, contratos, simulación, aplicación, entrada,
networking, paquetes, ProjectSettings ni recursos de fase 1.

Se entrega arte consumido por prefabs reales e importados, no una maqueta. La
integración de misión y escena sigue siendo de D2. Las capturas usan estados
guionizados con el fake común: **no certifican una partida ni red de fase 2**.

## Archivos exclusivos

Prefijo `Assets/_Project/`; cada archivo incluye su `.meta` generado por Unity.
El manifiesto enumera también fuentes y metadatos, sin ampliar `OWNERSHIP.md`.

| Ruta | Entrega |
| --- | --- |
| `Art/Sprites/Phase02/` | Hoja PNG original de astronauta, suelo, paredes, anillo, placas de agua/aire y relleno; seis SVG editables, prompt y procedencia. |
| `Art/Animations/Phase02/` | `Astronaut.controller`; `IdleDown`, `IdleRight`, `IdleUp`, `IdleLeft`, `WalkDown`, `WalkRight`, `WalkUp`, `WalkLeft` (`.anim`). |
| `Prefabs/Crew/Phase02/AstronautVisual.prefab` | SpriteRenderer, Animator, anillo de color y número/localidad. Sin movimiento, física ni red. |
| `Prefabs/World/Phase02/StationVisual.prefab` | Terminal de piso, alcance, nombre, ocupante/estado y progreso confirmado. |
| `Prefabs/World/Phase02/BaseInterior.prefab` | Interior serializado de dos habitaciones conectadas, paredes/obstáculos y tres estaciones. Vista previa del layout sintético común; `Bind` lo sustituye por el escenario de D2. |
| `Prefabs/UI/Phase02/OutpostPanel.prefab` | Tres indicadores principales y detalles desplegables; panel de tarea solo por proximidad. |
| `Runtime/Unity/World/Outpost/CrewVisual.cs` | Orientación y reposo/marcha, color por Slot, PlayerId separado del número, «TÚ» y tarea. No coloca la raíz. |
| `Runtime/Unity/World/Outpost/BaseInteriorView.cs` | Proyección de solo lectura de Bounds, Obstacles y Stations del escenario recibido. |
| `Runtime/Unity/World/Outpost/OutpostStationView.cs` | Disponibilidad, persona ocupante, segundos de trabajo y estancamiento confirmados. |
| `Runtime/Unity/World/Outpost/LocalCameraFraming.cs` | Encuadre opcional de la cámara existente: reserva HUD y ajusta tamaño ortográfico; D3 conserva todo el seguimiento. |
| `Runtime/Unity/Presentation/Outpost/OutpostPanel.cs` | Binding del puerto, filtros de vigencia y revisión, HUD, tareas, mensajes y ciclo de vida. |
| `Runtime/Unity/Presentation/Outpost/Editor/Phase02VisualAssets.cs` | Importación/autoría explícita de assets D4; instalación del puente opcional D3. No guarda escenas. |
| `…/Editor/Phase02VisualChecks.cs` | Runner optativo PlayMode con fake y capturas reales. |
| `…/Editor/Phase02D3LinkChecks.cs` | Runner optativo del enlace con componentes reales de D3, cuando están presentes. |
| `…/Editor/D3CrewVisualAdapter.cs.txt` | Fuente del adaptador a `ICrewAvatarView`, instalable después de reunir D3. |

Documentación propia: este HANDOFF y `docs/phase-02/handoffs/d4-evidence/**`.
No hay archivos nuevos en `Editor/**` de D5 ni `Input/**` de D3.

## Arte, geometría y cámara para D2/D3

- Plano XY, +X derecha, +Y arriba, **1 unidad = 1 m**. Base y padre de avatares en
  origen, escala uno, sin rotación. D3 coloca las raíces; D4 nunca las traduce.
- Hoja RGBA original **1254 × 1254**, 16 sprites importados. El corte usa celdas de
  313 × 313 y deja dos píxeles exteriores de padding. Cada celda mide 1,28 m;
  pivote `(0.5, 0.18)`, anclado al disco de suelo. La silueta ocupa aproximadamente
  1 m de alto; el anillo mide 0,768 × 0,384 m. El radio físico lo decide el
  escenario, no el sprite (la fixture común usa 0,35 m).
- Animator: `Facing` entero 0=abajo, 1=derecha, 2=arriba, 3=izquierda;
  `Walking` bool. Cuatro bucles de reposo de 1,5 s y cuatro de marcha de 0,625 s
  (duraciones importadas realmente por Unity, incluido el último frame).
  Se usan poses confirmadas, nunca teclas o distancia visual. La animación usa
  tiempo visual sin acumular trabajo ni mover la raíz.
- Ocho colores con número de hueco; «TÚ» solo para el miembro local. Slot no es
  identidad; el adaptador entrega también PlayerId y estación de trabajo.
- Orden sobre la capa Default existente: suelo -1000, límites exteriores -850,
  obstáculos -800/-799; cuerpo de estación/astronauta `1000 - round(20*y)`;
  anillo un nivel debajo del astronauta, progreso 2500 y texto 3000. Sin cambios
  de Sorting Layers, renderer ni luces. Material URP unlit existente de fase 1.
- Las paredes se dibujan con los **mismos rectángulos** del layout. La moldura
  exterior está fuera de Bounds; el bisel queda dentro del obstáculo. No hay
  Collider2D, Rigidbody2D ni segundo solver. Las placas de estación son
  terminales de piso transitables; mobiliario bloqueante requiere que D2 añada
  su huella al layout común de la build. D4 no inventa esa colisión.
- Vista previa: fixture aprobada `fixture-two-rooms`, Bounds `(0,0)-(16,10)`,
  paredes `(7.8,0)-(8.2,4)` y `(7.8,6)-(8.2,10)`: paso central de 2 m. Agua
  `(3,8)`, ciencia `(13,8)` y aire `(13,2)`. Es sintético, no una decisión nueva
  de mapa de producción ni datos NASA. No se crea un escenario alternativo.
- D2 puede añadir `LocalCameraFraming` a su cámara ortográfica existente. Altura
  visible inicial 9,5 m; reserva HUD superior/inferior. No crea cámara, cambia su
  posición ni sigue remotos. `CrewAvatarSync.followCamera` de D3 recibe esa misma
  cámara. Solo D3 sigue `LocalAvatar`; no añadir otro follow. El componente de
  encuadre restaura viewport/tamaño al deshabilitarlo.

Procedencia, condiciones y regeneración:
[ART_PROVENANCE.md](../../../Assets/_Project/Art/Sprites/Phase02/ART_PROVENANCE.md).
Astronauta generado mediante `imagegen` integrado; prompt conservado en
`Art/Sprites/Phase02/Source/astronaut-prompt.txt`. Los PNG y sus importadores
están dentro del proyecto. Reutilización por GUID del material, instrumento
científico, selección e iconos de fase 1; sus originales no cambiaron. El
servicio eléctrico esencial de fase 1 no se representa como oxígeno.

## Conexión concreta para D2

El contrato común no contiene `ICrewAvatarView`: D3 lo publicó en su propia rama.
Por eso el prefab de esta rama común contiene `CrewVisual` y compila sin D3.
**Antes de conectar el sincronizador**, en la integración que ya incluya D3:

1. `Tools > Trainer > Phase 02 > Install D3 visual adapter after integration`.
2. Esperar la compilación; ejecutar `Attach D3 adapter to astronaut prefab`.
   Instala un componente que implementa la interfaz real de D3 y llama a
   `CrewVisual.Present`. No copia interfaces, añade defines ni modifica D3.
   No requiere reconstruir los otros prefabs ni cambiar escenas automáticamente.
3. Asignar `AstronautVisual.prefab` al campo `avatarPrefab` de `CrewAvatarSync`,
   un padre en origen/escala uno y la cámara local existente. El sincronizador
   es el único que instancia, interpola y destruye astronautas.

D2 instancia `BaseInterior.prefab` y `OutpostPanel.prefab` y compone así con el
**mismo puerto** usado por D3 (host local o adaptador de cliente):

```csharp
keyboardInput.Bind(port, scenario);
avatarSync.Bind(port);
panel.Bind(port, scenario, baseInterior); // reemplaza la vista previa sintética
panel.ConfigureInteraction(
    keyboardInput.Interaction.RequestStart,
    keyboardInput.Interaction.RequestCancel,
    () => keyboardInput.Interaction.Pending,
    blocked => {
        if (blocked) keyboardInput.Block(panel);
        else keyboardInput.Unblock(panel);
    });
```

`Bind` con otro puerto retira callbacks y delegados del enlace anterior. Volver a
configurar la interacción después de ese Bind, también al sustituir el puerto.
Abrir/cerrar, alejarse según snapshot, reiniciar, disable, destroy o perder la
membresía liberan el bloqueo del panel. Al cerrar la composición: `panel.Bind(null,
null)` y deshacer los bindings de D3 según su HANDOFF.

El botón solo abre una estación dentro del radio desde la posición **confirmada**.
Empezar comprueba además estado Running, estación disponible, tarea ofrecida,
persona libre y solicitud pendiente compartida de D3. Cancelar exige tarea propia
y alcance, en Running/Paused. D2 vuelve a validar todo en la autoridad. Los clics
de UI no permiten seleccionar estaciones remotas para actuar. E/WASD pertenecen
exclusivamente a D3. No se envían comandos de tareas desde otro controlador D4.

Ocupación, trabajo, pausa por falta de servicio y recursos solo cambian con el
snapshot. Un resultado aceptado no inventa progreso. La pérdida de membresía
oculta datos vigentes y acciones; las respuestas antiguas de otro binding no los
restauran. Lecturas de gas ausentes/antiguas tienen texto explícito, sin convertirlas
en cero ni usar umbrales clínicos. HUD principal: energía en J, agua potable en kg
y primer objetivo requerido; comida, agua por tratar, O₂/CO₂, cargas y objetivos
adicionales están en detalles. No hay seis barras permanentes.

## Evidencia real y límites

Unity **6000.3.28f1**, copia aislada `/tmp/d4-phase02-verify`; el proyecto compartido
no se abrió en el Editor. Solo se devolvieron assets/metadatos de rutas D4.

| Ejecución | Resultado |
| --- | --- |
| Importación y serialización con `Phase02VisualAssets.Build`, batch sin gráficos | Exit 0: 16 sprites, 8 clips en bucle, controller y 4 prefabs con referencias reales. |
| `Phase02VisualChecks.Run`, PlayMode con gráficos | Exit 0, **102 checks** y 7 capturas sin retoque, **1440 × 919**. |
| Runner existente `MissionHUDTests.PrepareAndRun`, sin editarlo | Exit 0, **31 checks** de regresión del HUD de fase 1. |
| Enlace visual con D3 real, `Phase02D3LinkChecks.Run` | Exit 0, **13 checks**, fake común y componentes reales de sincronización/entrada D3. No es red ni autoridad de misión. |

El enlace se verificó sobre cinco fuentes D3 intactas de
`a76ae7339dbf3c19a34cf6e190efe1b40e2d960f`, extraídas **solo** a la copia temporal:
`CrewInterpolator`, `CrewInputSender`, `StationInteractionClient`, `CrewAvatarSync`
y `CrewKeyboardInput`. Se instaló el adaptador de texto y se comprobó: D3 instancia
ambos prefabs, entrega identidad/localidad y marcha a D4, coloca la raíz, sigue
solo al local, comparte el único pendiente de interacción, bloquea/desbloquea el
teclado real y elimina los avatares al desconectar. Ninguna fuente de D3 ni el
adaptador instalado se incorporó a esta rama sin D3. D2 debe hacer los dos pasos
del menú al reunir las ramas.

Los 102 checks cubren importadores, referencias, animaciones reales (dos sprites
de marcha distintos, raíz inmóvil), huellas de paredes, proximidad, bloqueo de UI,
solicitud diferida, resultado antes del snapshot, ocupación ajena, cancelación
propia, estancamiento, revisiones, rebind, disable/enable y desconexión. No son una
suite NUnit, ni pruebas físicas de teclado/ratón, build o partida entre procesos.
La paleta de ocho astronautas de la primera captura es una muestra visual de la
fixture Editor; no representa ocho participantes conectados.

Incidencia separada: Unity emite una `ArgumentOutOfRangeException` al arrancar
`UnityEditor.Search.SearchDatabase` / `SearchInit.IndexationOnStartup`. El runner
registra **esa excepción exacta** aparte; cualquier otro error/excepción falla el
pase. También hubo avisos nativos `JobTempAlloc` al cerrar el Editor. No se afirma
que el Editor no tenga avisos. Detalle en [editor-startup-issue.txt](d4-evidence/editor-startup-issue.txt).
Los primeros intentos corrigieron errores de compilación del autor y tamaño de
Game View antes de los pases finales; no se cuentan como pases.

Capturas revisadas visualmente:

- [Base y paleta de ocho números/colores](d4-evidence/01-base-and-eight-identities.png).
- [Solicitud cercana pendiente](d4-evidence/02-nearby-request-pending.png).
- [Ocupante y progreso confirmados](d4-evidence/03-confirmed-occupation-and-task.png).
- [Otro ocupante y tarea sin servicio](d4-evidence/04-other-occupant-stalled.png).
- Marcha real: [frame A](d4-evidence/05-walk-right-frame-a.png), [frame B](d4-evidence/06-walk-right-frame-b.png).
- [Desconexión sin datos vigentes](d4-evidence/07-disconnected.png).

Extractos de logs reales junto a las capturas; se omiten datos de licencia/máquina.
Los logs completos de estas ejecuciones permanecen en `/tmp/d4-phase02-*.log`.

## Repetir y pendientes de integración

En una copia aislada, con Unity de la versión del proyecto:

```sh
UNITY=/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity
PROJECT=/ruta/a/copia/aislada

"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod Trainer.Unity.Presentation.Outpost.Editor.Phase02VisualAssets.Build \
  -quit -logFile /tmp/d4-phase02-import.log

"$UNITY" -projectPath "$PROJECT" \
  -executeMethod Trainer.Unity.Presentation.Outpost.Editor.Phase02VisualChecks.Run \
  -d4-ci -logFile /tmp/d4-phase02-visual.log

"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod Trainer.Tests.UI.MissionHUDTests.PrepareAndRun \
  -logFile /tmp/d4-phase02-phase01-regression.log
```

Para el enlace, después de integrar D3 y completar Install/Attach, ejecutar
`Phase02D3LinkChecks.Run` en PlayMode (también admite batch sin gráficos). No usar
ese runner sin D3. Los runners no guardan escenas; por menú salen de PlayMode,
con `-d4-ci` cierran únicamente su instancia de prueba. Las capturas nuevas quedan
en `/tmp/d4-phase02-evidence` y deben revisarse antes de archivarlas.

Pendiente de **D2/D5**: inyectar escenario/autoridad reales, cablear la escena
compartida, probar teclado/clics físicos y resoluciones finales, completar tareas
en una build con dos procesos y confirmar que el mapa de producción coincide con
el layout. D4 no construyó players, inició servicios, probó red, publicó ni cerró
la fase 2 integrada.
