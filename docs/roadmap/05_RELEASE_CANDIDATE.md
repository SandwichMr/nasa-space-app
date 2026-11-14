# Fase 5 — Experiencia completa y candidato de entrega

**Objetivo:** Una build accesible, coherente y presentable que personas externas pueden jugar sin ayuda permanente del desarrollador.

**Arquitectura:** Unity Universal 2D -> Application -> Simulation, con una autoridad de sesión.

**Estado:** plan de desarrollo, no evidencia de que esta versión exista. Esta fase incluye gráficos, interacción, integración y build; no solo componentes aislados.

## Qué podrá jugar una persona externa

Abrir la entrega, entrar con su equipo, aprender los controles mediante una introducción corta, gestionar la base, realizar la misión y reaccionar a la contingencia. Al terminar entiende el costo de sus decisiones, puede reintentar y consultar qué fuentes reales se usaron.

No es la primera fase que dibuja el mundo o conecta botones. El juego ya existe desde las versiones anteriores; aquí se pule su cohesión, accesibilidad y entrega. No añadir sistemas nuevos grandes por querer impresionar al final.

## Entrada y alcance compartido

Partir de la misión jugable de fase 4. Acordar target final, tamaño de grupo soportado, ruta de acceso/hosting, condiciones de prueba y prioridades de corrección. Dos clientes fueron el mínimo de desarrollo, no una promesa automática de soporte para cinco jugadores. No cambiar SDK/Editor o todas las firmas al final sin motivo y aprobación.

## Trabajo paralelo y unificación

Antes de abrir ramas, registrar el commit de base, contratos reales, SDK/target y permisos de esta fase. Cada persona trabaja en una rama `feature/phase-05/d0X-rol`, con propiedad exclusiva de sus archivos. Las áreas indicadas abajo son el reparto para preparar la lista concreta de escritura; **no autorizan a una IA a editar toda esa carpeta**. Acordar esa lista y congelar las interfaces antes de comenzar.

D2 coordina composición y es el único editor de escenas compartidas en la tarea de integración autorizada. D4 entrega arte/prefabs; D3 componentes/adaptadores; D1 modelo; D5 validación. Paquetes/settings se cambian solo en una actualización común aprobada, nunca cinco veces en paralelo.

Al reunir las cinco entregas, usar `integration/phase-05`, retirar los fakes del flujo real, conectar referencias y probar la misma build. Nadie marca la fase terminada solo porque su rama compila. La integración está dentro de esta fase, aunque sea una tarea distinta del trabajo aislado. Si un contrato necesita cambiar, publicar una nueva base coordinada.

## D1 — Corrección y costo de simulación

**Trabajo independiente:** Cerrar regresiones numéricas, mejorar detección de eventos y costo de ejecución sin alterar ciencia para maquillar resultados. Documentar tolerancias y dominios del modelo.

**Área de propiedad propuesta:** `Simulation y tests existentes asignados; sin reescritura general.`

**Entrega a integración:** Modelo estable en la build final y configuraciones aprobadas. No sustituir evidencia faltante por una cifra bonita.

## D2 — Flujo completo y escena de entrega

**Trabajo independiente:** Pulir tutorial/progresión, navegación entre inicio/misión/resultado, reinicio y objetivos; integrar fixes y mantener una sola autoridad/runner por sesión.

**Área de propiedad propuesta:** `Application/Bootstrap y escenas con propiedad única; integrador de release.`

**Entrega a integración:** Recorrido completo con buen resultado, cancelación y error recuperable; sin pasos manuales del Editor o referencias rotas.

## D3 — Conectividad y errores finales

**Trabajo independiente:** Validar unión, capacidad acordada, autorización, duplicados, reconexión solo si fue implementada, pérdida de host y mensajes comprensibles en la topología elegida.

**Área de propiedad propuesta:** `Networking y tests asignados.`

**Entrega a integración:** Pruebas reales en equipos/red/target anunciados, sin prometer crossplay, migración o Internet no probados. Revisar términos/credenciales y no incluir secretos.

## D4 — Acabado visual, audio y accesibilidad

**Trabajo independiente:** Unificar sprites/animaciones/UI, contraste, tamaño de texto, iconos, señales y sonidos opcionales; mejorar tutorial/debrief sin depender de micrófono o color. Conservar atribución/licencias de assets.

**Área de propiedad propuesta:** `Art, Audio, Prefabs y Presentation asignados; no cambiar reglas físicas.`

**Entrega a integración:** Presentación coherente y usable por el público, no necesariamente arte comercial. No abrir una producción artística nueva que rompa el alcance.

## D5 — Validación de usuarios y paquete final

**Trabajo independiente:** Ejecutar regresión completa, pruebas de comprensión con usuarios adecuados/autorizados si es viable, revisión de fuentes/licencias, build limpia, instrucciones y ensayo de demo. No recopilar datos personales innecesarios.

**Área de propiedad propuesta:** `Tests/Editor tooling y docs de release según lista aprobada.`

**Entrega a integración:** Artefacto jugable, evidencias y limitaciones, créditos, explicación de datasets y pasos de acceso. Fallos pendientes visibles; nada de inventar aprobación o tests infantiles.

## Integración y cierre obligatorio

Unir en `integration/phase-05`, revisar el diff y generar desde un commit identificado la build candidata. Probarla sin depender de APIs NASA en vivo, pero sin afirmar que multiplayer online funciona sin conexión. Web necesita hosting/configuración y un navegador real; si se requiere acceso offline, probar un fallback descargable/local de verdad.

**La fase termina cuando:** alguien externo completa el flujo y puede explicar una restricción aprendida; la misma build está probada con el grupo soportado; hay controles claros, resultado/reintento, fuentes/créditos, instrucciones y demo ensayada. Publicar/mandar la entrega solo con autorización, no por ejecutar este plan.

**Fuera de alcance:** planeta adicional, mundo abierto, nuevas cámaras completas, voz pública, servicios no aprobados, certificaciones y funcionalidades prometidas pero no implementadas. Lo que no llegó se presenta como futuro trabajo.

## Regla de avance

No pasar a la fase siguiente hasta guardar el commit/build aceptados y los resultados reales de prueba. Se pueden explorar ideas futuras en una rama separada, pero no llamar completada una versión sin integrar. La fase 5 es el cierre de este roadmap; no se añade una fase extra de integración.
