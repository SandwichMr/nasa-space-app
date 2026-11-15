# Fase 4: evidencia de ejecución en Unity

Validación sobre `9785b73936d522232fc41513f49b01fb52551e55` con cambios locales; Unity **6000.3.28f1**, Linux. Las pruebas se ejecutaron en el Editor nativo y en su player compilado. No se cambió de versión, paquetes, backend, plataforma de la receta ni contrato científico. Ninguna ejecución lanzó dos Editores simultáneos sobre el proyecto.

**Resultado final automatizado:** puertas de Unity verificadas; la sesión de 30 minutos terminó con ambos clientes `RESULT PASS` y exit 0. La sesión humana no se ejecuta por indicación del usuario y sigue pendiente. La fase completa no está aceptada.

## Corrección posterior del piloto de aire

La limitación descrita abajo fue corregida posteriormente: el piloto cancela una regeneración bloqueada durante 3 s reales, libera la demanda y espera 110 % de la energía configurada del servicio antes de reintentar. El modelo físico y las tasas del escenario no cambian.

Unity 6000.3.28f1: **549/549 EditMode** y **76/76 PlayMode**, exit 0; evidencia final en `Logs/phase04-recovery/`. La regresión `AirContinuityPilotRecoversPowerStallAndRetriesService` exige un registro de regeneración realmente completada después del bloqueo y estación libre al finalizar.

Build nueva: UTC `2026-11-15T21:19:57.3431800Z`, ensamblado SHA256 `db11e71c1ab5eae0c334ce82f9f3b71bd59ac753c79f02cb97052a4ecccee0c0`. Metadatos y manifiesto completo en `Logs/phase04-recovery/`. La prueba corta de esta build terminó con ambos `RESULT PASS`. La corrida nueva a tiempo real fue detenida por solicitud de acelerar. El máximo admitido es ×8; la tentativa ×10 volvió a ×1 y fue descartada.

La corrida ×8 confirmó cancelación y tres recargas/reintentos a **599,3 / 1006,9 / 1414,4 s simulados**. Última muestra: unos **180,8 s reales / 1439,8 s simulados**, época 0, 3 cancelaciones por potencia, 11 servicios aceptados, reparación de agua y entrega conservadas. El contador `airServices` cuenta aceptaciones, no finalizaciones. El contador `requests` no incluye estas cancelaciones y por eso puede ser menor que `accepted`; se conservan los logs para interpretar los contadores correctamente.

El usuario pidió terminar al haber evidencia suficiente: la corrida se detuvo antes del veredicto final. **No certifica 30 minutos reales ni 4 minutos completos de la nueva build**. Logs: `Logs/phase04-integration/continuity-accelerated-x8-recovery/`; las interrupciones se rotulan explícitamente. Los resultados de 30 minutos y hashes del resto del informe corresponden a la build anterior y se conservan como históricos. La sesión humana sigue pendiente.

## Puertas y tipos de evidencia (build anterior)

Los conteos de XML se muestran como **total / passed / failed / inconclusive / skipped**. No se suman tipos de evidencia diferentes ni se equipara `Pending` a `Passed`.

| Puerta | Exit | Resultado | Evidencia local |
| --- | --- | --- | --- |
| EditMode completo, fases 1–4 | **0** | **548 / 548 / 0 / 0 / 0** | `Logs/phase04-integration/editmode-all.{xml,log}`; ejecución 20:47:16–20:47:32 UTC |
| Categoría `Phase04Long` | **0** | **2 / 2 / 0 / 0 / 0** | `editmode-long.{xml,log}`; 15 min simulados por guion, no minutos reales |
| Validador D5 | **2** | **102 Passed, 0 Failed, 2 Pending, 13 Info** | `d5-checks.{txt,log}`; pendientes explícitos: personas y build/run certificados por sus propias puertas |
| Validadores fases 3 y 2 | **0 / 0** | Fase 3: 34 aprobados, 0 fallos. Fase 2: 0 fallos; conserva sus pendientes manuales | `p3-checks.log`, `p2-checks.log`; no generan XML de Test Runner |
| PlayMode completo | **0** | **76 / 76 / 0 / 0 / 0** | `playmode.{xml,log}`; incluye wire, sockets reales de loopback y enlace de vistas desconocidas |
| Fixture D4, tres aspectos | **0 en cada formato** | 91 comprobaciones por formato, 12 estados cada uno; son fixtures, no autoridad ni red | `visual-final-{16x9,16x10,4x3}/`, logs homónimos |
| Escena productiva renderizada | **0 en 16:9, 16:10 y 4:3** | Composición real, controles de sesión visibles, reloj, detalles y pausa | `productive-visual-row-fixed-16x9/`; comprobaciones de píxeles de la fila de sesión |
| Build Linux productiva | **0** | `Succeeded`, puertas rápidas aprobadas, **sin** `-phase4BuildAnyway` | `build.log`, `build-info.txt`, `build-artifact-sha256.txt` |
| Prueba corta de la build definitiva | **0** | Ambos clientes **RESULT PASS**, exit 0; 180,8 s reales por piloto y 359,8 s simulados, escala de autoridad ×2 | `continuity-3min-accepted/{authority,water,air}.log`, `summary.json` |
| 30 minutos de esa build | **0, supervisor Passed** | 1809,44 s de pared; cada piloto 1800,8 s reales / 1799,9 s simulados; ambos RESULT PASS, epoch 0; autoridad cerró correctamente | `continuity-30min-accepted/{summary.json,authority.log,water.log,air.log}` |
| Muerte de proceso, reincorporación y reinicio | **Passed, exit 0** | Build definitiva: operación/repuesto liberados, pedido conservado y época 0→1 sin duplicar existencias. 71,8 s reales, autoridad ×4 | `built-session/`, `built-session-final.log` |
| Aislamiento de la build diagnóstica | **Passed** | Exit 2 diagnóstico esperado; manifiesto prueba que no alteró la build candidata | `build-diagnostic.log`, `diagnostic-isolation.txt` |
| Personas, gráficos y teclado, ≥30 min | **No ejecutada** | Sin participantes; no se inventan | [HUMAN_SESSION.md](HUMAN_SESSION.md) |

`final-gates.json`, `remaining-checks.json`, `execution-results.jsonl` y los XML/logs permanecen en `Logs/phase04-integration/`, ignorado por Git. Las pruebas de sockets PlayMode usan un doble de autoridad D3: no sustituyen las ejecuciones de la escena y del player reales.

## Defectos encontrados y corregidos

- El descubridor incluía dobles globales y anidados como producción. Se excluyen los fixtures y sus tipos anidados; la autoridad real vuelve a ser única.
- Dos pruebas hacían `Join` del fake antes de publicar el snapshot con el jugador. Se corrigió su preparación, sin cambiar el contrato del fake ni relajar las aserciones.
- El piloto desatendido no enviaba `Resume` desde `Ready`: la primera ejecución de tres minutos produjo dos `RESULT FAIL`, reloj en cero. Ahora solicita el mismo comando de sesión de la UI, únicamente tras confirmar la unión y solo desde `Ready`; no reanuda una pausa. Tres casos de regresión verifican Ready/Running/Paused.
- El piloto de aire iniciaba el tiempo de espera al planear la inspección, antes de llegar, y abandonaba la marcha. El tiempo de espera empieza al recibir la aceptación. Una prueba recorre el flujo por puerto/intenciones reales y exige una inspección en 30 s simulados.
- La fila Iniciar/Pausar/Reiniciar seguía tapada: reservar coordenadas no bastaba mientras el HUD pintaba un fondo opaco. El fondo deja libre esa fila y los controles tienen profundidad explícita. La revisión de capturas detectó el defecto aunque el runner anterior devolvía PASS; el runner ahora verifica píxeles visibles de estado y texto de botones en Ready, Running y Paused.
- La conformidad ya ejecuta dos salidas EVA reales, una con ciencia y otra con ciencia cancelada/regreso anticipado; observa los registros físicos de los modelos concretos. El caso de reserva usa un fixture rotulado con 8,1 kg iniciales para alcanzar el umbral por consumo en tiempo acotado, sin modificar tasas ni el escenario enviado.
- Una build forzada usa `Builds/Phase4LivingBase/Diagnostic/<target>/`, propósito `diagnostic-only` y exit 2. Su ruta no es la que consume el runner de aceptación.

La ejecución larga anterior, con la fila de sesión oculta, se interrumpió y **no cuenta como 30 minutos aprobados** (`continuity-30min/ABORTED.txt`). Las ejecuciones cortas fallidas se conservan por separado, sin sobrescribir la evidencia válida.

## Auditorías nuevas

`Phase04IntegratedBalanceTests`, ejecutadas dentro de las 548 pruebas:

- CO2: dos autoridades reales, 300 s, particiones de 0,25 s y 0,07 s. Diferencia **4,162962963794×10⁻⁷ kg**, inferior a **1,851851851852×10⁻⁵ kg**. No se cambia la regla del factor al final del paso de `SESSION_CONTRACT.md` §6.
- EVA: base, procesador, depósitos de gas, trajes y registros físicos reales. Cada miembro consume en cabina o en un traje; se verifican llenado, carga, consumo, gas de esclusa y reloj común. Residuos observados de masa < **3×10⁻¹³ kg** y energía < **2×10⁻¹¹ J**, dentro de tolerancias 10⁻⁸ kg y 10⁻⁶ J. La frontera declara entradas/salidas del cuerpo, CO2 extraído, salmuera y gas de esclusa; no supone que el cuerpo o los residuos no modelados estén en el inventario.
- Rechazo de `ProcessBatch`: se ocupa el destino mediante una inyección de fallo explícita después de validar el inicio. No se pierde agua adicional ni se interrumpe la sesión. Al retirar temporalmente la protección existente, Unity ejecutó **1 prueba, 1 fallo, exit 2**. Se restauró el archivo byte a byte y la suite completa volvió a verde. `processor-refusal-mutation.json` y ambos XML conservan la evidencia. No se presenta esa inyección como una ruta posible del escenario válido.
- Escenario productivo sin reparar: condición nominal a 239,75 s, degradada a 240 s, aún degradada a 539,75 s y fallada a 540 s. El fallo programado se mide aparte porque el piloto de continuidad lo repara antes de llegar a 540 s.

La conformidad integrada de autoridad obtiene **11 Passed, 0 Failed, 0 Pending**; sus balances de continuidad cierran incluyendo 16 kg de entrega y 0,6 kg de salmuera. Fases 1–3, fallo de envío del host y tooling D5 están dentro de la suite completa sin regresiones.

## Revisión visual

Los tres fixtures nativos finales terminaron con exit 0 y **91/91 comprobaciones** cada uno. Revisé las capturas extendidas: el panel EVA con scroll y el aviso de refugio sintético conviven con feedback aceptado; las lecturas stale/unavailable no se presentan como cero. Estas escenas guionizadas usan el fake común y no demuestran recepción de avisos por red. Capturas seleccionadas: [16:9](render/fixture-16x9.png), [16:10 con detalles EVA](render/fixture-16x10-eva-details.png), [4:3 con aviso](render/fixture-4x3-advisory.png).

En 4:3 se ve recorte lateral del mundo y algunos rótulos quedan junto al borde; el HUD se mantiene dentro del área de captura. Los rótulos del mundo también se recortan parcialmente en la composición 16:10 con detalles. Por tanto, la legibilidad de cada etiqueta en todo formato no queda certificada. Las capturas de la escena productiva confirman botones de sesión visibles en tres formatos; fixtures no equivalen a sesión humana.

## Incidencia histórica del piloto de aire (corregida arriba)

A partir de 300 s simulados el piloto queda en `ServiceAir (stalled: power not served)` y conserva ese estado en los reportes posteriores. La base no se congeló: el reloj avanzó aproximadamente un segundo simulado por segundo real, permaneció en época 0 y el piloto de agua siguió inspeccionando. Antes del estancamiento, el piloto de aire completó cuatro servicios y descargó el pedido; el piloto de agua completó la reparación del reciclador y tres rutas. El piloto desatendido no cancela esa tarea atascada ni reasigna su energía, así que el run sirve para verificar continuidad y el fallo de energía, pero no para afirmar recuperación autónoma completa. Un test separado de autoridad real (`LowBatteryPowerRecoveryCanCancelRegenerateAndResumeInTheSameEpoch`) sí pasó: cancelar, recargar y completar servicio en la misma época. La ejecución cubrió 1800 s de piloto; el supervisor tardó 1809,44 s, de 20:36:35.087 a 21:06:44.528 UTC. Ambos clientes acabaron con exit 0 y `RESULT PASS`; la autoridad cerró con exit 0. Su PASS corresponde a los criterios del piloto (continuidad, reparación/entrega y duración), no a una recuperación completa del piloto de aire.

## Artefacto definitivo

```text
commit 9785b73936d522232fc41513f49b01fb52551e55
working tree MODIFIED
editor 6000.3.28f1
target StandaloneLinux64
scene Assets/_Project/Scenes/Phase04_LivingBase.unity
built UTC 2026-11-15T20:33:17.8369410Z
purpose acceptance-candidate (quick gates only)
gates passed
executable SHA256 8027f7d1f9ae7dacfc826fb218adcc1ae7464af098a8a59397e7531c0f7ec0bc
Assembly-CSharp.dll SHA256 d2c846fdc1de3b429482070c447ccaba5c6653a52a2209c0c28fe5c1398881ef
```

El ejecutable de Unity es un lanzador cuyo hash puede coincidir entre builds distintas; por eso también se registra el ensamblado del juego y el manifiesto SHA256 de todos los archivos del artefacto. El manifiesto y `build-info.txt` copiados identifican la build exacta utilizada por los pilotos.

## Límites de aceptación

La puerta humana queda pendiente por decisión del usuario. Los avisos de refugio no se emiten en el escenario productivo (`keepPhase3Episode = false`); activarlos requiere una decisión explícita y otra validación. La evidencia automática no prueba comprensión, diversión ni teclado físico.
