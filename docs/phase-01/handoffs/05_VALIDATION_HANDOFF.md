# HANDOFF — Fase 1 (sala cooperativa de energía), D5: validación y primera build

Fecha: 2026-11-14. Sustituye al HANDOFF anterior de D5 (entrega aislada, commit `2e13231`, ya integrada en `main`).

> **Resumen honesto:** no compilé ni ejecuté C# ni Unity en esta entrega. En este equipo no hay Unity y en el
> entorno del agente no hay compilador C#. Ejecuté solo: un modelo de referencia independiente (Python),
> el análisis de decisión del escenario y una auditoría estática de GUIDs sobre git. Las pruebas integradas
> nuevas están **preparadas, no ejecutadas**. No hay prueba con red real ni build ejecutada.

## Base

| Dato | Valor |
|---|---|
| Base | `main` = `origin/main` = `0f05e38` (*documentation-addon*), encima de `6247318` (integración de las 5 ramas de fase 1) |
| Rama de trabajo | `feature/phase-01/d05-validacion`, creada desde `main` (`0f05e38`) siguiendo la convención del roadmap; publicada en `origin` con la autorización del usuario. Sin merge. |
| Otras ramas vistas | `origin/feature/phase-01/d01-simulacion` (`e1cc4d2`), `origin/feature/phase-01/d04-room-visuals` (`df0bb12`). No hay ramas `d02`, `d03` ni `d05`. |
| Escena de misión | **No existe en ninguna rama.** `Assets/_Project/Scenes/` solo tiene `.gitkeep`; Build Settings sigue con `SampleScene`. |
| SDK de red | Ninguno aprobado (`Packages/manifest.json` sin transporte). |
| Ejecución previa de mis checks | El commit `6247318` del integrador registra «D5 23 passed / 0 failed / 10 pending» en una copia aislada con Unity 6000.3.28f1. Es evidencia del integrador, no mía, y corresponde a la versión anterior de los checks. |

`main` cambió una regla de mis archivos: `ExceedsExactRange` pasó de error a warning. Lo conservo.

## Archivos de esta entrega (solo área D5)

| Archivo | Estado | Contenido |
|---|---|---|
| `Assets/_Project/Editor/Validation/PhaseOneFixtures.cs` | nuevo | 4 fixtures sintéticos con resultados esperados independientes |
| `Assets/_Project/Editor/Validation/PhaseOneIntegrationChecks.cs` | nuevo | Checks sobre `MissionSession` + `PowerSystem` reales; dos clientes por loopback a través de `NetworkCommandBridge` y `SessionConnectionController` reales; revisión de decisión del escenario |
| `Assets/_Project/Editor/Build/PhaseOneBuildChecks.cs` | modificado | Build con lista explícita de escenas de misión (rechaza SampleScene y plantillas, sin tocar Build Settings); auditoría de escena y prefabs; escaneo del fake en producción; inventario de tests |
| `docs/phase-01/handoffs/05_VALIDATION_HANDOFF.md` | modificado | Este documento |
| `docs/phase-01/handoffs/d5-evidence/*` | nuevo | `reference_model.py`, `guid_audit.py` y sus salidas reales |

Sin cambios: el validador, los dos archivos NUnit de D5, los contratos, el fake, escenas, prefabs, paquetes y ProjectSettings. No se activó ningún define ni asmdef.

## Niveles de evidencia

| Nivel | Qué significa | Estado |
|---|---|---|
| Compilación | Unity o Roslyn compilan los archivos nuevos | **Pendiente**: sin Unity ni compilador accesibles |
| Smoke (fake) | Checks contra `FakeSessionPort` | Ejecutados por el integrador en `6247318` (versión anterior); no reejecutados |
| `real-session` | `MissionSession` + `PowerSystem` reales en un proceso | **Preparado, no ejecutado** |
| `loopback` | Autoridad real + bridge + 2 controladores de cliente sobre un transporte en memoria | **Preparado, no ejecutado**. Prueba cableado y protocolo, **no** una red |
| `real-connection` | Dos procesos con el SDK aprobado | **Bloqueado**: no hay SDK ni binding |
| Build ejecutada | Build de la escena de misión, abierta en dos instancias | **Bloqueado**: no hay escena de misión ni Unity aquí |

## Fixtures y resultados esperados (SINTÉTICOS, no datos NASA)

Los esperados salen de `d5-evidence/reference_model.py`. Es un modelo por eventos con fracciones exactas, escrito desde el texto del contrato y del handoff de D2, no desde `PowerSystem`. La salida está en `reference_model_output.txt`. Unidades: J, W, s.

| Fixture | Configuración (E0, cap, gen, esencial, ciencia, carga, descarga) / reglas | Esperado clave |
|---|---|---|
| S1 `BalanceObjectiveRestart` | 18000, 36000, 50, 60, 20, 40, 40 / objetivo 60 s, duración 600 s | 60 s sin ciencia → 17400 J; +30 s con ciencia → 16500 J y 30 s de trabajo; la pausa congela todo; `Tick(40)` termina en 120 s con **ObjectiveAchieved**, 15600 J y 60 s; Restart → época 1, revisión 0, 18000 J |
| S2 `DepletionEssentialLost` | 600, 1000, 50, 60, 20, 40, 40 / 100 s, 600 s | Batería vacía a los **20 s**; la carga esencial queda servida a 50 de 60 W → **Interrupted**, 20 s de trabajo |
| S3 `PartialScienceTimeExpired` | 0, 1000, 70, 60, 20, 40, 40 / 10 s, 30 s | Ciencia servida a 10 de 20 W → **0 trabajo**; a los 30 s, `Completed` por **TimeExpired** |
| S4 `ChargeToFullCurtailment` | 900, 1000, 100, 60, 20, 30, 40 / 100 s, 600 s | La carga limitada a 30 W llena la batería a los 3,33 s; queda en 1000 J (excedente recortado); con ciencia, 10 s de trabajo |

Cada fixture compara época, revisión, estado, tiempo, energía, trabajo y potencias después de **cada** paso. Tolerancias: 1e-6 s/J y 1e-9 W. Además, el validador pasa por cada transición.

## Checks preparados

Se lanzan desde el menú `Tools > Trainer > Phase 1 > Run validation checks` o con `RunChecksFromCommandLine`. El informe queda en `Builds/Phase1Check/phase1-checks.txt`.

- **`real-session`:**
  - `Fixture_S1…S4`;
  - `RepeatedCommandAppliedOnce` (más un conflicto de ID);
  - `StaleEpochAndRestartReplay`;
  - `PauseFreezesClockAndEnergy`;
  - `FullRealStreamPassesValidator` (ticks irregulares de 0,7 s, agotamiento y reinicio).
- **`loopback` (2 clientes):**
  - `TwoClientsSyncToOneAuthority`;
  - `ActionFromOneClientHasConsequencesForBoth`: A reanuda y B activa la ciencia; los dos ven los valores de referencia de S1;
  - `DuplicateAndForeignIdOverNetwork`;
  - `RestartFromOneClientInvalidatesOldEpochForOther`;
  - `SharedPauseStopsClockForBoth`;
  - `ClientStreamsPassValidator`;
  - `LateJoinerGetsCurrentState`;
  - `HostLossLeavesResultUnknown`;
  - `TwoProcessesOverApprovedSdk`: **Pending**.
- **`design-review`:** `DecisionReview_ScenarioDefinitionDefaults`, y el mismo análisis sobre el `ScenarioDefinition` asignado en la escena. Siempre informa, nunca falla: decidir valores es tarea del producto.
- **`static-audit`:**
  - escena de misión: `MissionBootstrap` con escenario y runner asignados, un solo `SimulationRunner`, HUD, cámara, `PowerRoomView`, scripts faltantes y GUIDs sin resolver;
  - todos los prefabs de `Assets/_Project/Prefabs`;
  - `NoFakeInProductionCode`.
- **`test-inventory`:** qué tests compila Unity hoy y cuáles solo usan el fake.
- **`build`:** `MissionSceneForBuild`. `BuildFromCommandLine -phase1Scenes "<escena>"` construye **solo** escenas de misión y nunca modifica Build Settings ni cambia de target.

## Comandos realmente ejecutados y resultados

| Comando | Resultado |
|---|---|
| Copia de solo lectura del `.git` del equipo + `git log --graph --all`, `git diff --stat`, `git show` | Base y ramas arriba. Las 5 ramas de fase 1 están mergeadas en `6247318`. |
| `python3 reference_model.py` | S1–S4 como en la tabla (`reference_model_output.txt`) |
| Mismo modelo con los valores por defecto de `ScenarioDefinition` | Ciencia siempre activa → ObjectiveAchieved a 120 s con **2600 J**. Sin ciencia → TimeExpired a 300 s (estado `Completed`). Ciencia desde 200 s → TimeExpired con 100 s de trabajo. |
| `python3 guid_audit.py main` y `… d04-room-visuals` | `main`: 1 referencia, 0 sin resolver. `d04`: 25 referencias, 1 sin resolver (`Phase01Sprite.mat` → `13c02b14c4d048fa9653293d54f6e0e1`; probable shader de URP, que vive en el paquete y no en el repo; `AuditPrefabs` lo confirmará en Unity) |
| Búsqueda de Unity Hub o Editor en este PC (sesión anterior) | No encontrados |
| Búsqueda de `dotnet`, `csc` o `mono` en el entorno del agente | No hay ninguno → **sin compilación** |

## Pruebas desactivadas o solo con el fake (en `main`)

| Archivo | Estado |
|---|---|
| `Tests/EditMode/Application/MissionSessionTests.cs`, `MissionOutcomeEvaluatorTests.cs` | **Desactivadas** (`#if TRAINER_MISSION_TESTS`) |
| `Tests/EditMode/Simulation/PowerSystemTests.cs`, `SimulationClockTests.cs` | **Desactivadas** en `main` (`TRAINER_SIMULATION_TESTS`). En `feature/phase-01/d01-simulacion` D1 las movió a `Editor/` sin guard |
| `Tests/EditMode/Validation/PhaseOneScenarioValidatorTests.cs`, `Tests/PlayMode/Integration/PhaseOneIntegrationTests.cs` (D5) | **Desactivadas** (`TRAINER_PHASE1_VALIDATION_TESTS`): dependen del ensamblado Editor. Sus casos equivalentes corren sin NUnit en los checks |
| `Tests/EditMode/Application/Editor/FakeSessionPortTests.cs` | Compila; **solo fake** |
| `Tests/EditMode/Application/Editor/PhaseOneRegressionTests.cs` | Compila |
| `Tests/PlayMode/Networking/NetworkSessionAdapterTests.cs` | Compila (`UNITY_INCLUDE_TESTS`); fake + loopback de D3 |
| `Tests/PlayMode/UI/MissionHUDTests.cs` | Runner bajo `UNITY_EDITOR`; **solo fake** |

**Propuesta (sin aplicar):** mover `MissionSessionTests` y `MissionOutcomeEvaluatorTests` a `Tests/EditMode/Application/Editor/` y quitar el `#if`, igual que hizo D1. No requiere asmdef.

## Defectos y huecos reportados a sus responsables (no corregidos)

**D2 — misión y escena**

1. **No existe la escena de misión**: es bloqueante para la build, la auditoría de escena y la prueba con dos instancias. `MissionBootstrap.Compose` no se llama en ningún sitio. Tampoco hay composición de cliente (`SessionConnectionController` → `MissionHUD.Bind`).
2. **El escenario por defecto no ofrece decisión.** La generación (100 W) supera la carga esencial (60 W), así que la esencial nunca puede perderse, y dejar la ciencia siempre activa gana con 2600 J de sobra. El roadmap pide «una decisión comprobable». Propongo que el equipo elija valores donde mantener la ciencia activa arriesgue la carga esencial (por ejemplo, generación menor que la carga esencial) y que lo valide con `DecisionReview`.
3. **TimeExpired se publica como `Completed`.** El HUD muestra «Misión completada» aunque el objetivo no se cumplió (S3 y «sin ciencia»). El snapshot tampoco lleva objetivo ni motivo: es el pendiente 3 del handoff de D2, que sigue abierto. Requiere un cambio común de contrato.
4. **Snapshot obsoleto tras cambiar la ciencia sin tick.** `With()` reutiliza el `Power` anterior, de modo que al desactivar la ciencia en pausa el estado confirmado sigue mostrando la demanda científica hasta el próximo `Tick`. El validador lo señala como `ScienceRequestedWhileDisabled` (warning) y `PauseFreezesClockAndEnergy` lo anota.

**D4 — UI y sala**

5. **Legibilidad para 7–12 años**: el HUD muestra J/W en bruto, «Época/Revisión» y códigos como `SessionEnded` o `EpochMismatch`. No indica cuánto trabajo falta (depende del punto 3). Tampoco señala que la ciencia parcial no progresa.
6. Las referencias de los prefabs de `d04-room-visuals` resuelven, salvo el shader del material (probablemente del paquete URP); hay que confirmarlo con `AuditPrefabs` en Unity. `PowerRoomView` aún no está en `main`.

**D3 — red**

7. No hay binding del SDK: `real-connection` sigue bloqueado. Tampoco hay política de permisos por rol.

**Equipo**

8. Decidir la ubicación de los tests desactivados (ver arriba).

## Pasos reproducibles

1. **Rama:** `git checkout feature/phase-01/d05-validacion` (ya publicada).
2. **Compilar:** abrir el proyecto con Unity **6000.3.28f1** y comprobar que la consola no tiene errores. Si los hay en mis archivos, corregirlos en esta rama.
3. **Checks:** `Tools > Trainer > Phase 1 > Run validation checks`, o con el Editor cerrado:
   `Unity.exe -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.RunChecksFromCommandLine -logFile Logs/phase1-checks.log`
   Esperado: `real-session` y `loopback` en Passed; `real-connection`, la escena y la build en Pending. Guardar `Builds/Phase1Check/phase1-checks.txt` y copiar el resumen aquí.
4. **Tests del equipo:** `-runTests -testPlatform EditMode` y `-testPlatform PlayMode`. Guardar los XML.
5. **Build (cuando exista la escena de D2):**
   `… -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.BuildFromCommandLine -phase1Scenes "Assets/_Project/Scenes/<Mission>.unity"`
   La salida va a `Builds/Phase1Mission/<target>/`. Después, `git status` debe mostrar Build Settings y ProjectSettings sin cambios.
6. **Dos instancias (cuando exista el binding del SDK):** abrir la build dos veces (host y cliente); cada uno activa o pausa; anotar si se probó en local, LAN o Internet; probar pérdida de host, resultado y reintento.
7. **Reproducir los esperados:** `python3 docs/phase-01/handoffs/d5-evidence/reference_model.py`.

## Checklist de cierre de fase (estado actual)

- [ ] Ambos clientes ven el mismo estado confirmado: loopback preparado; red real bloqueada (SDK).
- [ ] Ambos pueden realizar acciones permitidas: loopback preparado; permisos sin definir.
- [ ] La ciencia cambia energía y trabajo: fixtures S1–S4 preparados; sin ejecutar.
- [ ] El servicio esencial sigue las reglas: S2 preparado; el escenario por defecto no lo pone en riesgo (defecto 2).
- [ ] Resultado y reinicio funcionan: preparados; la UI no distingue éxito de tiempo agotado (defecto 3).
- [ ] FakeSessionPort no es la autoridad de producción: check preparado (`NoFakeInProductionCode`).
- [ ] Escena visible e interactiva fuera del Editor: bloqueado (sin escena).

Commit y push de la rama D5 autorizados por el usuario. Sin merge ni publicación de builds.
