# Propiedad de archivos — fase 5

En esta fase **un solo ejecutor** asume D1–D5 (`FASE_05_PROMPT_UNICO_COMPLETO`). Las responsabilidades siguen siendo límites arquitectónicos: cada ruta tiene un único **área** dueña, y los cambios a archivos existentes se limitan a la lista exacta de abajo. No hay ramas por rol: preparación en `setup/phase-05/contracts`, desarrollo e integración en `feature/phase-05/complete`.

- `dir/**` cubre el directorio, su contenido y sus `.meta`. Una ruta exacta cubre ese archivo y su `.meta`.
- Prefijo omitido en tablas: `Assets/_Project/`.

## Congelado

- **Fase 5 (esta base):** `Runtime/Simulation/Contracts/Incidents/**`, `Runtime/Application/Contracts/Incidents/**` (incluye fake y fixtures), `Tests/EditMode/Phase05/Contracts/**`, `docs/phase-05/SESSION_CONTRACT.md`, `docs/phase-05/OWNERSHIP.md`, `docs/phase-05/COMMON_BASE.md`. Un cambio posterior exige actualizar implementaciones, red, pruebas y documentación a la vez, y registrarlo en `INTEGRATION.md`.
- **Fases 1–4:** todo lo que sus `OWNERSHIP.md` marcan FROZEN.

## Común (nadie lo cambia en esta fase)

`Packages/**`, `ProjectSettings/**`, `Assets/Settings/**`, `Assets/Scenes/**`, `Assets/ThirdParty/**`, `_Project/Settings/**`, `.github/**`, `.vscode/**`, `docs/roadmap/**`, `docs/research/**`, todas las escenas y escenarios de fases 1–4. Unity reescribe `ProjectSettings/*.asset` al importar: esos cambios no se suben (solo `git add -- RUTAS`). No se edita `EditorBuildSettings.asset`: el build de fase 5 pasa la escena de forma explícita.

## Por área

| Área | Responsabilidad | Código y assets nuevos | Pruebas | Modificación exacta de archivos existentes |
| --- | --- | --- | --- | --- |
| D1 | Efectos físicos reversibles y recuperación | `Runtime/Simulation/Incidents/**` | `Tests/EditMode/Phase05/Simulation/**` | `Runtime/Simulation/LifeSupport/OutpostBaseSimulation.cs` (implementa `IPowerCapacityModifiers`; sin modificadores = fase 4 idéntica), `Runtime/Simulation/LifeSupport/OutpostPowerDispatch.cs` (despacho con capacidad efectiva), `Runtime/Simulation/BaseOperations/BaseEquipmentSimulation.cs` (implementa `IEquipmentIncidentTarget`; sin llamadas = fase 4 idéntica) |
| D2 | Director, incidentes, encargos, mejora, autoridad de fase 5, escenario, composición, escena | `Runtime/Application/Incidents/**`, `Runtime/Unity/Bootstrap/Incidents/**`, `Runtime/Unity/Configuration/Incidents/**`, `Editor/Composition/Phase05/**`, `Data/Scenarios/Phase05/**`, `Scenes/Phase05_LivingBase.unity` | `Tests/EditMode/Phase05/Application/**` | — (la autoridad de fase 4 se compone sin cambios) |
| D3 | Adquisición DONKI, normalización, caché, cable `'T' 'L'`, host/cliente, interacción local | `Runtime/Unity/Integration/SpaceWeather/**`, `Runtime/Unity/Networking/Incidents/**`, `Runtime/Application/Crew/LivingBaseInteractionClient.cs`, `Runtime/Unity/Input/Incidents/**`, `tools/data-pipeline/phase05/**` | `Tests/EditMode/Phase05/Networking/**`, `Tests/PlayMode/Phase05/Networking/**` | — (los codificadores `'T' 'B'` ya son `internal` del mismo ensamblado) |
| D4 | Tarjetas de incidente, avisos, panel eléctrico, encargos, mejora, fuentes, marcas en el mundo | `Art/Phase05/**`, `Audio/Phase05/**`, `Prefabs/UI/Phase05/**`, `Prefabs/World/Phase05/**`, `Runtime/Unity/Presentation/Incidents/**` | `Tests/EditMode/Phase05/Presentation/**`, `Tests/PlayMode/Phase05/Presentation/**` | — |
| D5 | Datos congelados, validadores, build Linux, piloto (inerte sin flag), evidencia | `Assets/StreamingAssets/ScientificData/Phase05/**`, `Editor/Validation/Phase05/**`, `Editor/Build/Phase05/**`, `Runtime/Unity/Validation/Phase05/**` | `Tests/EditMode/Phase05/Validation/**`, `Tests/PlayMode/Phase05/Validation/**` | — |

Documentación: D2 `docs/phase-05/{INTEGRATION.md}`; D5 `docs/phase-05/{DATA_PROVENANCE,VALIDATION,ACCEPTANCE,HUMAN_SESSION}.md` y `docs/phase-05/integration-evidence/**`; todos los handoffs en `docs/phase-05/IMPLEMENTATION_REPORT.md` (una sección por área). D4 `docs/phase-05/art/**`.

Las pruebas EditMode van en una subcarpeta `Editor/` (ensamblado Editor predefinido, sin asmdef). Las PlayMode bajo `UNITY_INCLUDE_TESTS`, como en fases 2–4.

## Cadena de dependencias y sin doble autoridad

- D1 es el único que cambia stocks y capacidades físicas (base, potencia, equipo, repuestos). D2 nunca suma ni resta stock: pide efectos D1 con ids derivados.
- D2 es la única autoridad: director, incidentes, encargos, mejora, validación de comandos y publicación. La composición monta **un** runner y **un** reloj (el de fase 3, avanzado por tramos).
- D3 es el único transporte y el único que adquiere datos externos (herramienta del desarrollador/host, fuera del tick). El cliente nunca consulta NASA, sortea incidentes ni aplica premios.
- D4 solo lee snapshots y llama intenciones.
- D5 no altera la producción: su piloto solo envía intenciones de un jugador y solo con `-trainer-phase5-autopilot`.

## Lista legible por máquina

```ownership
FROZEN Assets/_Project/Runtime/Simulation/Contracts/Incidents/**
FROZEN Assets/_Project/Runtime/Application/Contracts/Incidents/**
FROZEN Assets/_Project/Tests/EditMode/Phase05/Contracts/**
FROZEN docs/phase-05/SESSION_CONTRACT.md
FROZEN docs/phase-05/OWNERSHIP.md
FROZEN docs/phase-05/COMMON_BASE.md
COMMON Assets/_Project/Tests/EditMode/Phase05.meta
D1 Assets/_Project/Runtime/Simulation/Incidents/**
D1 Assets/_Project/Tests/EditMode/Phase05/Simulation/**
D1 Assets/_Project/Runtime/Simulation/LifeSupport/OutpostBaseSimulation.cs
D1 Assets/_Project/Runtime/Simulation/LifeSupport/OutpostPowerDispatch.cs
D1 Assets/_Project/Runtime/Simulation/BaseOperations/BaseEquipmentSimulation.cs
D2 Assets/_Project/Runtime/Application/Incidents/**
D2 Assets/_Project/Runtime/Unity/Bootstrap/Incidents/**
D2 Assets/_Project/Runtime/Unity/Configuration/Incidents/**
D2 Assets/_Project/Editor/Composition/Phase05/**
D2 Assets/_Project/Data/Scenarios/Phase05/**
D2 Assets/_Project/Scenes/Phase05_LivingBase.unity
D2 Assets/_Project/Tests/EditMode/Phase05/Application/**
D2 docs/phase-05/INTEGRATION.md
D3 Assets/_Project/Runtime/Unity/Integration/SpaceWeather/**
D3 Assets/_Project/Runtime/Unity/Networking/Incidents/**
D3 Assets/_Project/Runtime/Application/Crew/LivingBaseInteractionClient.cs
D3 Assets/_Project/Runtime/Unity/Input/Incidents/**
D3 Assets/_Project/Tests/EditMode/Phase05/Networking/**
D3 Assets/_Project/Tests/PlayMode/Phase05/Networking/**
D3 tools/data-pipeline/phase05/**
D4 Assets/_Project/Art/Phase05/**
D4 Assets/_Project/Audio/Phase05/**
D4 Assets/_Project/Prefabs/UI/Phase05/**
D4 Assets/_Project/Prefabs/World/Phase05/**
D4 Assets/_Project/Runtime/Unity/Presentation/Incidents/**
D4 Assets/_Project/Tests/EditMode/Phase05/Presentation/**
D4 Assets/_Project/Tests/PlayMode/Phase05/Presentation/**
D4 docs/phase-05/art/**
D5 Assets/StreamingAssets/ScientificData/Phase05/**
D5 Assets/_Project/Editor/Validation/Phase05/**
D5 Assets/_Project/Editor/Build/Phase05/**
D5 Assets/_Project/Runtime/Unity/Validation/Phase05/**
D5 Assets/_Project/Tests/EditMode/Phase05/Validation/**
D5 Assets/_Project/Tests/PlayMode/Phase05/Validation/**
D5 docs/phase-05/DATA_PROVENANCE.md
D5 docs/phase-05/VALIDATION.md
D5 docs/phase-05/ACCEPTANCE.md
D5 docs/phase-05/HUMAN_SESSION.md
D5 docs/phase-05/integration-evidence/**
ALL docs/phase-05/IMPLEMENTATION_REPORT.md
```
