# Handoff D1 — fase 4: balances continuos, equipo y recuperación

**Estado:** entrega D1 publicada en su rama. **No** es la aceptación de la fase 4. Falta la autoridad continua (D2), la red (D3), las vistas (D4), la validación integrada (D5) y la ejecución nativa en Unity (ver Límites).

## Git

| Dato | Valor |
| --- | --- |
| Rama | `feature/phase-04/d01-simulation` (nombre del encargo; `COMMON_BASE.md` sugería `d01-balances`) |
| `main` = `origin/main` al empezar | `1ca26923ad7d29cfe7ebdbb2969c331a8875c4e0` (fast-forward, sin commits locales) |
| Base común D2 (`setup/phase-04-shared-contracts`) | `9b96f80ee8102073152d9e51cad4beb5a353aeb5`, verificada con `git merge-base --is-ancestor 1ca2692… 9b96f80…` |
| Rama creada con | `git switch -c feature/phase-04/d01-simulation 9b96f80ee8102073152d9e51cad4beb5a353aeb5` |
| Contratos congelados | sin cambios (`git diff 9b96f80 -- <rutas FROZEN>` vacío) |

Lista de escritura (todas dentro de las rutas D1 de `OWNERSHIP.md`):

- `Assets/_Project/Runtime/Simulation/BaseOperations/**` (nuevo):
  - `BaseEquipmentSimulation.cs`;
  - `BaseOperationsTransfers.cs`;
  - `BaseOperationsBalances.cs`;
  - sus `.meta` y `BaseOperations.meta`.
- `Assets/_Project/Runtime/Simulation/LifeSupport/OutpostBaseSimulation.cs`: implementa `IAirServiceCapacity` (mantenimiento autorizado por archivo).
- `Assets/_Project/Tests/EditMode/Phase04/Simulation/**` (nuevo): `Editor/` con 4 archivos y sus `.meta`.
- `docs/phase-04/handoffs/01_BALANCES_HANDOFF.md`: este archivo. El encargo lo llamaba `01_CONTINUOUS_RESOURCES_HANDOFF.md`, pero se usa la ruta congelada en `OWNERSHIP.md`.

## Qué se entrega

| Pieza | Tipo | Qué hace |
| --- | --- | --- |
| `BaseEquipmentSimulation` | `IBaseEquipmentModel` | Retención del reciclador, dos lechos, flujo de entrada, repuestos, avería programada, efectos idempotentes y libro mayor |
| `OutpostBaseSimulation` | `+ IAirServiceCapacity` | Multiplica la retirada máxima de CO2 por el factor. `Reset` lo pone a 1, que reproduce la fase 2 bit a bit (probado) |
| `BaseOperationsTransfers` | estático | La mitad «fase 2» (`ResourceTransfer`) de cada par: lote, ruta, recomprobación y descarga. También `Fit`, `SpareRoom` y `DeriveId` (ids deterministas por comando) |
| `BaseOperationsBalances` | estático, solo lectura | Cierre del reciclador, cierre agua+comida base+reciclador con flujos de frontera exterior, y partición de tripulación interior/EVA |

Todo es C# puro (`Trainer.Simulation.BaseOperations`), sin `UnityEngine` ni red, compilado solo contra `netstandard` 2.1 con `-warnaserror`.

## Auditoría de fases 2–3 (doble cobro)

- **Interior/EVA**: la fase 3 ya separa a la tripulación.
  - Sellados → `EvaDemand`, en `ExpeditionAuthority.SuitsDemand`.
  - No sellados → `OutpostDemand.CrewAboard`, en `ExpeditionAuthority.BaseDemand`.
  - `EvaSuitSimulation` solo cobra los trajes listados.
  - No se encontró doble cobro.
- **Fase 4**: los lechos deben cargarse con **el mismo** número que la cabina: `EquipmentDemand(baseDemand.CrewAboard)`. `BaseOperationsBalances.CrewPartitionError(presentes, outpostDemand, evaDemand, equipmentDemand)` devuelve un texto si alguien se cobra dos veces, ninguna, o si lechos y cabina no coinciden. D2 puede llamarlo en cada paso (barato) y D5 en sus validadores.
- **Un solo reloj**: ninguno de los modelos tiene reloj propio. Solo avanzan cuando la autoridad llama a `Advance(dt, …)`.
  - `Advance(0)` no hace nada.
  - Sin llamada (pausa, `Ready`) no hay consumo ni carga de lechos.
  - Al salir o volver de EVA o al terminar una tarea, nada se reinicia; solo `Reset` (es decir, `Restart`).
- **Dos Advance sobre el mismo paso**: cada modelo valida `dt` y su reloj suma exactamente lo avanzado. Las pruebas comprueban que el reloj del equipo y el de la base coinciden tras la secuencia documentada. Si la autoridad llamara dos veces, el reloj del modelo se adelantaría al de la sesión. D5 puede comparar `BaseEquipmentSnapshot.SimulatedTimeSeconds` con `PowerSnapshot.SimulatedTimeSeconds`.

## Unidades y frontera

| Magnitud | Unidad | Dónde |
| --- | --- | --- |
| Energía almacenada | J | `PowerSnapshot.EnergyJoules` (fase 2, sin cambios) |
| Potencia (generación, esencial, ciencia, servicio) | W | `PowerSnapshot`, `OutpostSimulationSnapshot.Service*Watts` |
| Agua potable / sin tratar, comida | kg | `LifeSupportSnapshot` (fase 2) |
| Retención del reciclador, lote, salmuera | kg | `WaterProcessorState.HoldKilograms`, `EquipmentLedger` |
| Conductividad del producto | µS/cm | `WaterProcessorState.ProductConductivity` (`Unavailable` = sin valor, nunca 0) |
| O2 / CO2 de cabina | kPa (lectura), kg (verdad del modelo) | fase 2 |
| Carga de lecho, flujo de entrada, factor de retirada | fracción [0, 1] | `AirRevitalizationState` |
| Repuestos | unidades | `BaseEquipmentSnapshot.Spares` (`Count`/`Capacity`) |

**Frontera**: base (potable + sin tratar + comida) ∪ reciclador (retención).

- Cruzan la frontera exterior:
  - consumo de la tripulación;
  - retorno de agua;
  - salmuera (sale y no vuelve);
  - descargas de pedidos (entran);
  - llenado de trajes de fase 3 (sale);
  - CO2 retirado (sale).
- **Interno**: los movimientos base ↔ reciclador, que son pares con el mismo id.

## Tablas de flujo

### Agua (un lote, condición nominal; valores de la fixture sintética)

| Paso | Base (fase 2) | Equipo (D1) | Masa |
| --- | --- | --- | --- |
| Completar `ProcessBatch` | `BatchIntake(id, 2)`: sin tratar −2, salida +2 | `ReceiveBatch(id, 2)`: retención +1.8, salmuera +0.2 | 2 = 1.8 + 0.2 |
| `RouteProduct AcceptToPotable` | después: `ProductToPotable(id2, 1.8)`: potable +1.8, entrada | antes: `ReleaseToPotable(id2, 1.8)`: retención → 0 | 1.8 |
| `RouteProduct Reprocess` | después: `ProductToUntreated(id2, h)` | antes: `ReleaseForReprocess(id2, h)` | h |
| `RecheckPotable` | `RecheckPotable(id3, kg)`: potable −kg, sin tratar +kg, saldo 0 | — | 0 neto |

Degradado: recuperación 0.75 (1.5 kg de producto y 0.5 kg de salmuera) y producto a 80 µS/cm, por encima de la línea de 40.

**Importante para D2**: comprobar el sitio de destino con `BaseOperationsTransfers.Fit(hold, stock, capacidad) == hold` **antes** de `Release*`. Una vez liberado, el producto solo existe en la transferencia de la base. Si esta fallara por capacidad, el auditor combinado lo mostraría como residuo negativo.

### Aire

| Estado | Carga del lecho activo | Factor aplicado a la base |
| --- | --- | --- |
| En línea | `+LoadingPerCrewSecond × CrewAboard` por segundo, tope en 1 | `BedFactor(carga) × flujo` |
| Aislada | sin carga | 0 (el CO2 sube; el O2 no cambia, es otro dispositivo) |
| `SwitchBed` (en línea) | intercambia activo y reserva | según el nuevo activo |
| `RegenerateStandbyBed` | reserva → 0 | — |
| Entrada restringida (programada) | — | × `RestrictedFlowFraction` hasta `ReplaceInletFilter` (aislada, con repuesto) |

### Repuestos y pedidos

| Paso | Base | Equipo |
| --- | --- | --- |
| `Intervene` completado | — | `ReplaceProcessorFilter` / `ReplaceInletFilter`: −1 repuesto (exige `Isolated`). La reserva la lleva D2 |
| `UnloadDelivery` completado | `DeliveryInflow(DeriveId(cmd, "unload-mass"), potable, comida)`: entrada por frontera, solo lo que cabe (`Fit`). `null` si no cabe nada → `NoStorageCapacity` | `ReceiveSpares(DeriveId(cmd, "unload-spare-<clave>"), clave, n)` con `n ≤ SpareRoom`. Todo o nada |
| Replay del mismo comando | `AlreadyApplied` (mismo id y contenido) | `AlreadyApplied` |

## Reglas del modelo de equipo (decisiones D1 dentro del contrato)

- **Avería programada**:
  - en `DegradeAtSeconds` pasa a `Degraded`, exactamente en ese instante;
  - sin reemplazo de filtro, `FailAfterDegradedSeconds` después pasa a `Failed`;
  - aislar **no** detiene ese temporizador.
- **Reemplazo de filtro**:
  - devuelve `Nominal` y anula el temporizador;
  - **no** rearma el guion: la avería ocurre una vez por `Reset`;
  - la máquina sigue `Isolated`: solo `Restore` (es decir, `SetOperatingState Online`) la vuelve a poner en línea.
- **Lo perdido no vuelve**: reparar no devuelve salmuera ni energía. `EquipmentLedger.BrineOutflowKilograms` nunca baja.
- **El producto retenido conserva la conductividad con la que se hizo**: si la máquina se degrada con producto dentro, ese producto sigue siendo nominal.
- **`SampleProduct`**:
  - con retención, lectura nueva de ese producto;
  - vacío, prueba de la máquina tal como está (20 o 80 µS/cm);
  - vacío y `Failed`, `Unavailable` (sin valor, nunca 0).

  Para que el `TestEquipment` de D2 pruebe la reparación, la retención debería estar vacía o contener producto hecho tras la reparación.
- **Vaciar la retención** (`Release*`) se permite aislada o fallada: drenar no es procesar. Así se evita un bloqueo con producto degradado dentro durante la reparación.
- **Estado ya fijado**: `SetOperatingState` a ese mismo estado devuelve `InvalidForState`, sin modificador fantasma.
- **Efectos rechazados**: no cambian nada y **no** consumen su id. Un reintento válido con el mismo id se aplica.
- **`ReceiveSpares`**:
  - clave desconocida → `InvalidForState`;
  - si supera la capacidad → `ExceedsCapacity`, sin entrega parcial.

  La parte que cabe la calcula D2 con `SpareRoom`.
- **Lechos**:
  - la carga no depende del flujo de entrada (simplificación declarada);
  - entre la rodilla y la saturación el factor cae de forma continua, pero la autoridad aplica el factor publicado tras cada paso. Ese tramo depende del tamaño de paso de la autoridad. `SecondsUntilEquipmentEvent` corta en la rodilla y en la saturación.
- **Eventos**: un evento justo al final de un `Advance` se aplica en ese `Advance`. Un evento programado en t = 0 se aplica al inicio del primer `Advance` positivo (`SecondsUntilEquipmentEvent` nunca devuelve 0).

## Ejemplo de composición para D2 (orden de `SESSION_CONTRACT §6`)

```csharp
// Una sola base, un solo equipo, un solo reloj (los de la autoridad).
var outpost = new OutpostBaseSimulation(parameters);           // también es IAirServiceCapacity
var equipment = new BaseEquipmentSimulation();
outpost.Reset(scenario.Expedition.Outpost.Simulation);         // como ExpeditionAuthority en fase 3
equipment.Reset(scenario.Equipment);

void Step(double dt)                                           // dt ya cortado por SecondsUntil*
{
    OutpostDemand baseDemand = BaseDemand();                   // no sellados (fase 3)
    var equipmentDemand = new EquipmentDemand(baseDemand.CrewAboard);
    Debug.Assert(BaseOperationsBalances.CrewPartitionError(present, baseDemand, SuitsDemand(), equipmentDemand) == null);
    equipment.Advance(dt, equipmentDemand);                                        // 1
    ((IAirServiceCapacity)outpost).SetCarbonDioxideRemovalFactor(equipment.Snapshot.Air.RemovalFactor); // 2
    /* 3. paso de fase 3: outpost.Advance(dt, baseDemand), EVA, roster, esclusa */
    /* 4–7. operaciones, pedidos, regla irreversible, publicación (D2) */
}

// Corte del paso:
double cut = Math.Min(equipment.SecondsUntilEquipmentEvent(equipmentDemand),
                      outpost.SecondsUntilServiceChange(baseDemand));   // y SecondsUntilServiceEvent de fase 3

// Lote completado (id = id del comando o derivado):
if (outpost.Apply(BaseOperationsTransfers.BatchIntake(id, batchKg)) == ResourceTransferStatus.Applied)
    equipment.Apply(EquipmentEffect.ReceiveBatch(id, batchKg));
```

`Phase04BalanceScenarioTests.Harness` es la versión ejecutable de este ejemplo. Es un **driver de prueba**, no una autoridad: no valida comandos ni decide nada en producción.

## Para D5: cómo verificar balances

- `BaseOperationsBalances.ProcessorResidualKilograms(equipment.Snapshot)` ≈ 0 en todo momento.
- `initial = WaterAndFoodKilograms(life0, equip0)`, y después `WaterAndFoodResidualKilograms(initial, life, outpost.Ledger, equip, descargasKg, llenadosTrajeKg)` ≈ 0. Un par aplicado en un solo lado da un residuo distinto de 0 (probado: −2 kg).
- `CrewPartitionError(...) == null` en cada paso.
- Fase 2: `LifeSupportLedger` sigue cerrando la masa de base (sin cambios).

## Configuración

- **Sintética**: `BaseEquipmentConfiguration` (D2 la lleva en el escenario). Las fixtures de prueba están en `Phase04SimulationFixtures`, con la misma forma que `BaseOperationsFixtures.Equipment()`:
  - lote de 2 kg; recuperación 0.90 / 0.75;
  - 20 / 80 µS/cm frente a una línea de 40;
  - degradación a 120 s y fallo 180 s después;
  - lechos: 0.002 por tripulante y segundo, rodilla 0.6, 0.2 de factor saturado.
- **Rotulado**: todo va etiquetado «SINTÉTICO». Son **parámetros de juego**: no son datos de NASA, de hardware ni de calidad de agua, ni umbrales clínicos.
- **Fuera del modelo**:
  - no hay 98 % genérico, ni MTBF, ni tasas médicas;
  - la línea de 40 µS/cm es la aceptación del escenario, no un índice de salud;
  - la energía de las operaciones la cobra la base como carga de servicio (fase 2) y no se devuelve al cancelar.

## Pruebas (escritas antes que la implementación)

Ubicación: `Assets/_Project/Tests/EditMode/Phase04/Simulation/Editor/` (ensamblado Editor predefinido, sin asmdef).

| Archivo | Pruebas | Cubre |
| --- | --- | --- |
| `Phase04EquipmentModelTests.cs` | 25 | Reset y entrada inválida; carga de lechos y rodilla; partición de `dt`; avería y fallo en el instante exacto; masa del lote nominal y degradado; rechazos sin cambio de estado; liberación exacta; drenaje aislado o fallado; idempotencia, `IdConflict` e id no consumido al rechazar; reparación sin doble cobro ni devolución de agua; reparación tras fallo; muestras; aislar o restaurar el aire sin modificador fantasma; lechos; entrada restringida; repuestos todo o nada; snapshots inmutables; Reset |
| `Phase04AirCouplingTests.cs` | 8 | `IAirServiceCapacity` en el mismo objeto; factor 1 = fase 2 bit a bit; rechazo de valores inválidos; acumulación de CO2 con masa conservada; factor 0 sin afectar al O2; recuperación hasta el objetivo; Reset → 1; orden de paso equipo → factor → base |
| `Phase04BalanceScenarioTests.cs` | 12 | Constructores de transferencias y signos de frontera; ids derivados; ajuste parcial o rechazo por capacidad; descarga duplicada entregada una vez; mitad rechazada sin estado intermedio; auditor de partición; auditor combinado que detecta un par roto; pausa sin reloj propio; ida y vuelta de EVA con un reloj y un cobro por miembro; fin de lote sin reset; ejecución reproducible estable → avería → escasez → reparación → reposición; control sin reparación ni reposición que agota el agua potable con escasez visible (`UnservedPotableWater` > 0) |

En la ejecución reproducible (degradación a 1500 s, reparación a 2200 s, descarga a 2600 s, 5000 s en total):

- en la ventana degradada no llega producto a potable y la salmuera crece;
- tras reparar vuelve a llegar;
- la descarga sube el agua potable más de 9.9 kg;
- sin descargas, el agua total baja: **no es autosuficiente**;
- la comida solo entra con la descarga;
- dos ejecuciones dan valores idénticos bit a bit;
- los residuos son < 1e-9 kg en cada paso.

## Comandos ejecutados y resultados

Unity no está instalado en el lado WSL. Se usó el toolchain del Editor Windows 6000.3.28f1 (Roslyn `DotNetSdkRoslyn/csc.dll` y .NET 6.0.21 de `NetCoreRuntime`), igual que en fases anteriores. Se usó también el NUnit del paquete integrado `com.unity.ext.nunit` y un runner por reflexión (`Logs/phase04-d01/runner/Runner.cs`, ignorado por git). El script compila:

1. `Runtime/Simulation/**` + `Runtime/Application/**` solo contra `netstandard.dll` 2.1, `-warnaserror` (CS1591/CS1573 excluidos);
2. las pruebas;
3. el runner, que las ejecuta.

| Ejecución | Resultado | Log (ignorado por git) |
| --- | --- | --- |
| Línea base antes de tocar nada (contratos fase 4, modelo base fase 2, energía, reloj) | pureza exit 0; **95/95** | `Logs/phase04-d01/baseline/` |
| Pruebas D1 antes de implementar | **no compilan** (`BaseEquipmentSimulation` no existe): rojo confirmado | `Logs/phase04-d01/red/compile-tests.txt` |
| Tras implementar: D1 + misma regresión | pureza exit 0; **140/140** (45 nuevas) | `Logs/phase04-d01/green/results.txt` |
| Regresión gestionada ampliada: todo archivo de prueba EditMode sin dependencia de Unity en `Application/`, `Phase03/{Application,Contracts,Simulation}`, `Contracts/` y `Crew/`, más lo anterior (incluye `OutpostAuthorityTests` y las autoridades de fase 3 sobre el `OutpostBaseSimulation` modificado) | **327/327** | `Logs/phase04-d01/regression/results.txt` |

## Límites y pendientes

- **Unity nativo: no ejecutado.**
  - Faltan importación, `Assembly-CSharp(-Editor)` y Test Runner EditMode.
  - En este equipo el proyecto está en el sistema de archivos WSL y solo hay Editor Windows. No se intentó abrir el proyecto por ruta UNC ni generar `Library/`.
  - El Editor no estaba abierto (solo Unity Hub).
  - Pendiente en un equipo con el proyecto local:

    ```sh
    Unity -batchmode -nographics -projectPath <proyecto> -runTests -testPlatform EditMode -testFilter Phase04 \
      -testResults Logs/phase04-d01/editmode-phase04.xml -logFile Logs/phase04-d01/editmode-phase04.log
    ```

    Esa ejecución también debe confirmar que Unity conserva los `.meta` nuevos (GUID aleatorios, formato de este repositorio).
- **No ejecutadas aquí**: pruebas con `UnityEngine`/`Trainer.Unity` (bootstrap, red, validadores de Editor), PlayMode, sockets, render y partida con personas.
- **Dependencias aún falsas o ausentes**:
  - la autoridad continua (D2) no existe todavía en esta rama;
  - el escenario real `Phase04_LivingBase_Synthetic.asset` es de D2;
  - hasta la integración, el resto del equipo trabaja con `FakeBaseOperationsSessionPort`/`BaseOperationsFixtures`.
- **Fuera de alcance de fase 4**: API NASA, director de eventos, persistencia y agricultura.
- **Integración**: antes de entregar se hizo `git fetch` (ver resumen de entrega). Cualquier novedad de `origin/main` o de la base común posterior a `9b96f80` no se ha incorporado; si existe, es una nueva base coordinada de D2.
