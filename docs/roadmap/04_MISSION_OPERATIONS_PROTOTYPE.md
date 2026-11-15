# Fase 4 — Base viva: recursos, mantenimiento y sesión continua

**Estado:** planificación revisada, no implementación ni aceptación. Sustituye el alcance anterior de fase 4.
**Objetivo:** convertir la base y la expedición de fase 3 en una sesión cooperativa que continúa mientras se juega. Completar una tarea o expedición no termina la partida.
**Arquitectura:** Unity Universal 2D → Application → Simulation; una autoridad, un reloj y un inventario confirmado.

## Entrada y transición desde fase 3

Conservar el interior, los astronautas, movimiento/red, recursos, contratos EVA, esclusa, refugio y resultados de expedición que realmente estén implementados. No reabrir ni reescribir la fase 3 en paralelo. Inspeccionar su entrega integrada antes de declarar la entrada aceptada.

D2 debe separar explícitamente el ciclo de la sesión del ciclo de una tarea/expedición. El cierre por cumplir objetivos de fases anteriores queda disponible como modo de entrenamiento/regresión; no puede seguir cerrando el modo continuo. Cancelar una expedición devuelve al flujo de base. Una avería o reserva baja no produce automáticamente una derrota: debe existir una ventana y una vía de recuperación según el escenario. Si hay una condición realmente irreversible, documentar su regla y ofrecer reinicio, no fingir continuidad.

Por defecto el mundo progresa durante una sesión activa; no se consume fuera de una partida ejecutándose. Acordar pausa autoritativa, velocidad del tiempo, abandono y salida voluntaria. Persistencia entre reinicios se cierra en fase 6; no prometer un servidor persistente 24/7.

## Experiencia y prioridad P0

- Caminar, verse y repartirse trabajo dentro/fuera de la base existente.
- HUD con barras funcionales de batería, agua potable y comida; O2 y CO2 diferenciados, con unidades, estado y límites del modelo. Añadir generación/demanda eléctrica y tendencia; detalle de agua sin tratar y residuos disponible sin saturar el HUD. Desconocido/antiguo no es cero.
- Consumo continuo y reposición/reciclaje con límites explícitos. Cada barra debe tener causa, consecuencia y acción del jugador; nunca ser un contador decorativo.
- Ciclo jugable: observar → priorizar → intervenir → comprobar efecto → recuperar margen → continuar.
- Al menos dos estaciones con interacción significativa conectada al mismo modelo, además de su animación/progreso: tratamiento de agua y diagnóstico/servicio de aire. Elegir una ruta/estado correcto usando información visible; no un minijuego desconectado ni una solución clínica universal.
- Un contratiempo de equipo reproducible: degradación del tratamiento de agua. Diagnosticar, aislar cuando proceda, reparar con tiempo/repuesto y comprobar antes de restituir. No introducir todos los desastres juntos.
- Encargos científicos como oportunidades dentro de la sesión. Su recompensa no termina la base; no sustituir mantenimiento por otra lista finita.

## D1 — Balances continuos y recuperación

**Propiedad propuesta:** implementaciones nuevas bajo `Runtime/Simulation/BaseOperations/**` y sus pruebas bajo `Tests/EditMode/Phase04/Simulation/**`; mantenimiento de implementaciones previas solo en lista coordinada, no de contratos congelados.

1. Conectar energía, demanda de tripulación, agua por calidad, comida y servicios de aire al reloj único. Auditar doble cobro interior/EVA.
2. Modelar degradación y reparación sin devolver automáticamente agua/energía ya perdida.
3. Definir entradas externas para sostener una sesión: reciclaje con pérdidas y reabastecimiento explícito, limitado y con costo/plazo de escenario; registrar origen y destino de masa. Comida no aparece por pulsar un botón y un circuito con pérdidas no es autosuficiente.
4. Registrar unidades, frontera, supuestos sintéticos y límites. No inventar MTBF, porcentajes universales o fisiología clínica.

**Entrega:** balances verificables, demandas activas y vías de recuperación que impidan una cuenta regresiva inevitable disfrazada de juego continuo.

## D2 — Sesión continua, acciones y montaje

**Responsable de preparación común y cierre de integración.** Propiedad propuesta: `Runtime/Application/BaseOperations/**`, `Runtime/Unity/Bootstrap/BaseOperations/**` y escena/asset asignados explícitamente al aprobar la base.

1. Preparar tipos reales para ciclo de sesión, estado de equipos, observaciones, interacción y finalización de encargos independiente del cierre global.
2. Implementar transición base ↔ expedición ↔ base sin destruir stocks, repetir cobros o reiniciar el reloj.
3. Implementar las dos interacciones de estación y la secuencia de reparación. Validar pasos en autoridad; el cliente no envía «completado» arbitrario. Una interacción debe depender de observaciones/acciones, no solo esperar una barra.
4. Garantizar metas cortas y señales claras sin victoria automática al terminar un checklist. Informes periódicos son resúmenes no terminales.
5. Componer la escena acumulativa y montar los prefabs de D4; no dejarla reservada ni declarar que un merge la conectó solo.

**Entrega:** la misma base sigue operando tras completar/cancelar varias actividades, con decisiones y efectos visibles.

## D3 — Cooperación y estado continuo

**Propiedad propuesta:** `Runtime/Unity/Networking/BaseOperations/**` y `Tests/PlayMode/Networking/Phase04/**`, con adaptadores puros en rutas acordadas.

Sincronizar stocks, ocupación, pasos de mantenimiento, órdenes de reabastecimiento y estado de sesión. Incorporación tardía obtiene el estado actual, no los valores iniciales. Desconectar libera trabajo/reservas según política explícita, sin duplicar repuestos; reconexión solo si está implementada y verificada. Conservar identidades, épocas, validación de secuencias y deduplicación. No crear otro transporte, controlador de movimiento o reloj cliente.

## D4 — Barras, base legible e interacción

**Propiedad propuesta:** `Art/Phase04/**`, `Prefabs/UI/Phase04/**`, `Runtime/Unity/Presentation/BaseOperations/**`, prefabs de equipo asignados y `Tests/PlayMode/UI/Phase04/**`.

Implementar barras reales, tendencia/avisos comprensibles y estados del equipo. Mostrar qué falta y qué cambió tras una acción; texto/icono además de color. Dar interfaz visible a las dos interacciones, progreso/ocupación y rechazo de comandos. Mejorar coherencia visual del interior y estaciones existentes sin cambiar geometría autoritativa. D4 entrega prefabs y bindings; D2 es el único editor de la escena compartida. No resolverlo con otro panel técnico que tape el mundo.

## D5 — Prueba de continuidad, recuperación y utilidad

**Propiedad propuesta:** `Editor/Validation/Phase04/**`, `Editor/Build/Phase04/**`, pruebas integradas de fase 4 y handoff propio.

Comprobar límites de stocks, calidad del agua, reservas de equipo/repuestos, tiempo y balances. Fixtures: operación estable; escasez por decisiones; avería; reparación y recuperación; varias expediciones y encargos completos sin cierre global. Probar simultaneidad, incorporación tardía y desconexión durante reparación.

Criterio de aceptación propuesto: una sesión integrada de al menos 30 minutos de tiempo de jugador con dos clientes reales, varios ciclos de trabajo, una avería recuperada y una necesidad de reposición. El tiempo de simulación y su escala se registran aparte; no fingir crecimiento agrícola o ciclos lunares reales en ese intervalo. Verificar qué decisiones comprendieron los participantes, sin inventar pruebas de usuarios.

## Preparación común, integración y cierre

D2 prepara una sola actualización compatible y revisada: `docs/phase-04/{SESSION_CONTRACT,OWNERSHIP,COMMON_BASE}.md`, contratos mínimos, fake y fixtures etiquetadas. Debe publicar un SHA completo real antes de repartir consumidores. Si una sola IA asume D1+D2+D3+D5, sigue siendo responsable de esa preparación; no se exigen cuatro ramas ficticias.

Antes de arrancar, convertir las áreas propuestas en lista exacta y sin solapamientos de escritura; no constituyen permiso para editar toda la carpeta. El trabajo entra en `integration/phase-04`, con montaje explícito D2 y retiro de fakes de producción.

**La fase termina cuando:** la build deja de cerrar por objetivos, las barras reflejan consumos/recuperación reales, dos estaciones requieren decisiones, la base afronta y supera una avería y la sesión continúa con ambos clientes coherentes. Registrar importación, EditMode, PlayMode, validadores, build y ejecución real por separado.

**Fuera de alcance:** API NASA obligatoria en vivo, catálogo grande de desastres, agricultura autosuficiente, construcción libre y persistencia con host apagado. Los eventos externos y la variedad se entregan en fase 5, no se prometen como ya existentes.
