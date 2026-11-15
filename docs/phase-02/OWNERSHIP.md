# Propiedad de archivos — fase 2

Cada ruta tiene **un solo** dueño. Una ruta `dir/**` cubre ese directorio, todo su contenido, los `.meta` de dentro y el `.meta` del propio directorio. Una ruta exacta cubre ese archivo y su `.meta`. Los `.meta` de carpetas padre ya existentes (por ejemplo `Runtime/Simulation.meta`) y los `.gitkeep` son de COMMON. Nadie edita fuera de sus rutas: una ampliación se pide como cambio coordinado.

Prefijo omitido en la tabla: `Assets/_Project/`.

## Congelado (base común)

Nadie lo modifica por su cuenta. Un cambio requiere una nueva base coordinada: rama `setup/…`, commit único y aviso a los cinco.

- Fase 2: `Runtime/Simulation/Contracts/**`, `Runtime/Application/Contracts/**` (contratos, fake y fixtures), `Tests/EditMode/Contracts/**`, `docs/phase-02/SESSION_CONTRACT.md`, `docs/phase-02/OWNERSHIP.md`, `docs/phase-02/COMMON_BASE.md`.
- Contratos de fase 1, que ya estaban congelados:
  - `Simulation/Resources/PowerConfiguration.cs`, `Simulation/State/PowerSnapshot.cs`, `Simulation/Systems/IPowerSimulation.cs`, `Simulation/Systems/IIntegratedPowerSimulation.cs`;
  - `Application/Commands/**`, `Application/Queries/**`;
  - `Application/Sessions/{ISessionPort,SessionState,FakeSessionPort}.cs`, `Tests/EditMode/Application/Editor/FakeSessionPortTests.cs`;
  - `docs/phase-01/**` (registro histórico).

## Común (solo en una actualización común aprobada, nunca en paralelo)

`Packages/**`, `ProjectSettings/**`, `Assets/Settings/**` (URP), `Assets/Scenes/**` (SampleScene de la plantilla), `Assets/StreamingAssets/**`, `Assets/ThirdParty/**`, `_Project/Settings/**`, `AGENTS.md`, `.gitignore`, `.github/**`, `.vscode/**`, `tools/**`, `FIVE_PHASE_PLAYABLE_ROADMAP.zip`, `docs/roadmap/**`, `docs/research/**` y los `.md` de la raíz de `docs/`. Carpetas reservadas para fases futuras, sin dueño en fase 2: `Simulation/{Communications,Equipment,Eva,Events,Evidence}/**`, `Unity/DataLoading/**`, `Data/{CrewProfiles,Equipment,LearningContent}/**`.

## Por desarrollador

| D | Rol fase 2 | Código y assets nuevos | Pruebas | Mantenimiento de fase 1 (solo regresiones, sin cambiar firmas congeladas) |
| --- | --- | --- | --- | --- |
| D1 | Modelos de recursos, cabina y consumos; implementa `IOutpostSimulation` | `Runtime/Simulation/LifeSupport/**`, `Runtime/Simulation/Crew/**` | `Tests/EditMode/Simulation/**` (las nuevas en `Phase02/Editor/`) | `Simulation/Systems/PowerSystem.cs`, `Simulation/State/PowerState.cs`, `Simulation/Time/**` |
| D2 | Tareas, reservas, misión; implementa `IOutpostAuthority`; composición; **único editor de escenas** | `Runtime/Simulation/Tasks/**`, `Runtime/Application/Outpost/**`, `Runtime/Unity/Bootstrap/**`, `Runtime/Unity/Configuration/**`, `Data/Scenarios/**`, `Scenes/**` | `Tests/EditMode/Application/Phase02/**`, `Tests/EditMode/Bootstrap/**` | `Application/Sessions/MissionSession.cs`, `Application/Evaluation/MissionOutcomeEvaluator.cs` (sin cambiar `MissionRules`/`MissionOutcome`), `Tests/EditMode/Application/Editor/{MissionSessionTests,MissionOutcomeEvaluatorTests,MissionScenarioTests,PhaseOneRegressionTests}.cs` |
| D3 | Identidad/presencia, entrada, movimiento y colisión (implementa `ICrewRoster`), validación de entrada, red de fase 2 | `Runtime/Application/Crew/**`, `Runtime/Unity/Input/**`, `Runtime/Unity/Networking/**` (lo nuevo en `Networking/Outpost/`) | `Tests/EditMode/Crew/**`, `Tests/PlayMode/Networking/**` | incluido en `Runtime/Unity/Networking/**` |
| D4 | Sprites, animación, astronautas, base/estaciones visibles, prefabs visuales, UI. **No** crea controlador de movimiento ni inventario | `Art/**`, `Audio/**`, `Prefabs/**`, `Runtime/Unity/Presentation/**`, `Runtime/Unity/World/**` (lo nuevo en `Presentation/Outpost/` y `World/Outpost/`) | `Tests/PlayMode/UI/**` | incluido en sus rutas |
| D5 | Fixtures independientes, validadores, pruebas de integración, herramientas de build | `Editor/**` (lo nuevo en `Editor/Validation/Phase02/` y `Editor/Build/Phase02/`, con sus fixtures propias) | `Tests/EditMode/Validation/**`, `Tests/PlayMode/Integration/**` | incluido en sus rutas |

Cada D escribe su HANDOFF en `docs/phase-02/handoffs/0N_<ROL>_HANDOFF.md`. D4 y D5 tienen además `docs/phase-02/handoffs/d4-evidence/**` y `d5-evidence/**`.

Las pruebas NUnit de EditMode van en una subcarpeta `Editor/` dentro de la ruta del dueño, por ejemplo `Tests/EditMode/Crew/Editor/`. Así usan el ensamblado Editor predefinido, sin asmdef, como en fase 1. Para crear asmdef o defines globales hace falta una actualización común aprobada.

## Geometría de colisión: una sola autoridad

| Papel | Quién | Qué |
| --- | --- | --- |
| Produce la geometría | **D2** | Autoría del plano: `Bounds`, `Obstacles`, `Stations`, `SpawnPoints` en `Data/Scenarios/Phase02/`. `Runtime/Unity/Configuration/Outpost/` lo convierte a `OutpostLayout`/`OutpostScenario`. |
| Hace cumplir la colisión | **D3** | `ICrewRoster.Advance` en la autoridad. Toda posición confirmada cumple `OutpostLayout.IsAdmissible`. Es la **única** autoridad de colisión. |
| Consume para proximidad | **D2** | `StationPlacement.IsWithinReach` sobre posiciones confirmadas. |
| Dibuja conforme al plano | **D4** | Arte y prefabs alineados con el plano, de solo lectura. Ningún `Collider2D`/`Rigidbody2D` ni física de Unity decide o corrige posiciones; los cambios de geometría se piden a D2. |
| Valida | **D5** | Puntos de aparición y alcanzabilidad de las estaciones sobre `IsAdmissible`; coherencia entre plano y arte. |

## Cadena de dependencias y sin doble autoridad

- La simulación (D1) es el único que mueve stocks y energía. La autoridad de tareas (D2) es la única que decide progreso, completado y `Apply`. El roster (D3) es el único que mueve astronautas. La presentación (D4) solo lee snapshots y envía intenciones. La validación (D5) no altera el flujo de producción.
- En integración (`integration/phase-02`) D2 conecta referencias y escenas. Los fakes salen del flujo real.

## Lista legible por máquina

Patrones: `dir/**` o una ruta exacta, relativos a la raíz del repositorio. COMMON incluye además los `.meta` de carpetas padre compartidas y los `.gitkeep`.

```ownership
FROZEN Assets/_Project/Runtime/Simulation/Contracts/**
FROZEN Assets/_Project/Runtime/Application/Contracts/**
FROZEN Assets/_Project/Tests/EditMode/Contracts/**
FROZEN docs/phase-02/SESSION_CONTRACT.md
FROZEN docs/phase-02/OWNERSHIP.md
FROZEN docs/phase-02/COMMON_BASE.md
FROZEN Assets/_Project/Runtime/Simulation/Resources/PowerConfiguration.cs
FROZEN Assets/_Project/Runtime/Simulation/State/PowerSnapshot.cs
FROZEN Assets/_Project/Runtime/Simulation/Systems/IPowerSimulation.cs
FROZEN Assets/_Project/Runtime/Simulation/Systems/IIntegratedPowerSimulation.cs
FROZEN Assets/_Project/Runtime/Application/Commands/**
FROZEN Assets/_Project/Runtime/Application/Queries/**
FROZEN Assets/_Project/Runtime/Application/Sessions/ISessionPort.cs
FROZEN Assets/_Project/Runtime/Application/Sessions/SessionState.cs
FROZEN Assets/_Project/Runtime/Application/Sessions/FakeSessionPort.cs
FROZEN Assets/_Project/Tests/EditMode/Application/Editor/FakeSessionPortTests.cs
FROZEN docs/phase-01/**
COMMON Packages/**
COMMON ProjectSettings/**
COMMON Assets/Settings/**
COMMON Assets/Scenes/**
COMMON Assets/StreamingAssets/**
COMMON Assets/ThirdParty/**
COMMON Assets/_Project/Settings/**
COMMON AGENTS.md
COMMON .gitignore
COMMON .github/**
COMMON .vscode/**
COMMON tools/**
COMMON FIVE_PHASE_PLAYABLE_ROADMAP.zip
COMMON docs/roadmap/**
COMMON docs/research/**
COMMON docs/PROJECT_BRIEF.md
COMMON docs/PROJECT_STRUCTURE.md
COMMON docs/DEVELOPMENT_PRIORITIES.md
COMMON docs/DEVELOPMENT_ROADMAP.md
COMMON docs/SCIENTIFIC_RULES.md
COMMON Assets/_Project/Runtime/Simulation/Communications/**
COMMON Assets/_Project/Runtime/Simulation/Equipment/**
COMMON Assets/_Project/Runtime/Simulation/Eva/**
COMMON Assets/_Project/Runtime/Simulation/Events/**
COMMON Assets/_Project/Runtime/Simulation/Evidence/**
COMMON Assets/_Project/Runtime/Unity/DataLoading/**
COMMON Assets/_Project/Data/CrewProfiles/**
COMMON Assets/_Project/Data/Equipment/**
COMMON Assets/_Project/Data/LearningContent/**
D1 Assets/_Project/Runtime/Simulation/LifeSupport/**
D1 Assets/_Project/Runtime/Simulation/Crew/**
D1 Assets/_Project/Runtime/Simulation/Systems/PowerSystem.cs
D1 Assets/_Project/Runtime/Simulation/State/PowerState.cs
D1 Assets/_Project/Runtime/Simulation/Time/**
D1 Assets/_Project/Tests/EditMode/Simulation/**
D1 docs/phase-02/handoffs/01_LIFE_SUPPORT_HANDOFF.md
D2 Assets/_Project/Runtime/Simulation/Tasks/**
D2 Assets/_Project/Runtime/Application/Outpost/**
D2 Assets/_Project/Runtime/Application/Sessions/MissionSession.cs
D2 Assets/_Project/Runtime/Application/Evaluation/MissionOutcomeEvaluator.cs
D2 Assets/_Project/Runtime/Unity/Bootstrap/**
D2 Assets/_Project/Runtime/Unity/Configuration/**
D2 Assets/_Project/Data/Scenarios/**
D2 Assets/_Project/Scenes/**
D2 Assets/_Project/Tests/EditMode/Application/Phase02/**
D2 Assets/_Project/Tests/EditMode/Application/Editor/MissionSessionTests.cs
D2 Assets/_Project/Tests/EditMode/Application/Editor/MissionOutcomeEvaluatorTests.cs
D2 Assets/_Project/Tests/EditMode/Application/Editor/MissionScenarioTests.cs
D2 Assets/_Project/Tests/EditMode/Application/Editor/PhaseOneRegressionTests.cs
D2 Assets/_Project/Tests/EditMode/Bootstrap/**
D2 docs/phase-02/handoffs/02_MISSION_HANDOFF.md
D3 Assets/_Project/Runtime/Application/Crew/**
D3 Assets/_Project/Runtime/Unity/Input/**
D3 Assets/_Project/Runtime/Unity/Networking/**
D3 Assets/_Project/Tests/EditMode/Crew/**
D3 Assets/_Project/Tests/PlayMode/Networking/**
D3 docs/phase-02/handoffs/03_NETWORKING_HANDOFF.md
D4 Assets/_Project/Art/**
D4 Assets/_Project/Audio/**
D4 Assets/_Project/Prefabs/**
D4 Assets/_Project/Runtime/Unity/Presentation/**
D4 Assets/_Project/Runtime/Unity/World/**
D4 Assets/_Project/Tests/PlayMode/UI/**
D4 docs/phase-02/handoffs/04_VISUALS_HANDOFF.md
D4 docs/phase-02/handoffs/d4-evidence/**
D5 Assets/_Project/Editor/**
D5 Assets/_Project/Tests/EditMode/Validation/**
D5 Assets/_Project/Tests/PlayMode/Integration/**
D5 docs/phase-02/handoffs/05_VALIDATION_HANDOFF.md
D5 docs/phase-02/handoffs/d5-evidence/**
```
