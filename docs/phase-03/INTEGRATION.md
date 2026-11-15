# Integración de fase 3

Trabajo autorizado por el usuario: reunir las entregas, conectar D4 a la escena productiva y verificar el conjunto. Se trabaja en `integration/phase-03`, worktree `/home/leonardo/phase03-integration`, para conservar los cambios sin commit de roadmap en el proyecto original.

## Procedencia

- Base técnica D1/D2/D3/D5: `1aec99cd2a0ecd9ff588e4c8c6346bbd587417a3`.
- D4: `83ca41b` (guardado localmente para poder hacer el merge autorizado).
- Las cinco ramas D1–D5 de fase 2 son antepasados de la base técnica. No requieren un nuevo merge ni cherry-pick.
- El merge D4/técnico se realizó sin conflictos. No se altera main ni se hace push.

## Lista de escritura de integración

- `Editor/Composition/Phase03/Phase03SceneBuilder.cs`: prefabs D4 reales en el montaje y astronauta Phase03.
- `Scenes/Phase03_EvaAndShelter.unity`: regeneración conservando su GUID y el escenario existente.
- `Editor/Build/Phase03/Phase03BuildChecks.cs`: comprobar D4, ausencia de provisionales y procedencia de builds desde worktrees.
- `Runtime/Unity/Presentation/Expedition/ExpeditionBoundView.cs`: liberar bloqueos al perder membresía.
- `Runtime/Unity/Presentation/Expedition/ExpeditionPanel.cs`: bandas y modal opacos, scroll y espacio para los controles de sesión.
- `Runtime/Unity/World/Expedition/ExpeditionWorldView.cs` y `World/Outpost/OutpostStationView.cs`: títulos de preparación breves, restaurables y sin cambiar el comportamiento por defecto de fase 2.
- `Runtime/Unity/Bootstrap/Expedition/ExpeditionSceneComposition.cs`: indicar el estado real del listener después de Start.
- `Tests/EditMode/Phase03/Bootstrap/Editor/Phase03SceneTests.cs`: regresión de montaje y ciclo de conexión con D4 real.
- Tooling de validación de integración bajo `Runtime/Unity/Presentation/Expedition/Editor/` si las capturas reales lo requieren.
- Este informe y `docs/phase-03/integration-evidence/`.

Contratos, datos/valores del escenario, renderer, paquetes y ProjectSettings se conservan.

## Verificación de montaje

- Merge: `9e16eae504b0594074102370043fd22b50d616a7`, padres técnico `1aec99c` y D4 `83ca41b`.
- Unity nativo 6000.3.28f1: escena regenerada con D4, conservando el GUID y los datos del escenario.
- EditMode: **383/383**. Incluye regresión real de D4+puerto local+autoridad e inputs D3.
- PlayMode: **59/59**, incluidas pruebas de sockets WebSocket reales de las tres fases.
- Checks fase 3: **34/34**, incluidas comprobaciones explícitas de D4 y ausencia de componentes provisionales.
- Contratos y configuración global sin cambios. Unity generó configuración temporal de Editor, descartada en este worktree.

Build y recorridos de aceptación se registran después de ejecutarlos; estos resultados aún no sustituyen una partida con dos personas.

## Ajustes descubiertos con la escena real

La primera build (`b36e8ce`) pasó A y B con autoridad y dos clientes separados. La revisión gráfica de la escena real mostró acumulación de texto fuera del viewport de cámara y rótulos de preparación solapados. Se corrigen en D4 con bandas HUD opacas y títulos breves; los detalles pasan debajo de los controles de sesión y tienen scroll para conservar Reiniciar visible. `OutpostStationView.SetCaption/CaptionSize` es una extensión visual opcional, sin cambio de contratos ni del comportamiento por defecto de fase 2. D4 restaura los títulos al desconectar.

La composición refresca la indicación de escucha del anfitrión, porque `Host.Start` puede abrir el socket después de su montaje. No se cambia el transporte ni la política de escucha. La validación gráfica usa argumentos de loopback.

La primera captura automática se interrumpió para corregir estos defectos. El runner ahora solicita actualizaciones del player loop aunque el Editor pierda foco y despausa únicamente la incidencia conocida de indexación si Console Error Pause detiene la instancia de prueba. Cualquier otra excepción falla el runner. Sus capturas son del escenario/autoridad reales, con dos puertos locales en un proceso; la red entre procesos se comprueba por separado.

## Contribuciones incluidas

| Rol | Commits de fase 3 |
| --- | --- |
| D1 | `fb0cefa` |
| D2 | `ad5135b`, `0a6f858` |
| D3 | `9bda487`, `974d721`, `3748635` |
| D4 | `83ca41b` |
| D5 | `2166121`, `39c6495` |

Los handoffs originales describen sus entregas antes del montaje. Su manifiesto D4 corresponde al commit `83ca41b`; los ajustes posteriores están en el historial y manifiesto de integración.

## Resultado final

**D1–D5 integradas y verificadas automáticamente; la aceptación manual de la fase sigue pendiente.**

| Comprobación | Resultado |
| --- | --- |
| Merge técnico + D4 | Sin conflictos; commit `9e16eae` |
| Montaje productivo | Prefabs World/HUD Phase03 y astronauta variante Phase03, dos vistas reales; sin componentes provisionales ni fakes |
| EditMode (tres fases) | **383/383**, exit 0 |
| PlayMode (tres fases, incluidos sockets reales) | **59/59**, exit 0 |
| Checks D5 de fase 3 | **34/34**, exit 0 |
| Build Linux64 final | **Succeeded**, exit 0, fuente `90d56f78025fc72b9c6a6fc5a23e7c0bc337f298` |
| A: autoridad + dos clientes de esa build | **PASS/PASS**, `ScienceReturned`, t=99,0 s |
| B: autoridad + dos clientes de esa build | **PASS/PASS**, `ShelteredInTime`, t=235,0 s |
| Escena productiva en PlayMode con gráficos | Recorrido B **PASS/PASS**, dos puertos locales/una autoridad; siete capturas reales sin retoques |

A y B se ejecutaron en paralelo en sesiones separadas, cada una con tres procesos sobre WebSocket en 127.0.0.1 (puertos 23931/23932). Solo la autoridad avanzó el tiempo: CLI de prueba `-trainer-timescale 2`. Pilotos automáticos D5, sin clientes fake ni colocación directa de jugadores. Ambas sesiones cerraron sus procesos y sockets al acabar. El ensayo gráfico usa el mismo generador de escena productiva y el mismo piloto D5, con ritmo de prueba 3; sus dos puertos son locales al proceso, no se usa esa captura para certificar red.

Evidencia: [resumen](integration-evidence/verification.txt), [build y clientes](integration-evidence/process-acceptance.json), [guion reproducible](integration-evidence/run_process_acceptance.py), [resultado gráfico](integration-evidence/07-real-result.png) y [manifiesto](integration-evidence/ASSET_MANIFEST.tsv). Los logs completos y XML permanecen en `Logs/phase03-integration/` (ignorados). La build queda en `Builds/Phase3Mission/StandaloneLinux64/` (ignorada). El commit de archivo de evidencias posterior cambia solo documentación; el código compilado es `90d56f7`.

### Abrir y repetir

Abrir `/home/leonardo/phase03-integration` con Unity 6000.3.28f1 y la escena `Assets/_Project/Scenes/Phase03_EvaAndShelter.unity`. `Phase03SceneBuilder` ya regenera el montaje D4, sin resetear el escenario existente. Para repetir la validación de procesos después de generar la build: `python docs/phase-03/integration-evidence/run_process_acceptance.py`. Para repetir las capturas, el método CLI es `Trainer.Unity.Presentation.Expedition.Editor.Phase03IntegratedVisualChecks.Run`, con `-phase3-ci -trainer-bind 127.0.0.1 -trainer-port 23933` (sin batchmode ni nographics).

Pendiente: partida manual con dos personas y teclado/ratón, aceptación de legibilidad por niños, WebGL/LAN/TLS si se solicitan. No se declara terminada la fase ni se fusiona con main. No push ni publicación. Los cambios sin commit del roadmap en `/home/leonardo/My project` permanecen en su carpeta y rama originales.

Incidencia de Editor conocida: la indexación `UnityEditor.Search.SearchDatabase` volvió a emitir `ArgumentOutOfRangeException`; no se presenta como una ejecución sin incidencias del Editor. No hubo fallos de compilación ni otros errores de ejecución en la validación gráfica final. Las salidas y alcances exactos están en el resumen de evidencia.
