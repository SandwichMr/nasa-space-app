# Fase 1 — Desarrollador 4: interfaz 2D aislada

**Objetivo:** Crear la vista/prefab de la prueba y conectarlo exclusivamente al puerto existente, sin editar escena ni simulación.

**Arquitectura:** `Unity -> Application -> Simulation`; contratos congelados y trabajo aislado antes de unificar.

**Tecnología:** Unity Universal 2D / C#. Usar las versiones y dependencias de la base común, sin actualizarlas.

**Rama propia:** `phase1/interface-2d`.

## Regla principal: base congelada y alcance cerrado

**Este documento sustituye la asignación anterior de tu rol.** Trabaja sobre el commit común que contiene la base de contratos. No rediseñes esa base ni realices la integración final. Que una tarea te resulte útil no la vuelve autorizada.

Antes de empezar, lee el `AGENTS.md` del proyecto y `docs/phase-01/SESSION_CONTRACT.md` si existen; localiza las declaraciones reales y comprueba sus firmas. Esta planificación no demuestra que el prompt de contratos ya se haya ejecutado. Si falta la base o es incompatible, reporta el bloqueo; no la generes por tu cuenta.

### Base protegida, sin excepción automática

No modificar, mover, renombrar, duplicar ni ampliar estos tipos, estén donde estén: `PowerConfiguration`, `PowerSnapshot`, `IPowerSimulation`, `SessionState`, `SessionCommand`, `MissionSnapshot`, `CommandResult`, `ISessionPort` y `FakeSessionPort`. También es de solo lectura `docs/phase-01/SESSION_CONTRACT.md`.

El puerto compartido previsto es:

```csharp
void Submit(SessionCommand command);
bool TryGetSnapshot(out MissionSnapshot snapshot);
event Action<MissionSnapshot> SnapshotChanged;
event Action<CommandResult> CommandCompleted;
```

`Submit` envía una intención; aceptación/rechazo llegan por evento, no como un retorno síncrono inventado. Las firmas concretas de `IPowerSimulation`, constructores, namespaces y campos son las de la base real: no inventar otra variante. No usar herencia, reflection, clases parciales o métodos de extensión para alterar subrepticiamente la semántica del contrato.

### Lo único que puedes escribir

**Solo los archivos exactos de la lista permitida de tu rol, más sus `.meta` generados por Unity cuando correspondan.** No es autorización para editar toda una carpeta. Si un archivo permitido ya tiene trabajo ajeno o un tipo equivalente existe en otra ruta, no reemplazarlo ni crear un duplicado: pedir coordinación. No renombrar archivos para esquivar el límite.

Todo lo demás es de solo lectura, incluyendo archivos de otros roles, escenas `.unity`, configuración/activos compartidos, paquetes, `.asmdef`/`.asmref`, `ProjectSettings/`, `Packages/`, `.github/`, Git ignore/attributes, `AGENTS.md` y las demás notas. No instalar/actualizar SDK, paquetes, módulos, servicios o herramientas globales. No regenerar GUIDs, mover assets, aplicar formatters masivos ni hacer limpieza/refactorizaciones ajenas.

Unity puede producir cachés locales (`Library/`, `Temp/`, logs) al importar/probar; eso no autoriza versionarlas o cambiar configuración compartida. Si una herramienta altera un archivo seguido fuera de tu lista, reportarlo y excluirlo de tu entrega; no ejecutar un reset/restore/clean global que borre trabajo del usuario.

### Si necesitas algo de otro desarrollador

1. Explica el requisito en tu archivo `HANDOFF` permitido: qué falta, por qué y qué cambio mínimo propones.
2. Continúa solo con las tareas compatibles con los contratos disponibles. No simules una integración inexistente como si hubiera funcionado.
3. No crees una implementación alternativa de otro rol. Usa el fake compartido, los contratos y fixtures internos a tus tests cuando se puedan usar sin cambiar la API pública.
4. Si tu implementación no puede compilar sin una dependencia nueva, no entregues referencias rotas: marca ese punto como bloqueado y solicita autorización. Un SDK todavía no aprobado sí es una dependencia real; no prometemos independencia absoluta.

### Entrega sin unificar todavía

- Trabaja en una rama propia nacida del **mismo commit de base** que el resto. No mezcles ramas ajenas ni hagas merge/rebase/push a la rama compartida por iniciativa propia.
- Mantén los cambios listos para revisión, sin integrarlos automáticamente. Si se autorizan commits, selecciona solo tus rutas explícitas; nunca `git add .`.
- Revisa `git diff --name-only <commit-base>...HEAD` para cambios ya confirmados y también los cambios staged, unstaged y archivos nuevos. La revisión debe abarcar todo lo que entregas, no solo lo que ya está en HEAD.
- En tu HANDOFF registra commit de base, archivos creados/modificados, forma de usar tu componente, dependencias, tests realmente ejecutados/resultados, pendientes y pasos de conexión que el equipo deberá realizar después.
- No editar escenas para demostrar tu parte. Usar tests/configuración en memoria cuando la infraestructura existente lo permita. Si faltan test assemblies, no crear NUnit scripts que rompan la compilación normal: reportar el requisito sin añadir paquetes o `.asmdef`.

**La integración final ocurre después, en una tarea separada autorizada por el equipo.** Hasta entonces, “mi componente listo” no significa “juego conectado y validado”.

## Lista exacta de escritura permitida

- `Assets/_Project/Runtime/Unity/Presentation/ConnectionPanel.cs`
- `Assets/_Project/Runtime/Unity/Presentation/MissionHUD.cs`
- `Assets/_Project/Runtime/Unity/Presentation/PowerPanel.cs`
- `Assets/_Project/Runtime/Unity/Presentation/ScienceControls.cs`
- `Assets/_Project/Runtime/Unity/Presentation/ResultPanel.cs`
- `Assets/_Project/Prefabs/UI/MissionHUD.prefab`
- `Assets/_Project/Tests/PlayMode/UI/MissionHUDTests.cs`
- `docs/phase-01/handoffs/04_UI_HANDOFF.md`

## Qué haces

1. Usar el stack UI ya instalado/aprobado. No importar assets, fuentes, paquetes o dependencias nuevas. Crear la vista con elementos básicos disponibles; no es el diseño artístico final.
2. Implementar binding a `ISessionPort`: consultar el snapshot disponible, suscribirse a `SnapshotChanged`/`CommandCompleted` y enviar solicitudes mediante `Submit`. El puerto real se inyectará después; no buscar globalmente una clase concreta de otra rama.
3. Usar **el FakeSessionPort protegido que ya existe**, siguiendo su construcción/API real, para pruebas donde sea posible. No editarlo, copiarlo ni poner una batería/autoridad alternativa dentro del HUD.
4. Mostrar energía/capacidad, generación, carga esencial y ciencia solicitada/servida, tiempo/estado y trabajo científico. Usar unidades declaradas, señales con texto además de color y lectura progresiva.
5. Los clics envían `SetScienceEnabled(bool)` u otros comandos existentes. Mostrar pendiente y después aceptación/rechazo; no afirmar éxito ni descontar recursos antes de la respuesta.
6. Crear/configurar **solo tu prefab `MissionHUD.prefab`**, sin modificar escenas, otros prefabs o configuración de proyecto. Prepararlo para recibir el puerto por código en la integración posterior.
7. Añadir panel de conexión sin dependencia directa de la implementación D3: exposición/inyección local a tus componentes. Si hace falta un contrato nuevo de conexión compartido, pedirlo en HANDOFF en lugar de añadirlo a la base o editar el código de D3.
8. Limpiar listeners al deshabilitar/destruir/reiniciar la vista. Probar creación/destrucción y bindings en el entorno permitido, sin una escena nueva.

## Pruebas y límites

Un clic emite una intención; el resultado confirma o rechaza. El snapshot decide lo visible. Sin listeners dobles ni datos de una época previa, y sin presentar un valor retenido como conexión vigente.

No hay fisiología en fase 1: no decir «apagar ciencia produce oxígeno» ni mostrar muertes clínicas. El prefab y sus campos deben explicarse en HANDOFF para que otro lo conecte más tarde.

**No permitido:** crear más prefabs que el enumerado, nuevos iconos/sprites externos, scripts de movimiento, escenas de preview persistentes, navegación, inventario, mapa lunar, cambios de cámara o SDK. Si el prefab requiere un componente/asset que no existe y está fuera de tu lista, reportarlo, no fabricarlo en otra carpeta.

## Qué entregas para unificar después

Prefab y scripts propios, pasos de inyección del puerto, controles esperados, capturas/pruebas si realmente se pudieron obtener y limitaciones. No reemplazar el fake por el adaptador real tú mismo editando la escena: esa conexión se hace en la etapa de unificación autorizada.

## Resultado de fase e integración posterior

El destino conjunto es una prueba con dos clientes que observan la misma batería, una carga esencial y ciencia opcional; pueden solicitar cambios, ver sus consecuencias y obtener un resultado/reinicio coherentes. No incluye EVA, atmósfera fisiológica, radiación, cultivos, vehículos ni mundo abierto. Dos clientes son la prueba inicial, no el límite final del juego.

Cuando el equipo autorice unificar: revisar las cinco entregas; resolver peticiones al contrato en un cambio común explícito; integrar simulación y sesión, adaptador de red y prefab UI; conectar escenas una sola vez; y ejecutar validación/build con dos clientes. Si cambia una firma, publicar la nueva base y coordinar su adopción, no modificarla silenciosamente en una rama.

Las pruebas de este plan son aceptación futura. No hay implementación, compilación Unity o multiplayer verificados por el mero hecho de haber escrito estos documentos.
