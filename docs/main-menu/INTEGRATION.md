# Menú y multiplayer integrado con fase 5

Integración de `origin/main` en `509174a`, con la rama del menú. Entrada: `Assets/_Project/Scenes/MainMenu.unity`; gameplay: `Assets/_Project/Scenes/Phase05_LivingBase.unity`, generado por el constructor original de fase 5, con mapa/arte de fase 4 intactos. Ambas escenas están en EditorBuildSettings.

`PublicEndpoint.asset` conserva `ws://molecule-unlined.tun.ply.gg:43796/`, TCP hacia 7777 local. No requiere escritura de IP por el jugador. No se configura TLS ni Playit. Desarrollo permite `-trainer-server 127.0.0.1 -trainer-port 17777` por los argumentos existentes; no hay fallback silencioso.

`MainMenuNetworkAdapter` carga gameplay aditivamente. El evento `LivingBaseSceneComposition.Preparing` permite `ConfigureMenuClient` antes de resolver rol/Awake: fija Client, configura `LivingBaseNetworkClient` y desactiva el auto-connect/lectura duplicada de flags del cliente. La composición presenta su único puerto en Start y después el menú llama a Connect una vez. Se usa el cable fase 5, un transporte, una identidad y ningún runner/autoridad local.

Solo admisión y snapshot inicial confirmados ocultan el Canvas del menú y habilitan gameplay. El menú sigue vivo, sin DontDestroyOnLoad. «Volver al menú» desconecta exclusivamente este cliente y descarga gameplay; pérdida de conexión vuelve a error/reintentar. Los callbacks por intento se invalidan al cancelar. Una carga cancelada termina y se descarga. La conexión permanece en el MISMO cliente al entrar a jugar; no hay segundo Join.

Builds reales Linux (no simulaciones): `MainMenuBuilder.BuildAll` prepara escenario/escena, actualiza la lista de escenas y compila cliente integrado en `Builds/MainMenu/SpaceStationBreakdown.x86_64` y servidor en `Builds/build_linux_Server/SpaceStationBreakdownServer.x86_64`. El servidor es un player Linux ejecutado en modo dedicado `-batchmode -nographics -trainer-role authority`; no requiere instalar el módulo Dedicated Server. La UI no entra en su build. Si el cliente integrado se ejecuta en batchmode, enruta directamente a gameplay y conserva los flags de autoridad/cliente existentes.

Con Editor cerrado: `tools/build_linux_Server.sh` recompila el servidor. `tools/run_linux_Server.sh` inicia en 0.0.0.0:7777; un argumento numérico permite otro puerto para pruebas. No crea servicios ni cambia infraestructura. La autoridad usa los datos empaquetados existentes de fase 5 y es el único reloj.

Validación nativa local: servidor en 17777; clientes con `-main-menu-play-smoke -trainer-server 127.0.0.1 -trainer-port 17777`. `-main-menu-observer` mantiene otro cliente para verificar que salida/reentrada no detiene el reloj compartido. Sin flags de smoke no se automatiza ninguna acción. Logs/capturas en `Logs/main-menu/`. La disponibilidad del túnel público sigue a cargo del usuario.
