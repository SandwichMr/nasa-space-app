# Aceptación — fase 5

**Dictamen: NO aceptada todavía.**

- El código, los datos y las pruebas gestionadas están completos para los requisitos P0.
- Faltan todas las puertas nativas (escena generada, EditMode/PlayMode en Unity, build Linux, dos clientes, 30 minutos, fallback) y la sesión humana. Además, la presentación D4 es parcial (ver abajo).
- Esta revisión la hizo el mismo ejecutor que escribió D1–D4. **No es una revisión humana independiente.**

## Checklist del prompt

| Punto | Estado | Evidencia o motivo |
|---|---|---|
| Contratos publicados y verificados; documentación coherente | Hecho; push pendiente | `setup/phase-05/contracts`, `COMMON_BASE.md`, 21 pruebas de contrato |
| Tres tipos P0 de contratiempo con decisiones y recuperación reales | Hecho (gestionado) | Reciclador: degradación y reparación. Energía: disparo del convertidor y reset tras inspección; fallo del controlador con aislar, repuesto y restaurar. Clima espacial histórico y sintético con decisión de refugio o retorno evaluada |
| Director reproducible con presupuesto, cooldown, antirrepetición y descanso | Hecho (gestionado) | `Phase05DeterminismAndVarietyTests`, `Phase05DirectorAndPowerTests` |
| NASA histórico trazable + sintético sin red y fallback; sin falsa predicción ni dosis | Hecho (gestionado) | `DATA_PROVENANCE.md`, `Phase05HistoricalDatasetTests`, `TheSnapshotNeverPublishesTheHiddenSchedule` |
| Adquisición, normalización y caché sin claves en el cliente | Hecho | `tools/data-pipeline/phase05/**`; el cliente no lee datos externos |
| Ciencia renovable y mejora acotada sin premios duplicados ni fin global | Hecho (gestionado) | Pruebas del tablero y `ARealSortieFulfilsTheAcceptedAssignmentOnceAndPaysTheUpgrade` |
| UI legible: avisos, insignia de datos, panel eléctrico, encargos, fuentes | **Parcial** | Panel IMGUI `LivingBasePanel` + `LivingBaseText`, compilado pero **no visto en Unity**. Faltan prefabs y arte nuevos, marca de equipo afectado en el mundo, ruta al refugio sobre el layout, efectos visuales de degradación y aislamiento, y capturas 16:9, 16:10 y 4:3 |
| Escena productiva sobre la base actual, sin fakes ni segunda autoridad | **Pendiente** | El constructor existe (`Phase05SceneBuilder`); la escena no se ha generado ni versionado |
| Pruebas nativas, build y dos clientes | **Pendiente** | Bloqueo de libGL en este entorno |
| Corrida de ≥30 min y fallback comprobado | **Pendiente** | Requiere build; el piloto `-trainer-phase5-autopilot` está listo |
| Sesión humana | **Pendiente** | `HUMAN_SESSION.md` |
| `feature/phase-05/complete` publicada, SHA remoto verificado, `main` intacto | **Pendiente** (push); `main` intacto | Sin credenciales git en este entorno |

## Límites declarados

- No hay modo «actual»: la autoridad lo rechaza.
- **Cronología comprimida:** el episodio histórico usa una cronología didáctica. La instrucción de refugio es una decisión del juego, rotulada como tal.
- **Sin modelo de dosis:** no hay dosis, umbrales clínicos ni porcentajes de blindaje. La protección se confirma por estar dentro del refugio preparado, según las reglas de fase 3.
- **Sin retardo en el movimiento:** el retardo lunar de 1,6 s no se aplica al movimiento.
