# Fase 1 — Desarrollador 1: núcleo de simulación

**Objetivo:** Implementar la física eléctrica mínima respetando IPowerSimulation, sin tocar la base ni otras capas.

**Arquitectura:** `Unity -> Application -> Simulation`; contratos congelados y trabajo aislado antes de unificar.

**Tecnología:** Unity Universal 2D / C#. Usar las versiones y dependencias de la base común, sin actualizarlas.

**Rama propia:** `phase1/simulation-core`.

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

- `Assets/_Project/Runtime/Simulation/Time/SimulationClock.cs`
- `Assets/_Project/Runtime/Simulation/State/PowerState.cs`
- `Assets/_Project/Runtime/Simulation/Systems/PowerSystem.cs`
- `Assets/_Project/Tests/EditMode/Simulation/PowerSystemTests.cs`
- `Assets/_Project/Tests/EditMode/Simulation/SimulationClockTests.cs`
- `docs/phase-01/handoffs/01_SIMULATION_HANDOFF.md`

## Qué haces

Tu componente implementa **la interfaz existente `IPowerSimulation`** y utiliza los tipos protegidos `PowerConfiguration`/`PowerSnapshot`. No crear ni editar esos tipos. C# puro: sin UnityEngine, SDK de red, escenas o UI.

1. Identificar en la base cómo se inicializa/restablece, avanza y consulta la simulación; conservar exactamente esas firmas y unidades.
2. Escribir tests permitidos si la infraestructura existe. Usar segundos, joules y watts; datos sintéticos identificados y resultados calculados con herramientas.
3. Implementar reloj y balance: generación, batería, carga esencial prioritaria y ciencia opcional, según lo expresable en la configuración congelada.
4. Aplicar límites de potencia/energía y tratar agotamiento/carga completa dentro del intervalo. Registrar con los campos existentes lo que de verdad se sirve; no esconder energía negativa con clamps.
5. Mantener estado mutable interno y publicar snapshots sin referencias mutables expuestas. El tiempo avanza por solicitud explícita, no por FPS o un MonoBehaviour.
6. Comprobar reset y repetibilidad. Si la base no permite expresar un dato imprescindible —pérdidas, servicio parcial o tiempo servido—, documentar la limitación en HANDOFF; no añadir campos ni adjudicar valores implícitos.

## Pruebas de tu componente

- Balance energético analítico y límites de capacidad/potencia.
- Sin energía gratuita ni doble consumo; la carga esencial y ciencia no se confunden.
- Avance inválido rechazado; llamadas sin avance no consumen recursos.
- Reset reproduce condiciones iniciales sin mutar configuración.
- Refinamiento de paso compatible con tolerancias justificadas.

Son pruebas de modelo, no evidencia de rendimiento lunar real. No simular O2/CO2 ni un motor universal de todos los recursos.

## Qué reciben los demás

D2 recibirá una implementación construible de `IPowerSimulation`, sus requisitos y la evidencia de tests. No conectes tu componente a MissionSession, bootstrap o escena: describe esa conexión en HANDOFF. D3/D4 no deben leer tu estado mutable; usarán el puerto de sesión.

## Resultado de fase e integración posterior

El destino conjunto es una prueba con dos clientes que observan la misma batería, una carga esencial y ciencia opcional; pueden solicitar cambios, ver sus consecuencias y obtener un resultado/reinicio coherentes. No incluye EVA, atmósfera fisiológica, radiación, cultivos, vehículos ni mundo abierto. Dos clientes son la prueba inicial, no el límite final del juego.

Cuando el equipo autorice unificar: revisar las cinco entregas; resolver peticiones al contrato en un cambio común explícito; integrar simulación y sesión, adaptador de red y prefab UI; conectar escenas una sola vez; y ejecutar validación/build con dos clientes. Si cambia una firma, publicar la nueva base y coordinar su adopción, no modificarla silenciosamente en una rama.

Las pruebas de este plan son aceptación futura. No hay implementación, compilación Unity o multiplayer verificados por el mero hecho de haber escrito estos documentos.
