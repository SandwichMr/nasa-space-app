# D4 — Interfaz 2D aislada

Rama: `phase1/interface-2d`. Base: `4850e1d128ca1c1666d6cdd13e0a4de05d4f6687`
(`main` al comenzar; los contratos ya estaban incluidos desde `fd73722`). No se han
mezclado otras ramas, creado commits ni realizado push o integración de escenas.
El equipo debe confirmar que este HEAD es su mismo punto de partida al unificar.

## Archivos entregados

- `Assets/_Project/Runtime/Unity/Presentation/ConnectionPanel.cs`
- `Assets/_Project/Runtime/Unity/Presentation/MissionHUD.cs`
- `Assets/_Project/Runtime/Unity/Presentation/PowerPanel.cs`
- `Assets/_Project/Runtime/Unity/Presentation/ScienceControls.cs`
- `Assets/_Project/Runtime/Unity/Presentation/ResultPanel.cs`
- `Assets/_Project/Prefabs/UI/MissionHUD.prefab`
- `Assets/_Project/Tests/PlayMode/UI/MissionHUDTests.cs`
- Este HANDOFF; únicamente los `.meta` correspondientes generados por Unity.

## Prefab y uso posterior

El prefab tiene un GameObject activo, Transform y MissionHUD. No tiene referencias
serializadas por asignar. MissionHUD compone cuatro objetos de presentación C#;
los paneles no son componentes que deban añadirse a otros GameObjects.
Usa IMGUI (`com.unity.modules.imgui` ya presente), skin del motor y una columna
con scroll. No requiere Canvas, EventSystem, cámara, fuentes ni assets nuevos.
El YAML referencia el GUID real que Unity generó para MissionHUD.cs; no se inventó
ni cambió ningún GUID existente. Su carga e instanciación se comprobaron en PlayMode;
la visualización queda pendiente porque la ejecución fue sin gráficos.

En la integración posterior autorizada, instanciar este prefab e inyectar desde
el bootstrap/adaptador del equipo, sin búsquedas globales:

```csharp
using Trainer.Application;
using Trainer.Unity.Presentation;

// hud es el MissionHUD de la instancia del prefab; port lo proporciona el equipo.
hud.Bind(port); // ISessionPort, callbacks siempre en el hilo principal de Unity.
hud.SetConnectionStatus(true, "Conexión confirmada por el adaptador"); // opcional
// Ante pérdida de transporte, antes de conservar una observación antigua:
hud.SetConnectionStatus(false, "Conexión perdida"); // equivale a Bind(null)
// Tras reconectar: Bind(port) de nuevo, luego confirmar conexión externamente.
```

Para una prueba local explícita se puede inyectar
`new Trainer.Application.Development.FakeSessionPort()`; el prefab no lo crea.
Sus valores son ejemplos fijos, no una simulación física ni prueba multiplayer.
No hay dependencia directa de D3 ni un nuevo contrato compartido de conexión.
El adaptador debe notificar la pérdida y reinyectar tras recuperar el transporte.
Sin confirmación externa la UI dice «Transporte sin verificar», aunque exista puerto.

## Binding y comportamiento

- Se suscribe antes de consultar TryGetSnapshot y enviar comandos. Sin snapshot
  inicial no muestra valores eléctricos ni permite solicitudes.
- La batería muestra energía/capacidad en J; generación y demanda/servicio esencial
  y científico en W. Tiempo simulado y trabajo científico en s. Estado, época y
  revisión proceden del snapshot; ninguna decisión de resultado ocurre en la UI.
- Activar/desactivar ciencia, iniciar/continuar, pausar y reiniciar envían comandos
  existentes con Guid nuevo y época observada. No se hace toggle optimista.
- Un único comando pendiente bloquea más clics. CommandCompleted correlaciona ID y
  época y muestra aceptación/rechazo con el Reason del puerto. La aceptación no
  modifica datos visibles: los actualiza SnapshotChanged, en cualquier orden.
- Se ignoran snapshots de épocas/revisiones anteriores o duplicadas. Al cambiar
  época se invalida el pendiente anterior, salvo Restart hacia la siguiente época,
  cuyo resultado lleva la época original según contrato.
- Disable, Destroy y Bind retiran listeners propios. Disable y Bind borran estado,
  resultados y pendientes; Enable vuelve a consultar el puerto. No se reutiliza una
  confirmación externa de conexión después de deshabilitar.
- Sin polling, reloj, descuentos de energía ni autoridad adicional. No se calcula
  porcentaje de batería, fisiología, tiempos de cultivo ni objetivos nuevos.

## Validación realmente realizada

Unity real: `6000.3.28f1`; dependencias leídas de Packages/manifest.json, sin cambios.

1. Intento de ejecutar Unity en batch con `-executeMethod
   Trainer.Tests.UI.MissionHUDTests.PrepareAndRun`: salida 1, bloqueado por otra
   instancia que ya tiene este proyecto abierto. Log local:
   `/tmp/phase1-interface-unity.log`. No se cerró ni se controló esa instancia. Reintento CLI solicitado por el usuario:
   mismo bloqueo y salida 1; log `/tmp/phase1-interface-unity-retry.log`.
2. Compilación C# explícita con el Roslyn y runtime incluidos en ese Unity, usando
   las referencias/defines reales de `Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.rsp`,
   incluyendo los cinco scripts y MissionHUDTests.cs: salida 0, sin diagnósticos.
   Respuesta temporal `/tmp/phase1-ui-compile.rsp`, salida `/tmp/phase1-ui-check.dll`.
   Esto comprueba compilación con UNITY_EDITOR; no ejecuta callbacks ni renderizado.
3. `git diff --check`: salida 0. Revisión de cambios confirmados, staged, unstaged
   y archivos nuevos contra la base: sin modificaciones de archivos seguidos ajenos.

4. Tras cerrar el usuario la instancia del Editor, ejecución real por CLI con Unity
   `6000.3.28f1`, `-batchmode -nographics` y el mismo executeMethod: **salida 0**.
   Log local `/tmp/phase1-interface-unity-run.log`, con
   `MISSION_HUD_CHECKS_PASSED: 28`. El runner entró a PlayMode, cargó e instanció
   el prefab, comprobó bindings y eventos con el fake compartido, y verificó
   listeners después de Destroy diferido. Sin errores de compilación ni excepciones
   de los checks. Los mensajes iniciales del servicio de licencias no impidieron
   la ejecución ni el cierre correcto.

Pasaron los 28 smoke checks de este runner en PlayMode. No se ejecutó una suite
NUnit completa ni se validaron renderizado, clics físicos, capturas, build o multiplayer.

## Repetir las comprobaciones de comportamiento

No hay test assembly PlayMode existente y no se autorizó crearlo. Para evitar
NUnit en el ensamblado normal, MissionHUDTests.cs contiene un runner de smoke
checks bajo UNITY_EDITOR, con excepciones y conteo explícito, sin paquetes nuevos.
No se descubre como suite NUnit en Test Runner.

Desde modo edición, tras finalizar importación/compilación: menú
`Tools > Trainer > Run Mission HUD checks`. Entra a Play, instancia únicamente
objetos temporales, prueba y destruye el HUD; sale de Play al terminar. No guarda
la escena. Para batch, cerrar primero la instancia que tiene abierto el proyecto:

```sh
/path/to/Unity -batchmode -nographics -projectPath '/home/leonardo/My project' \
  -executeMethod Trainer.Tests.UI.MissionHUDTests.PrepareAndRun \
  -logFile /tmp/phase1-interface-unity.log
```

No añadir `-quit`: el runner espera PlayMode y Destroy diferido. En éxito escribe
`MISSION_HUD_CHECKS_PASSED` y sale 0; ante fallo registra excepción y sale 1.
El menú interactivo sale de Play, sin cerrar el Editor. Si falta el prefab, el
runner puede serializar exclusivamente la ruta autorizada; no reemplaza uno existente.

Los 28 checks ejecutados cubren snapshot inicial, unidades, ciencia confirmada,
rechazo terminal, resultado/trabajo, Restart, respuesta síncrona y diferida,
resultado antes/después de snapshot, bloqueo de duplicados, revisión/época antiguas,
rebind, ausencia de snapshot, desconexión y retiro de ambos listeners al deshabilitar
/destruir. La cola interna solo demora eventos del FakeSessionPort protegido;
no cambia sus resultados ni implementa física o sesión alternativa.

## Límites y coordinación

Validar manualmente aspecto y clics a las resoluciones objetivo; los checks llaman
las mismas acciones públicas usadas por botones pero no simulan ratón ni render.
IMGUI es una presentación básica sin diseño artístico ni navegación por mando.
El contrato no informa desconexión, timeout o permisos: D3 debe avisar explícitamente
la pérdida de conexión y entregar eventos en hilo principal. Sin respuesta, una
solicitud queda pendiente hasta reconectar/rebind; no hay reintentos automáticos.
El motivo de rechazo se muestra como código del contrato, sin traducción adicional.

Las importaciones del Editor generaron `.meta` de carpetas y contratos previos fuera de
esta lista. Son artefactos ajenos a esta entrega: no se seleccionan ni eliminan.
Antes de integrar, el responsable de la base debe coordinar su seguimiento. No
seleccionar todo el árbol con git add; revisar solo las ocho rutas listadas y sus
`.meta` correspondientes. Ningún cambio a contratos, escenas o configuración es
necesario para compilar esta entrega; la conexión real queda para la unificación.
