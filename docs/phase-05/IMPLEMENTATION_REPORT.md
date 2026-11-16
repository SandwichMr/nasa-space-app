# Informe de implementación — fase 5

**Ejecutor:** un único agente para D1–D5 (`FASE_05_PROMPT_UNICO_COMPLETO`).

**Ramas:**

- `setup/phase-05/contracts`: preparación.
- `feature/phase-05/complete`: desarrollo; último commit de código `8c68933`.

`main` no se tocó. Estado global: ver `ACCEPTANCE.md`; resultados: ver `VALIDATION.md`.

## D1 — Efectos físicos (`21d6f3d`)

- **Qué hace:**
  - `Runtime/Simulation/Incidents/**`: modificadores de capacidad eléctrica reversibles (merma de generación, límite de descarga, controlador aislado) y objetivo de incidentes de equipo (degradación del reciclador con temporizador de fallo, reparación, repuestos acotados, mejora de recuperación con techo).
  - Cambios mínimos en `OutpostBaseSimulation`, `OutpostPowerDispatch` y `BaseEquipmentSimulation`. **Sin modificadores, el resultado es idéntico bit a bit a fase 4** (`WithoutModifiersTheBaseIsBitForBitPhase4`, `ThePhase4ScriptedFaultIsUnchangedWhenNoPhase5EffectIsUsed`).
- **Garantías:**
  - Quitar un modificador no devuelve la energía perdida.
  - Las tasas no dependen de cómo se parte el paso de tiempo.
  - Los ids son idempotentes por reinicio.
  - Un rechazo no consume su id.
- **Pruebas:** `Tests/EditMode/Phase05/Simulation/**` (24).

## D2 — Autoridad, director, contenido y escena (`b55ad04`, `8c68933`)

- **Autoridad:** `Runtime/Application/Incidents/**`. `LivingBaseAuthority` compone `BaseOperationsAuthority` de fase 4 sin cambiarla.
- **Director con semilla:**
  - Gracia inicial, presupuesto de simultáneos, cooldown por clave, antirrepetición, descanso tras resolución y viabilidad.
  - Cada evaluación queda registrada.
  - El calendario oculto no se publica.
- **Incidentes:** `processor-filter-clog`, `array-converter-trip`, `discharge-controller-fault`, `space-weather`.
- **Tablero científico renovable:** `regolith-survey` y `crater-core-drill`, con créditos.
- **Mejora acotada:** `high-recovery-regulator`.
- **Unity:**
  - `LivingBaseScenarioDefinition`.
  - `LivingBaseBootstrap` y `LivingBaseRunner`: un solo reloj.
  - `LivingBaseSceneComposition`.
  - `Editor/Composition/Phase05/Phase05SceneBuilder.cs`.
- **Pruebas:** `Tests/EditMode/Phase05/Application/**` (26).
- **Pendiente:** generar y versionar la escena y el asset en Unity (`INTEGRATION.md`).

## D3 — Datos externos y red (`1070b87`, `0a5b661`)

- **Pipeline DONKI:** `tools/data-pipeline/phase05/**`. Colector acotado sin clave, normalizador determinista y 12 pruebas Python.
- **Cargador:** `HistoricalEpisodeLoader`, puro, con hash verificado y claves de problema estables. `HistoricalDatasetSource` lee archivos locales en Unity.
- **Cable `'T' 'L'`:** reutiliza los mensajes de fase 4 re-etiquetados y añade el comando de incidente (tipo 5) y la extensión de snapshot. Reensamblado acotado a 64 KB.
- **Red e intenciones:** `LivingBaseNetworkHost/Client/SessionAdapter/CommandBridge`, `LocalLivingBaseSessionPort`, `LivingBaseInteractionClient` y la tecla E de fase 5 (`LivingBaseInteractionInput`).
- **Pruebas:** 4 de dataset, 4 de bucle y 4 de cable (`VALIDATION.md`).

## D4 — Presentación (`8c68933`), PARCIAL

- **Entregado:**
  - `Runtime/Unity/Presentation/Incidents/LivingBasePanel.cs`, panel IMGUI por encima del HUD de fase 4. Columna izquierda: insignia HISTÓRICO/SINTÉTICO y fallback, incidentes, respuesta, tarjeta de clima espacial con base y vigencia, botón de fuentes, tablero científico y mejora. Panel lateral de estación: acciones eléctricas, mejora, progreso y cancelar.
  - `LivingBaseText.cs`: textos breves en español. Distingue «caducado: sin información vigente» de la condición normal, y lo desconocido de cero.
  - Escape cierra el panel y libera el bloqueo de movimiento.
- **Receta de montaje:**
  - `LivingBasePanel` va bajo `Phase05 Presentation Mount`.
  - Referencias obligatorias: `phase4` (`BaseOperationsPanel`) y `skin`.
  - `LivingBaseSceneComposition` lo vincula como `ILivingBaseView`/`ILivingBaseStationView`.
  - Lo hace automáticamente `Phase05SceneBuilder`.
- **No entregado:**
  - Prefabs y arte nuevos.
  - Marcas en el mundo (equipo afectado, ruta al refugio sobre el layout).
  - Efectos visuales de degradación y aislamiento.
  - Sonido.
  - Pruebas de presentación.
  - Capturas nativas.
- **Riesgo:** el panel no se ha visto en Unity; solo está compilado.

## D5 — Validación y builds (`1070b87`, `8c68933`)

- **Datos congelados y procedencia:** `DATA_PROVENANCE.md`.
- **`Editor/Build/Phase05/Phase05BuildTools.cs`:** comprobaciones de asset, dataset, fallback y escena (una sola autoridad y ningún componente de fase 4 activo), más una build Linux con lista de escenas explícita. Escribe en `Logs/phase05/d5/`.
- **Piloto `Runtime/Unity/Validation/Phase05/LivingBaseAutopilot.cs`:**
  - Es inerte sin su argumento.
  - Roles: `observer`, `repair` (navega al panel eléctrico y repara; va al refugio si hay instrucción en vigor) y `science` (acepta encargos).
  - Usa el mismo puerto y las mismas intenciones que el teclado.
  - Registra `[P5]` con semilla, hash de configuración, estado de los datos, incidentes, decisiones y avisos.
  - Al terminar escribe `RESULT PASS|FAIL`.
- **Pruebas de determinismo y variedad:** 3 (`VALIDATION.md`).

## Comandos para el usuario

Desde la raíz del proyecto y **con el Editor cerrado**:

```sh
U=~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity; L=Logs/phase05/native; mkdir -p $L

# 0. Publicar las ramas (nunca main) y verificar los SHA remotos
git push -u origin setup/phase-05/contracts
git push -u origin feature/phase-05/complete
git ls-remote origin setup/phase-05/contracts feature/phase-05/complete

# 1. Generar el asset y la escena (y luego versionarlos con rutas explícitas)
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.BuildFromCommandLine -logFile $L/scene.log
git add -- Assets/_Project/Scenes/Phase05_LivingBase.unity Assets/_Project/Scenes/Phase05_LivingBase.unity.meta \
  Assets/_Project/Data/Scenarios/Phase05.meta Assets/_Project/Data/Scenarios/Phase05/

# 2. Pruebas nativas
"$U" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults $L/editmode.xml -logFile $L/editmode.log
"$U" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults $L/playmode.xml -logFile $L/playmode.log

# 3. Validadores (exit 0 = pasa, 1 = falla) y build Linux (solo se construye si los validadores pasan)
"$U" -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.Build.Phase05.Phase05BuildTools.RunChecksFromCommandLine -logFile $L/checks.log
"$U" -batchmode -nographics -projectPath . -buildTarget Linux64 -executeMethod Trainer.Tooling.Build.Phase05.Phase05BuildTools.BuildLinuxFromCommandLine -logFile $L/build.log
sha256sum Builds/Phase5LivingBase/Linux/Phase5LivingBase.x86_64 Builds/Phase5LivingBase/Linux/Phase5LivingBase_Data/Managed/*.dll > $L/build-manifest.sha256

# 4. Autoridad + dos clientes durante 30 minutos reales (modo histórico)
P=Builds/Phase5LivingBase/Linux/Phase5LivingBase.x86_64
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7795 -trainer-phase5-autopilot observer -trainer-autopilot-minutes 31 -logFile $L/authority.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect -trainer-phase5-autopilot repair -trainer-autopilot-minutes 30 -logFile $L/repair.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7795 -trainer-connect -trainer-phase5-autopilot science -trainer-autopilot-minutes 30 -logFile $L/science.log
grep "\[P5\]" $L/*.log | grep -E "setup|RESULT|incident|advisory|assignment"

# 5. Fallback: dataset ausente → sintético rotulado y red de jugadores operativa
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7796 -trainer-phase5-dataset missing -trainer-phase5-autopilot observer -trainer-autopilot-minutes 6 -logFile $L/fallback-authority.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7796 -trainer-connect -trainer-phase5-autopilot repair -trainer-autopilot-minutes 5 -logFile $L/fallback-repair.log
grep "\[P5\] setup" $L/fallback-*.log   # se espera data=FallbackSynthetic(dataset-missing)
```

Para jugar con personas: la misma autoridad y los clientes con gráficos, sin `-trainer-phase5-autopilot`. Seguir `HUMAN_SESSION.md`.

## Defectos o riesgos conocidos

- **Panel sin ver en Unity:** puede tener problemas de layout o solapes con el HUD de fase 4.
- **Constructor de escena sin ejecutar:** si algún campo serializado de fase 4 tiene otro nombre, el constructor lanza una excepción clara con el nombre del campo. No deja una escena a medias, porque solo guarda al final.
- **Rol `repair` del piloto sin ejecutar:** usa el navegador de fase 4 y puede atascarse en geometría no prevista. En ese caso el registro mostrará el incidente abierto sin acciones.
