# Fase 1 — Desarrollador 5: validación y preparación de builds

**Objetivo:** Programar validadores/pruebas aisladas y preparar la validación conjunta sin modificar configuración o implementaciones ajenas.

**Arquitectura:** `Unity -> Application -> Simulation`; contratos congelados y trabajo aislado antes de unificar.

**Tecnología:** Unity Universal 2D / C#. Usar las versiones y dependencias de la base común, sin actualizarlas.

**Rama propia:** `phase1/validation-builds`.

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

- `Assets/_Project/Editor/Validation/PhaseOneScenarioValidator.cs`
- `Assets/_Project/Editor/Build/PhaseOneBuildChecks.cs`
- `Assets/_Project/Tests/EditMode/Validation/PhaseOneScenarioValidatorTests.cs`
- `Assets/_Project/Tests/PlayMode/Integration/PhaseOneIntegrationTests.cs`
- `docs/phase-01/handoffs/05_VALIDATION_HANDOFF.md`

## Qué haces

1. Inventariar **en solo lectura** Editor, target/módulos, SDK y entorno de pruebas. El equipo indica tener el mismo LTS/módulos, pero registrar las versiones reales antes de ejecutar. No instalar o cambiar nada para corregir una diferencia por tu cuenta.
2. Preparar fixtures en memoria dentro de tus tests, utilizando los constructores/tipos congelados. No crear `PhaseOneValidation.asset`, modificar el `ScenarioDefinition` de D2 ni añadir una segunda configuración de producción.
3. Elegir datos sintéticos rotulados para que exista un balance verificable; calcular expectativas con herramientas. No inventar rendimiento NASA ni copiar el algoritmo de producción como único oráculo.
4. Implementar el validador de configuración con los campos realmente disponibles: finitud, coherencia de capacidad/energía, unidades y límites representables. No añadir campos ni reglas de producto nuevas al contrato.
5. Probar los contratos/fake disponible y escribir casos de integración sin referencias directas a clases de otras ramas que todavía no existen en tu base. Inyectar implementaciones por interfaces; las pruebas que requieran los componentes reales quedan pendientes hasta unificar, sin romper la compilación del trabajo aislado.
6. Preparar checks de build solo con APIs/paquetes ya disponibles y sin cambiar escenas en Build Settings, Player Settings, renderer, paquetes o archivos globales. Si el target/build no puede ejecutarse bajo esos límites, documentar los pasos necesarios para la integración posterior.
7. Ejecutar lo posible, registrar comandos/resultados y distinguir pruebas unitarias, PlayMode, fake y dos clientes reales. No declarar validado un juego integrado porque el fake pasó tests.

## Matriz que preparas para la unificación

- Una autoridad y mismo estado publicado en dos clientes.
- Solicitudes repetidas sin doble aplicación; identidad/permisos no confiados al payload.
- Épocas/revisiones viejas sin efectos sobre el reinicio nuevo.
- Pausa compartida, reloj detenido y reset limpio.
- Límites de energía/potencia y demanda servida/no servida coherentes.
- Objetivo basado en trabajo efectivamente alimentado, no en clics o tiempo de UI.
- Mensajes claros ante conexión fallida, cliente desconectado y host perdido.
- Una build real ejecutada en las dos instancias/topología que se vayan a anunciar.

Cada caso debe decir «ejecutado», «falló» o «pendiente», con evidencia. No hace falta activar CI ni conseguir que todo pase hoy invadiendo otras ramas.

## Qué entregas para unificar después

Validadores y tests permitidos, fixtures internas, requisitos del entorno, resultados reales y checklist de integración/build en tu único HANDOFF. Cada desarrollador sigue siendo dueño de sus tests; si hallas un bug ajeno, reportarlo, no editar su archivo.

**No permitido:** cambiar `.github/`, configurar licencias, descargar módulos/SDK, crear assets compartidos, alterar Build Settings, escenas, contratos o defaults. Generar salidas de build/cachés en ubicaciones locales ignoradas cuando ya sea posible no autoriza publicar releases ni incluir esos binarios en la entrega de código.

## Resultado de fase e integración posterior

El destino conjunto es una prueba con dos clientes que observan la misma batería, una carga esencial y ciencia opcional; pueden solicitar cambios, ver sus consecuencias y obtener un resultado/reinicio coherentes. No incluye EVA, atmósfera fisiológica, radiación, cultivos, vehículos ni mundo abierto. Dos clientes son la prueba inicial, no el límite final del juego.

Cuando el equipo autorice unificar: revisar las cinco entregas; resolver peticiones al contrato en un cambio común explícito; integrar simulación y sesión, adaptador de red y prefab UI; conectar escenas una sola vez; y ejecutar validación/build con dos clientes. Si cambia una firma, publicar la nueva base y coordinar su adopción, no modificarla silenciosamente en una rama.

Las pruebas de este plan son aceptación futura. No hay implementación, compilación Unity o multiplayer verificados por el mero hecho de haber escrito estos documentos.
