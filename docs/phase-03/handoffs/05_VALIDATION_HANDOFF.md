# HANDOFF D5 — Fase 3: pruebas, validadores, build y aceptación entre procesos

Rama `feature/phase-03/technical-core`. Base común `35d97ceccb879142e8e60723b9558ca9c6b32f1f`. Commit de D5: `2166121`.

**Estado: NO ACEPTADA como fase terminada.**

- Lo técnico está montado y verificado en Unity nativo, en build y entre procesos (abajo).
- **Falta la entrega visual de D4**: el exterior, la esclusa y el refugio se ven con superposiciones PROVISIONALES.
- **Falta la partida con dos personas**.

## Archivos de D5

| Archivo (`Assets/_Project/…`) | Qué es |
| --- | --- |
| `Editor/Validation/Phase03/ExpeditionScenarioValidator.cs` | Validador del plano completo: cobertura de zonas, aislamiento por escotillas, alcance, capacidad, rutas, viabilidad dentro de la reserva, episodio rotulado con fin explícito |
| `Editor/Build/Phase03/Phase03BuildChecks.cs` | CLI de checks (escenario, fixture, auditoría de escena, fakes, descubrimiento de fase 2) y build de lista explícita |
| `Runtime/Unity/Validation/Phase03/ExpeditionAcceptanceScript.cs` | Jugador guionizado de los recorridos A/B: solo el puerto y las intenciones del jugador |
| `Runtime/Unity/Validation/Phase03/ExpeditionAutopilot.cs` | El mismo guion en la build. **Inerte** sin `-trainer-autopilot` |
| `Tests/EditMode/Phase03/Validation/Editor/Phase03AcceptanceFlowTests.cs` | Recorridos A y B sobre el escenario entregado y la pila real |
| `Tests/EditMode/Phase03/Validation/Editor/Phase03ScenarioValidatorTests.cs` | Validador: entregado y fixture aceptados; mutantes detectados |

## Comandos

```sh
U=~/Unity/Hub/Editor/6000.3.28f1/Editor/Unity; R=$PWD
# Escena y asset (D2; reproducible)
$U -batchmode -nographics -projectPath $R -executeMethod Trainer.Tooling.Composition.Phase03.Phase03SceneBuilder.BuildFromCommandLine -logFile $R/Logs/phase3-scene.log
# Pruebas
$U -batchmode -nographics -projectPath $R -runTests -testPlatform EditMode -testResults $R/Logs/editmode.xml -logFile $R/Logs/editmode.log
$U -batchmode -projectPath $R -runTests -testPlatform PlayMode -testResults $R/Logs/playmode.xml -logFile $R/Logs/playmode.log
# Checks (exit 1 si algo falla)
$U -batchmode -nographics -projectPath $R -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.RunChecksFromCommandLine -logFile $R/Logs/phase2-checks.log
$U -batchmode -nographics -projectPath $R -executeMethod Trainer.Tooling.BuildChecks.Phase03.Phase03BuildChecks.RunChecksFromCommandLine -logFile $R/Logs/phase3-checks.log
# Build de la escena de fase 3 (Builds/Phase3Mission/StandaloneLinux64/, con build-info.txt)
$U -batchmode -nographics -projectPath $R -buildTarget Linux64 -executeMethod Trainer.Tooling.BuildChecks.Phase03.Phase03BuildChecks.BuildFromCommandLine -logFile $R/Logs/phase3-build.log
```

## Receta host/cliente (personas)

```sh
P=Builds/Phase3Mission/StandaloneLinux64/Phase3Mission.x86_64
# Anfitrión que juega (ventana) o servidor dedicado (-batchmode -nographics); puerto solo en loopback o LAN privada
$P -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7777
# Cada jugador
$P -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect
```

Controles: WASD/flechas para moverse, **E** para interactuar con la estación o el panel de la esclusa en alcance, **R** para declarar el regreso, y la franja superior con Iniciar/Pausa/Reiniciar.

## Aceptación con dos procesos de juego (sin personas)

Tres procesos de la **misma build** en 127.0.0.1: una autoridad dedicada y dos clientes con el piloto, que juega como una persona (direcciones sostenidas e intenciones; nunca posiciones).

```sh
$P -batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7791 -logFile Logs/authority.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7791 -trainer-connect \
   -trainer-autopilot A -trainer-autopilot-role explorer -logFile Logs/explorer.log &
$P -batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7791 -trainer-connect \
   -trainer-autopilot A -trainer-autopilot-role support -logFile Logs/support.log
# Cada cliente termina con "[Autopilot] RESULT PASS|FAIL …" y exit 0/1. Cambiar A por B para el recorrido B.
```

- **Recorrido A**: el explorador prepara el traje (puesto, carga, O2 + agua, sellos), sale por la esclusa, toma la muestra de regolito, vuelve y entra. El apoyo trata agua en la base mientras tanto. Resultado esperado: `ScienceReturned`.
- **Recorrido B**: el explorador sale a los 125 s y empieza la muestra. El aviso SINTÉTICO llega a los 150 s (en cobertura). El explorador cancela, declara el regreso, vuelve, entra y va al refugio; el apoyo prepara el refugio al recibir el aviso. El fin explícito llega a los 235 s. Resultado esperado: `ShelteredInTime`. La ciencia cancelada no cuenta, y aun así el resultado es correcto.

## Resultados reales (Unity 6000.3.28f1, Linux; logs en `Logs/`, ignorado)

| Tipo de evidencia | Resultado |
| --- | --- |
| Entrada (etapa 0), sobre `7d6b914` | Reproducido: EditMode 300/302 (2 fallas de la auditoría de geometría). Corregido en `d31dd0b`: EditMode 302/302, PlayMode 56/56, checks D5 exit 0, build de fase 2 correcta. El cliente que ignoraba `-trainer-connect` también quedó corregido |
| Pureza (`Runtime/Simulation` + `Runtime/Application` solo contra `netstandard` 2.1, `-warnaserror`) | exit 0 |
| EditMode completo (fases 1, 2 y 3) | **382/382** (80 de fase 3). Tras la corrección del piloto, filtro `Phase03`: 80/80 |
| PlayMode completo (incluye sockets WebSocket reales de fases 1, 2 y 3 en un proceso) | **59/59** |
| Checks D5 de fase 2 | exit 0, sin `Failed` (siguen `Pending` la partida entre procesos y la de personas de fase 2) |
| Checks D5 de fase 3 | **32/32**. Margen del escenario entregado con traje lleno: regolito 136 s, cráter 88 s (limita el O2) |
| Build de fase 3 (Linux64, `Phase03_EvaAndShelter`) | Succeeded. `build-info.txt` dice `3748635` porque se generó desde el árbol con la corrección del piloto antes de su commit (`39c6495`); no hay otra diferencia |
| Builds de regresión de fases 1 y 2 sobre `39c6495` | Succeeded y Succeeded |
| **Recorrido A, 3 procesos de la build** (autoridad dedicada + 2 clientes, WebSocket en 127.0.0.1: 2 ESTAB, `Connected as P1/P2`) | **PASS / PASS**: `ScienceReturned` a t = 99,4 s |
| **Recorrido B, 3 procesos de la build** | **PASS / PASS**: `ShelteredInTime` a t = 235 s. Aviso recibido fuera a 150,0 s; cancelación y regreso declarado a 150,1 s; ingreso a 168,3 s; en el refugio a 175,8 s; el apoyo preparó el refugio de 150,0 a 156,1 s |

En el primer intento entre procesos, el piloto del **apoyo** falló con "no task end recorded". Era una carrera del guion de prueba: por la red, el "Accepted" llega antes que el snapshot que muestra la tarea en curso. Se corrigió en `39c6495` y se repitieron A y B completos. El primer intento de B se detuvo a mano porque ejecutaba el piloto antiguo.

Logs: `Logs/phase03-stage0/`, `phase03-regression/`, `phase03-build/`, `phase03-acceptance-{A,B}/` (`authority.log`, `explorer.log`, `support.log`, `sockets.txt`, `exit-codes.txt`).

Unity 6000.3.28f1 reescribe al importar o compilar `ProjectSettings/ProjectSettings.asset`, `Assets/Settings/*.asset` y `.vscode/settings.json` (y crea `SceneTemplateSettings.json`). Son archivos COMMON y **no se subieron**.

## Qué no se demostró

- Arte final de D4, legibilidad para niños y sonido.
- Partida con dos personas, teclado físico y render en pantalla (todo se ejecutó con `-nographics`).
- WebGL en navegador, `wss` y LAN entre dos equipos.

### Lista de comprobación con dos personas (rellenar en `d5-evidence/`)

- Cada jugador se ve y ve al otro; los astronautas caminan por la base y salen solo por la esclusa.
- El checklist se respeta, con su orden, y los costes se ven (energía de la base, agua potable, O2 de recarga).
- Nadie atraviesa una escotilla cerrada. Si dos piden la esclusa a la vez, solo uno la cicla.
- Fuera, el margen y el servicio limitante bajan; el opcional del cráter queda fuera de radio.
- El aviso aparece con hora de emisión, vigencia y recepción. Cancelar, regresar y entrar al refugio preparado protege. "Vencido" no dice "seguro".
- Resultado y Reiniciar. Desconectar a alguien fuera no deja fantasma.
