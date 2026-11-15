# Propiedad de archivos — fase 3

Cada ruta tiene **un solo** dueño. `dir/**` cubre el directorio, su contenido, los `.meta` de dentro y el `.meta` del directorio; una ruta exacta cubre ese archivo y su `.meta`. Los `.meta` de carpetas padre ya existentes y los `.gitkeep` son de COMMON. Nadie edita fuera de sus rutas; una ampliación se pide como cambio coordinado.

En esta fase **D1, D2, D3 y D5 los asume una misma tarea técnica** (rama `feature/phase-03/technical-core`). D4 trabaja aparte (arte y presentación). Las rutas siguen separadas por rol, para que D4 sepa exactamente qué es suyo y a qué se conecta.

Prefijo omitido en las tablas: `Assets/_Project/`.

## Congelado

- **Fase 3 (esta base)**: `Runtime/Simulation/Contracts/Eva/**`, `Runtime/Application/Contracts/Expedition/**` (contratos, puntos de montaje de D4, fake y fixtures), `Tests/EditMode/Phase03/Contracts/**`, `docs/phase-03/SESSION_CONTRACT.md`, `docs/phase-03/OWNERSHIP.md`, `docs/phase-03/COMMON_BASE.md`.
- **Fase 2 y fase 1**: todo lo que `docs/phase-02/OWNERSHIP.md` marca FROZEN sigue congelado.

Un cambio de lo congelado es una **nueva base coordinada**: un commit propio, avisado con su SHA completo antes de que nadie lo use.

## Común

Igual que en fase 2: `Packages/**`, `ProjectSettings/**`, `Assets/Settings/**` (URP), `Assets/Scenes/**`, `Assets/StreamingAssets/**`, `Assets/ThirdParty/**`, `_Project/Settings/**`, `AGENTS.md`, `.gitignore`, `.github/**`, `.vscode/**`, `tools/**`, `docs/roadmap/**`, `docs/research/**` y los `.md` de la raíz de `docs/`. Nadie los cambia en esta fase. Unity 6000.3.28f1 reescribe al importar o compilar `ProjectSettings/ProjectSettings.asset`, `Assets/Settings/*.asset` y `.vscode/settings.json`. Esos cambios **no se suben**.

## Por desarrollador

| D | Rol fase 3 | Código y assets nuevos | Pruebas | Mantenimiento de fase 2 (sin cambiar firmas congeladas) |
| --- | --- | --- | --- | --- |
| D1 | Servicios del traje, consumo, transferencias EVA y margen de regreso | `Runtime/Simulation/Eva/**` | `Tests/EditMode/Phase03/Simulation/**` | — |
| D2 | Autoridad única de la expedición, esclusa, avisos, refugio, misión, escenario, composición y **único editor de escenas** | `Runtime/Application/Expedition/**`, `Runtime/Unity/Bootstrap/Expedition/**` (incluye `Provisional/`), `Runtime/Unity/Configuration/Expedition/**`, `Data/Scenarios/Phase03/**`, `Scenes/Phase03_EvaAndShelter.unity`, `Editor/Composition/Phase03/**` | `Tests/EditMode/Phase03/Application/**`, `Tests/EditMode/Phase03/Bootstrap/**` | `Tests/EditMode/Bootstrap/Editor/Phase02InteriorMatchTests.cs` (corrección de la etapa 0) |
| D3 | Roster por zonas, transiciones, interacción, red de fase 3 y avatares de fase 3 | `Runtime/Application/Crew/ExpeditionInteractionClient.cs`, `Runtime/Unity/Networking/Expedition/**`, `Runtime/Unity/Input/Expedition/**` | `Tests/EditMode/Phase03/Networking/**`, `Tests/PlayMode/Networking/Phase03/**` | `Runtime/Application/Crew/CrewRoster.cs` (implementa `IZonedCrewRoster`), `Runtime/Unity/Networking/Outpost/OutpostWireProtocol.cs` (ayudantes compartidos visibles como `internal`), `Runtime/Unity/Networking/Outpost/CrewAvatarSync.cs` (`TryGetAvatar`), `Runtime/Unity/Networking/Outpost/OutpostNetworkClient.cs` (corrección de la etapa 0) |
| D4 | Exterior visible, esclusa/refugio, indicadores de compañero, retorno y avisos; prefabs y UI de fase 3 | `Art/Phase03/**`, `Audio/Phase03/**`, `Prefabs/World/Phase03/**`, `Prefabs/Crew/Phase03/**`, `Prefabs/UI/Phase03/**`, `Runtime/Unity/Presentation/Expedition/**`, `Runtime/Unity/World/Expedition/**` | `Tests/PlayMode/UI/Phase03/**` | sus rutas de fase 2 (`Prefabs/World/Phase02/**`, astronauta), solo si hace falta para el ensamblaje |
| D5 | Validadores, pruebas de ruta/aviso/protección, herramientas de build y aceptación entre procesos | `Editor/Validation/Phase03/**`, `Editor/Build/Phase03/**`, `Runtime/Unity/Validation/Phase03/**` (piloto de aceptación, inerte sin argumentos) | `Tests/EditMode/Phase03/Validation/**`, `Tests/PlayMode/Integration/Phase03/**` | — |

Cada D escribe su handoff en `docs/phase-03/handoffs/0N_<ROL>_HANDOFF.md`. D4 y D5 tienen además `docs/phase-03/handoffs/d4-evidence/**` y `d5-evidence/**`.

Como en fase 2, las pruebas EditMode van en una subcarpeta `Editor/` dentro de la ruta del dueño (ensamblado Editor predefinido, sin asmdef), y las PlayMode bajo `UNITY_INCLUDE_TESTS`.

## Archivos previstos (lista de escritura de esta tarea)

| D | Archivo | Qué |
| --- | --- | --- |
| D1 | `Runtime/Simulation/Eva/EvaSuitSimulation.cs` | `IEvaSimulation`: consumo por actividad, eventos de reserva y de cero, transferencias idempotentes, libro mayor |
| D1 | `Runtime/Simulation/Eva/ReturnMarginCalculator.cs` | Margen por servicio y limitante; regla única |
| D2 | `Runtime/Application/Expedition/ExpeditionAuthority.cs` | `IExpeditionAuthority`: validación, reservas, paso, publicación |
| D2 | `Runtime/Application/Expedition/{AirlockMachine,AdvisoryTimeline,ProtectionTracker,ReturnRouteField,ExpeditionEvaluator}.cs` | Piezas puras de la autoridad |
| D2 | `Runtime/Unity/Configuration/Expedition/ExpeditionScenarioDefinition.cs` + `Data/Scenarios/Phase03/Phase03_EvaAndShelter_Synthetic.asset` | Datos del escenario (sintético, rotulado) |
| D2 | `Runtime/Unity/Bootstrap/Expedition/{ExpeditionBootstrap,ExpeditionRunner,ExpeditionSceneComposition}.cs` | Composición y reloj único |
| D2 | `Runtime/Unity/Bootstrap/Expedition/Provisional/*.cs` | Vistas PROVISIONALES (rotuladas) hasta la entrega de D4 |
| D2 | `Editor/Composition/Phase03/Phase03SceneBuilder.cs` + `Scenes/Phase03_EvaAndShelter.unity` | Montaje reproducible de la escena |
| D3 | `Runtime/Unity/Networking/Expedition/{ExpeditionWireProtocol,ExpeditionCommandBridge,ExpeditionNetworkSessionAdapter,LocalExpeditionSessionPort,ExpeditionNetworkHost,ExpeditionNetworkClient,ExpeditionCrewPresenter}.cs` | Cable `'T' 'E'`, host, cliente y avatares de fase 3 |
| D3 | `Runtime/Unity/Input/Expedition/ExpeditionInteractionInput.cs` | Tecla E (interacción) y R (declarar regreso) |
| D5 | `Editor/Validation/Phase03/*.cs`, `Editor/Build/Phase03/Phase03BuildChecks.cs`, `Runtime/Unity/Validation/Phase03/ExpeditionAutopilot.cs` | Validadores, build, lanzador y piloto de aceptación |

## Montaje de D4 (puntos reales, sin interfaces inventadas)

- **Vistas**: componentes `IExpeditionView` bajo el objeto `Phase03 Presentation Mount` de la escena. D2 los descubre y les llama `Bind`/`Unbind`. Las vistas provisionales de D2 se retiran del flujo cuando llega la de D4 equivalente.
- **Astronauta**: componentes `IExpeditionCrewView` en el prefab que asigna D2 al `CrewAvatarSync` (hoy `Prefabs/World/Phase02/AstronautVisual.prefab`, de D4).
- **Mundo**: el plano se dibuja desde el escenario en tiempo de ejecución, como `BaseInteriorView.Configure` en fase 2. D4 entrega prefabs; D2 los coloca en la escena. **D4 no edita la escena.**
- **Intenciones**: D4 no crea comandos. Usa `IExpeditionIntents` del contexto.

## Geometría: una sola autoridad

| Papel | Quién | Qué |
| --- | --- | --- |
| Produce el plano, las zonas, la esclusa y las estaciones | D2 | Asset de `Data/Scenarios/Phase03/` |
| Hace cumplir la colisión (con escotillas como superposición) | D3 | `CrewRoster` (`IZonedCrewRoster`) en la autoridad |
| Decide las escotillas, la zona, el sellado y la proximidad | D2 | Autoridad de expedición, sobre posiciones confirmadas |
| Dibuja conforme al plano | D4 | Sin física de Unity que decida posiciones |
| Valida la cobertura de zonas, la conectividad con escotillas cerradas y el alcance | D5 | Validadores de fase 3 |

## Cadena de dependencias y sin doble autoridad

- D1 es el único que mueve stocks de la base y de los trajes.
- D2 es la única autoridad: zona, esclusa, sellado, tareas, recepción de avisos, protección y fin. No hay una segunda autoridad para el exterior.
- D3 es el único que mueve astronautas y el único transporte.
- D4 solo lee snapshots y llama intenciones.
- D5 no altera el flujo de producción: su piloto de aceptación solo envía las mismas intenciones que un jugador, y solo con `-trainer-autopilot`.

## Lista legible por máquina

Patrones relativos a la raíz del repositorio (sin repetir aquí las reglas de fase 2, que siguen vigentes).

```ownership
FROZEN Assets/_Project/Runtime/Simulation/Contracts/Eva/**
FROZEN Assets/_Project/Runtime/Application/Contracts/Expedition/**
FROZEN Assets/_Project/Tests/EditMode/Phase03/Contracts/**
FROZEN docs/phase-03/SESSION_CONTRACT.md
FROZEN docs/phase-03/OWNERSHIP.md
FROZEN docs/phase-03/COMMON_BASE.md
D1 Assets/_Project/Runtime/Simulation/Eva/**
D1 Assets/_Project/Tests/EditMode/Phase03/Simulation/**
D1 docs/phase-03/handoffs/01_EVA_SERVICES_HANDOFF.md
D2 Assets/_Project/Runtime/Application/Expedition/**
D2 Assets/_Project/Runtime/Unity/Bootstrap/Expedition/**
D2 Assets/_Project/Runtime/Unity/Configuration/Expedition/**
D2 Assets/_Project/Data/Scenarios/Phase03/**
D2 Assets/_Project/Scenes/Phase03_EvaAndShelter.unity
D2 Assets/_Project/Editor/Composition/Phase03/**
D2 Assets/_Project/Tests/EditMode/Phase03/Application/**
D2 Assets/_Project/Tests/EditMode/Phase03/Bootstrap/**
D2 docs/phase-03/handoffs/02_EXPEDITION_HANDOFF.md
D3 Assets/_Project/Runtime/Application/Crew/ExpeditionInteractionClient.cs
D3 Assets/_Project/Runtime/Unity/Networking/Expedition/**
D3 Assets/_Project/Runtime/Unity/Input/Expedition/**
D3 Assets/_Project/Tests/EditMode/Phase03/Networking/**
D3 Assets/_Project/Tests/PlayMode/Networking/Phase03/**
D3 docs/phase-03/handoffs/03_TRANSITIONS_HANDOFF.md
D4 Assets/_Project/Art/Phase03/**
D4 Assets/_Project/Audio/Phase03/**
D4 Assets/_Project/Prefabs/World/Phase03/**
D4 Assets/_Project/Prefabs/Crew/Phase03/**
D4 Assets/_Project/Prefabs/UI/Phase03/**
D4 Assets/_Project/Runtime/Unity/Presentation/Expedition/**
D4 Assets/_Project/Runtime/Unity/World/Expedition/**
D4 Assets/_Project/Tests/PlayMode/UI/Phase03/**
D4 docs/phase-03/handoffs/04_EXTERIOR_VISUALS_HANDOFF.md
D4 docs/phase-03/handoffs/d4-evidence/**
D5 Assets/_Project/Editor/Validation/Phase03/**
D5 Assets/_Project/Editor/Build/Phase03/**
D5 Assets/_Project/Runtime/Unity/Validation/Phase03/**
D5 Assets/_Project/Tests/EditMode/Phase03/Validation/**
D5 Assets/_Project/Tests/PlayMode/Integration/Phase03/**
D5 docs/phase-03/handoffs/05_VALIDATION_HANDOFF.md
D5 docs/phase-03/handoffs/d5-evidence/**
```
