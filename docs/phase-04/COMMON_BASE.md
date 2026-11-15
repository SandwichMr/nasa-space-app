# Base común de la fase 4

Preparación compartida de la fase 4: contratos, puntos de montaje de D4, fake, fixtures y reglas. **No** contiene la jugabilidad, la autoridad continua ni la escena. D2 las entrega después en `feature/phase-04/d02-living-base`.

## Punto de partida

- `main` = `origin/main` = `1ca26923ad7d29cfe7ebdbb2969c331a8875c4e0`, actualizado por fast-forward antes de crear la rama.
- Rama: `setup/phase-04-shared-contracts`, creada desde ese SHA.
- El SHA de esta base **no figura aquí**: se comunica después de crear el commit.
- Unity 6000.3.28f1 está instalado en el equipo de preparación. No hay cambios de paquetes, settings, renderer, escenas, prefabs, arte, asmdef ni defines.
- El árbol tenía un cambio sin commit en `ProjectSettings/ProjectSettings.asset`: una entrada vacía de iconos `VisionOS` que reescribió Unity. Se conserva en local y **no** se sube.

### Entrada desde la fase 3 (inspeccionada, no rehecha)

`docs/phase-03/INTEGRATION.md` registra D1–D5 integradas en `main` con verificación automática. Lo que reutiliza la fase 4:

- EditMode 383/383, PlayMode 59/59, checks de fase 3 34/34.
- Build Linux y dos recorridos con autoridad + dos clientes en procesos separados.

La **aceptación manual con dos personas** de las fases 2 y 3 sigue pendiente según esos documentos. La fase 4 no la declara cumplida. Se reutilizan sin cambios:

- `ExpeditionAuthority` (una autoridad, un roster);
- `OutpostBaseSimulation` (stocks y aire de cabina);
- `EvaSuitSimulation`;
- el cable `'T' 'E'` sobre Unity Transport/WebSocket;
- la escena `Phase03_EvaAndShelter` y sus prefabs D4.

No se detectó ningún componente esencial que falte para empezar la fase 4.

## Alcance entregado

| Entrega | Ruta |
| --- | --- |
| Contratos del modelo de equipo (reciclador, aire, repuestos, avería, efectos, acoplamiento del CO2) | `Assets/_Project/Runtime/Simulation/Contracts/BaseOperations/` (2 archivos) |
| Contratos de la base viva (sesión y actividades, barras, mantenimiento, logística, comandos, escenario, snapshot, puertos, montaje de D4) | `Assets/_Project/Runtime/Application/Contracts/BaseOperations/` (9 archivos) |
| Fake y fixtures sintéticas rotuladas | `Assets/_Project/Runtime/Application/Contracts/BaseOperations/Development/` (2 archivos) |
| Pruebas de la base (EditMode) | `Assets/_Project/Tests/EditMode/Phase04/Contracts/Editor/` (3 archivos, 31 pruebas) |
| Documentación | `docs/phase-04/SESSION_CONTRACT.md`, `OWNERSHIP.md` y este archivo |

Los `.meta` se escribieron con el formato que usa Unity 6000.3.28f1 en este repositorio (scripts: `fileFormatVersion` + `guid`; carpetas: `DefaultImporter`), con GUID aleatorios nuevos. Unity no pudo importar en esta preparación (ver Límites). La primera importación nativa debe confirmar que Unity los conserva sin reescribirlos.

## Verificaciones ejecutadas

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` compilados **solo** contra `netstandard` 2.1, `-warnaserror` (CS1591/CS1573 excluidos, como en fases anteriores), con el Roslyn y el .NET 6.0.21 del Editor | exit 0 |
| Pruebas de la base con un runner por reflexión fuera de Unity (NUnit `com.unity.ext.nunit` del proyecto, .NET 6.0.21 del Editor) | **31/31** |
| Regresión gestionada en el mismo ensamblado: contratos de fase 3 y de fase 2 (`Outpost*`, `FakeOutpostSessionPort*`) | 19/19 y 25 + 9/34 |
| **Unity nativo** (importación, `Assembly-CSharp`, Test Runner EditMode) | **No ejecutado: bloqueado** (ver Límites) |

Logs en `Logs/phase04-setup/` (ignorado por git): `purity.txt` y `managed-contract-tests.txt`.

Qué cubren las pruebas:

- valores inválidos y configuración observable (el producto nominal dentro de la línea y el degradado fuera);
- calidad de lectura (desconocido o antiguo ≠ cero);
- bandas únicas y separación reserva / agotado / límite / déficit;
- rodilla de los lechos;
- orden de reparación;
- trabajo de reparación ↔ aislamiento;
- vigencia de la observación ↔ secuencia de estado;
- forma de los comandos;
- ciclo de los pedidos;
- ciclo global frente a actividades: la sesión continua nunca termina por un objetivo;
- una salida que termina y otra que empieza en la misma sesión;
- operaciones ↔ estaciones de fase 2;
- reservas ≤ repuestos;
- inmutabilidad por reflexión;
- el fake.

**No es** una prueba de la autoridad, la simulación del equipo, la red ni la escena: todavía no existen.

## Cómo se incorpora (D1, D3+D5, D4 y cualquier rama nueva)

`<SHA_BASE>` = SHA completo comunicado tras el commit. `<SHA_MAIN>` = `1ca26923ad7d29cfe7ebdbb2969c331a8875c4e0`. Sin `reset --hard`, `stash` automático ni `push --force`.

```sh
git status --short --branch                      # 1. sin cambios propios pendientes (consérvalos si los hay)
git fetch origin --prune
git cat-file -t <SHA_BASE>                       # 2. debe responder "commit"
git merge-base --is-ancestor <SHA_MAIN> <SHA_BASE> && echo "incluye main"
git switch -c feature/phase-04/d0N-<rol> <SHA_BASE>    # 3. rama desde la base exacta
git diff --stat <SHA_BASE> -- Assets/_Project/Runtime/Simulation/Contracts/BaseOperations \
  Assets/_Project/Runtime/Application/Contracts/BaseOperations Assets/_Project/Tests/EditMode/Phase04/Contracts \
  docs/phase-04/SESSION_CONTRACT.md docs/phase-04/OWNERSHIP.md docs/phase-04/COMMON_BASE.md   # 4. vacío
```

Ramas previstas:

- `feature/phase-04/d01-balances`;
- `feature/phase-04/d02-living-base`;
- `feature/phase-04/d03-d05-continuity`;
- `feature/phase-04/d04-bars-stations`.

Si `origin/main` avanza durante el trabajo paralelo, nadie lo mezcla por su cuenta en lo congelado: se pide a D2 una base coordinada.

## Firmas reales para cada consumidor

- **D1**:
  - implementa `IBaseEquipmentModel` (`Reset`, `Snapshot`, `Advance(double, EquipmentDemand)`, `SecondsUntilEquipmentEvent`, `Apply(EquipmentEffect) → EquipmentEffectStatus`);
  - añade `IAirServiceCapacity` a `OutpostBaseSimulation`;
  - parámetros en `BaseEquipmentConfiguration`, `WaterProcessorConfiguration` y `AirRevitalizationConfiguration` (regla `BedFactor`);
  - prueba contra `BaseOperationsFixtures.Equipment()`.
- **D3+D5**:
  - host sobre `IBaseOperationsAuthority` (`Join`, `Leave`, dos `Submit`, `SubmitMovement`, `TryGetSnapshot`, `SnapshotChanged`, `CommandCompleted`);
  - cliente que implementa `IBaseOperationsSessionPort`;
  - interacción local que implementa `IBaseOperationsIntents`;
  - comandos `BaseOperationCommand` con `ShapeError`/`HasSameContent`;
  - snapshot completo `BaseOperationsSnapshot`;
  - fixtures de validación desde `BaseOperationsFixtures` y `BaseOperationsSnapshotBuilder`.
- **D4**:
  - `IBaseOperationsView.Bind(BaseOperationsViewContext)` / `Unbind()`;
  - lee `GaugeOf(GaugeKind)`, `EquipmentOf(EquipmentKind)`, `Water`, `Operations`, `Orders`, `Decisions` y `LatestReport`;
  - envía con `IBaseOperationsIntents`;
  - desarrolla con `FakeBaseOperationsSessionPort.Publish/Join/Accept/Reject/Disconnect`.

## Plano del escenario que entregará D2

Para que D4 pueda dibujar ya. El asset previsto es `Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset`. Parte del plano real de fase 3 (`Phase03_EvaAndShelter_Synthetic.asset`), en metros, con el mismo mapeo:

- **Base, ala EVA, esclusa y exterior**: sin cambios respecto a la fase 3 (`docs/phase-03/COMMON_BASE.md`).
- **Reciclador** `water-recycler` (9, 8.6) y **unidad de aire** `air-scrubber` (3, 8.6): las mismas estaciones de fase 2, ahora con significado de fase 4 (sin `treat-water` ni `check-cabin-air`).
- **Consola logística** `logistics-console`: propuesta en (12, 1.4) dentro de la base. D2 la confirma en su asset y comunica cualquier cambio.
- **Claves de texto**:
  - barras = nombres de `GaugeKind`;
  - hallazgos = nombres de `Findings`;
  - lecturas `product-conductivity`, `active-bed-loading`, `standby-bed-loading`, `inlet-flow`;
  - repuestos `processor-filter`, `inlet-filter`;
  - manifiesto `standard-resupply`;
  - oportunidades `regolith-sample`, `crater-core`;
  - explicaciones y resultados, que D2 lista en su handoff.

## Límites y dependencias

- **Unity nativo bloqueado en esta preparación.** El agente se ejecuta dentro de un snap con base Ubuntu Core sin `libGL.so.1`, y el Editor sale con `error while loading shared libraries: libGL.so.1` (exit 127) antes de abrir el proyecto. No es un fallo del proyecto. Pendiente:
  - importación + Test Runner EditMode con filtro `Phase04`, desde una terminal normal del equipo;
  - confirmar que los `.meta` no se reescriben.

  ```sh
  Unity -batchmode -nographics -projectPath <proyecto> -runTests -testPlatform EditMode -testFilter Phase04 \
    -testResults Logs/phase04-setup/editmode-phase04.xml -logFile Logs/phase04-setup/editmode-phase04.log
  ```

- Sin implementación del modelo de equipo, la autoridad continua, la red, la escena ni las vistas: son las etapas siguientes.
- **Fuera de alcance de fase 4**: API NASA, director grande de eventos, persistencia y agricultura.
- La avería es sintética y reproducible.
- Fixtures y valores: sintéticos y rotulados. No son datos de la NASA, de hardware, de calidad de agua ni umbrales clínicos.
- `AGENTS.md` sigue diciendo que no hay backend de red elegido, aunque Unity Transport/WebSocket está aprobado y en uso desde la fase 1. Es un archivo común y no se tocó.
