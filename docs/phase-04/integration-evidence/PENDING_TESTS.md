# Fase 4: pruebas pendientes

Actualización de ejecución: **2026-11-15**, Unity **6000.3.28f1**, base `9785b73936d522232fc41513f49b01fb52551e55` con los cambios locales de validación.

EditMode: **548/548**, largas: **2/2**, PlayMode: **76/76**, sin fallos ni inconclusas. La build Linux productiva se generó sin forzar puertas. La ejecución de 30 minutos reales sobre la build definitiva terminó: ambos clientes `RESULT PASS`, exit 0; 1800,8 s reales y 1799,9 s simulados, época 0. La autoridad cerró con exit 0.

Resultados, correcciones y artefactos: [RUN_RESULTS_2026-11-15.md](RUN_RESULTS_2026-11-15.md). La puerta 7 permanece pendiente por indicación del usuario: no hay participantes disponibles. Este archivo conserva los comandos y la lista de aceptación; no declara aceptada toda la fase.

## Corrección posterior y cierre por indicación del usuario

El piloto de aire ya cancela, recarga y reintenta automáticamente. Unity: **549/549 EditMode**, **76/76 PlayMode**, exit 0. La build nueva verificó tres recuperaciones a ×8; última muestra ≈180,8 s reales / 1439,8 s simulados, época 0. Se detuvo a petición del usuario tras obtener evidencia suficiente. No se atribuyen 30 minutos reales a esta build nueva; los resultados anteriores pertenecen a la build previa. Detalles y hashes actualizados al principio de [RUN_RESULTS_2026-11-15.md](RUN_RESULTS_2026-11-15.md). La sesión humana sigue pendiente.

## Antes de empezar

- Usa Unity 6000.3.28f1 y cierra el Editor sobre el proyecto: no lances una segunda instancia.
- Guarda logs y XML en `Logs/phase04-integration/`.
- Si una puerta falla, regístralo y no certifiques las siguientes.
- En cada puerta, anota por separado:
  - el exit code;
  - los conteos del XML (total, passed, failed, inconclusive, skipped);
  - el tipo de evidencia.
- *Inconclusive* o *Pending* no es *Passed*.

```sh
U=~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity; L=Logs/phase04-integration; mkdir -p $L
```

## Puerta 2: EditMode

### Comandos

```sh
"$U" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults $L/editmode-all.xml -logFile $L/editmode-all.log
"$U" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testCategory Phase04Long -testResults $L/editmode-long.xml -logFile $L/editmode-long.log
```

### Qué mirar

- [x] **Suite completa: 0 fallos.** Referencia previa de D3, en su rama sin D1/D2/D4/D5: 436/437. El único fallo era
      `Phase03SceneTests`.
- [x] `Phase03SceneTests.TheShippedSceneIsWiredWithoutPhysicsFakesOrASecondCable` debe pasar ahora. El escáner ya exime
      solo las carpetas de fixtures de contratos.
- [x] **Nueva:** `Phase04HostSendFailureTests.AFailedSendReleasesTheMemberOnceWithoutADisconnectEvent`. Esperado:
      `LeaveCount` = 1, 0 miembros y ningún `Leave` doble.
- [x] `Phase04IntegratedConformanceTests`:
  - `Competition` ahora exige `CommandIdConflict` si otro remitente reutiliza el id;
  - lo que siga en *Inconclusive* debe tener un motivo escrito.
- [x] `Phase04ValidationToolingTests`: primera ejecución real. El código de D5 nunca se había compilado antes de la
      integración.
- [x] Suites de D1 (`Tests/EditMode/Phase04/Simulation`), D2 (`…/Application`) y D3 (`…/Networking`).
- [x] Regresión de fases 1–3: mismas suites que antes, sin fallos nuevos.
- [x] `Phase04Long` (15 min simulados por prueba): sin cierre ni reinicio de la base.

## Puerta 3: validadores

Ejecutados: D5 **exit 2**, 102 comprobaciones aprobadas, 0 fallidas y 2 pendientes explícitos (personas y build/ejecución certificada por separado). Fases 2 y 3: **exit 0**. Exit 2 no equivale a aprobación completa.

```sh
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunChecksFromCommandLine -logFile $L/d5-checks.log
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase03.Phase03BuildChecks.RunChecksFromCommandLine -logFile $L/p3-checks.log
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.RunChecksFromCommandLine -logFile $L/p2-checks.log
```

- [x] Fase 4, códigos de salida: 0 = pasa, 1 = falla, 2 = pendiente. Revisa `d5-checks.txt`.
- [x] Auditoría de la escena productiva:
  - un runner, un host y un cliente;
  - sin componentes Fake, Development ni Provisional;
  - vistas D4 presentes;
  - piloto inerte.
- [x] Escenario enviado: plano, estaciones (consola en 9.0, 1.4) y conectividad.
- [x] Fases 2 y 3 sin regresión.

## Puerta 4: PlayMode y presentación

```sh
"$U" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults $L/playmode.xml -logFile $L/playmode.log
"$U" -projectPath . -force-glcore -executeMethod Trainer.Unity.Presentation.BaseOperations.Editor.Phase04VisualChecks.Run -d4-ci -logFile $L/d4-visual.log
```

- [x] PlayMode completo: **76/76**, exit 0. Referencia previa de D3: 75/75.
- [x] `Phase04SceneIntegrationTests.D4ViewsBindToUnknownReadingsWithoutSendingIntents`, ahora `[Test]`.
- [x] `Phase04NetworkWireTests` y `Phase04NetworkSocketTests`: sockets reales en 127.0.0.1.
- [x] **Visual D4, que necesita gráficos:** fixtures nativos finales a 16:9, 16:10 y 4:3; 91 comprobaciones por formato, exit 0. Capturas y límites visuales en el informe. El layout del panel cambió en la integración:
  - tarjetas compactadas a `y 80–173`;
  - fila de sesión libre en `y 42–76`;
  - aviso de refugio en línea propia;
  - bloque EVA en «Recursos y actividad».

  Revisa las capturas: sin solapes, textos sin recortar y botones Iniciar/Pausar/Reiniciar visibles. Prueba 16:9, 16:10
  y 4:3.

## Puerta 5: build de la escena productiva

```sh
"$U" -batchmode -nographics -projectPath . -buildTarget Linux64 -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.BuildFromCommandLine -logFile $L/build.log
```

- [x] La build se niega si una puerta rápida falla. **No usar `-phase4BuildAnyway` para certificar.**
- [x] Registra la información de la build (`Builds/Phase4LivingBase/<target>/build-info.txt`):
  - commit;
  - árbol limpio o modificado;
  - target;
  - escena;
  - sha256 del ejecutable.

## Puerta 6: ESA build, autoridad y dos clientes

`P` = el ejecutable de la puerta 5. Primero una prueba corta de 3 minutos; luego la de 30 minutos reales, en segundo
plano.

```sh
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7795 -logFile $L/authority.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect -trainer-autopilot continuity -trainer-autopilot-role water -trainer-autopilot-minutes 30 -logFile $L/water.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect -trainer-autopilot continuity -trainer-autopilot-role air -trainer-autopilot-minutes 30 -logFile $L/air.log
```

- [x] La autoridad registra `Listening on ws://127.0.0.1:7795/` (Unity añade `/`) y ambos clientes se conectan como jugadores.
- [x] Ambos clientes terminan con `[Autopilot] RESULT PASS`, exit 0, tras 1800,8 s reales (1800 s del piloto); escala de autoridad ×1.
  - El piloto ya falla si el estado queda congelado más de 15 s al final.
  - Revisa también las líneas de estado de cada 60 s.
- [x] Registro: ambos llegaron a `sim=1799.9s`, época 0; agua completó reparación y restitución del reciclador (una reparación, tres rutas); pedido descargado una vez y cuatro servicios de aire antes del estancamiento. El estado de fallo se observó. En los reportes de cada 60 s el reloj avanzó sin reiniciarse. La sesión de 30 min verifica continuidad, pero el piloto de aire no recupera su tarea cuando falta potencia (detalle y test separado en RUN_RESULTS).
- [x] **Desconexión y reconexión a mitad:** verificado en la build productiva; estado actual, sin ocupación fantasma, repuesto liberado y pedido conservado. Evidencia `built-session/verification.txt`.
- [x] **Reinicio:** época 0→1 observada por dos clientes remotos, sin recursos duplicados; `built-session/verification.txt`.

## Puerta 7: personas, gráficos y teclado (≥ 30 min reales)

**Pendiente, sin participantes**, por indicación del usuario. La cobertura automática de las puertas anteriores no sustituye esta sesión. Preparación y registro: [instrucciones de la sesión humana](HUMAN_SESSION.md).

Una autoridad y dos clientes con gráficos, sin `-trainer-autopilot`, o el anfitrión con gráficos más un cliente.

- [ ] Los dos jugadores entran y se ven, caminan y usan las estaciones por proximidad.
- [ ] Al abrir el panel (E en una estación o «Recursos y actividad»), WASD, E y R quedan bloqueados.
      Escape o «×» los liberan.
- [ ] Los recursos se consumen y se recuperan; las barras muestran unidades y tendencia, y «sin lectura» o «dato
      antiguo» no se confunde con 0.
- [ ] **Agua:** inspeccionar → procesar un lote → decidir potable o volver a tratar según la conductividad → comprobar el
      efecto.
- [ ] **Aire:** inspeccionar → cambiar de lecho o regenerar → comprobar la tendencia del CO2.
- [ ] **Avería del reciclador:** diagnosticar → aislar → cambiar el filtro (repuesto y tiempo) → probar → restituir.
      Lo perdido no se devuelve.
- [ ] **Reposición:** pedir → esperar el plazo → descargar. Aplicación única y límite de pedidos.
- [ ] **Ciencia y salida EVA:** completar o cancelar una salida **no** termina ni reinicia la base.
- [ ] **Avisos de refugio:** el escenario de fase 4 no los emite (`keepPhase3Episode = false`). Hay que decidirlo antes de
      poder probarlos.
- [ ] **Margen EVA:** visible en el pie con traje.
- [ ] Anota la hora de inicio y fin, el tiempo simulado, las incidencias y si se entendieron las decisiones. No inventes
      participantes.

## Cobertura de las pruebas que faltaban

| Prueba | Estado y evidencia |
| --- | --- |
| CO2 acoplado, dos particiones y cota documentada | **Passed**, `CoupledAuthorityCo2PartitionErrorStaysWithinTheDocumentedBound`: 0,25 s frente a 0,07 s; error 4,163×10⁻⁷ kg ≤ 1,852×10⁻⁵ kg. Contrato §6 intacto. |
| EVA completada y regreso anticipado tras cancelar ciencia | **Passed**, `CompletedOrAbortedEvaUsesRealSuitsAndKeepsTheContinuousBase` y conformidad `Phase04EvaConformance`. Ambos regresan a `Preparing` sin reiniciar la base. La salida abortada se registra como `Completed / returned-without-science`, no como ciencia completada. |
| Trajes y reparto de tripulación reales | **Passed**, auditoría de cada paso: dos miembros = cabina + trajes sellados; consumos de O2, agua y batería del modelo EVA real. |
| Balances de masa y energía, base + procesador + trajes | **Passed**, auditoría integrada con flujos de cuerpo, gases, salmuera, llenado y carga; balances adicionales de continuidad en D5. No se infiere conservación solo de un contador. |
| Lecturas Stale/Unavailable en pantalla | Capturas nativas a tres relaciones de aspecto; revisión visual registrada en el informe. Fixture rotulado, no sesión de red. |
| Escena productiva y sockets, unión, reloj, desconexión y reinicio | Runner `Phase04BuiltSessionChecks` sobre el ejecutable real: desconexión/reconexión a mitad de reparación y reinicio de época sin duplicación. Capturas productivas en Play Mode. |
| `ProcessBatch` rechazado sin pérdida adicional de masa | **Passed**, `ProcessorDestinationRefusalDoesNotDebitTheBaseOrFaultTheSession`: inyección explícita de un destino ocupado después de validar, no una ruta alcanzable del escenario normal. Sin protección: prueba roja real, 1/1 fallida; protección restaurada byte a byte y suite completa verde. |
| Build diagnóstica separada | **Passed**: ruta `Diagnostic/<target>`, metadatos `diagnostic-only`, exit 2 esperado. Manifiesto: 181/181 archivos de la build candidata intactos. |
| Fallo del escenario enviado sin reparación | **Passed**, `ShippedScenarioDegradesAt240AndFailsAt540WithoutRepair`: nominal hasta 239,75 s, degradado en 240 s y fallado en 540 s, sin cerrar la base. |

La suite completa final es **548/548**. La ejecución de las pruebas nuevas forma parte del XML de EditMode; sus nombres se encuentran en `Phase04IntegratedBalanceTests`. Los casos de conformidad que antes quedaban inconclusos ya se ejecutaron: **11/11 aprobados**, sin pendientes en esa suite. Para alcanzar reserva se usa un fixture de validación rotulado con 8,1 kg iniciales, conservando las tasas reales; no se cambia el escenario productivo.
