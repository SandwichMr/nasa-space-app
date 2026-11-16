# Integración — fase 5

## Ramas y commits

- **Rama de preparación:** `setup/phase-05/contracts`. Contiene contratos, fake, fixtures y `COMMON_BASE.md` con el SHA.
- **Rama de desarrollo:** `feature/phase-05/complete`. Commits:

| Commit | Contenido |
|---|---|
| `21d6f3d` | D1: capacidad eléctrica reversible y objetivo de incidentes de equipo |
| `b55ad04` | D2: autoridad de base viva, director con semilla, incidentes y tablero |
| `1070b87` | D3/D5: pipeline DONKI, episodio congelado del 2017-09-10 y cargador sin red |
| `0a5b661` | D3: cable `'T' 'L'`, adaptadores host/cliente, puerto local e intenciones |
| `8c68933` | D2/D4/D5: composición Unity, presentación, piloto, constructor de escena y validadores |

`main` no se tocó. El **push de ambas ramas está pendiente**: desde este entorno no hay credenciales git (`git push` falla). Ver «Comandos para el usuario» en `IMPLEMENTATION_REPORT.md`.

## Montaje de la escena productiva

La escena `Assets/_Project/Scenes/Phase05_LivingBase.unity` **no está versionada todavía**. Se genera en Unity con el menú `Tools/Trainer/Phase 5/Build scenario asset and scene`, o por línea de comandos con el Editor cerrado:

```sh
U=~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.BuildFromCommandLine -logFile Logs/phase05/d5/scene-build.log
```

Qué hace `Phase05SceneBuilder`:

1. Crea `Data/Scenarios/Phase05/Phase05_LivingBase_Synthetic.asset`, que referencia el escenario de fase 4 sin modificarlo.
2. **Copia** `Phase04_LivingBase.unity`, de modo que el mapa lunar, el arte, la cámara, los avatares y los controles se conservan.
3. Sustituye la composición, el bootstrap, el runner, el host y el cliente de fase 4 por los de fase 5. Copia por nombre todas las referencias serializadas compartidas y las opciones de host y cliente.
4. Elimina el piloto de fase 4.
5. Añade `LivingBaseInteractionInput`: la E de fase 4 queda desactivada y se usa como respaldo, para que una pulsación produzca una sola acción.
6. Añade el montaje `Phase05 Presentation Mount` con `LivingBasePanel` (usa el skin del panel de fase 4).
7. Añade `LivingBaseAutopilot`, inerte sin su argumento.

La escena y los assets de fase 4 no se modifican. Tras generarla, hay que versionar la escena, el asset y sus `.meta` con rutas explícitas.

## Una sola autoridad

- `LivingBaseRunner` es el único reloj: avanza la autoridad de fase 5, que compone la de fase 4, que a su vez compone la de fase 3.
- El bootstrap, el runner y el host están en un objeto que solo se activa en el proceso autoridad (`authorityOnly`). El cliente solo envía intenciones por el cable `'T' 'L'` y renderiza snapshots.
- El cliente no lee el dataset; lo hace la autoridad en `LivingBaseBootstrap`, con `HistoricalDatasetSource`, que lee archivos locales.

## Argumentos de lanzamiento (reales, implementados)

| Argumento | Efecto |
|---|---|
| `-trainer-role authority\|client` | Igual que en fase 4 (`MissionSceneComposition.RoleArgument`) |
| `-trainer-bind A`, `-trainer-port N`, `-trainer-server A`, `-trainer-connect` | Red de fase 4 sin cambios |
| `-trainer-phase5-mode historical\|synthetic` | Modo de datos (por defecto, el del asset: `Historical`) |
| `-trainer-phase5-seed N` | Semilla del director (por defecto, `20170910`) |
| `-trainer-phase5-dataset CARPETA` | Carpeta del dataset, relativa a `StreamingAssets` o absoluta. Una carpeta inexistente fuerza el fallback rotulado |
| `-trainer-timescale X` | Escala de tiempo de simulación |
| `-trainer-phase5-autopilot observer\|repair\|science` | Piloto D5; sin este argumento, el componente no hace nada |
| `-trainer-autopilot-minutes N` | Duración del piloto (por defecto 30). Al terminar escribe `[P5] RESULT PASS\|FAIL` y sale con 0 o 1 |

## Desviaciones registradas

- **Ubicación de las pruebas de cable PlayMode:** están en `Tests/PlayMode/Networking/Phase05/`, no en `Tests/PlayMode/Phase05/Networking/`. Se siguió la convención de fase 4, que guarda los codificadores `internal` bajo `UNITY_INCLUDE_TESTS`.
- **Validadores:** viven en `Editor/Build/Phase05/Phase05BuildTools.cs` (área D5), en lugar de `Editor/Validation/Phase05/`.
- **Presentación D4:** se entregó como panel IMGUI (`LivingBasePanel`) sobre el panel de fase 4. **Faltan:**
  - Prefabs y arte nuevos.
  - Marcas en el mundo del equipo afectado.
  - Ruta al refugio dibujada sobre el layout.
  - Capturas nativas 16:9, 16:10 y 4:3.

  El estado detallado está en `ACCEPTANCE.md`.
