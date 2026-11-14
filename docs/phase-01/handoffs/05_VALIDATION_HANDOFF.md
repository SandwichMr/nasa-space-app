# HANDOFF — Fase 1, Desarrollador 5: validación y builds

Fecha: 2026-11-14. Estado: **código entregado sin compilar ni ejecutar** (ver «Bloqueos»).

## Base y rama

| Dato | Valor comprobado |
|---|---|
| Commit base | `4850e1d128ca1c1666d6cdd13e0a4de05d4f6687` (= `main` local = `origin/main`) |
| Rama actual | `phase01/VALIDATION_AND_BUILDS`, creada desde `main` en ese commit (`.git/logs/HEAD`) |
| Rama indicada en `05_VALIDATION_AND_BUILDS.md` | `phase1/validation-builds` → **el nombre no coincide**; el equipo decide si se renombra (no lo hice) |
| Otras ramas remotas vistas | `origin/phase01/multiplayer-session` (= base), `origin/phase1/mission-integration` (`83604bc`, distinto; no inspeccionado) |
| Base compartida | Existen los 9 tipos congelados con las firmas de `SESSION_CONTRACT.md` y `FakeSessionPortTests.cs` |
| Editor del proyecto | `ProjectSettings/ProjectVersion.txt`: `6000.3.28f1 (8f082d2fe476)` |
| Paquetes relevantes | `com.unity.test-framework` 1.6.0 (NUnit `com.unity.ext.nunit` 2.0.5 en lock); sin paquete de red |
| Build Settings | Solo `Assets/Scenes/SampleScene.unity` (plantilla) |
| `.asmdef` en `Assets/_Project` | Ninguno |

La rama y la base se comprobaron leyendo `.git/HEAD`, `.git/logs/HEAD`, `refs` y `packed-refs`; no había un shell de git disponible.

## Archivos entregados (solo la lista permitida)

| Archivo | Ensamblado actual | Contenido |
|---|---|---|
| `Assets/_Project/Editor/Validation/PhaseOneScenarioValidator.cs` | Assembly-CSharp-Editor | Validador C# puro (`Trainer.Tooling.Validation`) |
| `Assets/_Project/Editor/Build/PhaseOneBuildChecks.cs` | Assembly-CSharp-Editor | Inventario de entorno, batería ejecutable sin NUnit, build opcional (`Trainer.Tooling.BuildChecks`) |
| `Assets/_Project/Tests/EditMode/Validation/PhaseOneScenarioValidatorTests.cs` | **Excluido de la compilación** (`#if`) | Tests NUnit del validador |
| `Assets/_Project/Tests/PlayMode/Integration/PhaseOneIntegrationTests.cs` | **Excluido de la compilación** (`#if`) | Matriz de integración sobre `ISessionPort`; los casos reales quedan en `Ignore` |
| `docs/phase-01/handoffs/05_VALIDATION_HANDOFF.md` | — | Este documento |

No se modificó ningún otro archivo. Unity generará los `.meta` de los `.cs` y de las carpetas nuevas `Tests/EditMode/Validation` y `Tests/PlayMode/Integration` al importar; son los únicos `.meta` aceptables en esta entrega.

## Uso

### Validador (`PhaseOneScenarioValidator`)
Todas las funciones devuelven un `ValidationReport` (`Issues`, `HasErrors`, `Contains(code)`).
- **Error**: contradice el contrato o la contabilidad física.
- **Warning**: diagnóstico. No es una regla de producto nueva.

| Función | Comprueba |
|---|---|
| `ValidateConfiguration(cfg)` | Valores finitos y no negativos; ≤ 2^53 (`ExceedsExactRange`); energía inicial ≤ capacidad. Warnings: `PossibleUnitMismatch` (capacidad < 1 s de la mayor potencia, por ejemplo Wh tecleados como J), `ZeroCapacity`, `ChargingDisabled`, `DischargeDisabled`, `EssentialNotFullyServable`, `ScienceNotFullyServable` |
| `ValidatePowerSnapshot(s, cfg?)` | Energía ≤ capacidad; servido ≤ solicitado; servido ≤ generación + descarga máxima; capacidad igual a la configurada |
| `ValidatePowerTransition(a, b, cfg)` | Tiempo no retrocede; capacidad constante; ganancia ≤ min(gen, carga máx.)·Δt; pérdida ≤ min(descarga máx., esencial + ciencia)·Δt. **Supone que no hay pérdidas**, porque el contrato no declara eficiencias |
| `ValidateMissionSnapshot(m, cfg?)` | Época y revisión ≥ 0, estado definido, trabajo científico finito |
| `ValidateMissionTransition(a, b, cfg?)` | `EpochRegressed`, `RevisionRegressed`, `RevisionReusedWithDifferentContent`, `ClockAdvancedWhileNotRunning`, `ScienceWorkDecreased`, `ScienceWorkExceedsElapsedTime`, `ScienceWorkWithoutServedPower`, `RestartNotClean` (revisión 0 de una nueva época) |
| `ValidateCommand`, `ValidateCommandResult`, `ValidateReplay` | Forma del comando con los códigos del contrato, correlación ID/época y que el replay sea idéntico |

### Checks ejecutables sin NUnit
- **En el Editor**: `Tools > Trainer > Phase 1 > Run validation checks`. Escribe `Builds/Phase1Check/phase1-checks.txt`; `/Builds/` está en `.gitignore`.
- **En modo batch** (con el proyecto cerrado en otros Editores):
  ```
  "<Unity 6000.3.28f1>\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\Melissa\Documents\GitHub\nasa-space-app" -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.RunChecksFromCommandLine -logFile Logs\phase1-checks.log
  ```
  Código de salida: 0 si ningún check está en `Failed`, 1 en caso contrario.
- **Build opcional**, solo con el target activo y las escenas ya habilitadas (no cambia la configuración): `Tools > Trainer > Phase 1 > Build active target with current settings`, o `-executeMethod ...PhaseOneBuildChecks.BuildFromCommandLine`. La salida va a `Builds/Phase1Check/<target>/`. Solo prueba la pipeline sobre `SampleScene`, no el juego.
- **Tests NUnit existentes** (los de la base):
  ```
  Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform EditMode -testResults Logs\editmode-results.xml -logFile Logs\editmode.log
  ```

### Fixture sintético (no son datos NASA)
`PowerConfiguration(18000 J, 36000 J, gen 50 W, esencial 60 W, ciencia 20 W, carga máx. 40 W, descarga máx. 40 W)`. Las expectativas se calcularon con Python, fuera del código de producción:
- 10 W de déficit durante 60 s → 17400 J.
- 30 W de déficit durante 60 s → 15600 J.
- Cotas de 60 s: 2400 J de ganancia y 2400 J de pérdida.

## Comandos ejecutados realmente

| Comando / acción | Resultado |
|---|---|
| Lectura de `.git/HEAD`, `logs/HEAD`, `refs/heads/*`, `packed-refs` | Rama y base confirmadas (arriba) |
| Lectura de contratos, `FakeSessionPort`, `FakeSessionPortTests`, `manifest.json`, `packages-lock.json`, `ProjectVersion.txt`, `EditorBuildSettings.asset`, `.gitignore` | Inventario de arriba |
| `python3` para calcular las expectativas del fixture | 17400 / 15600 J; cotas de 2400 J |
| Búsqueda de compilador C# (`mono`, `mcs`, `csc`, `dotnet`) en el entorno del agente | No hay ninguno → **no se compiló** |
| Búsqueda de Unity Hub / Unity Editor en el PC (resolución de aplicaciones) | No encontrados → **no se ejecutó Unity** |

**No hay resultados de compilación, tests ni builds.** Todo el código se revisó solo de forma estática.

## Matriz para la unificación

| Caso | Estado | Evidencia / requisito |
|---|---|---|
| Validador sobre el fixture sintético (configuración, balance, cotas, pausa, épocas, reinicio) | **Pendiente de ejecutar** | `RunValidatorChecks()`; necesita Unity |
| Fake: replay sin doble aplicación, ID en conflicto, época vieja/futura, replay de Restart, pausa/reanudar, reinicio limpio, stream monótono, códigos de comandos malformados, correlación | **Pendiente de ejecutar** | `RunFakeSessionChecks()`; necesita Unity. Solo prueba el fake |
| Una autoridad y mismo estado en dos clientes | Pendiente | Dev 3 + backend de red aprobado |
| Identidad/permisos no confiados al payload | Pendiente | Autoridad real |
| Pausa compartida con reloj detenido | Pendiente | El fake nunca avanza el tiempo; hacen falta autoridad y simulación |
| Límites de energía, demanda servida/no servida | Pendiente | `IPowerSimulation` de Dev 1; caso `Real_PowerSimulationRespectsEnergyBounds` preparado |
| Objetivo basado en trabajo alimentado | Pendiente | Evaluación de Dev 2 |
| Mensajes ante conexión fallida, desconexión o host perdido | Pendiente | Dev 3 + Dev 4 |
| Build real en dos instancias | Pendiente | Escena integrada en Build Settings + topología elegida |

## Bloqueos y hallazgos (no corregidos: son de otros o requieren autorización)

1. **No hay Unity accesible en este PC** (ni Hub ni Editor detectados). Hace falta Unity 6000.3.28f1 con su módulo de target para ejecutar cualquier cosa.
2. **Ensamblados de test.** Las rutas permitidas para los tests NUnit no están bajo una carpeta `Editor` y no existe ningún `.asmdef`. Sin protección caerían en Assembly-CSharp, que no tiene NUnit ni ve el código Editor, y romperían la compilación del juego. Por eso están envueltas en `#if TRAINER_PHASE1_VALIDATION_TESTS && UNITY_INCLUDE_TESTS`.
   - Para activarlas, la integración debe crear asmdefs para runtime (contratos), para `Editor/Validation` (solo Editor) y de test (EditMode y PlayMode) que referencien ambos, y luego definir el símbolo.
   - Un asmdef no puede referenciar Assembly-CSharp ni Assembly-CSharp-Editor; por eso el cambio debe ser explícito y común. Mientras tanto, los mismos casos corren sin NUnit desde `PhaseOneBuildChecks`.
3. **Nombre de rama** distinto del documentado (ver la tabla de arriba). Los prefijos remotos también son inconsistentes (`phase01/` y `phase1/`).
4. **Observación sobre `FakeSessionPort`** (no es un bug del contrato y no lo toqué): un comando con `Guid.Empty` queda registrado en el diccionario. Si se reenvía `Guid.Empty` con otro contenido, se responde `CommandIdConflict` en vez de `InvalidCommandId`. El dueño decide si importa.
5. **`PublishExample` crea trabajo científico sin tiempo simulado.** El validador lo marca como `ScienceWorkExceedsElapsedTime` (check `ValidatorDetectsScriptedFakeWorkJump`). Es lo esperado y confirma que los fixtures del fake no son físicos.
6. **Huecos del contrato que conviene cerrar en la integración:**
   - ¿Hay pérdidas o eficiencia de batería? Las cotas actuales suponen que no.
   - ¿`ScienceRequestedWatts` debe ser 0 con la ciencia desactivada? Hoy solo es un warning.
   - ¿Es obligatoria la revisión 0 tras un Restart en la autoridad real?

## Checklist para la integración futura

- [ ] Instalar o confirmar Unity `6000.3.28f1` y su módulo de target; registrar `Application.unityVersion` real.
- [ ] Abrir el proyecto; confirmar que compila sin errores y que solo aparecen `.meta` de las rutas de esta entrega (`git status`).
- [ ] Ejecutar `Run validation checks`; guardar `Builds/Phase1Check/phase1-checks.txt`. Esperado: `environment`, `validator-synthetic` y `fake` en Passed; los `real-pending` en Pending.
- [ ] Ejecutar los tests EditMode existentes (`-runTests -testPlatform EditMode`) y guardar el XML.
- [ ] Ejecutar opcionalmente `BuildFromCommandLine` y después comprobar con `git status` que no cambió ningún archivo seguido.
- [ ] Acordar en un cambio común la estructura de asmdefs (bloqueo 2) y luego definir `TRAINER_PHASE1_VALIDATION_TESTS`.
- [ ] Rellenar `PhaseOneIntegrationTests.ConnectedClientsFactory` (Dev 3) y `PowerSimulationFactory` (Dev 1); escribir la espera asíncrona del transporte.
- [ ] Pasar `ValidateMissionTransition` sobre el stream de cada cliente en la prueba de dos clientes y comparar época/revisión entre ellos.
- [ ] Acordar con Dev 1 la expectativa de 15600 J a 120 s (o la nueva si el modelo declara pérdidas).
- [ ] Conectar la escena de fase 1 en Build Settings (tarea de integración) y repetir la build en las dos instancias anunciadas.

No se hizo commit, push, merge ni publicación de builds.
