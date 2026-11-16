# Integración del menú

Base: `4903c587da1590d35d7a937cdc4f596d3dfe0512`. Rama: `feature/main-menu`.

Abrir `Assets/_Project/Scenes/MainMenu.unity` y pulsar Play. Prefab reutilizable: `Assets/_Project/Prefabs/UI/MainMenu/MainMenu.prefab`. Reconstrucción explícita: `Tools > Trainer > Main Menu > Create scene and prefab` (reemplaza solamente escena/prefab propios; conserva configuración existente). UI uGUI e Input System ya instalados; fuente builtin LegacyRuntime.

Configuración serializada: `Data/MainMenu/PublicEndpoint.asset` → `MainMenuConfiguration`: publicHost, publicPort, path, secure, timeoutSeconds. Endpoint público autorizado por el usuario: `ws://molecule-unlined.tun.ply.gg:43796/`; túnel TCP hacia el puerto local 7777. La build usa el puerto público 43796, nunca el interno. TLS solo si está realmente habilitado. No introducir credenciales. El menú nunca cae a localhost. No hay sondeos ni socket adicional al transporte existente.

El prefab serializa `MainMenuView.configuration`, las seis páginas (home/help/settings/credits/multiplayer/connection), botones, textos y controles de ajustes; todas las referencias las asigna el constructor. `launchAdapter` referencia `MainMenuNetworkAdapter`, componente real del mismo prefab. La página multiplayer permite Conectarse/Volver sin escribir direcciones. El único EventSystem está en la escena, con InputSystemUIInputModule. No duplicarlo al montar el prefab.

## Frontera propuesta, pendiente de acuerdo con fase 5

`MainMenuNetworkAdapter` implementa `IMainMenuLaunchPort`, crea un único `UnityTransportClient` y un `BaseOperationsConnectionController` existentes, y expone su `Session` real. No usa un fake ni un cliente provisional de sondeo. Puede sustituirse por el componente del integrador asignado a `MainMenuView.launchAdapter`. `Connect(attempt, configuration)` recibe la configuración pública en runtime; `Changed(attempt,state,detail)` informa estados reales. `Cancel(attempt)` cancela y libera ese intento; `Leave()` abandona solo al cliente. Los ids invalidan callbacks tardíos incluso si Cancel emite eventos sincrónicos. El flujo impide doble clic, admite reintento y limita el intento completo a 3–60 segundos. `Joined` significa admisión y snapshot inicial confirmados, nunca transporte abierto.

Gameplay encontrado: `Assets/_Project/Scenes/Phase04_LivingBase.unity`. No existe escena de fase 5 en el checkout inspeccionado. Su `BaseOperationsSceneComposition.Awake` resuelve rol por flags, con Authority como valor inicial; Start prepara su cliente y conecta según argumentos. NO cargarla ciegamente desde el menú: puede crear autoridad accidental. No se cambian argumentos de proceso desde runtime.

`BaseOperationsNetworkClient.Configure(WebSocketClientOptions)` admite opciones antes de Prepare/Connect, pero la composición de escena, rol, referencias del cliente y propiedad de transporte necesitan montaje coordinado. El cliente del menú conserva su único transporte mientras se permanece en esa escena; destruirla libera la sesión. No se carga gameplay automáticamente ni se crea otra identidad. El integrador debe conservar ese contexto o reemplazar el adaptador antes de habilitar la transición.

Receta del integrador:

1. Montar el adaptador y fijar explícitamente rol Client antes del Awake de composición; preservar flags actuales `-trainer-role authority|client`, `-trainer-server`, `-trainer-port`, `-trainer-path`, `-trainer-secure`, `-trainer-server-name`, `-trainer-connect` para desarrollo.
2. Reutilizar `MainMenuNetworkAdapter.Session` en el montaje de presentación, o reemplazar el adaptador por una composición que configure una sola instancia de `BaseOperationsNetworkClient` antes de Prepare. No crear ambos clientes. Si hay carga previa a admisión, emitir Loading y mantener la interfaz visible hasta sincronizar. No crear otra autoridad, Join, inventario ni reloj.
3. Propiedad: en esta entrega el adaptador conserva el transporte dentro del menú; el menú no carga escenas ni usa DontDestroyOnLoad. El montaje definitivo debe decidir y probar su conservación durante la carga. Cancel/Leave limpian la conexión y suscripciones del intento. Tras Joined, la composición definitiva debe conservar el mismo cliente/identidad; documentar el montaje concreto cuando exista fase 5.
4. Al salir o perder conexión, liberar ese cliente y cargar MainMenu una vez. No detener la autoridad remota ni reiniciar la base de otros jugadores.
5. Establecer MainMenu como entrada de la build integrada y gameplay como segunda escena. EditorBuildSettings y escenas compartidas no se modifican en esta rama. En headless/authority conservar entrada directa al bootstrap dedicado, sin menú ni autojoin. La UI se desactiva en batchmode; eso por sí solo no compone un servidor.
6. Probar menú → servidor real → admisión/snapshot → gameplay; fallo/reintento y regreso sin afectar otro cliente. Endpoint y adaptador de protocolo fase 4 ya configurados; pendiente del servidor y del montaje final de fase 5.

Build Linux independiente: Unity `-batchmode -nographics -job-worker-count 4 -projectPath <proyecto> -executeMethod Trainer.Editor.MainMenu.MainMenuBuilder.BuildLinux -quit -logFile Logs/main-menu/build.log`. El método pasa una lista explícita de escenas y no cambia EditorBuildSettings. No ejecutar CLI si otro Editor usa el proyecto. Esta build contiene solo menú y no acredita gameplay integrado.

Preferencias locales: PlayerPrefs con prefijo `main-menu.`; volumen/mute y resolución/modo confirmados. Cancelar revierte audio; pantalla tiene 12 segundos para confirmar y revierte también al cerrar/desactivar. Resolución administrada por Editor/navegador se indica y deshabilita. Los ajustes no modifican simulación ni servidor.
