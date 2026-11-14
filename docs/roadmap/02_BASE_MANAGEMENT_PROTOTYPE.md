# Fase 2 — Base administrable y tripulación 2D

**Objetivo:** La sala se convierte en una base donde los jugadores se representan, se reparten tareas y gestionan suministros/servicios.

**Arquitectura:** Unity Universal 2D -> Application -> Simulation, con una autoridad de sesión.

**Estado:** plan de desarrollo, no evidencia de que esta versión exista. Esta fase incluye gráficos, interacción, integración y build; no solo componentes aislados.

## Qué podrá jugar el equipo

Entrar en la base que ya funciona; controlar una representación sencilla de su astronauta en un espacio 2D; desplazarse entre estaciones; elegir tareas de soporte o ciencia; observar que una persona/equipo no puede hacer todo simultáneamente; completar una misión interior y revisar el resultado.

Añadir agua potable versus pendiente de tratar, comida almacenada y un modelo de aire de cabina acotado por la arquitectura elegida. La UI prioriza dos o tres indicadores y despliega el resto, no obliga a seguir seis barras sin explicación. Alimentos suficientes en una misión corta no necesitan una crisis artificial.

**Regla jugable:** atender un servicio ocupa tiempo, persona y energía; continuar ciencia puede retrasar otra necesidad. Una desconexión no duplica trabajadores ni deja recursos consumiéndose sin responsable conocido.

## Entrada y cambios de contrato

Partir del commit/build jugable de fase 1. Antes de las ramas, acordar entidades de jugador, tareas, recursos/unidades y observaciones de cabina. Extender contratos en una actualización común aprobada; no sobrecargar los campos eléctricos existentes con significados nuevos. La elección de control 2D debe ser sencilla y comprobada, no un salto a primera persona 3D.

## Trabajo paralelo y unificación

Antes de abrir ramas, registrar el commit de base, contratos reales, SDK/target y permisos de esta fase. Cada persona trabaja en una rama `feature/phase-02/d0X-rol`, con propiedad exclusiva de sus archivos. Las áreas indicadas abajo son el reparto para preparar la lista concreta de escritura; **no autorizan a una IA a editar toda esa carpeta**. Acordar esa lista y congelar las interfaces antes de comenzar.

D2 coordina composición y es el único editor de escenas compartidas en la tarea de integración autorizada. D4 entrega arte/prefabs; D3 componentes/adaptadores; D1 modelo; D5 validación. Paquetes/settings se cambian solo en una actualización común aprobada, nunca cinco veces en paralelo.

Al reunir las cinco entregas, usar `integration/phase-02`, retirar los fakes del flujo real, conectar referencias y probar la misma build. Nadie marca la fase terminada solo porque su rama compila. La integración está dentro de esta fase, aunque sea una tarea distinta del trabajo aislado. Si un contrato necesita cambiar, publicar una nueva base coordinada.

## D1 — Aire, agua y consumos

**Trabajo independiente:** Implementar una cabina mezclada declarada, demanda de tripulación y servicios O2/CO2 diferenciados; agua por calidad y comida/outputs con frontera explícita. No importar tasas universales o un 98% genérico.

**Área de propiedad propuesta:** `Simulation/Systems, Resources, Crew; tests de masa/servicios propios.`

**Entrega a integración:** Modelos reducidos con unidades/contexto y límites. Si falta evidencia, usar una fixture marcada y reducir la afirmación, no fabricar un umbral médico.

## D2 — Tareas e interacción

**Trabajo independiente:** Implementar acciones en estaciones, ocupación exclusiva de persona/equipo, costos/duración, objetivo interior y retorno al flujo de misión. Integrar la ampliación en la escena existente.

**Área de propiedad propuesta:** `Application; Simulation/Tasks según propiedad acordada; Unity/Bootstrap; escena compartida única.`

**Entrega a integración:** Misión de gestión interior con decisiones y resultado. El scheduler y los consumos tienen un único dueño, sin aplicar demanda dos veces.

## D3 — Jugadores y acciones compartidas

**Trabajo independiente:** Sincronizar identidad, presencia/movimiento 2D según estrategia aprobada, tareas y cambios de suministros. Validar uso simultáneo de estaciones y desconexión sin duplicar inventarios.

**Área de propiedad propuesta:** `Unity/Networking y adaptadores de entrada/movimiento asignados; tests de concurrencia.`

**Entrega a integración:** Dos representaciones coherentes y tareas confirmadas por autoridad. No confiar en cantidades/posición imposible declaradas arbitrariamente por cliente.

## D4 — Base y astronautas visibles

**Trabajo independiente:** Crear sprites/representaciones 2D simples, habitaciones/estaciones distinguibles, señal de quién ocupa qué, progreso y paneles de recursos. Animaciones mínimas legibles, no producción artística completa.

**Área de propiedad propuesta:** `Art/Sprites/Phase02, Animations/Phase02; Prefabs/Crew y World; Presentation/Input/World con archivos exclusivos.`

**Entrega a integración:** Base navegable visualmente comprensible y controles sobre la misma sesión. Entregar prefabs a D2; no editar la escena compartida en paralelo.

## D5 — Escenario y regresión de recursos

**Trabajo independiente:** Probar exclusión de tareas, consumos, calidad del agua, cabina y red; validar configuración y generar build que conserve la experiencia de fase 1.

**Área de propiedad propuesta:** `Tests/Phase02 por área asignada; Editor/Validation; documentación/fixtures de prueba propias.`

**Entrega a integración:** Evidencia de dos clientes repartiendo trabajo, inventarios coherentes, errores claros y reinicio limpio. No afirmar enfermedad/seguridad clínica a partir de un medidor.

## Integración y cierre obligatorio

Unir las cinco partes en `integration/phase-02`, enlazar acciones a estaciones y consumos, aplicar recursos en la autoridad y probar la misión interior completa. Los fakes no pueden quedar alimentando paneles de producción.

**La fase termina cuando:** dos personas controlan sus representaciones, hacen tareas distintas, no duplican una pieza/persona/stock, completan una necesidad interior y ciencia opcional, ven costos y terminan/reinician la misión en una build. La versión anterior sigue funcionando.

**Se aplaza:** exterior/EVA, protección radiológica, modelado multizona complejo, fisiología clínica, cosecha instantánea, construcción libre.

## Regla de avance

No pasar a la fase siguiente hasta guardar el commit/build aceptados y los resultados reales de prueba. Se pueden explorar ideas futuras en una rama separada, pero no llamar completada una versión sin integrar. La fase 5 es el cierre de este roadmap; no se añade una fase extra de integración.
