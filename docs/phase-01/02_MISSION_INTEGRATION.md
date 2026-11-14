# Fase 1 — Desarrollador 2: lógica de sesión, sin integración final

**Objetivo:** Implementar MissionSession sobre los contratos congelados y dejar la composición preparada para unirla después.

**Arquitectura:** `Unity -> Application -> Simulation`; contratos congelados y trabajo aislado antes de unificar.

**Tecnología:** Unity Universal 2D / C#. Usar las versiones y dependencias de la base común, sin actualizarlas.

**Rama propia:** `phase1/mission-integration`.

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

- `Assets/_Project/Runtime/Application/Sessions/MissionSession.cs`
- `Assets/_Project/Runtime/Application/Evaluation/MissionOutcomeEvaluator.cs`
- `Assets/_Project/Runtime/Unity/Bootstrap/MissionBootstrap.cs`
- `Assets/_Project/Runtime/Unity/Bootstrap/SimulationRunner.cs`
- `Assets/_Project/Runtime/Unity/Configuration/ScenarioDefinition.cs`
- `Assets/_Project/Tests/EditMode/Application/MissionSessionTests.cs`
- `Assets/_Project/Tests/EditMode/Application/MissionOutcomeEvaluatorTests.cs`
- `docs/phase-01/handoffs/02_MISSION_HANDOFF.md`

## Cambio respecto a tu asignación anterior

**Ya no debes definir o modificar contratos ni conectar escenas durante esta entrega.** La base se creó antes de separar el trabajo y ahora es de solo lectura también para ti. Ser integrador futuro no te concede permiso para tocar hoy las otras ramas, prefabs, contratos o configuración.

## Qué haces

1. Leer los tipos reales y la documentación del contrato. Implementar `MissionSession` respetando `ISessionPort`; recibir `IPowerSimulation` por composición, sin depender de una clase concreta de D1.
2. Implementar estados/transiciones usando el enum protegido, solicitudes de ciencia y resultados asíncronos por `CommandCompleted`. Publicar snapshots por `SnapshotChanged` y mediante `TryGetSnapshot` según la base.
3. Aplicar IDs/épocas, deduplicación, rechazo de tráfico antiguo y reinicio limpio sin ampliar `SessionCommand`, `CommandResult` o `MissionSnapshot`.
4. Resolver la autorización desde un contexto de autoridad confiable compatible con la base. Si el puerto no proporciona una ruta segura para vincular el remitente a permisos, reportar el bloqueo; no confiar en un playerId o flag de host enviado por el cliente ni alterar el contrato.
5. Evaluar trabajo científico efectivamente servido. Si el snapshot físico no permite determinarlo correctamente, reportar el dato faltante; no tratar tiempo transcurrido como trabajo servido a ciegas.
6. Preparar `ScenarioDefinition`, conversión a configuración en memoria y código de bootstrap/runner sin asignarlo a una escena ni crear un asset compartido. No mutar authoring durante play ni referenciar implementaciones de otras ramas que todavía no están disponibles.
7. Probar la sesión mediante una implementación interna a tus tests de `IPowerSimulation`, con la misma firma. No modificar el fake compartido ni crear una segunda API pública.

## Pruebas de tu componente

Comandos y transiciones permitidos; resultado correlacionado al ID; repetición sin doble aplicación; épocas viejas rechazadas; reset de estado/objetivos; snapshots consistentes; suscripciones sin duplicación. Respetar la política de permisos documentada en la base; si no existe, pedir decisión antes de fijarla silenciosamente.

No se espera una escena funcional conectada durante este trabajo aislado. El bootstrap se entrega sin wiring final y su forma de uso se documenta.

## Qué entregas para unificar después

API de composición y pasos exactos para conectar la implementación D1, el adaptador D3 y el prefab D4, indicando dónde debe existir la única autoridad/runner. El equipo autorizará después la edición de escenas y la integración. No modificar ahora `SESSION_CONTRACT.md`, clases base, paquetes, archivos de D1/D3/D4/D5 o la rama compartida.

## Resultado de fase e integración posterior

El destino conjunto es una prueba con dos clientes que observan la misma batería, una carga esencial y ciencia opcional; pueden solicitar cambios, ver sus consecuencias y obtener un resultado/reinicio coherentes. No incluye EVA, atmósfera fisiológica, radiación, cultivos, vehículos ni mundo abierto. Dos clientes son la prueba inicial, no el límite final del juego.

Cuando el equipo autorice unificar: revisar las cinco entregas; resolver peticiones al contrato en un cambio común explícito; integrar simulación y sesión, adaptador de red y prefab UI; conectar escenas una sola vez; y ejecutar validación/build con dos clientes. Si cambia una firma, publicar la nueva base y coordinar su adopción, no modificarla silenciosamente en una rama.

Las pruebas de este plan son aceptación futura. No hay implementación, compilación Unity o multiplayer verificados por el mero hecho de haber escrito estos documentos.
