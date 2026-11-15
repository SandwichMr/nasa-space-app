# Base común de la fase 3

Preparación compartida de la fase 3: contratos, puntos de montaje de D4, fake, fixtures y reglas. **No** contiene la jugabilidad ni la escena: eso va en commits posteriores de la misma rama.

## Punto de partida

- `main` = `origin/main` = `7d6b914f34b72278e7e9a203ba9ead2f7f56fed8`, con el árbol limpio.
- Rama de trabajo: `feature/phase-03/technical-core`.
- Commit padre de esta base: `d31dd0b` (`fix(phase2): stabilize entry gates for phase 3`), que corrige dos defectos de entrada (ver abajo).
- El SHA de esta base **no figura aquí**: se comunica después de crear el commit.
- Unity 6000.3.28f1 en Linux, con licencia activa en este equipo. Sin cambios de paquetes, settings, escenas de fase 1/2, prefabs, arte, asmdef ni defines.

### Estado de entrada verificado (etapa 0)

| Comprobación (nativa, Unity 6000.3.28f1) | Antes (`7d6b914`) | Después (`d31dd0b`) |
| --- | --- | --- |
| Importación y compilación | exit 0 | exit 0 |
| EditMode | 302 casos: 300 aprobadas, 2 fallidas (`ConfiguredGeometryAudit*`), exit 2 | **302/302**, exit 0 |
| Checks D5 de fase 2 | — | exit 0, sin `Failed` (`Pending`: dos procesos y dos personas) |
| PlayMode (incluye sockets WebSocket reales en un proceso) | — | **56/56**, exit 0 |
| Build Linux de `Phase02_InteriorBase` | — | Succeeded |
| Host + 2 clientes en 127.0.0.1 | el cliente no conectaba (solo LISTEN) | 2 ESTAB, `Connected as P1/P2` |

Causas y correcciones (mínimas, sin borrar ni relajar aserciones):

1. El ayudante de prueba exigía que `SceneManager.SetActiveScene` devolviera `true`, y Unity devuelve `false` cuando esa escena **ya** es la activa.
2. `OutpostNetworkClient` ignoraba `-trainer-server/-trainer-connect` cuando la composición llamaba a `Prepare()` antes de su `Start`.

**La fase 2 sigue sin aceptarse**: falta la partida con dos personas (`docs/phase-02/PROGRESS_SNAPSHOT.md`).

## Alcance entregado

| Entrega | Ruta |
| --- | --- |
| Contratos del modelo EVA (servicios, demanda, transferencias, lecturas, margen) | `Assets/_Project/Runtime/Simulation/Contracts/Eva/` (3 archivos) |
| Contratos de expedición (zonas, esclusa, trajes, reglas de tarea, refugio, radio, avisos, misión, escenario, comandos, snapshot, puertos, montaje de D4) | `Assets/_Project/Runtime/Application/Contracts/Expedition/` (9 archivos) |
| Fake y fixtures sintéticas rotuladas | `Assets/_Project/Runtime/Application/Contracts/Expedition/Development/` (2 archivos) |
| Pruebas de la base (EditMode) | `Assets/_Project/Tests/EditMode/Phase03/Contracts/Editor/` (3 archivos, 19 pruebas) |
| Documentación | `docs/phase-03/SESSION_CONTRACT.md`, `OWNERSHIP.md` y este archivo |

Los `.meta` los generó Unity al importar, con GUID nuevos.

## Verificaciones ejecutadas

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` compilados **solo** contra `netstandard` 2.1, `-warnaserror` (CS1591/CS1573 excluidos, como en fases anteriores) | exit 0 |
| Pruebas de la base con un runner por reflexión fuera de Unity (NUnit del proyecto, .NET 6.0.21 del Editor) | 19/19 |
| **Unity nativo**: importación y compilación de `Assembly-CSharp` y `Assembly-CSharp-Editor` + Test Runner EditMode con filtro `Phase03` | 19/19, exit 0 |

Qué cubren las pruebas:

- valores inválidos;
- el margen como mínimo y nunca media;
- la forma de los comandos y la correspondencia con fase 2;
- avisos, vigencia y fuente histórica obligatoria;
- evaluaciones;
- la correspondencia de resultados;
- las referencias del escenario;
- los invariantes del snapshot: suministro del traje, ocupantes de la esclusa, sin información futura, revelación solo al final;
- la inmutabilidad por reflexión;
- el fake.

**No es** una prueba de la autoridad, la simulación, la red ni la escena: todavía no existen.

## Cómo se incorpora (D4 y cualquier rama nueva)

`<SHA>` = el SHA completo comunicado tras el commit. Sin `reset --hard` ni `push --force`.

```sh
git status                                       # 1. revisar cambios propios sin commit
git fetch origin
git cat-file -t <SHA>                            # 2. debe responder "commit"
git switch -c feature/phase-03/d04-exterior <SHA>   # 3. rama de D4 desde la base exacta
# Con arte previo en otra rama: git merge <rama-de-arte> (sin tocar lo congelado).
git merge-base --is-ancestor <SHA> HEAD && echo "base incluida"
git diff --stat <SHA> -- Assets/_Project/Runtime/Simulation/Contracts/Eva \
  Assets/_Project/Runtime/Application/Contracts/Expedition Assets/_Project/Tests/EditMode/Phase03/Contracts \
  docs/phase-03/SESSION_CONTRACT.md docs/phase-03/OWNERSHIP.md docs/phase-03/COMMON_BASE.md   # 4. vacío
```

D4 desarrolla contra `IExpeditionSessionPort`, con `FakeExpeditionSessionPort` y `ExpeditionFixtures`. Implementa `IExpeditionView`/`IExpeditionCrewView` en sus rutas y nunca contra el código de otra rama. El coordinador reunirá las ramas en `integration/phase-03`.

## Plano del escenario que entregará D2

Para que D4 pueda dibujar ya. Son los datos de D2, el asset previsto `Data/Scenarios/Phase03/Phase03_EvaAndShelter_Synthetic.asset`. El dibujo debe leerse en tiempo de ejecución de `ExpeditionScenario`; si D2 cambia el plano, lo comunica.

Metros, plano cenital, mismo mapeo que fase 2 (X = X, Y = Y, 1 unidad = 1 m):

- **Límites**: x 0..58, y −6..16. La base de fase 2 (x 0..18, y 0..10) se conserva con sus estaciones y apariciones.
- **Ala EVA** (x 18.3..26, y 0..10), con acceso desde el laboratorio por la puerta x 18..18.3, y 6.5..9.3:
  - **Sala de preparación** (arriba): `suit-rack-a` (19.4, 8.6), `suit-rack-b` (21.3, 8.6), `suit-charger` (23.2, 8.6), `consumables-panel` (25.1, 8.6) y `suit-check` (23.0, 6.0).
  - **Refugio** (abajo; zona `Shelter` x 18.3..26, y 0..4.85): puerta x 19..21; `shelter-panel` (24.8, 1.1).
- **Esclusa**:
  - cámara x 26.3..29.0, y 6.2..9.2, 2 plazas;
  - escotilla interior x 26..26.3, y 6.9..8.5;
  - escotilla exterior x 29..29.3, y 6.9..8.5;
  - paneles `airlock-inner-panel` (26.15, 7.7) y `airlock-outer-panel` (29.15, 7.7);
  - entrada exterior (29.8, 7.7).
- **Exterior** (x 29.3..58):
  - ciencia cercana `regolith-site` (37, 4);
  - ciencia opcional `crater-rim` (53.5, 12.5), fuera del alcance de radio;
  - rocas (33, 9.5, 35, 11.5), (40, −1, 43, 1.5), (44.5, 3, 46, 9), (49, 9, 51, 11) y (50.5, 14, 56.5, 14.6);
  - antena (27.8, 11), alcance 20 m.
- **Claves de texto**:
  - trajes `suit-a`, `suit-b`;
  - tareas `don-suit-a`, `don-suit-b`, `charge-suit`, `fill-suit`, `check-suit`, `prepare-shelter`, `collect-regolith`, `crater-core`, más las de fase 2;
  - objetivos `crew-safe`, `regolith-sample`, `crater-core`;
  - mensajes `particle-warning`, `particle-update`, `shelter-period-end`.

## Límites y dependencias

- Sin implementación del modelo EVA, la autoridad, la red, la escena ni las vistas: son las etapas siguientes.
- Fixtures y valores: sintéticos y rotulados. No son datos de la NASA, de trajes ni procedimientos EVA.
- `AGENTS.md` sigue diciendo que no hay backend de red elegido, aunque Unity Transport/WebSocket está aprobado y en uso desde la fase 1. Es un archivo común y no se tocó.
