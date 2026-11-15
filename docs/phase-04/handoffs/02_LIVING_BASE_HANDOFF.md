# Fase 4 · D2 · Base viva — handoff

Rama: `feature/phase-04/d02-living-base`. Prefijo omitido: `Assets/_Project/`.

## Git

| Paso | SHA |
| --- | --- |
| Base común (`setup/phase-04-shared-contracts`, congelada) | `9b96f80ee8102073152d9e51cad4beb5a353aeb5` |
| D1 incorporado (merge coordinado de `bdbce87`) | `5fd0be2` |
| **Nueva base coordinada**: `EndedAtSeconds` asignado en `BaseSessionStatus` y `ActivityRecord` (contrato congelado; también lo reportó D3) | `69464b5` |
| Entrega D2 | commit que contiene este archivo |

## Lista de escritura (todas con su `.meta`)

- `Runtime/Application/BaseOperations/BaseOperationsAuthority{,.Policy,.Operations,.Publication}.cs`
- `Runtime/Application/Expedition/ExpeditionAuthority.cs` (mantenimiento acordado: política inyectable y ganchos de paso)
- `Runtime/Application/Contracts/BaseOperations/Session.cs` (solo el arreglo `69464b5`)
- `Runtime/Unity/Configuration/BaseOperations/BaseOperationsScenarioDefinition.cs`
- `Runtime/Unity/Bootstrap/BaseOperations/{BaseOperationsBootstrap,BaseOperationsRunner,BaseOperationsSceneComposition}.cs`
- `Runtime/Unity/Bootstrap/BaseOperations/Provisional/{ProvisionalLocalBaseOperationsPort,ProvisionalBaseOperationsIntents,ProvisionalBaseOperationsHud}.cs`
- `Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset`
- `Editor/Composition/Phase04/Phase04SceneBuilder.cs`
- `Scenes/Phase04_LivingBase.unity` (**pendiente**: ver «Escena»)
- `Tests/EditMode/Phase04/Application/Editor/{Phase04LivingBaseHarness,Phase04SessionCycleTests,Phase04WaterAndAirTests,Phase04RepairAndLogisticsTests,Phase04OccupancyAndDisconnectTests}.cs`

No se tocan ProjectSettings, Packages, renderer ni escenas de fases 1–3.

## Una autoridad, un reloj

`BaseOperationsAuthority` **compone** la `ExpeditionAuthority` de fase 3; no la duplica. Le inyecta una política interna `IExpeditionSessionPolicy`. Sin política, la autoridad de fase 3 se comporta como antes, y su regresión se mantiene.

Orden de un paso:

1. `ConfirmReach`.
2. Demanda de la base, incluida la potencia de las operaciones en curso.
3. `equipment.Advance` (D1).
4. Factor de retirada de CO2.
5. `baseModel.Advance` (D1).
6. `AfterStep`: refresca máquinas, acredita operaciones, avanza pedidos, lleva el tiempo de esencial sin servir, tendencias e informe.

**Modo continuo (`BaseSessionMode.Continuous`)**

- No aplica la duración de misión.
- Una salida termina cuando ya no se está en Preparing y nadie está sellado. Entonces la fase vuelve a Preparing, se limpia la decisión, se reinician los objetivos y se cierra la actividad `sortie`. El reloj, los stocks, la tripulación y los equipos se conservan.
- Perder la energía esencial solo es irreversible tras `EssentialUnservedGraceSeconds` (60 s), y solo se sale con Reiniciar.

**Modo entrenamiento (`Training`)**: las reglas finitas de fase 3.

## Reglas decididas (comunicar)

- **La consola de logística está en (9.0, 1.4)**, radio 1.2. La propuesta (12, 1.4) de COMMON_BASE cae sobre la pared x 11.85–12.15. D4: coloca `LogisticsConsole` según el escenario, no según COMMON_BASE.
- **Aislar el reciclador exige la retención vacía.** Si no lo está, se rechaza con `HoldNotEmpty`.
- **Las observaciones se crean solas** al terminar `ProcessBatch`, `Isolate`, `TestEquipment` y `Restore`, además de `Inspect`. Una observación está vigente si su `StateSequence` coincide y `now < ValidUntil` (60 s).
- **Repuestos**: una operación los reserva y D1 los consume al completarse. El efecto D1 y el `ResourceTransfer` de la base comparten id: el del comando, o `DeriveId` en las descargas.
- **Avería sintética**: degradación a los 240 s y fallo 300 s después.

## Claves

**Explicación de decisión**

- Producto: `accepted-off-spec-product`, `accepted-within-limit`, `reprocessed-off-spec-product`, `reprocessed-within-limit`.
- Lechos: `bed-switch-not-needed`, `switched-to-loaded-bed`, `bed-switch-restored-removal`, `standby-bed-regenerated`, `standby-bed-already-clean`.
- Reparación: `isolated-for-repair`, `processor-filter-replaced`, `processor-filter-not-indicated`, `inlet-filter-replaced`, `inlet-filter-not-indicated`, `restored-after-passed-test`.

**Actividades**: `sortie`, `water-batch`, `air-service`, `water-processor`, `air-revitalization`, y la clave del manifiesto.

**Resultado de actividad**

- Logradas: `product-accepted`, `product-reprocessed`, `bed-switched`, `equipment-restored`, `delivery-unloaded`.
- Ciencia: `science-completed`, `science-returned`, `returned-without-science`.
- Interrumpidas: `order-cancelled`, `performer-moved-away`, `performer-left`, `session-ended`.
- Fallidas: `processor-failed`, `insufficient-untreated-water`, `no-storage-capacity`, `equipment-refused`.

## Escena y cadena de componentes

`Phase04SceneBuilder` (menú *Tools/Trainer/Phase 4/Build scenario asset and scene*) genera la escena:

| Objeto | Componentes | Función |
| --- | --- | --- |
| `Main Camera` | `LocalCameraFraming` | Encuadre local |
| `Global Light 2D` | — | Luz global |
| `BaseOperationsComposition` | `BaseOperationsSceneComposition` | Raíz de composición |
| `BaseOperationsAuthority` (authority-only) | `BaseOperationsRunner`, `BaseOperationsBootstrap` | Reloj único y autoridad |
| `CrewPresentation` | `CrewKeyboardInput` (E desactivada), `CrewAvatarSync` (astronauta de fase 3), `ExpeditionInteractionInput`, `ExpeditionCrewPresenter` | Tripulación |
| `BaseInterior` (prefab D4 de fase 2) | — | Plano de la base |
| `SessionControls` | — | Controles de sesión |
| `Phase03 Presentation Mount` | `ExpeditionPanel`, `ExpeditionWorld` | Vistas de fase 3 |
| `Phase04 Presentation Mount` | ver abajo | Vistas de fase 4 |

En `Phase04 Presentation Mount` el builder pone los prefabs de D4 `BaseOperationsPanel` y `BaseOperationsWorld` si existen. Si no, pone la vista **PROVISIONAL D2** rotulada (`ProvisionalBaseOperationsHud`, IMGUI).

En esta rama solo hay rol autoridad con puerto local provisional. El rol cliente registra `NoClientYet`, porque la red de fase 4 llega con D3.

## Receta de integración (`integration/phase-04`; D2 es el único editor de escena)

Las APIs de D3 y D4 son las que esos roles reportaron. **No están en esta rama.** Hay que verificarlas tras el merge del coordinador.

1. **D3, autoridad**
   - En `BaseOperationsSceneComposition.ComposeAuthority`, sustituir `ProvisionalLocalBaseOperationsPort` por `BaseOperationsNetworkHost.Attach(authority)`.
   - Obtener el puerto local con `host.CreateLocalPort()`.
   - Añadir el host al objeto authority-only.
2. **D3, cliente**
   - Añadir el objeto client-only con `BaseOperationsNetworkClient`.
   - Para el rol cliente, usar `Prepare(...)` y `Present(port)` en lugar de `NoClientYet`.
3. **Intenciones**
   - Sustituir `ProvisionalBaseOperationsIntents` por `BaseOperationsInteractionInput.Bind(port, scenario, phase3Intents)`.
   - Pasar su cliente al `BaseOperationsViewContext`.
4. **D4**: reejecutar el builder. Monta `BaseOperationsPanel` y `BaseOperationsWorld`, y la vista provisional desaparece sola. Colocar `WaterProcessor`, `AirUnit` y `LogisticsConsole` en las coordenadas del escenario.
5. **Limpieza**: borrar `Provisional/` cuando ya no lo referencie nada.

## Verificación real

| Comprobación | Resultado |
| --- | --- |
| Compilación pura de Simulation y Application (netstandard 2.1, `-warnaserror`) | exit 0 |
| Pruebas Phase04, runner NUnit managed fuera de Unity (31 contrato, 45 D1, 27 D2) | 103/103 |
| Regresión fases 1–3 sin dependencias de Unity | 251/251 |
| Compilación offline de `Runtime/**` y `Editor/Composition/**` contra los módulos de Unity 6000.3.28f1 | 0 errores |

Límites de la regresión de fases 1–3:

- Solo se ejecutan los `[Test]` sin parámetros; los `TestCase` no los ejecuta el runner.
- Las pruebas que dependen de Unity no se ejecutaron.

**Sin ejecutar: Unity nativo.** En este entorno (snap) falta `libGL.so.1`. Queda pendiente, en una máquina con Unity:

```
Unity -batchmode -nographics -projectPath <repo> -executeMethod Trainer.Tooling.Composition.Phase04.Phase04SceneBuilder.BuildFromCommandLine -logFile Logs/phase4-scene.log
Unity -batchmode -nographics -projectPath <repo> -runTests -testPlatform EditMode -testFilter Phase04 -testResults Logs/phase04/editmode.xml -logFile Logs/phase04/editmode.log
```

El asset del escenario se escribió a mano en YAML mínimo. Contiene `m_Script`, la referencia al asset de fase 3 y `scenarioId`; el resto toma los valores por defecto del C#. Unity lo reserializa al importarlo.

## Avisos a otros roles

- **D3+D5**: `Phase03BuildChecks.ScanProductionFakeUsage` marca `BaseOperationsFixtures`. Es un archivo de la base común; el ajuste del escáner es de D5, no de D2.
- **Todos**: incorporar `69464b5` (arreglo de `EndedAtSeconds`).
