# Integración de fase 3

Trabajo autorizado por el usuario: reunir las entregas, conectar D4 a la escena productiva y verificar el conjunto. Se trabaja en `integration/phase-03`, worktree `/home/leonardo/phase03-integration`, para conservar los cambios sin commit de roadmap en el proyecto original.

## Procedencia

- Base técnica D1/D2/D3/D5: `1aec99cd2a0ecd9ff588e4c8c6346bbd587417a3`.
- D4: `83ca41b` (guardado localmente para poder hacer el merge autorizado).
- Las cinco ramas D1–D5 de fase 2 son antepasados de la base técnica. No requieren un nuevo merge ni cherry-pick.
- El merge D4/técnico se realizó sin conflictos. No se altera main ni se hace push.

## Lista de escritura de integración

- `Editor/Composition/Phase03/Phase03SceneBuilder.cs`: prefabs D4 reales en el montaje y astronauta Phase03.
- `Scenes/Phase03_EvaAndShelter.unity`: regeneración conservando su GUID y el escenario existente.
- `Editor/Build/Phase03/Phase03BuildChecks.cs`: comprobar D4, ausencia de provisionales y procedencia de builds desde worktrees.
- `Runtime/Unity/Presentation/Expedition/ExpeditionBoundView.cs`: liberar bloqueos al perder membresía.
- `Tests/EditMode/Phase03/Bootstrap/Editor/Phase03SceneTests.cs`: regresión de montaje y ciclo de conexión con D4 real.
- Tooling de validación de integración bajo `Runtime/Unity/Presentation/Expedition/Editor/` si las capturas reales lo requieren.
- Este informe y `docs/phase-03/integration-evidence/`.

Contratos, datos/valores del escenario, renderer, paquetes y ProjectSettings se conservan.

## Verificación de montaje

- Merge: `9e16eae504b0594074102370043fd22b50d616a7`, padres técnico `1aec99c` y D4 `83ca41b`.
- Unity nativo 6000.3.28f1: escena regenerada con D4, conservando el GUID y los datos del escenario.
- EditMode: **383/383**. Incluye regresión real de D4+puerto local+autoridad e inputs D3.
- PlayMode: **59/59**, incluidas pruebas de sockets WebSocket reales de las tres fases.
- Checks fase 3: **34/34**, incluidas comprobaciones explícitas de D4 y ausencia de componentes provisionales.
- Contratos y configuración global sin cambios. Unity generó configuración temporal de Editor, descartada en este worktree.

Build y recorridos de aceptación se registran después de ejecutarlos; estos resultados aún no sustituyen una partida con dos personas.
