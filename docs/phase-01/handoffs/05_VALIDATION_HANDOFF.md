# HANDOFF — D5: validación tras correcciones de fase 1

Fecha: 2026-11-14. Base de corrección: `c0ef26089bf8261124b62b438e7e85e821a14691`. Cambios sin commit, merge ni push. Sustituye las afirmaciones estáticas antiguas «no hay escena/SDK/binding»: los tres existen y el wiring está incorporado en la copia aislada. Esto no certifica una build jugable.

## Checks actuales

`PhaseOneBuildChecks.RunAll` conserva los fixtures sintéticos, checks del fake, sesión real y loopback en memoria. Sus categorías no se mezclan con sockets ni con tests NUnit.

La auditoría de escena ahora exige un compositor, bootstrap/runner únicos, un host, un cliente y una sala. `AuditComposition` comprueba referencias host→bootstrap→runner/escenario, cliente/HUD/sala/cámara, objetos de rol guardados inactivos y en grupos disjuntos, cámara ortográfica activa y las tres estaciones diferentes enlazadas. Detecta campos ausentes, no solo componentes existentes. La función no entra en PlayMode ni abre conexiones; su pase no prueba callbacks, render ni partida. El audit distingue los GUID builtin de los GUID de assets.

`TwoProcessesOverApprovedSdk` sigue Pending, con motivo correcto: existe Unity Transport 2.7.4/WebSocket, pero este runner no lanza procesos reales. Los pendientes de HUD/render/build también describen pruebas por ejecutar, no piezas supuestamente inexistentes.

El build usa lista explícita de misión, rechaza SampleScene/plantillas y no modifica Build Settings ni target. Build Settings sigue con SampleScene para el flujo normal. Entry point:

```sh
# Ejecutar desde esta copia; requiere un Editor operativo. No se ejecutó durante estas correcciones.
UNITY='/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity'
PROJECT="$PWD"
timeout --signal=TERM --kill-after=10s 900s "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.BuildFromCommandLine \
  -phase1Scenes Assets/_Project/Scenes/Phase01_PowerCoop.unity -logFile "$PROJECT/Logs/phase1-build.log"
```

## Ejecuciones reales de la corrección

Evidencias nuevas: `Logs/fixes-20261114T230623Z/` dentro de la copia aislada. El sandbox de esta continuación permite escribir solo en la copia y `/tmp`; el padre debe copiar informes si necesita el destino exterior solicitado.

- Fuentes intactas ya incluidas con el Editor: Burst 1.8.31 (53 archivos), Mathematics 1.3.3 (91), Collections 2.7.1 (70) y UTP 2.7.4 (93) compiladas con Roslyn para obtener referencias managed. No se instalaron paquetes ni se alteraron fuentes del SDK. No es importación Unity, compilación nativa de Burst ni prueba de sockets.
- Compilación externa del proyecto: runtime y Editor separados (`Assembly-CSharp`, 39 fuentes; `Assembly-CSharp-Editor`, 14), sin exclusión del binding UTP. Exit 0. Los archivos con guards propios conservan sus guards; no se cambian defines del proyecto.
- NUnit mediante runner por reflexión, .NET 6.0.21 y NUnit real local: **122 passed / 0 failed / 0 skipped**. PowerSystem 30, Clock 5, MissionSession 21, OutcomeEvaluator 7, MissionScenario 19, FakeSessionPort 6, NetworkSessionAdapter 34. No es Unity Test Runner ni PlayMode. MissionScenario incluye 11 casos nuevos de presentación, dos TimeExpired, objetivo/reinicio, interrupción, lectura en pausa/Ready, detalle de conexión y geometría de viewport/hit areas.
- Checks D5 ejecutados desde el arnés: **9 validator-synthetic, 10 fake, 8 real-session y 8 loopback pasados**; **1 real-connection Pending**. No sumarlos a los 122 como una suite NUnit única.
- Sondas independientes: **829 aserciones pasadas**, incluyendo balances 17400/16200/15600 J, servicio efectivo de 0,5 s, 200 configuraciones analíticas × 2 tamaños de paso y recetas/textos corregidos.
- Auditoría estática de GUIDs: 39 referencias deduplicadas por asset, sin GUID duplicado ni referencias locales fileID sin resolver, contando los tres GUID URP instalados. Escena: un host, un cliente, una sala y un runner. No es un pase de AssetDatabase.
- Parseo YAML y referencias de rol/prefab: 27 objetos válidos; rechaza 7/7 mutantes solo en memoria (host ausente, referencias de cliente/sala ausentes, bootstrap de host nulo, rol cruzado, cámara incorrecta y HUD de sala nulo). No modifica la escena al probar sensibilidad.

Los tests de composición/ciclo de vida y los runners HUD/D4 se ampliaron y compilaron; NO se ejecutaron porque necesitan Unity. Las capturas originales D4 y resultados de procesos de D3 son históricos y no cuentan como prueba de esta versión.

## Pruebas desactivadas y pendientes reales

Los dos archivos D5 `PhaseOneScenarioValidatorTests` y `PhaseOneIntegrationTests` conservan `TRAINER_PHASE1_VALIDATION_TESTS && UNITY_INCLUDE_TESTS`. El segundo tiene factories null y casos Ignore/Inconclusive. No se activaron ni contaron. Los casos reales equivalentes de sesión/loopback sí corrieron por el runner de checks; esto no convierte el loopback en red.

No se volvió a lanzar Unity: la revisión anterior terminó con exit 1 por inicialización de licencia, sockets `Operation not permitted` e IPC fallido del Package Manager. No hay indicio de cambio de entorno. No se desactivó seguridad ni se cerraron procesos ajenos.

Pendiente ejecutar en Unity: importación/AssetDatabase; tests EditMode/PlayMode y runners; comprobación del HUD ante estado/resultado asíncronos, pérdida de host y reintento; render/clics en 1280 × 800 o mayor; build de misión y dos participantes en procesos diferentes. Luego separar, si se prueban, sockets en un proceso, procesos locales, LAN, Internet y WebGL/wss.

Receta de dos procesos (host con HUD local y cliente):

```sh
./Phase1Check.x86_64 -trainer-role authority -trainer-port 7777 -logFile host.log
./Phase1Check.x86_64 -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect -logFile client.log
```

Para ver ambas salas no arrancar el host con `-nographics`. Un servidor headless + dos clientes son tres procesos. El reintento es manual desde HUD; no se implementó migración. Para HTTP/ws y HTTPS/wss, ver D3, que da endpoints/puertos/path explícitos. No se configuró ningún servicio público.

Los contratos, física, scenario asset, paquetes/versiones y ProjectSettings no cambiaron. Las potencias conservan la semántica de último paso medido; el resultado ahora distingue objetivo/tiempo/interrupción y muestra la meta. La misma build y escenario siguen siendo necesarios para clasificar resultados en el cliente sin ampliar el contrato.
