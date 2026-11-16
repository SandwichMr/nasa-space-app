# Entrega del menú principal

Rama `feature/main-menu`; base `4903c587da1590d35d7a937cdc4f596d3dfe0512`. El SHA final se obtiene con `git rev-parse feature/main-menu` (evita una referencia circular dentro del propio commit).

Abrir `Assets/_Project/Scenes/MainMenu.unity` y Play. Presentación uGUI real, prefab y configuración pública enlazados; ayuda, créditos, volumen/silencio, ajustes locales, confirmación/reversión de pantalla, menú multiplayer y conexión real al endpoint público autorizado. Menú español con recursos originales reutilizados; ver `ART_PROVENANCE.md`.

Archivos propios: escena MainMenu, Runtime/Unity/MainMenu, Editor/MainMenu, Art/MainMenu, Prefabs/UI/MainMenu, Data/MainMenu, Tests/EditMode/MainMenu y docs/main-menu, con .meta generados por Unity. Sin modificaciones permanentes a paquetes, escenas de gameplay, build settings, transporte, modelos o configuración compartida.

## Validación

Unity nativo 6000.3.28f1. Constructor de escena ejecutado correctamente. 15 pruebas EditMode pasadas, 0 fallos: `Logs/main-menu/editmode.xml` y `tests.log`. Cubren doble clic, sincronización sin falsa confirmación, cancelación y callbacks síncronos/tardíos, timeout, reintento, liberación/dispose, salida voluntaria, pérdida de conexión tras admisión, endpoint ausente/privado, integrador ausente, persistencia/corrupción/audio y referencias serializadas/EventSystem único.

Comando reproducible de tests: Unity `-batchmode -nographics -job-worker-count 4 -projectPath <proyecto> -runTests -testPlatform EditMode -testFilter Trainer.Tests.MainMenu -testResults Logs/main-menu/editmode.xml -logFile Logs/main-menu/tests.log`. Dos procesos batch propios se detuvieron al recrear escena; se terminaron únicamente esos procesos. Se reintentó con cuatro workers y render OpenGL; no se modificó la versión del Editor. No se cerró ningún Editor del usuario.

Build Linux independiente compilada correctamente: `Builds/MainMenu/SpaceStationBreakdown.x86_64`. Capturas y resultado del recorrido nativo en `Logs/main-menu/native-smoke.log`. La evidencia permanece local en Logs/main-menu y la build en Builds/MainMenu, ambos ignorados por Git.

El ejecutable admite `-main-menu-smoke`: recorrido optativo de pantallas y capturas reales sin transporte falso. Sin el argumento no se ejecuta el recorrido. Se prueban ajustes de audio, navegación de botones, ayuda, créditos, error/reintento/volver y capturas en 16:9, 16:10 y 4:3. Incluye eventos de teclado Tab/Enter/Escape inyectados en el Input System real; no equivale a prueba manual ni integración de gameplay.

## Pendientes precisos

Endpoint autorizado: `ws://molecule-unlined.tun.ply.gg:43796/` (TCP hacia 7777 local). `MainMenuNetworkAdapter` usa el transporte y controlador existentes de fase 4. El usuario se encarga de disponibilizar el servidor. El montaje de gameplay de fase 5 sigue pendiente. El intento real al endpoint terminó con `ProtocolError`, informado por el transporte; no hubo admisión/snapshot confirmados. Se muestra error y se permite reintentar. No se afirma conexión online exitosa, gameplay, retorno entre escenas o convivencia con otro cliente. No se verificó todavía la confirmación/reversión de pantalla mediante interacción manual. Tampoco se certifica Windows/WebGL. Pantalla conectando enlazada al transporte real; sin fake productivo.

No hay escena de fase 5 en este checkout. `INTEGRATION.md` describe la frontera propuesta y la receta para montar un solo cliente, conservar rol dedicado y acordar el cambio de escenas. No se contactó a un integrador externo; el acuerdo y montaje final siguen pendientes.

Antes de distribución pública, el equipo debe verificar los derechos de sus referencias de base/terreno aportadas originalmente; no se inventaron licencias ni autores.
