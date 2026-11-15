# Fase 4: pruebas pendientes

Este documento lista lo que falta para aceptar la fase 4. Se refiere al commit `c5488a1` (`main` = `integration/phase-04-assembly`).

Lo único ejecutado es el builder de la escena. Compiló todo con exit 0 y 0 errores. Ninguna de las pruebas de abajo se
ha ejecutado sobre el conjunto integrado.

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

- [ ] **Suite completa: 0 fallos.** Referencia previa de D3, en su rama sin D1/D2/D4/D5: 436/437. El único fallo era
      `Phase03SceneTests`.
- [ ] `Phase03SceneTests.TheShippedSceneIsWiredWithoutPhysicsFakesOrASecondCable` debe pasar ahora. El escáner ya exime
      solo las carpetas de fixtures de contratos.
- [ ] **Nueva:** `Phase04HostSendFailureTests.AFailedSendReleasesTheMemberOnceWithoutADisconnectEvent`. Esperado:
      `LeaveCount` = 1, 0 miembros y ningún `Leave` doble.
- [ ] `Phase04IntegratedConformanceTests`:
  - `Competition` ahora exige `CommandIdConflict` si otro remitente reutiliza el id;
  - lo que siga en *Inconclusive* debe tener un motivo escrito.
- [ ] `Phase04ValidationToolingTests`: primera ejecución real. El código de D5 nunca se había compilado antes de la
      integración.
- [ ] Suites de D1 (`Tests/EditMode/Phase04/Simulation`), D2 (`…/Application`) y D3 (`…/Networking`).
- [ ] Regresión de fases 1–3: mismas suites que antes, sin fallos nuevos.
- [ ] `Phase04Long` (15 min simulados por prueba): sin cierre ni reinicio de la base.

## Puerta 3: validadores

```sh
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunChecksFromCommandLine -logFile $L/d5-checks.log
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase03.Phase03BuildChecks.RunChecksFromCommandLine -logFile $L/p3-checks.log
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.RunChecksFromCommandLine -logFile $L/p2-checks.log
```

- [ ] Fase 4, códigos de salida: 0 = pasa, 1 = falla, 2 = pendiente. Revisa `d5-checks.txt`.
- [ ] Auditoría de la escena productiva:
  - un runner, un host y un cliente;
  - sin componentes Fake, Development ni Provisional;
  - vistas D4 presentes;
  - piloto inerte.
- [ ] Escenario enviado: plano, estaciones (consola en 9.0, 1.4) y conectividad.
- [ ] Fases 2 y 3 sin regresión.

## Puerta 4: PlayMode y presentación

```sh
"$U" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults $L/playmode.xml -logFile $L/playmode.log
"$U" -projectPath . -force-glcore -executeMethod Trainer.Unity.Presentation.BaseOperations.Editor.Phase04VisualChecks.Run -d4-ci -logFile $L/d4-visual.log
```

- [ ] PlayMode completo. Referencia previa de D3: 75/75.
- [ ] `Phase04SceneIntegrationTests.D4ViewsBindToUnknownReadingsWithoutSendingIntents`, ahora `[Test]`.
- [ ] `Phase04NetworkWireTests` y `Phase04NetworkSocketTests`: sockets reales en 127.0.0.1.
- [ ] **Visual D4, que necesita gráficos.** El layout del panel cambió en la integración:
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

- [ ] La build se niega si una puerta rápida falla. **No usar `-phase4BuildAnyway` para certificar.**
- [ ] Registra la información de la build (`Builds/Phase4LivingBase/<target>/build-info.txt`):
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

- [ ] La autoridad registra `Listening on ws://127.0.0.1:7795 (cable 'T' 'B' v1)` y ambos clientes `Connected as P<n>`.
- [ ] Ambos clientes terminan con `[Autopilot] RESULT PASS`.
  - El piloto ya falla si el estado queda congelado más de 15 s al final.
  - Revisa también las líneas de estado de cada 60 s.
- [ ] Anota por separado:
  - el tiempo real (`real=`) y el simulado (`sim=`);
  - la avería del reciclador: degradación a 240 s simulados y fallo 300 s después;
  - su reparación y restitución;
  - un pedido descargado y un servicio de aire.
- [ ] **Desconexión y reconexión a mitad:** mata un cliente durante una reparación y relánzalo. Debe incorporarse con el
      estado actual, no con el inicial. Sin ocupación fantasma, con el repuesto liberado y el pedido conservado.
- [ ] **Reinicio:** nueva época en todos los clientes y ningún recurso duplicado.

## Puerta 7: personas, gráficos y teclado (≥ 30 min reales)

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

## Pruebas que faltan por escribir

| Prueba | Dueño | Por qué |
| --- | --- | --- |
| CO2: dos particiones de paso con la autoridad real acoplada, diferencia ≤ cota documentada | D2/D1 | Hipótesis confirmada por lectura, no medida. Un factor medio exacto exige cambiar `SESSION_CONTRACT.md` §6 (congelado) |
| Salida EVA en modo continuo, completada y cancelada (vuelta a `Preparing`) | D5 | Sigue *Pending* en la conformidad |
| Las pruebas «EVA» ejercitan trajes y el reparto de tripulación, no solo un contador | D1/D2 | No revisado |
| Balances completos de energía y masa, base más trajes | D5 | Incompleto |
| Lecturas Stale/Unavailable vistas en pantalla, no solo enlazadas | D4/D5 | La prueba actual no juzga lo que se ve |
| Escena productiva en Play Mode, con sockets: unión, reloj, desconexión, reinicio | D5 | Se retiró de PlayMode porque `[UnityTest]` no existe sin asmdef de pruebas. Cubrirla con un runner del Editor o con la puerta 6 |
| `ProcessBatch` rechazado por el equipo sin pérdida de masa | D2 | Endurecimiento sin caso rojo posible con el escenario válido |
| Separar la build diagnóstica de la aceptada en `Phase04BuildChecks` | D5 | No revisado en la integración |
