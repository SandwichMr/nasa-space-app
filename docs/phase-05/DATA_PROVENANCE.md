# Procedencia de datos — fase 5

## Modos

| Modo | Fuente | Red en ejecución |
|---|---|---|
| `Historical` (por defecto) | Episodio DONKI congelado en `Assets/StreamingAssets/ScientificData/Phase05/donki-2017-09-10/` | Ninguna; lectura de archivo local |
| `Synthetic` | Generador con semilla (`SyntheticEpisodeRules` del escenario) | Ninguna |
| Actual / observación en vivo | **No se ofrece.** La autoridad lo rechaza (prueba `CurrentObservationModeIsRefused`) | — |

Si los archivos históricos faltan, son inválidos o el hash no coincide, la sesión cae a episodios **sintéticos rotulados** y publica la clave del problema:

- `dataset-missing`
- `dataset-manifest-invalid`
- `dataset-hash-mismatch`
- `dataset-schema-unsupported`
- `dataset-invalid`

Nunca cae a «sin peligro». La insignia de datos lo muestra.

## Episodio histórico: 10 de septiembre de 2017 (región activa 12673)

- **Id:** `donki-2017-09-10-ar12673`.
- **Fuente:** API DONKI de NASA GSFC CCMC/SWRC (`https://ccmc.gsfc.nasa.gov/DONKI-API/get/...`).
- **Descarga:** consultas acotadas, sin clave, una sola vez el 2026-11-16 a las 04:34 UTC.
- **Registro de solicitudes:** `tools/data-pipeline/phase05/raw/2017-09-10/requests.jsonl`. Guarda URL, estado, bytes y SHA-256 de cada respuesta. Los mismos datos se copian en `manifest.json`.
- **Consultas:**
  - FLR del 2017-09-10.
  - SEP del 2017-09-10 al 11.
  - Notificaciones SEP y FLR del 2017-09-10 al 11.
  - Notificaciones SEP del 2017-09-12.
- **Licencia y aviso (copiados en el manifiesto):** DONKI es «Experimental Research Information» y se ofrece tal cual (descargo del SWRC). NOAA SWPC es la fuente oficial de pronósticos de clima espacial de EE. UU. Hay que citar CCMC/DONKI, y el uso no implica aval.
- **Registros conservados (se citan, no se reinterpretan):**
  - Fulguración `2017-09-10T15:35:00-FLR-001`.
  - SEP `2017-09-10T16:25:00-SEP-001` y `2017-09-10T16:45:00-SEP-001`.
  - Notificaciones `20170910-AL-001`, `-002`, `-004` y `-005`, más `20170912-AL-001`.
- **SHA-256 en el commit `1070b87`:**
  - `episode.json`: `ad3b540d6f9379f8d3c4321798e661a9452ae4f57ab06c617901280ab4da39a1`.
  - `manifest.json`: `527b646e7973085907f462ae353886fbbfdab20987a46efc5c94034bba2db748`.
  - El manifiesto guarda el hash del episodio, y `HistoricalEpisodeLoader` lo verifica antes de usar el archivo (límite: 256 KB por archivo).

## Qué es dato real y qué es decisión didáctica

Cada mensaje lleva un campo `basis`, y la interfaz lo muestra:

- **`SourceNotification`:** el mensaje cita texto real de DONKI. Ejemplo: la vigilancia por fulguración `hist-watch`.
- **`DidacticDecision`:** la instrucción de refugio, sus actualizaciones y su fin (`hist-instruction`, `hist-update-*`, `hist-end`). Son decisiones del escenario para niños, **no** órdenes operativas reales.

Otras reglas del episodio:

- **Cronología comprimida:** inicio didáctico a los 18 s y fin a los 180 s. Está documentado en `timelineNote` y en `tools/data-pipeline/phase05/timeline_2017-09-10.json`. No reconstruye la dosis local ni procedimientos reales de EVA. Las ubicaciones terrestres no se trasladan a la base lunar.
- **Fenómenos separados:** fulguración, CME, SEP y GCR son tipos distintos; no se convierte uno en otro.
- **Sin dosis:** no se calcula dosis ni se muestran umbrales clínicos.
- **Desconocido no es cero:** un valor desconocido se muestra como desconocido.
- **Caducidad no es «todo bien»:** al caducar un mensaje se muestra «sin información vigente».
- **Sin datos futuros:** un registro se publica solo cuando su hora didáctica de emisión ya pasó, así que el cliente nunca recibe picos o finales futuros. Lo cubre la prueba `TheSnapshotNeverPublishesTheHiddenSchedule`.
- **`submissionTime`:** no se trata como primera publicación universal; la hora de emisión es una decisión didáctica declarada.

## Reproducción

```bash
cd tools/data-pipeline/phase05
python3 donki_collect.py          # opcional: requiere red; escribe raw/ y requests.jsonl (sin clave, acotado)
python3 normalize_episode.py      # raw/ + cronología → episode.json + manifest.json en StreamingAssets
python3 -m unittest discover tests
```

- **Determinismo:** la normalización es determinista; los mismos datos crudos y la misma cronología producen bytes idénticos.
- **Pruebas:** las 12 pruebas Python pasaron (`Logs/phase05/d3/pipeline-tests.txt`). `Phase05HistoricalDatasetTests` (4 pruebas) carga los archivos versionados y comprueba hash, esquema y tipos.

No hay claves, tokens, direcciones privadas ni datos personales en los datos crudos, el manifiesto ni los logs.
