# Base común — fase 5

Preparación D2-A con revisión científica D5, publicada en la rama `setup/phase-05/contracts`. Es un **hito técnico**, no la finalización de la fase.

## Git

| Dato | Valor |
| --- | --- |
| Remoto | `https://github.com/SandwichMr/nasa-space-app.git` (el prompt cita `space-station-breakdown.git`; no se cambió el origin) |
| `main` de partida | `4903c587da1590d35d7a937cdc4f596d3dfe0512` (fast-forward con `origin/main`, árbol limpio tras descartar, con autorización del usuario, 4 cambios locales: espacio en `MissionBootstrap.cs`, paquete `com.unity.recorder` y su lock, iconos VisionOS en `ProjectSettings.asset`) |
| Commit de contratos | ver la nota del commit siguiente (un commit no puede contener su propio SHA) |
| Rama de desarrollo | `feature/phase-05/complete`, creada desde el SHA publicado de esta rama |

## Alcance entregado

| Entrega | Ruta |
| --- | --- |
| Contratos físicos D1 (modificadores eléctricos, objetivo de incidentes del equipo) | `Assets/_Project/Runtime/Simulation/Contracts/Incidents/IncidentEffects.cs` |
| Contratos de aplicación (datos/procedencia, episodios, escenario y catálogo, estado y snapshot, comandos, puertos y montaje D4) | `Assets/_Project/Runtime/Application/Contracts/Incidents/` (6 archivos) |
| Fake y fixtures rotuladas | `Assets/_Project/Runtime/Application/Contracts/Incidents/Development/` (2 archivos) |
| Pruebas de la base | `Assets/_Project/Tests/EditMode/Phase05/Contracts/Editor/Phase05ContractTests.cs` (21 pruebas) |
| Documentación | `docs/phase-05/SESSION_CONTRACT.md`, `OWNERSHIP.md` y este archivo |

Los `.meta` se escribieron con el formato de Unity 6000.3.28f1 de este repositorio, GUID aleatorios nuevos.

## Verificaciones ejecutadas

| Comprobación | Resultado |
| --- | --- |
| Pureza: `Runtime/Simulation/**` + `Runtime/Application/**` compilados solo contra `netstandard` 2.1, `-warnaserror` (CS1591/CS1573 excluidos), Roslyn y .NET 6.0.21 del Editor 6000.3.28f1 | exit 0 |
| Pruebas con runner por reflexión fuera de Unity (NUnit `com.unity.ext.nunit` del proyecto, .NET 6.0.21 del Editor): fase 5 + regresión de contratos de fases 2–4 | **105 / 105**, 0 fallos, 0 omitidas (fase 5: 21; fase 4: 31; fase 3: 19; fase 2: 34) |
| **Unity nativo** (importación, `Assembly-CSharp`, Test Runner) | **No ejecutado en este hito**: el Editor del usuario estaba abierto sobre el proyecto (PID observado 75680) y no se lanza el CLI en paralelo. Queda para `feature/phase-05/complete`. |

Logs: `Logs/phase05/setup/purity.txt` y `managed-contract-tests.txt` (ignorados por Git).

## Revisión científica D5 de esta base (datos y tiempos)

Consultas reales y acotadas al servidor oficial (sin API key: la especificación OpenAPI publicada no declara `security`). Fecha de las consultas: 2026-11-16 UTC.

| Consulta | Estado | Bytes | SHA-256 del cuerpo |
| --- | --- | --- | --- |
| `https://ccmc.gsfc.nasa.gov/DONKI/api/openapi/` (especificación DONKI-API 2.0, servidor `https://ccmc.gsfc.nasa.gov/DONKI-API`) | 200 | 55295 | `16d2253db190cd4e8673ffc40f3d7fae2facde48cef09ef4ce93157c5705430d` |
| `…/DONKI-API/get/FLR?startDate=2017-09-10&endDate=2017-09-10` | 200 | 1468 | `fb20b2d23bc702c98eda830dc6d6f9e5b1cd320a0f220aacab29d0ffcc67b770` |
| `…/get/SEP?startDate=2017-09-10&endDate=2017-09-11` | 200 | 1943 | `012f648d8d6925bfe68fc1d2839c65edabd3c09bc6c943b135fa658f7e647141` |
| `…/get/notifications?startDate=2017-09-10&endDate=2017-09-11&type=SEP` | 200 | 14265 | `28e1d1d13d1fdc054bef9102959e567aed604f14232c8231f4d18bcd18492bb4` |
| `…/get/notifications?startDate=2017-09-10&endDate=2017-09-10&type=FLR` | 200 | 6955 | `5bc5eab57a60cb9e12ffd7609db02809aa3aa6fdc2edc505e059c72456d24f11` |
| `…/get/notifications?startDate=2017-09-12&endDate=2017-09-12&type=SEP` | 200 | 7659 | `0f53196fa56b3ed8ff955ed25f9f4c42e28aaa1038159c9bb80500d5928a7106` |

Parámetros verificados en la especificación: `startDate`/`endDate` (`yyyy-MM-dd`, ventana máxima de 60 días), `type` en notificaciones (FLR, SEP, CME, …), `catalog` y `version` en FLR/SEP. HTTP 200 no prueba idoneidad científica: la idoneidad se revisó leyendo los registros.

**Episodio recomendado:** 10 de septiembre de 2017, región activa 12673. Tiene registros separados y trazables de cada fenómeno, sin convertir uno en otro:

- FLR `2017-09-10T15:35:00-FLR-001`: clase X8.2, GOES-15 XRS 1–8 Å, pico 16:06Z (versión 4, `submissionTime` 23:46Z: la clase definitiva no estaba disponible al principio).
- SEP `2017-09-10T16:25:00-SEP-001` (GOES-13 >100 MeV) y `2017-09-10T16:45:00-SEP-001` (GOES-13 >10 MeV), medidos en órbita geoestacionaria terrestre; `2017-09-10T20:00:00-SEP-001` (SOHO COSTEP, L1).
- Notificaciones con hora de emisión demostrable: `20170910-AL-001` (16:07Z, flare ≥ M5.0, «posible aumento de protones»), `-AL-002` (16:41Z, >100 MeV > 1 pfu), `-AL-003` (16:47Z, X8.2 confirmado), `-AL-004` (16:58Z, >10 MeV > 10 pfu), `-AL-005` (17:08Z, resumen y asociación), `-AL-008` (22:56Z, SOHO), `20170912-AL-001` (12 sep 14:53Z: evento todavía en curso, ~200 pfu).

Decisiones para el contrato (ya reflejadas en los tipos):

1. La primera disponibilidad demostrable de cada mensaje es su `messageIssueTime`; la de un registro de catálogo es su `submissionTime`. No se presentan como primera publicación universal.
2. La clase X8.2 se conoció después de la primera alerta (M5.0 a las 16:07Z). Una partida que reproduce las 16:07Z no puede mostrar «X8.2»: solo se publican mensajes emitidos.
3. DONKI no emite un «fin» del evento SEP en esta ventana; el 12 de septiembre seguía en curso. El fin del periodo de refugio del juego es una **decisión didáctica** (`MessageBasis.DidacticDecision`), nunca un all-clear de la NASA. El tipo `EpisodeMessage` lo impone.
4. Las mediciones son de GOES (órbita terrestre) y SOHO (L1), no de la superficie lunar. Los pfu no se convierten en dosis ni en daño; la respuesta del juego es cualitativa.
5. Política de uso: los mensajes incluyen el descargo de SWRC/CCMC («Experimental Research Information… provided as is»; NOAA SWPC es la fuente oficial de pronósticos de EE. UU.). Los términos se citarán en `DATA_PROVENANCE.md` con el dataset congelado.

## Cómo se incorpora la rama de desarrollo

```sh
git status --short --branch          # sin cambios ajenos
git fetch origin --prune
git merge-base --is-ancestor origin/main <SHA_COMUN> && echo contiene-main
git switch -c feature/phase-05/complete <SHA_COMUN>
```

## Límites de esta base

- Unity nativo no se ejecutó (Editor abierto). Los `.meta` deben confirmarse en la primera importación nativa.
- El episodio histórico real aún no está congelado en `StreamingAssets` (D5 en la rama de desarrollo); la fixture histórica de `LivingBaseFixtures` es explícitamente de desarrollo.
- Ningún comportamiento de juego existe todavía: director, efectos, red, vistas y escena son trabajo de `feature/phase-05/complete`.
