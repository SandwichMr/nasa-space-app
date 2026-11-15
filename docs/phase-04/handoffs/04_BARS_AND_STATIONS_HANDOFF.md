# D4 — Barras y estaciones de la base viva

Estado: entrega de presentación conectable, **WIP de integración**. No declara aceptada la fase 4; falta el montaje productivo D2 y la validación integrada D3+D5.

- Checkout original: `/home/leonardo/My project`.
- Main actualizado con `git pull --ff-only origin main`: `1ca26923ad7d29cfe7ebdbb2969c331a8875c4e0`.
- Preparación D2 publicada y verificada en remoto: `9b96f80ee8102073152d9e51cad4beb5a353aeb5` (`setup/phase-04-shared-contracts`). Incluye el main actualizado.
- Rama propia: `feature/phase-04/d04-presentation` (nombre solicitado por el encargo). Base congelada durante esta entrega.
- El usuario confirmó que los otros procesos Codex locales están inactivos. Sin agentes escritores paralelos ni Editor del usuario abierto al comenzar.
- Se usa este nombre de handoff porque es el asignado por el OWNERSHIP real de D2.

## Lista exacta de escritura, antes de implementar

Cada asset/fuente incluye su `.meta` generado por Unity. Solo se crean los `.meta` de carpetas nuevas dentro de las áreas D4.

- `Assets/_Project/Art/Phase04/Sprites/{water-processor,air-unit,logistics,battery,water,food,oxygen,carbon-dioxide}.png`.
- `Assets/_Project/Art/Phase04/Sprites/Source/{water-processor,air-unit,logistics,battery,water,food,oxygen,carbon-dioxide}.svg`.
- `Assets/_Project/Art/Phase04/ART_PROVENANCE.md`.
- `Assets/_Project/Prefabs/UI/Phase04/BaseOperationsPanel.prefab`.
- `Assets/_Project/Prefabs/World/Phase04/{BaseOperationsWorld,WaterProcessor,AirUnit,LogisticsConsole}.prefab`.
- `Assets/_Project/Runtime/Unity/Presentation/BaseOperations/{BaseOperationsBoundView,BaseOperationsText,BaseOperationsPanel}.cs`.
- `Assets/_Project/Runtime/Unity/Presentation/BaseOperations/BaseOperationsCameraFraming.cs`: encuadre optativo sobre la cámara existente, sin crear cámara ni seguir/mover astronautas.
- `Assets/_Project/Runtime/Unity/Presentation/BaseOperations/Editor/{Phase04VisualAssets,Phase04VisualChecks}.cs`.
- `Assets/_Project/Runtime/Unity/World/BaseOperations/{BaseOperationsWorldView,BaseEquipmentView}.cs`.
- `Assets/_Project/Tests/PlayMode/UI/Phase04/BaseOperationsPresentationTests.cs`.
- Este handoff y `docs/phase-04/handoffs/d4-evidence/{verification.txt,ASSET_MANIFEST.tsv,01-resources-fake.png,02-water-decision-fake.png,03-air-decision-fake.png,04-repair-fake.png,05-occupied-fake.png,06-unknown-fake.png,07-disconnected-fake.png,08-restored-fake.png,09-low-resources-fake.png,10-exterior-fake.png}`.

No se editan contratos, fakes, escenas, geometría, entrada, red, simulación, Packages, renderer o ProjectSettings. El astronauta, las animaciones, los suelos, los muros y el exterior se reutilizan por referencia.

## Entrega y comportamiento

- Cinco lecturas esenciales permanentes: batería, agua potable, comida, O2 y CO2. Solo los stocks con capacidad declarada se dibujan como fracción; los gases son kPa con la línea del escenario. Nunca se normaliza CO2 como vida, ni se calculan consumos, bandas, tendencias o estados clínicos.
- Generación, demanda solicitada y balance eléctrico en W. Las tendencias son las publicadas por la autoridad, sin extrapolación. Desconectado, sin lectura y antiguo tienen rótulos explícitos; tampoco se presenta una observación antigua del equipo como estado normal actual.
- Detalles desplazables de agua sin tratar, producto retenido, repuestos libres/reservados, informe periódico y salidas finalizadas. Tooltips de las lecturas explican la acción. Los informes no terminan la base.
- Panel por proximidad confirmada, sin acceso remoto. Agua: conductividad y línea de aceptación junto a las dos rutas del producto, procesamiento, recomprobación y reparación. Aire: lecturas de los dos lechos/entrada, cambio de lecho, regeneración y reparación. Se usan los tiempos/potencias del escenario; no hay un puzzle sin efecto ni un coste local.
- Estados físicos legibles de reciclador/aire y consola logística en las posiciones del escenario. Se reemplazan visualmente solo esos terminales de `BaseInteriorView` y se restauran al desenlazar. Sin otra geometría, colisión, reloj, controlador ni inventario.
- La ocupación, progreso, espera por potencia, repuesto reservado, prueba y restitución proceden del snapshot. Aceptar una solicitud no restaura stocks. El resultado de una decisión es el `DecisionRecord` de la autoridad.
- Cerrar, Escape, perder foco, alejarse según el snapshot, deshabilitar, desconectar, reiniciar o cambiar binding liberan el bloqueo de movimiento. Unbind retira listeners de puerto y de ambos juegos de intenciones. El pendiente pertenece a D3, también al cerrar el panel.
- Arte vectorial local, ocho PNG RGBA reales y cinco prefabs serializados por Unity. Se conserva el astronauta y su controller original. Procedencia e importación en `Assets/_Project/Art/Phase04/ART_PROVENANCE.md`.

## Puertos y callbacks reales

`BaseOperationsPanel` y `BaseOperationsWorldView` implementan `IBaseOperationsView` mediante `BaseOperationsBoundView`:

- `Bind(BaseOperationsViewContext)` / `Unbind()`.
- `IBaseOperationsSessionPort.BaseOperationsChanged`, `MembershipChanged`, `TryGetBaseOperations`.
- Orden de datos por `(Epoch, Revision)` y filtrado por `Scenario.Id`; no se revive un snapshot de un binding anterior.
- `IBaseOperationsIntents.RequestOperation(station, action, choice)`, `RequestCancelOperation`, `RequestPlaceOrder`, `RequestCancelOrder`, `RequestUnload`, `HasPending`, `LastReason`, `Answered`.
- `IExpeditionIntents.HasPending` y `Answered` sobre el mismo contexto; E/R y WASD siguen siendo de D3.
- `context.BlockMovement(this)` / `UnblockMovement(this)` con dueño por vista.

No se construye ni envía `BaseOperationCommand` desde las vistas. El único driver que los construye está bajo `Tests/PlayMode/UI/Phase04`, `#if UNITY_EDITOR`, rotulado como fixture y sobre el fake común. No se entrega ningún fake dentro de prefabs.

## Receta exacta de montaje D2

1. Integrar esta rama y la implementación real D1/D2/D3 en la integración coordinada, sin cambiar los contratos congelados unilateralmente. Usar `Scenes/Phase04_LivingBase.unity`; D4 no la creó ni la editó.
2. Reutilizar el interior, `World/Phase03/ExpeditionWorld.prefab` y `Crew/Phase03/ExpeditionAstronaut.prefab`, con raíz en origen, escala uno. El escenario de producción lo decide D2. No trasladar las posiciones de la fixture a ese escenario.
3. Instanciar `Prefabs/UI/Phase04/BaseOperationsPanel.prefab` y `Prefabs/World/Phase04/BaseOperationsWorld.prefab` bajo **Phase04 Presentation Mount**. Asignar el interior existente antes de Bind:

   ```csharp
   equipmentWorld.ConfigureInterior(baseInterior);
   var viewContext = new BaseOperationsViewContext(
       presentedPort, scenario, operationsIntents, expeditionIntents,
       (owner, blocked) => {
           if (blocked) keyboardInput.Block(owner);
           else keyboardInput.Unblock(owner);
       });
   equipmentWorld.Bind(viewContext);
   operationsPanel.Bind(viewContext);
   ```

   Si la composición descubre y enlaza todos los `IBaseOperationsView`, dejarle esas dos llamadas a ella: no duplicarlas. `presentedPort` es el mismo puerto local/cliente que alimenta entrada, avatares y vistas de fase 3; las intenciones son las reales de D3. Al sustituir puerto o cerrar, llamar Unbind antes del nuevo Bind.
4. Mantener `ExpeditionWorldView` bajo **Phase03 Presentation Mount** enlazado al contexto de fase 3 sobre ese mismo puerto. Mantener sus avatares y signos de esclusa/refugio. Retirar el HUD provisional de fase 4 y **no montar `ExpeditionPanel` a la vez**: este HUD ya presenta reservas, margen local y avisos recibidos, y duplicarlo superpone paneles. `BaseOperationsPanel` usa el formateador real de márgenes de fase 3.
5. En la **cámara ortográfica existente** que sigue D3, deshabilitar el antiguo `LocalCameraFraming` si existe y añadir `BaseOperationsCameraFraming`; asignarle `BaseOperationsPanel` con `Configure(panel)` o Inspector. Reserva las bandas del HUD y la zona derecha al abrir panel/detalles; no crea cámara ni cambia su posición/rotación. Altura visible predeterminada 10.8 m, configurable por D2. Al deshabilitarlo restaura viewport/tamaño. No habilitar dos componentes de encuadre simultáneos.
6. `WaterProcessor`, `AirUnit` y `LogisticsConsole` son dependencias serializadas de `BaseOperationsWorld.prefab`; no colocarlos otra vez a mano. Se instancian una vez en las posiciones de `scenario.Layout`, incluyendo la consola confirmada por D2. No añadir colliders a estos prefabs de piso.
7. Verificar el plano real y varias resoluciones tras el montaje. Probar proximidad/teclado/clics físicos y dos clientes reales: las capturas de esta rama no sustituyen esas pruebas.

## Verificación reproducible

Todas las ejecuciones de esta entrega usan **el checkout original en la laptop**, Unity `6000.3.28f1`. Se comprobó la ausencia del Editor del usuario antes de lanzar las instancias propias; no se usaron clones ni worktrees nuevos.

```sh
mkdir -p Logs/d4-phase04
UNITY=/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity
"$UNITY" -batchmode -nographics -projectPath '/home/leonardo/My project' \
  -executeMethod Trainer.Unity.Presentation.BaseOperations.Editor.Phase04VisualAssets.Build \
  -quit -logFile Logs/d4-phase04/import-verified.log

"$UNITY" -batchmode -nographics -projectPath '/home/leonardo/My project' \
  -runTests -testPlatform EditMode -testFilter Phase04 \
  -testResults Logs/d4-phase04/editmode-phase04.xml \
  -logFile Logs/d4-phase04/editmode-phase04.log

"$UNITY" -projectPath '/home/leonardo/My project' -force-glcore \
  -executeMethod Trainer.Unity.Presentation.BaseOperations.Editor.Phase04VisualChecks.Run \
  -d4-ci -logFile Logs/d4-phase04/visual-verified.log
```

El último runner abre una escena vacía **sin guardarla**, crea objetos temporales y renderiza estados del **fake común**. El flag `-d4-ci` cierra solo esa instancia al terminar. Sin el flag, el menú sale de PlayMode. No es una escena alternativa entregada para sustituir el montaje. No ejecutar con el proyecto abierto en el Editor del usuario o con trabajo de escena sin guardar.

Resultados medidos y lista de comprobaciones: `d4-evidence/verification.txt`. Los logs y XML completos quedan en `Logs/d4-phase04/` (ignorados). La suite de presentación sigue el runner nativo establecido por `MissionHUDTests`; **no se presenta como NUnit**. El pase EditMode sí es el Test Runner de Unity con el NUnit realmente incluido.

| Verificación de esta entrega | Resultado real |
| --- | --- |
| Importación nativa, compilación C# y serialización final | exit 0; ocho sprites y cinco prefabs |
| Unity Test Runner EditMode, filtro `Phase04` | **31/31**, 0 fallos, 0 omitidas; XML en Logs |
| Runner nativo PlayMode de presentación sobre fake común | exit 0; **76 comprobaciones** |
| Capturas de render | **10 PNG, 1440 × 909**, revisadas visualmente; sin retoque, rotuladas FAKE |
| Auditoría de assets y propiedad | 71 archivos de assets/fuentes/metadatos, `.meta` presentes, GUID nuevos únicos, 43 referencias de prefabs resueltas; ocho sprites RGBA con transparencia efectiva; todas las rutas dentro de D4 |
| Configuración/contratos/escenas previas | diff vacío contra la preparación común; sin cambios ajenos |

Las comprobaciones incluyen recursos llenos/bajos/desconocidos/antiguos, gases con sus unidades, ocupación/espera, solicitudes pendientes sin duplicar, respuesta aceptada sin inventar efecto, rechazo explicado, observación antigua, salir de alcance, cierre/disable/rebind/desconexión, nueva época y limpieza de listeners/bloqueos. También se verifican tres posiciones autoritativas y conservación del controller del astronauta.

Capturas: [base](d4-evidence/01-resources-fake.png), [decisión de agua](d4-evidence/02-water-decision-fake.png), [aire](d4-evidence/03-air-decision-fake.png), [reparación](d4-evidence/04-repair-fake.png), [ocupación/prueba](d4-evidence/05-occupied-fake.png), [desconocido/antiguo](d4-evidence/06-unknown-fake.png), [desconexión](d4-evidence/07-disconnected-fake.png), [servicio restituido](d4-evidence/08-restored-fake.png), [reservas bajas](d4-evidence/09-low-resources-fake.png), [exterior reutilizado](d4-evidence/10-exterior-fake.png). Las capturas de reparación y restitución **no prueban la simulación D1/D2**; esos estados están publicados expresamente por la fixture.

Primer intento gráfico: error de compilación propio `CS0246` por un import omitido en la fixture de pruebas. Entró en modo seguro; se corrigió y se cerró la instancia de prueba propia, sin contar ese intento como aprobado. Se recompiló sin interfaz antes de repetir PlayMode. Los ajustes posteriores de aro, encuadre y layout se volvieron a verificar.

## Límites y pendientes explícitos

- Producción: falta montar estos prefabs con la autoridad continua y el puerto real en la escena acumulativa de D2. No se ejecutó una build de fase 4, sockets, partida con dos procesos/personas ni aceptación de 30 minutos. No se declara aceptada la fase.
- **Residuo cuantitativo no publicado**: el snapshot congelado publica `Water.HoldKilograms`, agua sin tratar y el tanque, pero no `EquipmentLedger.BrineOutflowKilograms`. El detalle indica “cantidad no publicada”; no calcula pérdidas a partir de lotes o recuperaciones supuestas. Si D2 quiere esa cifra en UI, necesita una extensión coordinada del contrato y un SHA nuevo. No se cambió el contrato.
- Los textos reconocen claves estables documentadas (conductividad, lechos, entrada, motivos y ejemplos de explicación). Las claves nuevas de D2 se muestran como resultado confirmado con su clave hasta coordinar su traducción; no se inventa una consecuencia.
- Mantener congelada la base `9b96f80ee8102073152d9e51cad4beb5a353aeb5`. En el fetch de revisión, `origin/main` y la preparación seguían iguales. Se detectó la publicación D1 `origin/feature/phase-04/d01-simulation` en `bdbce8778723376d4695f1fd3cdea056ce78a98f`; no se incorporó unilateralmente.
- No NASA/API, persistencia, cultivos, director de eventos ni red nueva. La avería y los valores de la fixture son sintéticos.

Publicación D4: commit(s) propios en `feature/phase-04/d04-presentation`; la respuesta de entrega incluye SHA completo y lectura remota verificada. No se publica main ni se crea un merge de D2.
