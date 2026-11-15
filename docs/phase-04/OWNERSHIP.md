# Propiedad de archivos — fase 4

Cada ruta tiene **un solo** dueño.

- `dir/**` cubre el directorio, su contenido, los `.meta` de dentro y el `.meta` del propio directorio.
- Una ruta exacta cubre ese archivo y su `.meta`.
- Los `.meta` de carpetas padre ya existentes son de COMMON. Lo mismo `Tests/EditMode/Phase04.meta`, que crea esta base.
- Nadie edita fuera de sus rutas. Una ampliación se pide como cambio coordinado.

Roles de esta fase:

- **D1**: balances y equipo.
- **D2**: sesión continua, acciones y **montaje de escena**.
- **D3+D5**: red y validación, una misma persona o tarea. Tiene ambas áreas, pero no controla D1/D2 ni las escenas.
- **D4**: barras, base legible e interacción.

Prefijo omitido en las tablas: `Assets/_Project/`.

## Congelado

- **Fase 4 (esta base)**: `Runtime/Simulation/Contracts/BaseOperations/**`, `Runtime/Application/Contracts/BaseOperations/**` (contratos, montaje de D4, fake y fixtures), `Tests/EditMode/Phase04/Contracts/**`, `docs/phase-04/SESSION_CONTRACT.md`, `docs/phase-04/OWNERSHIP.md` y `docs/phase-04/COMMON_BASE.md`.
- **Fases 1–3**: todo lo que `docs/phase-02/OWNERSHIP.md` y `docs/phase-03/OWNERSHIP.md` marcan FROZEN sigue congelado.

Un cambio de lo congelado es una **nueva base coordinada**: un commit propio de D2, avisado con su SHA completo antes de que nadie lo use.

## Común (nadie lo cambia en esta fase)

`Packages/**`, `ProjectSettings/**`, `Assets/Settings/**` (URP/renderer), `Assets/Scenes/**`, `Assets/StreamingAssets/**`, `Assets/ThirdParty/**`, `_Project/Settings/**`, `AGENTS.md`, `.gitignore`, `.github/**`, `.vscode/**`, `tools/**`, `docs/roadmap/**`, `docs/research/**`, los `.md` de la raíz de `docs/` y **todas las escenas de fases 1–3**.

Unity 6000.3.28f1 reescribe al importar o compilar `ProjectSettings/ProjectSettings.asset`, `Assets/Settings/*.asset` y `.vscode/settings.json`. Esos cambios **no se suben**: se añaden solo rutas explícitas con `git add -- RUTAS`.

**Configuración de build**: no se edita `ProjectSettings/EditorBuildSettings.asset`. Las herramientas de build de fase 4 (D5) pasan la ruta de `Scenes/Phase04_LivingBase.unity` de forma explícita, como en fase 3.

## Por desarrollador

| D | Rol fase 4 | Código y assets nuevos | Pruebas | Mantenimiento exacto de archivos existentes (sin cambiar firmas congeladas) |
| --- | --- | --- | --- | --- |
| D1 | Balances continuos, modelo de equipo, avería sintética, reposición y su efecto único | `Runtime/Simulation/BaseOperations/**` | `Tests/EditMode/Phase04/Simulation/**` | `Runtime/Simulation/LifeSupport/OutpostBaseSimulation.cs`: implementa `IAirServiceCapacity` (factor 1 tras `Reset` = fase 2 idéntica). Auditar el doble cobro interior/EVA sin cambiar contratos. |
| D2 | Autoridad continua única, ciclo de sesión, acciones de estación, reparación, pedidos, escenario, composición y **único editor de escenas** | `Runtime/Application/BaseOperations/**`, `Runtime/Unity/Bootstrap/BaseOperations/**` (incluye `Provisional/`), `Runtime/Unity/Configuration/BaseOperations/**`, `Data/Scenarios/Phase04/**`, `Scenes/Phase04_LivingBase.unity`, `Editor/Composition/Phase04/**` | `Tests/EditMode/Phase04/Application/**`, `Tests/EditMode/Phase04/Bootstrap/**` | `Runtime/Application/Expedition/ExpeditionAuthority.cs`: política de cierre inyectable y ganchos de paso para la autoridad continua. La política por defecto es la finita actual, sin cambio de comportamiento: la regresión de fase 3 sigue igual. |
| D3 (D3+D5) | Red de fase 4, interacción local, sincronización de stocks/ocupación/pasos/pedidos/sesión, incorporación tardía y desconexión | `Runtime/Unity/Networking/BaseOperations/**`, `Runtime/Application/Crew/BaseOperationsInteractionClient.cs` (implementa `IBaseOperationsIntents`), `Runtime/Unity/Input/BaseOperations/**` | `Tests/EditMode/Phase04/Networking/**`, `Tests/PlayMode/Networking/Phase04/**` | `Runtime/Unity/Networking/Expedition/ExpeditionWireProtocol.cs`: solo dar visibilidad `internal` a los codificadores que reutilice el cable `'T' 'B'`, sin cambiar el cable `'T' 'E'`. Es el **único** dueño de cualquier cambio en transportes y protocolos antiguos. |
| D4 | Barras reales, tendencia/avisos, estado de equipos, paneles de las dos interacciones, rechazo de comandos y coherencia visual del interior | `Art/Phase04/**`, `Audio/Phase04/**`, `Prefabs/UI/Phase04/**`, `Prefabs/World/Phase04/**`, `Runtime/Unity/Presentation/BaseOperations/**`, `Runtime/Unity/World/BaseOperations/**` | `Tests/PlayMode/UI/Phase04/**` | Sus rutas de fases 2 y 3 (`Prefabs/World/Phase02/**`, `Prefabs/World/Phase03/**`, astronauta), solo si el montaje lo exige y sin cambiar la geometría autoritativa. |
| D5 (D3+D5) | Validadores, fixtures de continuidad, herramientas de build y aceptación entre procesos | `Editor/Validation/Phase04/**`, `Editor/Build/Phase04/**`, `Runtime/Unity/Validation/Phase04/**` (piloto de aceptación, inerte sin argumentos) | `Tests/EditMode/Phase04/Validation/**`, `Tests/PlayMode/Integration/Phase04/**` | — |
| Coordinador | Integración en `integration/phase-04` | `docs/phase-04/INTEGRATION.md`, `docs/phase-04/integration-evidence/**` | — | Merges autorizados; ningún cambio de código propio |

Cada D escribe su handoff en `docs/phase-04/handoffs/0N_<ROL>_HANDOFF.md`. D4 y D5 tienen además `d4-evidence/**` y `d5-evidence/**`.

Las pruebas EditMode van en una subcarpeta `Editor/` dentro de la ruta del dueño (ensamblado Editor predefinido, sin asmdef). Las PlayMode van bajo `UNITY_INCLUDE_TESTS`, como en las fases 2 y 3.

## Archivos previstos (lista de escritura)

| D | Archivo | Qué |
| --- | --- | --- |
| D1 | `Runtime/Simulation/BaseOperations/BaseEquipmentSimulation.cs` | `IBaseEquipmentModel`: retención, lechos, flujo, repuestos, avería programada, efectos idempotentes, libro mayor |
| D1 | `Runtime/Simulation/BaseOperations/BaseOperationsBalances.cs` (opcional) | Auditoría de balances de masa y energía para D5 |
| D2 | `Runtime/Application/BaseOperations/BaseOperationsAuthority.cs` | `IBaseOperationsAuthority`: un reloj, validación, reservas, paso, publicación |
| D2 | `Runtime/Application/BaseOperations/{SessionCycle,StationOperations,RepairTracker,LogisticsBoard,GaugePublisher}.cs` | Piezas puras de la autoridad |
| D2 | `Runtime/Unity/Configuration/BaseOperations/BaseOperationsScenarioDefinition.cs` + `Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset` | Datos del escenario (sintético, rotulado) |
| D2 | `Runtime/Unity/Bootstrap/BaseOperations/{BaseOperationsBootstrap,BaseOperationsRunner,BaseOperationsSceneComposition}.cs` | Composición y reloj único |
| D2 | `Runtime/Unity/Bootstrap/BaseOperations/Provisional/*.cs` | Vistas PROVISIONALES rotuladas, hasta la entrega de D4 |
| D2 | `Editor/Composition/Phase04/Phase04SceneBuilder.cs` + `Scenes/Phase04_LivingBase.unity` | Montaje reproducible de la escena real |
| D3 | `Runtime/Unity/Networking/BaseOperations/{BaseOperationsWireProtocol,BaseOperationsCommandBridge,BaseOperationsNetworkSessionAdapter,LocalBaseOperationsSessionPort,BaseOperationsNetworkHost,BaseOperationsNetworkClient}.cs` | Cable `'T' 'B'`, host, cliente y puerto local |
| D3 | `Runtime/Application/Crew/BaseOperationsInteractionClient.cs`, `Runtime/Unity/Input/BaseOperations/BaseOperationsInteractionInput.cs` | Intenciones locales y teclas |
| D4 | `Runtime/Unity/Presentation/BaseOperations/*.cs`, `Prefabs/UI/Phase04/*.prefab`, `Prefabs/World/Phase04/*.prefab` | Vistas `IBaseOperationsView` y prefabs |
| D5 | `Editor/Validation/Phase04/*.cs`, `Editor/Build/Phase04/Phase04BuildChecks.cs`, `Runtime/Unity/Validation/Phase04/BaseOperationsAutopilot.cs` | Validadores, build, lanzador y piloto de aceptación |

## Montaje de D4 (puntos reales, sin interfaces inventadas)

- **Vistas de fase 4**: componentes `IBaseOperationsView` bajo el objeto `Phase04 Presentation Mount` de la escena. D2 los descubre y les llama `Bind`/`Unbind`.
- **Vistas de fase 3**: `IExpeditionView` bajo `Phase03 Presentation Mount`, con su contexto sobre el mismo puerto.
- **Astronauta**: `IExpeditionCrewView` en el prefab que D2 asigna al `CrewAvatarSync`.
- **Retirada de provisionales**: las vistas provisionales de D2 salen del flujo cuando llega la equivalente de D4.
- **Mundo**: el plano se dibuja desde el escenario en tiempo de ejecución. D4 entrega prefabs y D2 los coloca. **D4 no edita la escena.**
- **Intenciones**: D4 no crea comandos. Usa `IBaseOperationsIntents` y `IExpeditionIntents` del contexto.
- **Prefabs antes de la build final**: el primer prefab de D4 se desarrolla contra `FakeBaseOperationsSessionPort` y `BaseOperationsFixtures`, sin depender de una build final.

## Geometría: una sola autoridad

| Papel | Quién | Qué |
| --- | --- | --- |
| Produce el plano, las zonas, la esclusa y las estaciones (incluida `logistics-console`) | D2 | Asset de `Data/Scenarios/Phase04/` |
| Hace cumplir la colisión | D3 | El mismo `CrewRoster` (`IZonedCrewRoster`) en la autoridad |
| Decide la proximidad, la zona y la ocupación | D2 | Autoridad continua, sobre posiciones confirmadas |
| Dibuja conforme al plano | D4 | Sin física de Unity que decida posiciones |
| Valida la cobertura, la conectividad y el alcance | D5 | Validadores de fase 4 |

## Cadena de dependencias y sin doble autoridad

- D1 es el único que mueve stocks (base, trajes, retención, repuestos).
- D2 es la única autoridad: sesión, acciones, observaciones, pasos, pedidos y fin. La composición monta **un** runner y **un** reloj. No hay segunda autoridad, ni otro controlador, ni otro inventario.
- D3 es el único que mueve astronautas y el único transporte.
- D4 solo lee snapshots y llama intenciones.
- D5 no altera el flujo de producción. Su piloto solo envía las intenciones de un jugador, y solo con `-trainer-autopilot`.
- En `integration/phase-04` los fakes se retiran del flujo de producción. D2 entrega la receta de conexión (handoff) antes de la integración.

## Lista legible por máquina

Patrones relativos a la raíz del repositorio (las reglas de fases 2 y 3 siguen vigentes).

```ownership
FROZEN Assets/_Project/Runtime/Simulation/Contracts/BaseOperations/**
FROZEN Assets/_Project/Runtime/Application/Contracts/BaseOperations/**
FROZEN Assets/_Project/Tests/EditMode/Phase04/Contracts/**
FROZEN docs/phase-04/SESSION_CONTRACT.md
FROZEN docs/phase-04/OWNERSHIP.md
FROZEN docs/phase-04/COMMON_BASE.md
COMMON Assets/_Project/Tests/EditMode/Phase04.meta
D1 Assets/_Project/Runtime/Simulation/BaseOperations/**
D1 Assets/_Project/Tests/EditMode/Phase04/Simulation/**
D1 Assets/_Project/Runtime/Simulation/LifeSupport/OutpostBaseSimulation.cs
D1 docs/phase-04/handoffs/01_BALANCES_HANDOFF.md
D2 Assets/_Project/Runtime/Application/BaseOperations/**
D2 Assets/_Project/Runtime/Unity/Bootstrap/BaseOperations/**
D2 Assets/_Project/Runtime/Unity/Configuration/BaseOperations/**
D2 Assets/_Project/Data/Scenarios/Phase04/**
D2 Assets/_Project/Scenes/Phase04_LivingBase.unity
D2 Assets/_Project/Editor/Composition/Phase04/**
D2 Assets/_Project/Tests/EditMode/Phase04/Application/**
D2 Assets/_Project/Tests/EditMode/Phase04/Bootstrap/**
D2 Assets/_Project/Runtime/Application/Expedition/ExpeditionAuthority.cs
D2 docs/phase-04/handoffs/02_LIVING_BASE_HANDOFF.md
D3 Assets/_Project/Runtime/Unity/Networking/BaseOperations/**
D3 Assets/_Project/Runtime/Application/Crew/BaseOperationsInteractionClient.cs
D3 Assets/_Project/Runtime/Unity/Input/BaseOperations/**
D3 Assets/_Project/Runtime/Unity/Networking/Expedition/ExpeditionWireProtocol.cs
D3 Assets/_Project/Tests/EditMode/Phase04/Networking/**
D3 Assets/_Project/Tests/PlayMode/Networking/Phase04/**
D3 docs/phase-04/handoffs/03_CONTINUITY_NETWORK_HANDOFF.md
D4 Assets/_Project/Art/Phase04/**
D4 Assets/_Project/Audio/Phase04/**
D4 Assets/_Project/Prefabs/UI/Phase04/**
D4 Assets/_Project/Prefabs/World/Phase04/**
D4 Assets/_Project/Runtime/Unity/Presentation/BaseOperations/**
D4 Assets/_Project/Runtime/Unity/World/BaseOperations/**
D4 Assets/_Project/Tests/PlayMode/UI/Phase04/**
D4 docs/phase-04/handoffs/04_BARS_AND_STATIONS_HANDOFF.md
D4 docs/phase-04/handoffs/d4-evidence/**
D5 Assets/_Project/Editor/Validation/Phase04/**
D5 Assets/_Project/Editor/Build/Phase04/**
D5 Assets/_Project/Runtime/Unity/Validation/Phase04/**
D5 Assets/_Project/Tests/EditMode/Phase04/Validation/**
D5 Assets/_Project/Tests/PlayMode/Integration/Phase04/**
D5 docs/phase-04/handoffs/05_CONTINUITY_VALIDATION_HANDOFF.md
D5 docs/phase-04/handoffs/d5-evidence/**
COORDINATOR docs/phase-04/INTEGRATION.md
COORDINATOR docs/phase-04/integration-evidence/**
```
