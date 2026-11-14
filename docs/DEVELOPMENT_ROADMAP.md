# DEVELOPMENT ROADMAP — Cinco fases, cinco prototipos jugables

## Regla que cambia la planificación

**Cada fase termina en una versión integrada que se puede abrir y jugar.** Código aislado, un prefab suelto o una colección de tests no cierran una fase. Cada versión conserva lo que ya funcionaba y añade una experiencia completa pequeña.

Este roadmap **sustituye la numeración de entrega anterior** de seis fases más preparación, así como la secuencia antigua P0/P1/P2. El SCOPE sigue siendo la referencia científica, no el calendario de implementación. Sus mecanismos de profundidad y parámetros pendientes no se convierten automáticamente en requisitos de estas cinco entregas.

**Confirmado:** Unity Universal 2D/C#, multiplayer y equipo de cinco desarrolladores. Leo indica que todos tienen el mismo LTS/módulos; registrar la versión exacta real. Las entregas de las IA todavía no se han inspeccionado aquí y no se declara ningún prototipo terminado.

**Propuesta para concretar:** pequeña base lunar hipotética, presentación 2D, dos clientes como prueba inicial y crecimiento progresivo. Cámara definitiva, cantidad final de jugadores, sitio, hardware, duración, backend y target requieren acuerdo. Una sala cenital provisional permite probar sin cerrar la identidad visual.

## Las cinco versiones

| Fase | Prototipo que debe existir al cerrar | Decisión que se puede jugar |
|---|---|---|
| 1 | **Sala cooperativa de energía**, visible y conectada | Encender ciencia o conservar reserva para un servicio esencial |
| 2 | **Base 2D administrable**, con astronautas/tareas y suministros | Repartir personas, tiempo y energía entre necesidades reales |
| 3 | **Misión exterior con regreso y refugio** | Completar ciencia adicional o retornar con margen |
| 4 | **Misión lunar con contingencias y operaciones** | Diagnosticar, reparar y replanear con información y recursos limitados |
| 5 | **Candidato de entrega accesible y presentable** | Jugar la experiencia completa y entender qué enseñaron sus decisiones |

No son cinco juegos independientes: son cinco versiones acumulativas del mismo proyecto. Tampoco son cinco roles convertidos en fases.

## Cinco partes independientes dentro de cada fase

Se mantiene una responsabilidad reconocible para cada persona:

1. **D1 — Simulación y recursos:** reglas físicas y tests de modelo.
2. **D2 — Gameplay y composición:** objetivos, comandos y conexión de escenas; coordinador de integración.
3. **D3 — Multiplayer:** autoridad, transporte, solicitudes y sincronización.
4. **D4 — Presentación 2D:** gráficos necesarios, prefabs, UI y feedback desde el inicio.
5. **D5 — Validación y entrega:** fixtures, validadores, pruebas entre sistemas y build reproducible; también programa desde el principio.

Cada documento de fase incluye las cinco partes, sus áreas exclusivas, dependencias, entregables y cierre. Las personas pueden ayudarse, pero cualquier transferencia de propiedad se acuerda antes de editar archivos ajenos.

## Cómo hacer paralelo real y terminar en un solo branch

1. **Punto de partida común dentro de la fase:** inspeccionar lo existente, acordar contratos/SDK/target y registrar un commit de base. No construir otra vez lo que ya funciona. Las firmas concretas deben existir y ser conocidas antes de dividirse; una lista de intenciones no reemplaza el contrato.
2. **Ramas de trabajo separadas:** `feature/phase-NN/d01-simulation`, `d02-gameplay`, `d03-multiplayer`, `d04-presentation`, `d05-validation`, todas desde la misma base aprobada.
3. **Contrato congelado durante el trabajo paralelo:** tipos y firmas compartidos son de solo lectura. Si hace falta una modificación, se propone, se aprueba y se publica una nueva base coordinada. No ampliar interfaces unilateralmente ni duplicarlas.
4. **Trabajo aislado:** cada persona toca solo su área/archivos aprobados. Se permiten contratos y fixtures/fakes ya acordados; estos facilitan avance, pero no prueban que exista integración ni conectividad real.
5. **Unificación dentro de la misma fase:** revisar y reunir los cinco cambios en **`integration/phase-NN`**. Una sola persona modifica la escena compartida al hacer el wiring, con una tarea y rutas autorizadas. Integrar versiones pequeñas temprano; no dejar la primera unión para el último día.
6. **Prueba del conjunto y build:** retirar fakes del flujo de producción, probar clientes reales, misión completa, errores y reinicio. Corregir regresiones con responsables claros. Se puede volver a las ramas de trabajo sin abrir la propiedad global de todo el proyecto.
7. **Cierre:** conservar una build jugable y el commit aceptado. Si quieren pasarla a `main`, hacerlo mediante el proceso de revisión acordado; este documento no ejecuta merges/pushes. La siguiente fase parte de esa versión, no de cinco bases distintas.

El resultado compartido está en un único branch de integración por fase, no en cinco demos inconexas. El trabajo puede ser paralelo después del acuerdo mínimo; paquetes ausentes o contratos incompletos siguen siendo dependencias reales.

## Qué se exige en todas las fases

- Una **escena funcional**, controles conectados, objetivo, consecuencia, resultado y reintento.
- Gráficos propios o con licencia comprobada: placeholders legibles desde fase 1; el pulido aumenta después. No esperar hasta la última fase para dibujar el mundo.
- Dos clientes reales como mínimo inicial y misma autoridad de simulación. Esa cantidad no fija la capacidad definitiva.
- Una build fuera del Editor y pasos para abrir/conectar. Si se reclama acceso online por Internet, verificar esa topología; dos procesos locales no bastan para esa afirmación.
- Una pantalla breve de qué cambió, qué cuesta y qué está simplificado; fuentes accesibles desde que se usa evidencia externa.
- Tests de la función nueva y regresiones de la versión previa. Una fase no termina si rompe la anterior.
- La evidencia y limitaciones reales: no anunciar pruebas, hardware, datos en vivo o seguridad que no se verificaron.

## Cómo recuperar el trabajo que ya está en curso

**No tirar las cinco ramas ni mandar a sus IA a reescribir todo.** Las asignaciones estrictas existentes son paquetes de implementación de la fase 1, no su entrega completa.

1. Terminar/revisar lo que cada rama puede entregar con sus permisos actuales.
2. Auditar los tipos base, implementaciones y dependencias; conservar lo correcto.
3. Dar una tarea explícita de integración para conectar escena, sesión, red y prefab. Los agentes que aún ejecutan tareas estrictas no reciben permisos nuevos por leer este roadmap.
4. Añadir los gráficos y la interacción 2D mínima que faltan para la sala jugable.
5. Ejecutar la aceptación de fase 1 antes de pasar a fase 2.

**Cambio deliberado:** la integración ya no es un pendiente que se arrastra después de declarar una fase terminada. Forma parte de su definición de terminado. Sigue siendo una tarea separada de las ramas aisladas para proteger el proyecto.

## Arquitectura y límites que se conservan

`Unity/adaptadores -> Application -> Simulation`. Simulación y aplicación siguen separadas de gráficos/SDK; una autoridad aplica consumos y tiempo; snapshots/observaciones van a clientes y UI; los jugadores envían intenciones. PowerConfiguration, PowerSnapshot, IPowerSimulation, SessionState, SessionCommand, MissionSnapshot, CommandResult, ISessionPort y FakeSessionPort existentes no se reescriben arbitrariamente.

En las fases siguientes sí se pueden necesitar contratos nuevos: versionarlos y acordarlos **antes** de abrir las ramas de esa fase. No intentar resolver un modelo creciente con campos falsos, herencia oculta o firmas inventadas. Leer `docs/PROJECT_STRUCTURE.md` para la estructura vigente Universal 2D, no recrear el árbol 3D antiguo.

## Ciencia que ninguna fase permite inventar

- Conservar masa/energía en la frontera modelada; distinguir W de J, O2 de CO2 y agua potable de agua total.
- Valores medidos, referencias, cálculos y supuestos tienen etiquetas diferentes. Un dato desconocido no es cero.
- Vacío lunar sin refrigeración convectiva exterior; iluminación local, no una noche lunar cada pocos minutos por decoración.
- EVA limitada por rutas, ingreso y servicios, sin protocolo médico universal.
- Flare/CME/SEP/GCR son distintos. Alertas cualitativas mientras no existan kernels validados; ningún Kp, clase X o pfu se convierte directamente en dosis humana.
- La demora Tierra-base no es lag ni retraso impuesto a conversaciones de compañeros cercanos.
- Cultivos y reservas respetan el reloj físico; no cosechas instantáneas ni autosuficiencia inventada. La planificación de producción puede enseñarse sin cosechar durante una misión corta.
- No APIs en vivo obligatorias, pronósticos sin validar, probabilidades clínicas o supuestas certificaciones.

## Profundidad fuera de estas cinco entregas

Marte con calibración propia, mundo abierto, construcción libre, vehículos complejos, logística interplanetaria, ISRU industrial, redes avanzadas, contramedidas médicas y transporte radiológico completo no son obligatorios para terminar este prototipo del challenge. Se conservan como investigación/backlog; si algo se desea incorporar, debe sustituir alcance de forma explícita, no sumarse sin límite.

## Documentos de fase

- [Fase 1 — Energía cooperativa](roadmap/01_POWER_COOP_PROTOTYPE.md).
- [Fase 2 — Gestión de base](roadmap/02_BASE_MANAGEMENT_PROTOTYPE.md).
- [Fase 3 — Exterior y refugio](roadmap/03_EVA_AND_SHELTER_PROTOTYPE.md).
- [Fase 4 — Operaciones y contingencias](roadmap/04_MISSION_OPERATIONS_PROTOTYPE.md).
- [Fase 5 — Candidato de entrega](roadmap/05_RELEASE_CANDIDATE.md).

Los enlaces asumen instalación como `docs/DEVELOPMENT_ROADMAP.md` y `docs/roadmap/`. Las cinco notas son asignaciones de planificación, no prompts autorizando a una IA a tocar todo el proyecto.

Referencia científica: SCOPE (síntesis A–L y anexos). Challenge: https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/
