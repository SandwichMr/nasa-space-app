# HANDOFF D1 — Fase 3: servicios del traje, consumo y margen de regreso

Rama `feature/phase-03/technical-core` (D1, D2, D3 y D5 en una misma tarea técnica). Base común: `35d97ceccb879142e8e60723b9558ca9c6b32f1f`. Commit de D1: `fb0cefa`.

## Archivos

| Archivo (`Assets/_Project/…`) | Qué es |
| --- | --- |
| `Runtime/Simulation/Eva/EvaSuitSimulation.cs` | `IEvaSimulation`: consumo de los trajes sellados, eventos de reserva/cero, transferencias idempotentes, libro mayor |
| `Runtime/Simulation/Eva/ReturnMarginCalculator.cs` | Regla única del margen: por servicio, limitante y nunca promedio |
| `Tests/EditMode/Phase03/Simulation/Editor/Phase03EvaSuitSimulationTests.cs` | 9 pruebas del modelo |
| `Tests/EditMode/Phase03/Simulation/Editor/Phase03ReturnMarginTests.cs` | 5 pruebas del margen |

Los contratos (`Runtime/Simulation/Contracts/Eva/`) son de la base común. El modelo de base de fase 2 (`OutpostBaseSimulation`) **no cambia**.

## Modelo educativo reducido (declarado)

Hay tres servicios por traje, con su unidad, capacidad y una **reserva declarada** que el plan no debe usar. La reserva es un valor de escenario, no un estándar de seguridad.

| Servicio | Unidad | Consumo | Se repone con |
| --- | --- | --- | --- |
| `Oxygen` | kg | `Rest`/`Walk`/`Work` kg/s | `FillOxygen`, desde el **suministro de recarga EVA** |
| `Battery` | J | `Rest`/`Walk`/`Work` W, más la herramienta de la tarea (solo aquí) | `ChargeBattery`: energía del cargador de la base |
| `Feedwater` | kg | `Rest`/`Walk`/`Work` kg/s (calor rechazado por evaporación) | `FillFeedwater`: agua potable de la base |

- **Integración exacta**: dentro de un `Advance` la actividad es constante. El consumo es `min(nivel, tasa × t)`, independiente del paso (probado con 1 paso de 40 s frente a 160 de 0,25 s). Nunca baja de 0. La demanda que encuentra un servicio vacío queda como no servida en el libro mayor y **nunca** se toma de otro servicio ni de otro traje. **No existe transferencia de O2 ni de batería entre trajes.**
- **Eventos**: `SecondsUntilServiceEvent` da el cruce de la reserva y después el de cero. La autoridad corta su paso ahí.
- **Libro mayor**: cierra por servicio: nivel(t) = nivel(0) + llenado/almacenado − consumido. La carga cumple almacenado + no almacenado = entregado: lo que no cabe es calor, ni se crea ni se guarda.

## Quién calcula qué y cuándo (sin doble conteo)

| Coste | Sistema | Cuándo | Al cancelar |
| --- | --- | --- | --- |
| Metabolismo de quien **no** está sellado | base de fase 2 (`OutpostDemand.CrewAboard`) | cada paso | — |
| Metabolismo de quien **está** sellado | `EvaSuitSimulation.Advance`, solo los trajes que lista la autoridad | cada paso | lo consumido no vuelve |
| Herramienta de una tarea exterior | batería del traje (`ToolWatts`) | mientras progresa | no vuelve |
| Energía del cargador | base (carga de servicio de la tarea `charge-suit`) | mientras progresa | no vuelve; el traje no recibe nada |
| Energía almacenada en el traje | `Apply(ChargeBattery)`, una vez por id | al completar | — |
| Agua potable → traje | base `Apply(ResourceTransfer)` (−X potable, +X salida por frontera), después `Apply(FillFeedwater)` con el mismo id | al completar | nada |
| O2 de recarga → traje | `Apply(FillOxygen)` (id derivado) | al completar | nada |
| Gas de reposición de la esclusa | `Apply(UseMakeupGas)` | una vez al empezar cada represurización | — |
| Quién está sellado, actividad, progreso, cuándo aplicar | autoridad (D2) | — | — |

## Margen de regreso

```
utilizable = nivel − reserva
necesario  = tasa Walk × recorrido + tasa Rest × (espera de la esclusa + represurización)
margen     = (utilizable − necesario) / tasa actual        (s; la tasa actual incluye la herramienta)
```

- **Margen del miembro**: el **menor** de los tres. El primero que se queda corto es el limitante; empata el orden declarado. Nunca es un promedio; el contrato lo rechaza.
- **Estado**: `Sufficient` (≥ 0; el 0 exacto es el límite), `Insufficient` (< 0: ya se usa la reserva) o `ReturnUnavailable` (sin gas para la represurización, aunque sobren servicios).
- **Por qué es conservador**: la reserva nunca cuenta como utilizable; se incluyen la espera y el ingreso; la ruta la da una malla de 0,25 m (D2) y nunca es más corta que la línea recta.
- **Equipo**: la autoridad publica el margen de cada sellado, y el equipo es el **peor**.
- **Validez**: cada estimación vale para el estado confirmado de `ComputedAtSeconds`.

## Valores del escenario entregado (SINTÉTICOS, comprimidos para que una salida dure minutos)

| Servicio | Capacidad | Reserva | Rest / Walk / Work | En el soporte al inicio |
| --- | --- | --- | --- | --- |
| O2 | 0,40 kg | 0,08 kg | 0,0016 / 0,0022 / 0,0030 kg/s | 0,12 kg |
| Batería | 5000 J | 1000 J | 18 / 20 / 22 W (+10 W regolito, +20 W cráter) | 2600 J |
| Agua de refrigeración | 0,80 kg | 0,16 kg | 0,0012 / 0,0028 / 0,0045 kg/s | 0 kg |

Suministros: recarga de O2 2,0 kg y gas de reposición 3,0 kg (0,30 kg por represurización). Cargador: 200 W × 12 s, eficiencia 1.

Medido por el validador de D5 (traje lleno, directo desde la esclusa a cada sitio, la tarea y el margen en reposo): regolito, 136 s; cráter, 88 s. En ambos limita el O2. Ir a los dos sitios seguidos consume más y deja menos margen; no hay una cifra medida de ese itinerario. El margen se publica en cada snapshot para que el equipo decida.

No se modela CO2 del traje, balance térmico, presión, fugas, fallos ni ninguna consecuencia clínica. No es un traje real ni una receta de preparación EVA.

## Resultados reales

- Unity 6000.3.28f1 nativo, EditMode con filtro `Phase03`: las 14 de D1 pasan (dentro de 80/80 de fase 3; la regresión completa está en el handoff de D5).
- Pureza: `Runtime/Simulation` y `Runtime/Application` compilan solo contra `netstandard` 2.1 con `-warnaserror`.

## Pendiente

Sustituir los valores sintéticos por fuentes recuperadas y contextualizadas antes de presentarlos como datos.
