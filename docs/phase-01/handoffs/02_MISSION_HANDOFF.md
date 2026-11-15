# HANDOFF — D2: composición integrada de fase 1

Estado de correcciones: 2026-11-14, sobre `c0ef26089bf8261124b62b438e7e85e821a14691`, sin commit/merge/push. Sustituye los pendientes de wiring de la entrega aislada. No certifica ejecución de Unity ni jugabilidad.

## Composición actual

`Assets/_Project/Scenes/Phase01_PowerCoop.unity` contiene:

- Un `MissionSceneComposition`, un HUD y el prefab existente `PowerRoom`, con sus tres estaciones.
- `MissionAuthority`, guardado inactivo: `MissionBootstrap`, el único `SimulationRunner` y `SessionNetworkHost`, con bootstrap asignado y escucha al iniciar.
- `MissionClient`, guardado inactivo: `SessionNetworkClient`, sin simulación local.
- La cámara ortográfica y luz de la plantilla, sin cambiar renderer/materiales ni ajustes globales.

El compositor selecciona rol en Awake, desactiva primero el grupo contrario y después activa el propio. Solo Authority crea `PowerSystem` y compone `MissionSession`. Un player WebGL fuerza Client. El cliente no llama Tick/Advance ni crea recursos autoritativos.

En Client se suscribe a `ConnectionStatusChanged`; solo al recibir el estado inicial confirmado conecta el puerto remoto al HUD. Connecting/Synchronizing/pérdida limpian observaciones y pendientes y conservan el detalle. Disable/Destroy del compositor retiran listeners; Enable recupera el estado del enlace actual sin duplicarlos. La reincorporación vuelve a enlazar el adaptador, incluso si empieza en otra época. El HUD ofrece Conectar/reintentar y Desconectar para el cliente.

La política de conexión es explícita: `connectOnStart=0`, `readLaunchArguments=1`. El enlace comienza con `-trainer-connect`, `?connect=1` o el botón del HUD. No hay reconexión automática ni migración de host.

El compositor conecta sala/HUD/cámara y encuadra en runtime los sprites existentes a la derecha del HUD, entre la cabecera y los controles. No guarda cambios de cámara en assets. Referencia de verificación visual: 1280 × 800 o mayor; el HUD tiene scroll para ventanas pequeñas. Pendiente comprobar render y clics físicos en Unity/build.

## Objetivo y presentación

Se conservan configuración, `MissionSession`, evaluador, contratos y contabilidad integrada. El escenario sintético sigue en E0=2400 J, capacidad=6000 J, generación=100 W, esencial=60 W, ciencia=80 W, objetivo=120 s, duración=240 s y tiempo ×2. Siempre encendida produce 60 s de trabajo; 60 s encendida + 60 s apagada + 60 s encendida logra 120 s a t=180 s. No son datos NASA.

El compositor pasa reglas y `DescribeOutcome` al HUD. La autoridad usa `MissionSession.Outcome`; el cliente clasifica el snapshot con las mismas reglas de esta build. Resultado distingue objetivo logrado, tiempo agotado e interrupción y muestra trabajo/meta/restante. No se cambió `MissionSnapshot`: builds con escenarios diferentes y el motivo exacto de un Restart fallido siguen siendo límites del contrato actual.

Las potencias están rotuladas como último paso medido (Reset: lectura inicial), separadas de la intención de ciencia confirmada. Cambiar ciencia en Ready/Paused no muta física ni inventa una lectura nueva. No se usa `Advance(0)` como mutación.

## Arranque y build

El build normal sigue apuntando a SampleScene. Para construir esta misión se usa el entry point D5 con lista explícita; no requiere cambiar Build Settings:

```sh
# Desde esta copia aislada; requiere Unity/licencia/IPC accesibles.
UNITY='/home/leonardo/Unity/Hub/Editor/6000.3.28f1/Editor/Unity'
PROJECT="$PWD"
timeout --signal=TERM --kill-after=10s 900s "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.BuildFromCommandLine \
  -phase1Scenes Assets/_Project/Scenes/Phase01_PowerCoop.unity -logFile "$PROJECT/Logs/phase1-build.log"
```

Para dos participantes en dos procesos, el host conserva HUD local; no usar `-nographics` si se quiere ver su sala:

```sh
./Phase1Check.x86_64 -trainer-role authority -trainer-port 7777 -logFile host.log
./Phase1Check.x86_64 -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect -logFile client.log
```

Headless + dos clientes es otra topología: tres procesos. Ver D3 para HTTP/ws y HTTPS/wss. No se arrancó ninguno de esos players durante estas correcciones.

## Evidencia y pendientes

Compilación Roslyn externa: runtime (39 fuentes) y Editor (14) separados, contra módulos reales de Unity y ensamblados compilados desde fuentes locales intactas de UTP 2.7.4/dependencias. No se instalaron paquetes. No equivale a compilación/importación Unity, Burst ni IL2CPP.

NUnit por reflexión: 122/122 pasados, incluidos 19 casos de `MissionScenarioTests` (antes 8), con regresiones TimeExpired sin ciencia y siempre encendida, éxito/reinicio, interrupción, medición y conexión. Los tests de composición/ciclo de vida Unity están preparados y compilados, pero no ejecutados. El arnés mantiene separados fake, sesión real, loopback en memoria y sockets.

No se reintentó Unity: la revisión ya documentó bloqueo de licencia, sockets e IPC del Package Manager y no hay evidencia de cambio de entorno. Pendientes: importar la escena, ejecutar tests Unity y HUD, render/clics, build de misión y host/cliente reales, pérdida/reintento, LAN/Internet/WebGL/TLS. Nadie debe marcar cerrada la fase con el pase managed.

Los contratos, paquetes, versiones y ProjectSettings permanecen intactos. Cualquier cliente conectado conserva los permisos existentes de ciencia/pausa/reinicio; no se inventaron roles nuevos.
