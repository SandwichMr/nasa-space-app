# Fase 1 — Desarrollador 3: adaptador multiplayer

**Objetivo:** Construir la conectividad y adaptación del puerto existente, sin modificar sesión, contratos, UI o proyecto global.

**Arquitectura:** `Unity -> Application -> Simulation`; contratos congelados y trabajo aislado antes de unificar.

**Tecnología:** Unity Universal 2D / C#. Usar las versiones y dependencias de la base común, sin actualizarlas.

**Rama propia:** `phase1/multiplayer-session`.

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

- `Assets/_Project/Runtime/Unity/Networking/SessionConnectionController.cs`
- `Assets/_Project/Runtime/Unity/Networking/NetworkSessionAdapter.cs`
- `Assets/_Project/Runtime/Unity/Networking/NetworkCommandBridge.cs`
- `Assets/_Project/Tests/PlayMode/Networking/NetworkSessionAdapterTests.cs`
- `docs/phase-01/handoffs/03_NETWORKING_HANDOFF.md`

## Qué haces

1. Inspeccionar los paquetes/servicios ya disponibles y la solución de red **ya aprobada** por el equipo. Si no hay SDK/target aprobado y disponible, documentar el requisito. No instalar paquetes, editar manifest/lock/settings, configurar servicios ni contratar nada.
2. Usar `ISessionPort` como frontera. El host envolverá una sesión autorizada; el cliente enviará comandos y publicará respuestas/snapshots recibidos. No crear otra física ni otra definición de mensajes de aplicación.
3. Serializar mediante DTO de transporte privados/internos dentro de tus archivos cuando el SDK lo requiera, convirtiendo desde/hacia los tipos congelados. No poner atributos de red en las clases base ni exigir que snapshots de solo lectura se vuelvan mutables.
4. Preservar IDs, épocas y revisiones; asociar identidad al remitente real. Una solicitud no puede declararse host. Los resultados vuelven por evento asíncrono, no por una respuesta síncrona fingida.
5. Separar errores/estado de conexión de los recursos de misión. No inventar eventos nuevos dentro de `ISessionPort`: reportar lo que falte o exponer detalles específicos del adaptador sin cambiar el contrato común.
6. Conservar el orden de autoridad, filtrar snapshots viejos y entregar estado inicial coherente al incorporarse. No duplicar la deduplicación de D2 con reglas incompatibles.
7. Probar transporte y conversión con el fake compartido donde su API lo permita; no modificarlo. Si faltan un harness/scene/paquetes necesarios para una conexión real, reportarlo y entregar los tests posibles sin presentar el fake como networking verificado.
8. Documentar desconexiones y configuración necesaria para la prueba final. Host perdido implica una interrupción controlada en el alcance inicial; no prometer migración/reconexión persistente.

## Validación posible sin invadir otras áreas

Tests de conversión, correlación de respuestas, descarte de estado antiguo y entrega única, solo dentro del entorno de tests ya disponible. La prueba con dos clientes reales se ejecutará cuando exista un harness permitido o tras autorizar el wiring conjunto. No crear una escena propia ni alterar la compartida para adelantar esa aceptación.

Si el proyecto usa Web, revisar compatibilidad del transporte; no asumir UDP directo. Una prueba local no prueba Internet. La latencia de red no es el retraso de comunicación Tierra-base.

## Qué entregas para unificar después

Adaptadores compilables con las dependencias ya presentes, parámetros y pasos de composición, limitaciones y pruebas efectivamente realizadas. Si la elección de SDK bloquea parte de la implementación, delimitarla honestamente en HANDOFF y pedir al equipo una actualización común de base.

D2 decidirá el wiring en la tarea posterior autorizada; D4 conectará sus controles a ese componente mediante la ruta acordada. No editar sus archivos, escenas, prefabs, bootstrap, `.asmdef`, contratos, paquetes o servicios.

## Resultado de fase e integración posterior

El destino conjunto es una prueba con dos clientes que observan la misma batería, una carga esencial y ciencia opcional; pueden solicitar cambios, ver sus consecuencias y obtener un resultado/reinicio coherentes. No incluye EVA, atmósfera fisiológica, radiación, cultivos, vehículos ni mundo abierto. Dos clientes son la prueba inicial, no el límite final del juego.

Cuando el equipo autorice unificar: revisar las cinco entregas; resolver peticiones al contrato en un cambio común explícito; integrar simulación y sesión, adaptador de red y prefab UI; conectar escenas una sola vez; y ejecutar validación/build con dos clientes. Si cambia una firma, publicar la nueva base y coordinar su adopción, no modificarla silenciosamente en una rama.

Las pruebas de este plan son aceptación futura. No hay implementación, compilación Unity o multiplayer verificados por el mero hecho de haber escrito estos documentos.
