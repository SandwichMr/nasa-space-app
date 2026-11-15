# Handoff D5 — validación independiente, continuidad y builds (fase 4)

**Estado: WIP publicado para integración. NO acepta la fase.** El tooling, los fixtures, los validadores, el piloto y las pruebas están programados contra la base común de D2. Ninguna implementación de D1, D2, D3 ni D4 de fase 4 estaba publicada al escribir esto, así que todo lo que depende de ellas sale **Pending** (EditMode: *Inconclusive*), nunca *Passed*.

## Git

| Dato | Valor |
| --- | --- |
| `origin/main` actualizado | `1ca26923ad7d29cfe7ebdbb2969c331a8875c4e0` |
| Base común D2 (`origin/setup/phase-04-shared-contracts`) | `9b96f80ee8102073152d9e51cad4beb5a353aeb5` |
| Verificación | `git merge-base --is-ancestor 1ca2692… 9b96f80…` → sí |
| Rama | `feature/phase-04/d05-validation`, creada desde `9b96f80…` |
| Ramas de fase 4 de D1, D2, D3 y D4 en `origin` | ninguna al preparar esta entrega |

La base congelada no se tocó.

**Diferencia pendiente de coordinación:** `OWNERSHIP.md` todavía nombra el rol combinado «D3+D5» y la rama `feature/phase-04/d03-d05-continuity`. Esta entrega es solo D5, en `feature/phase-04/d05-validation`, y toca únicamente rutas D5. D2 debe publicar el OWNERSHIP con D3 y D5 separados.

## Lista de escritura (solo rutas D5 de OWNERSHIP)

```
Assets/_Project/Runtime/Unity/Validation/Phase04/**      (+ Phase04.meta)
  Phase04Navigator.cs               planificador de rejilla (escotillas cerradas). Solo propone direcciones
  BaseOperationsActors.cs           IBaseOperationsActor + PortIntentsActor (puerto + intenciones de D3)
  BaseOperationsContinuityScript.cs piloto de continuidad por roles (agua; aire y logística)
  BaseOperationsAutopilot.cs        MonoBehaviour INERTE sin -trainer-autopilot continuity
Assets/_Project/Editor/Validation/Phase04/**              (+ Phase04.meta)
  Phase04Check.cs                   estados Passed/Failed/Pending/Info, tipos de evidencia separados, códigos de salida 0/1/2
  Phase04ImplementationDiscovery.cs descubrimiento de implementaciones REALES + resolutor de constructores
  Phase04Spies.cs                   espías de registro (modelo base y equipo): reenvían y registran, no deciden
  BaseOperationsScenarioValidator.cs validador del escenario completo (fase 3 + reglas de fase 4)
  BaseOperationsStreamValidator.cs  invariantes de continuidad sobre el flujo de snapshots
  BaseEquipmentConformance.cs       conformidad del modelo de equipo D1 y del acoplamiento de aire
  Phase04Session.cs                 sesión en memoria sobre la autoridad real (AuthorityActor)
  BaseOperationsAuthorityConformance.cs suite integral en memoria + carrera por puertos locales de D3
  Phase04Validation.cs              punto de entrada de las puertas gestionadas
Assets/_Project/Editor/Build/Phase04/**                   (+ Phase04.meta)
  Phase04BuildChecks.cs             puertas, auditoría de escena, build con escenas explícitas y ejecución de la build
Assets/_Project/Tests/EditMode/Phase04/Validation/**      (+ Validation.meta, Editor/)
  Phase04ValidationToolingTests.cs  el tooling detecta lo que dice detectar (fixture/fake)
  Phase04IntegratedConformanceTests.cs conformidad real (Failed → fallo; Pending → Inconclusive)
Assets/_Project/Tests/PlayMode/Integration/Phase04/**     (+ Phase04.meta)
  Phase04SceneIntegrationTests.cs   escena productiva en Play Mode + prefabs D4 con lecturas desconocidas
docs/phase-04/handoffs/05_CONTINUITY_VALIDATION_HANDOFF.md
```

No se escribe en `ProjectSettings/`, `Packages/`, escenas, prefabs, red, simulación, autoridad ni presentación. El piloto lo coloca **D2**, que es el único editor de escenas.

## Qué cubre la suite (requisito → dónde)

| Requisito del encargo | Implementación | Tipo de evidencia |
| --- | --- | --- |
| Operación estable y balances de energía y masa por el flujo compuesto real | `StableOperation` + `Balances` (pares de id equipo↔base, ningún id aplicado dos veces, el agua potable y la comida no suben dentro de `Advance`, libro mayor cerrado) | autoridad en memoria (D2+D1) |
| Reserva baja por consumo real, reposición y recuperación | `ReserveAndResupply`: la comida no sube con el pedido sino al descargar | autoridad en memoria |
| Avería del reciclador: diagnóstico, aislamiento, repuesto, prueba y restitución sin recuperar lo perdido | `RecyclerRepair` + conformidad D1 (salmuera no reembolsada) | autoridad en memoria + D1 |
| Dos interacciones con decisiones distintas y resultados confirmados; rechazo de un éxito inventado por el cliente | `Interactions`: 8 comandos forjados o fuera de orden se rechazan sin efecto; aceptar ≠ reprocesar; cambiar lecho ≠ regenerar | autoridad en memoria |
| Varias tareas y pedidos completados y cancelados sin cerrar la sesión ni reiniciar | `Activities` + invariante de flujo «continuity» | autoridad en memoria |
| Expedición EVA completada o cancelada dentro de la sesión continua | **Pending**: falta el guion de salida por esclusa hasta que D2 publique el cierre `→ Preparing` | — |
| Dos usuarios compitiendo por equipo o repuesto; comandos repetidos | `Competition`: `StationOccupied`, repetición y conflicto de id, reserva exclusiva, la cancelación libera el repuesto | autoridad en memoria |
| Pausa, desconexión durante reparación, incorporación tardía, cambio de época | `PauseLeaveJoinRestart` + invariantes «pause» y «restart» | autoridad en memoria |
| Consumo interior/EVA único, sin doble modelo ni runner | Descubrimiento (un modelo base, un roster, un EVA); `SingleConsumption` (Σdt base = Σdt equipo = reloj); auditoría de escena (un runner) | descubrimiento + memoria + escena |
| Regresión de fases anteriores y modos finitos | `Regression()` (descubrimiento de fase 2, plan del fixture de fase 3) + suites nativas de fases 2 y 3 (abajo) | regresión |
| Lecturas antiguas o desconocidas que la UI no muestra como cero o seguro | Invariante «readings»; `UnknownReadings`; PlayMode: prefabs D4 con CO2/O2 `Unavailable` sin errores ni intenciones | memoria + PlayMode; **lo que se ve en pantalla queda pendiente** de la evidencia de D4 o de una persona |
| Validadores que descubren implementaciones reales y separan tooling Editor de runtime | `Phase04ImplementationDiscovery`: una implementación de contrato fuera del ensamblado del player es **Failed** | descubrimiento |
| Sin fakes en producción | Auditoría de escena (componentes `Fake`/`Development`) + `ScanProductionFakeUsage` (fuentes del player) | escena |

Las carreras por puertos locales de D3 (`RunThroughLocalPorts`, tipo `ports-in-memory-D3`) ejecutan los mismos pilotos a través de `IBaseOperationsSessionPort` + `IBaseOperationsIntents`. **No son sockets.**

## Cómo encuentra el descubrimiento las implementaciones (sin depender de nombres de clase)

- **D1**: un único `IBaseEquipmentModel`. `IAirServiceCapacity` debe estar en el **mismo** tipo que el único `IOutpostSimulation`.
- **D2**: un único `IBaseOperationsAuthority`, que no implemente `IOutpostAuthority` ni `IExpeditionAuthority`. Se usa el constructor público con más parámetros resolubles y el tick `public void Tick|Advance|Step(double)`.
- **Resolutor de argumentos**:
  - escenario (fase 4, 3 o 2), `OutpostLayout`, `CrewMovementSettings`, `OutpostConfiguration`, `BaseEquipmentConfiguration`;
  - parámetros con valor por defecto;
  - el único tipo de producción asignable, construido con constructor vacío, `static For(escenario)`, `(OutpostLayout, CrewMovementSettings)` o `(Parámetros)` con su fábrica estática única (o `SyntheticTraining()`).
- **Espías**: si el constructor recibe las interfaces `IOutpostSimulation` e `IBaseEquipmentModel`, se pasan espías de registro alrededor de los objetos reales. Si recibe tipos concretos, los balances usan solo snapshots y el informe lo indica.
- **D3**: puerto local = `IBaseOperationsSessionPort` con constructor `(IBaseOperationsAuthority)` y `Join()` público. Intenciones = `IBaseOperationsIntents` con constructor `(puerto[, BaseOperationsScenario])`.
- **D4**: `IBaseOperationsView` fuera del espacio de nombres `Provisional`.

Si D2 o D3 eligen otra forma, el check sale **Pending** con el motivo. Se amplía el resolutor en integración; no se adivina.

## Orden de puertas y comandos (desde la raíz del repo, con Unity 6000.3.28f1)

**No lances Unity CLI si el proyecto está abierto en el Editor.** Si una puerta falla, se registra y las siguientes no se certifican.

```sh
# 1. Importación y compilación (la primera compilación real de este código: aquí no había compilador)
Unity -batchmode -nographics -projectPath . -quit -logFile Logs/phase04/import.log
# 2. EditMode (rápido; excluye las corridas largas)
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testFilter "Phase04" -testCategory "!Phase04Long" \
  -testResults Logs/phase04/editmode.xml -logFile Logs/phase04/editmode.log
#    corridas largas en memoria (15 min simulados por prueba)
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testCategory "Phase04Long" \
  -testResults Logs/phase04/editmode-long.xml -logFile Logs/phase04/editmode-long.log
#    regresión de fases 1–3 (suites completas)
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/phase04/editmode-all.xml -logFile Logs/phase04/editmode-all.log
# 3. Validadores (0 pasa, 1 falla, 2 sin fallos pero con pendientes)
Unity -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunChecksFromCommandLine -logFile Logs/phase04/d5-checks.log
#    y las de fases 2 y 3: Phase02BuildChecks / Phase03BuildChecks.RunChecksFromCommandLine
# 4. PlayMode
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults Logs/phase04/playmode.xml -logFile Logs/phase04/playmode.log
# 5. Build (escena explícita; no toca EditorBuildSettings; se niega si una puerta rápida falla)
Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.BuildFromCommandLine -logFile Logs/phase04/d5-build.log
# 6. Ejecución de ESA build: autoridad + 2 clientes con el piloto, 30 minutos REALES
Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunBuildFromCommandLine -phase4Minutes 30 -phase4Port 7795 -logFile Logs/phase04/d5-run.log
```

En el Editor también están los menús `Tools/Trainer/Phase 4/Run D5 checks (quick)`, `(full, long)` y `Build living base scene`.

**Salidas:**

- `Logs/phase04/d5-checks.txt`, `d5-build-gates.txt`, `run-summary.txt` y `run-<utc>/{authority,water,air}.log`;
- `Builds/Phase4LivingBase/<target>/build-info.txt`: commit, árbol limpio o modificado, editor, target, escenas, GUID del escenario, estado de las puertas, resultado y hora UTC.

**Ejecución manual de la build** (mismos argumentos de red que en las fases 1–3):

```sh
P=Builds/Phase4LivingBase/StandaloneWindows64/Phase4LivingBase.exe
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7795 -logFile Logs/phase04/authority.log
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect \
   -trainer-autopilot continuity -trainer-autopilot-role water -trainer-autopilot-minutes 30 -logFile Logs/phase04/water.log
$P ... -trainer-autopilot-role air ...      # segundo cliente
# Cada cliente termina con "[Autopilot] RESULT PASS|FAIL … real=…s" y exit 0/1, con estado cada 60 s reales.
```

Tiempo real y tiempo simulado se registran por separado: `real=` es el reloj de pared del proceso y `sim=` el reloj de la autoridad.

## Matriz de resultados de esta entrega

| Puerta | Resultado | Nota |
| --- | --- | --- |
| Compilación C# de estos archivos | **No ejecutada** | Este entorno no tenía .NET, Mono ni Unity, y la red estaba cerrada para instalarlos. Hice una revisión estática línea a línea contra las firmas reales de la base. La primera compilación real es la puerta 1. |
| EditMode `Phase04ValidationToolingTests` (fixture/fake) | **No ejecutada** | Ídem |
| EditMode `Phase04IntegratedConformanceTests` | **No ejecutada** | Hoy debería dar *Inconclusive*: faltan D1, D2 y D3 |
| Validadores `RunChecksFromCommandLine` | **No ejecutado** | Hoy debería salir con código 2 (Pending) si el tooling compila |
| PlayMode, escena productiva | **No ejecutada** | Falta la escena de D2 (*Inconclusive*) |
| Build y ejecución de dos procesos | **No ejecutada** | Falta la escena de D2 |
| 30 minutos reales con dos personas | **Pendiente** | Receta abajo. No se inventan participantes. |

No se marcó nada como *Passed* sin ejecutarlo.

## Partida con personas (pendiente; receta)

1. Haz la build con las puertas 1 a 5 en verde.
2. Usa dos equipos o dos ventanas: una autoridad y dos clientes sin `-trainer-autopilot`, con gráficos y teclado.
3. Jueguen al menos **30 minutos reales**: varios ciclos de agua, la avería del reciclador (a los 120 s simulados en el fixture) diagnosticada y reparada, un pedido descargado y el servicio de aire.
4. Anoten:
   - hora de inicio y fin;
   - tiempo simulado final (el del HUD o del log de la autoridad);
   - incidencias;
   - si las barras «sin dato» se distinguieron de 0 o de «normal»;
   - si entendieron la decisión aceptar o reprocesar.
5. Guarden logs y capturas en `docs/phase-04/handoffs/d5-evidence/`.

Un piloto automático **no** prueba diversión, comprensión, render ni teclado humano.

## Defectos y peticiones a propietarios

Ninguno confirmado: no había implementaciones que ejecutar. Hay dos puntos que el piloto y la suite exigen y que el contrato deja abiertos. Los pido explícitos a D2:

1. **Observación vigente tras `Isolate`.** `Intervene` y `Restore` exigen una observación vigente, pero `RepairSequence` prohíbe `Inspect` mientras la máquina está aislada. Si `Isolate` cambia el `StateSequence` sin publicar una observación nueva, la reparación queda imposible. `RecyclerRepair` lo marcará **Failed** con ese motivo.
2. **Cierre de la salida EVA en modo continuo.** Hace falta saber cómo una salida vuelve a `Preparing` para escribir el guion de expedición completada o cancelada (hoy **Pending**).

A D3: la forma del puerto local `(IBaseOperationsAuthority)` + `Join()` y del cliente de intenciones `(port[, scenario])`. Si es otra, avisad para ampliar el resolutor.

## Dependencias pendientes de integración

- D1: `IBaseEquipmentModel`, `IAirServiceCapacity` en `OutpostBaseSimulation`.
- D2: autoridad continua con tick, asset del escenario, escena `Phase04_LivingBase.unity` con un `BaseOperationsAutopilot` colocado y la receta de conexión.
- D3: host y cliente `'T' 'B'`, puerto local e intenciones.
- D4: vistas `IBaseOperationsView` y prefabs bajo `Prefabs/UI/Phase04` y `Prefabs/World/Phase04`.
- Coordinador: integración en `integration/phase-04`, ejecución de las puertas en orden y revisión. **Publicar este tooling no acepta la fase.**
