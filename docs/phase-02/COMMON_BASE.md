# Base común de la fase 2

Tarea única, ejecutada con D1–D5 en pausa. Prepara contratos, fake y reglas. **No** inicia ni termina la fase 2 jugable.

## Punto de partida

- Commit padre: `3dd53c506abbe9a15a614d02dae8b1d38fe40634` (`fix(phase1): integrate mission scene, networking and presentation corrections`). Al empezar era `main` = `origin/main`, sin cambios posteriores, con el árbol limpio.
- Rama de trabajo: `main`, por indicación del coordinador. El commit de esta base se crea directamente en `main`. Su SHA **no** figura aquí: se comunica después de crearlo.
- Unity 6000.3.28f1. Paquetes y versiones sin cambios (incluido `com.unity.transport` 2.7.4, ya presente). No se tocaron ProjectSettings, escenas, prefabs, arte, código funcional de fase 1, asmdef ni defines.

## Alcance entregado

| Entrega | Ruta |
| --- | --- |
| Contratos de recursos y cabina, punto de integración con energía (`IOutpostSimulation`) | `Assets/_Project/Runtime/Simulation/Contracts/LifeSupport/` (4 archivos) |
| Contratos de jugadores, presencia, movimiento, plano/colisión, estaciones/tareas, comandos, snapshot y puertos | `Assets/_Project/Runtime/Application/Contracts/Outpost/` (10 archivos) |
| Fake de desarrollo y fixtures sintéticas rotuladas | `Assets/_Project/Runtime/Application/Contracts/Development/` (2 archivos) |
| Pruebas NUnit de la base (EditMode, ensamblado Editor predefinido) | `Assets/_Project/Tests/EditMode/Contracts/Editor/` (4 archivos, 34 pruebas) |
| Documentación | `docs/phase-02/SESSION_CONTRACT.md`, `OWNERSHIP.md`, este archivo |

Se añaden 7 `.meta` de carpeta y 20 de script, con GUID aleatorios únicos y el formato que ya usa el repositorio. Así los cinco parten de los mismos GUID para los archivos congelados. Unity no se abrió: al importar puede completar los campos por defecto del importador.

## Verificaciones ejecutadas (fuera de Unity)

No son una build de Unity ni el Unity Test Runner. Se usaron el compilador Roslyn y el runtime .NET 6.0.21 que trae el Editor, el NUnit real de `com.unity.ext.nunit` y un runner por reflexión (`[Test]`, `[TestCase]`, `[SetUp]`, `[TearDown]`). El arnés vive fuera del repositorio y no se entrega.

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` (31 fuentes) compilados **solo** contra `netstandard.dll`, `-warnaserror+`, con `-doc` para validar referencias XML | exit 0, 0 avisos |
| NUnit, base de fase 2: valores, snapshot, fake, ejemplos | 34/34 (11 + 8 + 9 + 6) |
| NUnit, regresión pura de fase 1: PowerSystem 30, Clock 5, MissionSession 21, Evaluator 7, FakeSessionPort 6 | 69/69 |
| Sensibilidad: 8 mutantes sobre una copia (secuencia, época, doble reserva, balance de masa, borde de colisión, resultado inventado, lista no copiada, diagonal) | los 8 detectados (1–3 fallos cada uno) |
| `Assembly-CSharp` completo (55 fuentes, con Unity, Networking y UTP) para player Linux, `UNITY_EDITOR` y `UNITY_WEBGL`, más `Assembly-CSharp-Editor` (18 fuentes) | exit 0 en los cuatro. Avisos: solo CS0649 previos de archivos Unity de fase 1; 0 en los contratos |
| Pruebas de fase 1 que dependen de tipos Unity | `MissionScenarioTests` 19/19. `PhaseOneRegressionTests` 16/17: `RunnerOwnershipIsReleasedByUnbindAndDestroy` falla con `SecurityException: ECall methods must be packaged into a system module` (necesita el motor nativo). **Idéntico en el commit padre**, así que no es una regresión |
| Propiedad: bloque `ownership` de `OWNERSHIP.md` contra los 435 archivos versionados o nuevos | 87 reglas; sin solapes, sin varios dueños, sin archivos sin dueño. Un solape inyectado en una copia se detecta |
| GUID de `.meta` | sin duplicados en `Assets/` |

Para compilar `Assembly-CSharp` fuera de Unity, Unity.Mathematics 1.3.3, Burst 1.8.31, Collections 2.7.1 y Transport 2.7.4 se compilaron como referencias managed. Salen de los `.tgz` que trae el Editor, extraídos a una carpeta temporal sin modificarlos. No se instaló nada en el proyecto. No equivale a importación, Burst nativo ni IL2CPP.

**Pendiente de ejecutar en Unity**: importación (AssetDatabase y `.meta`), Test Runner EditMode de `Tests/EditMode/Contracts/Editor/` y regresión completa de fase 1 (incluidos los casos PlayMode). Después, la build de fase 1 para confirmar que sigue funcionando.

## Qué es garantía y qué no

- **Garantía de tipos y constructores**, probada: valores inválidos y límites, inmutabilidad, exclusión persona/estación en el snapshot, coherencia entre resultado y estado, balance de masa, lecturas ausentes nunca convertidas en 0.
- **Regla compartida implementada**, probada: orden de movimiento (`MovementInputPolicy`), forma de los comandos (`ShapeError`), colisión (`IsAdmissible`), alcance (`IsWithinReach`), orden de snapshots (`IsNewerThan`).
- **Comportamiento del fake**, probado: diferido, sin resultados inventados, abandono al desconectar, snapshots solo hacia delante, `Joined` con estado. El fake **no** es autoridad.
- **Reglas que implementarán otros**, documentadas pero **no** validadas porque no existe autoridad:
  - D2: deduplicación de ids, orden de validación, proximidad continua, costos y llamada a `Apply`, `Join`/`Leave`, revisiones;
  - D3: roster, colisión resuelta, cable de fase 2 y adaptadores;
  - D1: modelos y `Apply` todo o nada.

## Límites y dependencias

- No se implementó movimiento, mapa, sprites, animaciones, modelos de cabina, agua o comida, scheduler, misión nueva, transporte de fase 2 ni wiring de escenas.
- Permisos de pausa y reinicio por rol: sin decidir, como en fase 1.
- Fixtures: valores sintéticos rotulados. No son datos de la NASA ni tasas reutilizables.
- `AGENTS.md` sigue diciendo "No networking backend is selected yet", aunque Unity Transport/WebSocket está aprobado y en uso. No lo edité: es un archivo común y requiere una actualización aprobada.
- Dependencias entre D:
  - D2 compone D1 (`IOutpostSimulation`) y D3 (`ICrewRoster`);
  - D3 adapta `IOutpostAuthority` a la red y ofrece `IOutpostSessionPort`;
  - D4 consume `IOutpostSessionPort` (el fake hasta la integración);
  - D5 valida todo lo anterior.
  Hasta la integración, cada D trabaja contra los contratos y el fake, nunca contra el código de otra rama.

## Cómo incorporar EXACTAMENTE esta base (cada D)

`<SHA>` = el SHA completo comunicado tras el commit. No usar `reset --hard` ni `push --force`.

```sh
git status                                   # 1. Revisar cambios propios sin commit
git add <tus archivos> && git commit -m "wip: ..."   # 2. Si los hay: commit en tu rama actual (o git stash push -u)
git fetch origin
git cat-file -t <SHA>                        # 3. Debe responder "commit"
git merge-base --is-ancestor <SHA> origin/main && echo "en main"
git switch -c feature/phase-02/d0X-rol <SHA> # 4. Rama de fase 2 que parte exactamente de la base
# Si necesitas trabajo propio previo que no está en main: git merge <tu-rama-anterior>, resolviendo
# conflictos SIN tocar los archivos congelados.
git merge-base --is-ancestor <SHA> HEAD && echo "base incluida"
git diff --stat <SHA> -- Assets/_Project/Runtime/Simulation/Contracts Assets/_Project/Runtime/Application/Contracts \
  Assets/_Project/Tests/EditMode/Contracts docs/phase-02/SESSION_CONTRACT.md docs/phase-02/OWNERSHIP.md \
  docs/phase-02/COMMON_BASE.md                # 5. Debe salir vacío: congelados intactos
```

Si usaste `git stash`, recupera los cambios con `git stash pop` en la rama que corresponda. A partir de ahí, cada D edita solo sus rutas de `OWNERSHIP.md`. Si hace falta ampliar un contrato, se pide una nueva base coordinada; no se cambia en la rama propia.
