# Validación — fase 5

**Política:** cada puerta se registra como `PASA`, `FALLA` o `PENDIENTE`. Una ejecución externa (fuera de Unity) no sustituye a una nativa. Nada pendiente se presenta como aprobado.

## Entorno de esta sesión

- **Unity:** Editor 6000.3.28f1, Universal 2D.
- **Bloqueo del CLI de Unity:** el agente corre dentro de un snap y el CLI falla al cargar `libGL`. Por eso **no se ejecutó nada nativo**: ni importación del Editor, ni EditMode/PlayMode en Unity, ni validadores, ni escena, ni build, ni procesos de jugador.
- **Ejecución gestionada:** las pruebas se ejecutaron con el compilador Roslyn de Unity y un runner NUnit propio (.NET), sobre las mismas fuentes. Los archivos que usan UnityEngine o Transport se excluyen de esas corridas.
- **Comprobación de compilación:** se compilaron Assembly-CSharp y Assembly-CSharp-Editor completos con el Roslyn de Unity, usando las referencias y defines de `Library/Bee/.../*.rsp`. Esto prueba que compila, **no** que funcione en Unity.

## Resultados obtenidos (commit `8c68933`)

| Puerta | Estado | Evidencia |
|---|---|---|
| Compilación Assembly-CSharp + Editor (Roslyn de Unity) | PASA (`runtime_exit=0 editor_exit=0`) | `Logs/phase05/d5/unity-compiler-check.txt` |
| Pureza: Simulation/Application sin UnityEngine ni red | PASA | `purity.sh` exit 0 |
| Regresión gestionada pura (fases 1–5, EditMode puros) | PASA, 441/441, 0 omitidas | `Logs/phase05/d5/managed-regression-pure.txt` |
| Red fase 5: bucle por API pública (4) + cable byte a byte (4) | PASA, 8/8 | `Logs/phase05/d5/managed-network.txt` |
| Pipeline DONKI (Python) | PASA, 12/12 | `Logs/phase05/d3/pipeline-tests.txt` |
| EditMode nativo completo | PENDIENTE | bloqueo libGL |
| PlayMode nativo | PENDIENTE | bloqueo libGL |
| Generación de escena `Phase05_LivingBase.unity` | PENDIENTE | requiere Unity |
| Validadores `Phase05BuildTools.RunChecks` | PENDIENTE | requiere Unity |
| Build Linux | PENDIENTE | requiere Unity |
| Autoridad + 2 clientes en procesos reales | PENDIENTE | requiere build |
| Corrida de ≥30 min reales | PENDIENTE | requiere build |
| Corrida con fallback (dataset ausente) | PENDIENTE | requiere build |
| Sesión humana | PENDIENTE | sin participantes (`HUMAN_SESSION.md`) |

Logs anteriores: `Logs/phase05/d2/managed-regression.txt` y `Logs/phase05/d3/*.txt`. Están en `Logs/`, que git ignora; se conservan localmente.

## Catálogo de pruebas de fase 5 (82)

| Archivo | Pruebas | Cubre |
|---|---|---|
| `Tests/EditMode/Phase05/Contracts/Editor/Phase05ContractTests.cs` | 21 | Contratos, fake y fixtures |
| `Tests/EditMode/Phase05/Simulation/Editor/Phase05PowerModifierTests.cs` | 13 | Capacidad reversible; quitar un modificador no devuelve pérdidas; sin modificadores la base es idéntica a fase 4 |
| `Tests/EditMode/Phase05/Simulation/Editor/Phase05EquipmentIncidentTests.cs` | 11 | Degradación y reparación del reciclador; aislamiento y repuestos |
| `Tests/EditMode/Phase05/Application/Editor/Phase05DirectorAndPowerTests.cs` | 12 | Gracia, pausa, semilla, antirrepetición, disparo del convertidor, fallo del controlador, ids e idempotencia, ocupación, abandono, reinicio de época, calendario oculto |
| `Tests/EditMode/Phase05/Application/Editor/Phase05SpaceWeatherAndBoardTests.cs` | 10 | Episodio sintético rotulado, refugio protegido o no, histórico, fallback, modo actual rechazado, sin incidente eléctrico durante un episodio, tablero renovable, encargo cumplido una vez y mejora pagada |
| `Tests/EditMode/Phase05/Application/Editor/Phase05HistoricalDatasetTests.cs` | 4 | Dataset versionado: procedencia, cualquier byte cambiado da `dataset-hash-mismatch`, claves estables para archivos ausentes o rotos, reproducción sin red sin mostrar la clase de la fulguración antes de su emisión |
| `Tests/EditMode/Phase05/Networking/Editor/Phase05NetworkLoopbackTests.cs` | 4 | Clientes con la autoridad real por la API pública: identidades distintas e incorporación tardía, reparación eléctrica que responde solo al emisor, desconexión que abandona la solicitud pendiente, el cliente solo ve los registros publicados |
| `Tests/PlayMode/Networking/Phase05/Phase05NetworkWireTests.cs` | 4 | Cable `'T' 'L'`: una hora sintética ida y vuelta exacta (snapshot máximo de 2338 B), históricos con procedencia, comandos ajenos o rotos rechazados, snapshots truncados nunca publicados |
| `Tests/EditMode/Phase05/Validation/Editor/Phase05DeterminismAndVarietyTests.cs` | 3 | Ver abajo |

### Determinismo, variedad y garantías del director (D5)

Resultado de la ejecución gestionada:

- **Misma semilla:** dos horas simuladas producen la misma secuencia de huellas publicadas, en modo sintético y en histórico.
- **Semillas distintas:** de 12 semillas, al menos 6 historias de incidentes son distintas en una hora.
- **Garantías durante dos horas:** semillas 3, 11 y 29, con una tripulación que repara la energía como un jugador. El director tomó 24, 19 y 17 decisiones. En todas las instantáneas publicadas se cumplieron:
  - presupuesto de incidentes simultáneos;
  - antirrepetición;
  - cooldown por clave;
  - descanso tras cada resolución;
  - gracia inicial;
  - ningún incidente eléctrico iniciado durante un episodio abierto.
- **Sin reparaciones,** el presupuesto se satura con 2–3 incidentes. Ese caso no se usa como prueba de las garantías.

## Pendiente de cobertura (identificado, no implementado)

- **Conservación cruzada completa:** agua potable, sin tratar, producto y residuo; O2/CO2; gas de esclusa; trajes con pasos de tiempo distintos. La cubren en parte las pruebas de fases 2–4 y las de D1, pero no hay una prueba de fase 5 dedicada.
- **Escenarios adversos con procesos reales:** desconexión durante una reparación, incorporación tardía o mensaje antiguo. El bucle gestionado cubre la incorporación tardía y la desconexión; las pruebas de aplicación cubren la idempotencia de ids.
- **Detector de estancamiento en la corrida larga:** existe en el piloto (falla si el reloj no avanza o retrocede), pero no se ha ejecutado.

## Comandos nativos para el usuario

Están en `IMPLEMENTATION_REPORT.md`, sección «Comandos para el usuario». Ejecutarlos con el Editor cerrado y guardar XML y logs en `Logs/phase05/native/`.
