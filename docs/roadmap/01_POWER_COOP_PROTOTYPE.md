# Fase 1 — Sala cooperativa de energía

**Objetivo:** Una base pequeña se ve en pantalla y dos participantes pueden jugar una decisión de energía, completar el objetivo y reintentar.

**Arquitectura:** Unity Universal 2D -> Application -> Simulation, con una autoridad de sesión.

**Estado:** plan de desarrollo, no evidencia de que esta versión exista. Esta fase incluye gráficos, interacción, integración y build; no solo componentes aislados.

## Qué podrá hacer una persona al abrir la build

Crear/unirse a la sesión; ver una sala 2D con batería, servicio esencial e instrumento científico representados; seleccionar una estación y activar/desactivar ciencia; observar el consumo y decidir cuándo detenerse para conservar reserva; obtener resultado y reiniciar.

No basta un Canvas vacío o botones desconectados. Los recursos visuales pueden ser muy sencillos, pero deben existir y representar lo que cambia. No se necesita todavía caminar ni animar astronautas: una interacción por selección permite cerrar este prototipo sin inflar el controlador online. La sala/vista 2D es provisional, no una elección definitiva de cámara artística.

**Regla jugable:** el experimento necesita trabajo efectivamente alimentado y la carga esencial tiene prioridad. Objetivo, stocks y parámetros deben permitir una decisión comprobable sin cifras falsas. La carga esencial aún es una abstracción: no mostrar O2/CO2 clínicos ni fingir un modelo de atmósfera.

## Contrato y entrada compartida

Aprovechar los tipos y la base ya encargados: IPowerSimulation, ISessionPort, comandos, resultados y snapshots. Submit es una intención; CommandCompleted/SnapshotChanged comunican resultados. Auditar lo generado antes de usarlo, sin asumir que cumple el prompt. Elegir SDK/target real, permiso de pausa/reinicio y reglas de sesión antes de abrir trabajo adicional. El fake es para desarrollo, no la partida final.

## Trabajo paralelo y unificación

Antes de abrir ramas, registrar el commit de base, contratos reales, SDK/target y permisos de esta fase. Cada persona trabaja en una rama `feature/phase-01/d0X-rol`, con propiedad exclusiva de sus archivos. Las áreas indicadas abajo son el reparto para preparar la lista concreta de escritura; **no autorizan a una IA a editar toda esa carpeta**. Acordar esa lista y congelar las interfaces antes de comenzar.

D2 coordina composición y es el único editor de escenas compartidas en la tarea de integración autorizada. D4 entrega arte/prefabs; D3 componentes/adaptadores; D1 modelo; D5 validación. Paquetes/settings se cambian solo en una actualización común aprobada, nunca cinco veces en paralelo.

Al reunir las cinco entregas, usar `integration/phase-01`, retirar los fakes del flujo real, conectar referencias y probar la misma build. Nadie marca la fase terminada solo porque su rama compila. La integración está dentro de esta fase, aunque sea una tarea distinta del trabajo aislado. Si un contrato necesita cambiar, publicar una nueva base coordinada.

## D1 — Simulación eléctrica

**Trabajo independiente:** Responsable del reloj, generación, batería, demanda esencial/científica, límites y snapshots físicos. Reutilizar la implementación ya encargada; cerrar balances y agotamiento sin inventar otra API.

**Área de propiedad propuesta:** `Simulation/Time, State, Systems; tests unitarios de simulación.`

**Entrega a integración:** Motor conforme al contrato, configuración sintética rotulada y tests analíticos. Puede avanzar sin UI/red usando fixtures.

## D2 — Misión y escena funcional

**Trabajo independiente:** Responsable de objetivo, comandos, resultado/reinicio y composición. Durante la integración aprobada conectar runner, escena y componentes; no dejarlo como trabajo posterior a la fase.

**Área de propiedad propuesta:** `Application/Sessions, Commands, Evaluation; Unity/Bootstrap y Configuration; Scenes, con propiedad única.`

**Entrega a integración:** Misión completa y escena preparada para recibir el prefab/puerto real. Puede desarrollar con IPowerSimulation de prueba hasta recibir D1.

## D3 — Sesión multiplayer

**Trabajo independiente:** Responsable de dos clientes conectados a una sola autoridad, comandos confirmados, snapshots, época/revisión y errores/desconexión mínimos. Trabajar solo con dependencias aprobadas.

**Área de propiedad propuesta:** `Unity/Networking; tests de transporte/adaptación.`

**Entrega a integración:** Adaptador real y receta de conexión. El fake facilita pruebas de borde, pero al cerrar se necesitan dos clientes reales.

## D4 — Sala 2D, arte básico y UI

**Trabajo independiente:** Responsable de un prefab visual de sala/estaciones y del HUD: batería, potencia, ciencia, conexión y resultado. Crear o seleccionar recursos con licencia, aunque sean formas/sprites simples. No esperar arte final para mostrar la base.

**Área de propiedad propuesta:** `Unity/Presentation y World; Prefabs/UI y World; Art/Sprites/Phase01 e Icons/Phase01, con archivos asignados.`

**Entrega a integración:** Prefabs y recursos visuales legibles. Empieza con FakeSessionPort y después D2 los conecta al puerto real; no mutar la física desde UI.

## D5 — Validación y primera build

**Trabajo independiente:** Responsable de fixture, validadores, pruebas de integración, pasos de ejecución y build con dos instancias. Revisar que el jugador pueda actuar y entender el costo, no solo que conecte.

**Área de propiedad propuesta:** `Editor/Validation y Build; Tests de integración propios; docs de prueba de fase.`

**Entrega a integración:** Reporte real de build/conexión/balances/reinicio y fallos. No modificar código de compañeros para hacer pasar pruebas.

## Integración y cierre obligatorio

1. Revisar las cinco entregas que ya están en curso y conservar lo correcto. Los handoffs estrictos anteriores son subtrabajos de implementación de esta fase.
2. Crear la tarea de integración con permiso explícito para wiring/escenas y assets necesarios. No reinterpretar permisos antiguos mientras una IA sigue trabajando.
3. Conectar simulación -> sesión -> adaptador -> prefab; ninguna instancia cliente calcula su propia batería autoritativa.
4. Instalar/conectar la sala visual de D4 y probar objetivo, resultado y reintento.
5. Ejecutar build real, dos clientes y regresiones. Marcar si solo se probó local/LAN o también Internet.

**La fase termina cuando:** ambos ven el mismo estado confirmado; ambos pueden realizar acciones permitidas; ciencia cambia energía/trabajo; el servicio esencial sigue las reglas; resultado/reinicio funcionan; no se usa FakeSessionPort como autoridad de producción. La escena es visible e interactiva fuera del Editor.

**Se aplaza:** movimiento de personajes, EVA, radiación, cultivo, averías complejas, mapa grande y arte definitivo.

## Regla de avance

No pasar a la fase siguiente hasta guardar el commit/build aceptados y los resultados reales de prueba. Se pueden explorar ideas futuras en una rama separada, pero no llamar completada una versión sin integrar. La fase 5 es el cierre de este roadmap; no se añade una fase extra de integración.
