# Entrega integrada

`main` de fase 5 (`509174a`) se fusionó con el menú (`3f050ef`) mediante merge, preservando ambos historiales. El commit final de integración se identifica con `git rev-parse main` tras publicar.

Abrir `Assets/_Project/Scenes/MainMenu.unity` y Play. «Jugar online» → «Conectarse» carga `Phase05_LivingBase` en modo cliente, usa su único transporte fase 5 y muestra gameplay solo después de admisión + snapshot. El control «Volver al menú» abandona solo ese cliente; permite volver a conectar con identidad nueva. Mapa y arte de fase 4 conservados por el constructor de fase 5. Ver `INTEGRATION.md`.

Endpoint público autorizado: `ws://molecule-unlined.tun.ply.gg:43796/` (TCP hacia 7777 local). El usuario informa que el túnel todavía no funciona; la validación usa servidor local real mediante argumentos explícitos. No se afirma disponibilidad pública ni TLS.

## Builds

Cliente jugable: `Builds/MainMenu/SpaceStationBreakdown.x86_64`.
Servidor real: `Builds/build_linux_Server/SpaceStationBreakdownServer.x86_64`.
Recompilar servidor: `tools/build_linux_Server.sh` (Editor cerrado).
Ejecutar dedicado en puerto 7777: `tools/run_linux_Server.sh`; puerto alternativo: `tools/run_linux_Server.sh 17777`.
Ambas builds usan Linux; el servidor emplea el modo dedicado existente (`-batchmode -nographics -trainer-role authority`), sin instalar paquetes ni módulo Dedicated Server.

`MainMenuBuilder.BuildAll` prepara las dos escenas y compila ambos players. Conserva escenario/escena fase 5 existentes; no regenera sus GUID en cada build. EditorBuildSettings registra MainMenu primero y Phase05 después. No se modifican paquetes, renderer ni assets artísticos globales. Se revierten los cambios automáticos de serialización de Unity en la configuración compartida ajena a las escenas de entrada.

## Validación

19 pruebas EditMode pasadas (15 del menú + 4 de red fase 5), cero fallos: `Logs/main-menu/integrated-editmode.xml`, `focused-tests.log`. Comando: Unity `-batchmode -nographics -job-worker-count 4 -projectPath <proyecto> -runTests -testPlatform EditMode -testFilter 'Trainer.Tests.MainMenu;Trainer.Tests.Phase05.Networking' -testResults Logs/main-menu/integrated-editmode.xml -logFile Logs/main-menu/focused-tests.log`.

Se inició también la suite completa; se interrumpió durante las pruebas extensas de variedad del director para priorizar la entrega. No se declara pasada. Se detuvo únicamente el proceso batch propio; no había otro Editor abierto. Logs parciales: `full-editmode-interrupted.log` e `integrated-tests.log`.

Las builds Linux se compilaron realmente. La primera comprobación admitió dos clientes distintos contra el servidor local. La prueba posterior inicia la misión mediante el mismo comando Resume de la UI antes de comprobar movimiento/reloj. El recorrido optativo `-main-menu-play-smoke` valida snapshot confirmado, rol Client sin autoridad local, movimiento por teclado confirmado, salida/reentrada y nueva identidad; `-main-menu-observer` mantiene otro cliente para verificar continuidad del reloj al salir el primero. No usa fakes ni acceso directo al estado del servidor. Resultados en `local-player.log`, `local-observer.log` y `local-server.log`; captura `10-integrated-gameplay.png`.

La comprobación anterior del menú independiente verificó teclado Tab/Enter/Escape, audio, ayuda, créditos, fallo/reintento y escalas 16:9, 16:10 y 4:3. Confirmación/reversión de pantalla no se ha certificado por interacción manual. No se certifican Windows/WebGL ni funcionamiento del endpoint público.

Resultado final nativo local: PASS. `local-player.log` confirma movimiento con `crew=2`, regreso y REJOIN PASS; `local-observer.log` confirma que el reloj continuó hasta 25.97 s con `crew=1` tras la salida del otro cliente. Ambos recorridos terminaron con código 0. Captura real: `Logs/main-menu/10-integrated-gameplay.png`. El servidor temporal de prueba se detuvo después; el script de arranque queda listo para puerto 7777.
