# Fase 4 — Misión con averías, información y planificación

**Objetivo:** La expedición y la base forman una misión con contingencias diagnosticables, apoyo diferido y más de una estrategia.

**Arquitectura:** Unity Universal 2D -> Application -> Simulation, con una autoridad de sesión.

**Estado:** plan de desarrollo, no evidencia de que esta versión exista. Esta fase incluye gráficos, interacción, integración y build; no solo componentes aislados.

## Qué podrá jugar el equipo

Gestionar la base y su salida bajo una contingencia seleccionada: refrigeración degradada, agua no aprobada o equipo aislado/averiado. Revisar evidencias, consultar un procedimiento local, pedir apoyo terrestre fechado, decidir qué posponer y comprobar una reparación antes de volver al servicio.

Agregar planificación de producción de alimentos con una instalación pequeña y acotada: decidir energía/luz/agua/tiempo para mantener un cultivo o proceso elegido. No es un botón que crea raciones. En una misión corta puede enseñarse el costo de mantener la producción futura sin obtener cosecha. Si se modela rendimiento/crecimiento, exigir datos/condiciones y tiempo simulado compatibles; sin esa calibración, mostrar la limitación y mantener el stock alimentario separado.

No introducir todos los desastres juntos: seleccionar una variante comprensible de misión y después ampliar. La información incompleta tiene causa y fecha, no ocultación arbitraria.

## Entrada y cambios de contrato

Partir de fase 3 y acordar equipos/estados de fallo, lecturas con antigüedad, órdenes/mensajes, destinos de residuos y alcance del modelo térmico/producción. Los coeficientes desconocidos y políticas se identifican antes de hacer nuevas promesas físicas. Un radioenlace entre compañeros no recibe el retraso de Tierra.

## Trabajo paralelo y unificación

Antes de abrir ramas, registrar el commit de base, contratos reales, SDK/target y permisos de esta fase. Cada persona trabaja en una rama `feature/phase-04/d0X-rol`, con propiedad exclusiva de sus archivos. Las áreas indicadas abajo son el reparto para preparar la lista concreta de escritura; **no autorizan a una IA a editar toda esa carpeta**. Acordar esa lista y congelar las interfaces antes de comenzar.

D2 coordina composición y es el único editor de escenas compartidas en la tarea de integración autorizada. D4 entrega arte/prefabs; D3 componentes/adaptadores; D1 modelo; D5 validación. Paquetes/settings se cambian solo en una actualización común aprobada, nunca cinco veces en paralelo.

Al reunir las cinco entregas, usar `integration/phase-04`, retirar los fakes del flujo real, conectar referencias y probar la misma build. Nadie marca la fase terminada solo porque su rama compila. La integración está dentro de esta fase, aunque sea una tarea distinta del trabajo aislado. Si un contrato necesita cambiar, publicar una nueva base coordinada.

## D1 — Fallos de servicio y dependencias

**Trabajo independiente:** Añadir el modelo térmico reducido requerido por la contingencia y sus cargas/rechazo; efectos físicos de degradación; proceso de suministro/producción alimentaria elegido con límites, entradas/outputs y tiempo. No simular por barras clínicas.

**Área de propiedad propuesta:** `Simulation/Systems, Equipment y Resources asignados; tests de conservación/tiempo.`

**Entrega a integración:** Una contingencia coherente y producción/planificación acotada. Batería cargada no garantiza enfriamiento y las cosechas no avanzan en otro reloj.

## D2 — Diagnóstico, reparación y misiones

**Trabajo independiente:** Crear secuencia inspeccionar/aislar/intervenir/probar/restituir con herramienta/repuesto/tiempo, procedimiento local y objetivos alternativos. Registrar comandos y decisión/costo para debrief.

**Área de propiedad propuesta:** `Application; Equipment/Tasks/Communications según reparto explícito; integración de escena.`

**Entrega a integración:** Misión reparable o cancelable sin botón de cura universal. Una pieza cambiada no elimina por sí sola una causa común ni devuelve recursos perdidos.

## D3 — Información y acciones distribuidas

**Trabajo independiente:** Sincronizar reparación exclusiva, inventarios comprometidos, observaciones permitidas y mensajes de misión con disponibilidad. Mantener transporte de jugadores separado de cola Tierra-base y revisar órdenes obsoletas.

**Área de propiedad propuesta:** `Networking y adaptadores de comunicación asignados; tests de idempotencia/autorización.`

**Entrega a integración:** La misma contingencia y tarea en todos, sin conocimiento futuro filtrado ni demora científica aplicada al chat/pings de compañeros.

## D4 — Evidencias, equipos y explicación visual

**Trabajo independiente:** Representar estados de equipo, inspección/reparación, stock/cultivo/proceso y mensajes en tránsito. Añadir comparación final de decisiones comprensible para niños, sin mostrar cada subsistema todo el tiempo.

**Área de propiedad propuesta:** `Art/Phase04, prefabs y paneles específicos; feedback de estados con archivos asignados.`

**Entrega a integración:** Se entiende qué está fallando, qué falta para intervenir y qué costo tiene la elección. No usar color como única evidencia ni un panel técnico infinito.

## D5 — Datos, escenarios y comparación

**Trabajo independiente:** Comprobar fuentes/licencias/contextos, calibración y restricciones del modelo; fixtures reproducibles de variantes; prueba de reparación/desconexión/repetición y comparación con referencias válidas.

**Área de propiedad propuesta:** `Pipeline/fixtures/Tests/Phase04 según lista aprobada; builds y documentación de ciencia.`

**Entrega a integración:** Build con misión normal y una contingencia jugables; debrief trazable. No afirmar pronóstico, tasa de fallos o producción alimentaria validada si solo hay supuestos.

## Integración y cierre obligatorio

Unir en `integration/phase-04`, jugar una misión completa con la contingencia elegida y comprobar que el modelo físico, observaciones, reparación y resultado cuentan la misma historia. La producción futura de alimentos debe tener una decisión visible, no solo un decorado; documentar sus límites.

**La fase termina cuando:** el grupo diagnostica y actúa o cancela, paga costos reales del modelo, puede continuar sin una respuesta terrestre inmediata y entiende el resultado. Existen variantes pequeñas/repetibles y no se rompieron las salidas de fase 3.

**Se aplaza:** MTBF inventados, simulación clínica, agricultura autosuficiente, ISRU industrial, red de relays completa, predicción solar entrenada sin validación y dosimetría no resuelta.

## Regla de avance

No pasar a la fase siguiente hasta guardar el commit/build aceptados y los resultados reales de prueba. Se pueden explorar ideas futuras en una rama separada, pero no llamar completada una versión sin integrar. La fase 5 es el cierre de este roadmap; no se añade una fase extra de integración.
