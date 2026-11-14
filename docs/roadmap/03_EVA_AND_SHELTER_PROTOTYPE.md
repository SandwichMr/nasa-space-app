# Fase 3 — Trabajo exterior, regreso y refugio

**Objetivo:** La base tiene un exterior pequeño y una expedición cooperativa con decisiones de retorno/protección.

**Arquitectura:** Unity Universal 2D -> Application -> Simulation, con una autoridad de sesión.

**Estado:** plan de desarrollo, no evidencia de que esta versión exista. Esta fase incluye gráficos, interacción, integración y build; no solo componentes aislados.

## Qué podrá jugar el equipo

Preparar una salida, atravesar la esclusa con tiempo/recursos declarados, ir a un objetivo cercano, obtener ciencia y regresar. Un segundo objetivo opcional obliga a comparar el beneficio con el margen de regreso. Un episodio de partículas explícitamente authored o archivado/contextualizado puede motivar la cancelación y entrada al refugio.

La base sigue operando mientras hay gente fuera; refugio significa acceso efectivo y servicios, no tocar un icono mágico. El escenario no exige una dosis humana calculada para enseñar que continuar fuera y cambiar de plan tienen consecuencias.

## Entrada y cambios de contrato

Usar la build de fase 2. Acordar antes de dividir: ubicaciones/rutas, estados de esclusa/traje, servicios limitantes y reserva, observación/avisos, ingreso efectivo y quién puede autorizar acciones. Preparación EVA se presenta como simplificación educativa declarada si faltan arquitectura/protocolos; no receta médica universal.

## Trabajo paralelo y unificación

Antes de abrir ramas, registrar el commit de base, contratos reales, SDK/target y permisos de esta fase. Cada persona trabaja en una rama `feature/phase-03/d0X-rol`, con propiedad exclusiva de sus archivos. Las áreas indicadas abajo son el reparto para preparar la lista concreta de escritura; **no autorizan a una IA a editar toda esa carpeta**. Acordar esa lista y congelar las interfaces antes de comenzar.

D2 coordina composición y es el único editor de escenas compartidas en la tarea de integración autorizada. D4 entrega arte/prefabs; D3 componentes/adaptadores; D1 modelo; D5 validación. Paquetes/settings se cambian solo en una actualización común aprobada, nunca cinco veces en paralelo.

Al reunir las cinco entregas, usar `integration/phase-03`, retirar los fakes del flujo real, conectar referencias y probar la misma build. Nadie marca la fase terminada solo porque su rama compila. La integración está dentro de esta fase, aunque sea una tarea distinta del trabajo aislado. Si un contrato necesita cambiar, publicar una nueva base coordinada.

## D1 — Servicios EVA y margen

**Trabajo independiente:** Modelar recursos/servicios del traje dentro del alcance aprobado, ruta/tiempo e ingreso; calcular el margen conservador y limitar por el primer servicio relevante. Incluir costos explícitos de esclusa/retorno sin contarlos dos veces.

**Área de propiedad propuesta:** `Simulation/Eva, Crew y Resources asignados; tests de retorno/consumo.`

**Entrega a integración:** Una regla explicable por tripulante; peor margen del equipo cuando corresponda. No promediar mochilas ni asumir transferencia de O2 inexistente.

## D2 — Expedición y evento de misión

**Trabajo independiente:** Crear checklist educativo, objetivo exterior mínimo/opcional, cancelación, ingreso a protección y resultado. Separar evento del Sol, información disponible y acción. Integrar rutas/zonas y misión en la escena.

**Área de propiedad propuesta:** `Application; Events/Tasks según reparto; Bootstrap/escena y lógica de interacción asignada.`

**Entrega a integración:** Expedición completa y aviso con timestamps/validez. Cancelar correctamente puede cerrar con buen resultado; no convertir fin de flare en all-clear universal.

## D3 — Transiciones y cooperación exterior

**Trabajo independiente:** Sincronizar salida/entrada, estado del compañero, exclusión de esclusa/equipo y eventos autorizados. Comprobar rechazo de acciones viejas al cambiar de zona o reiniciar.

**Área de propiedad propuesta:** `Unity/Networking; tests de transiciones/concurrencia propios.`

**Entrega a integración:** Interior/exterior y protección coherentes en ambos clientes; nadie se teletransporta por un comando inválido ni sigue consumiendo en una copia divergente.

## D4 — Exterior y feedback de retorno

**Trabajo independiente:** Crear superficie pequeña, ruta legible, estaciones de ciencia, esclusa/refugio e indicadores de compañero/retorno. Representar polvo/ambiente sin sugerir atmósfera terrestre. Avisos claros con iconos/texto y sonido opcional.

**Área de propiedad propuesta:** `Art/Phase03 por tipos, Prefabs/World y Crew asignados, World/Presentation.`

**Entrega a integración:** Una salida visualmente reconocible, no solo otro botón. La dirección y entrada al refugio deben entenderse sin depurador ni voz obligatoria.

## D5 — Pruebas de ruta, aviso y protección

**Trabajo independiente:** Diseñar casos de retorno límite, tarea cancelada sin reembolso, aviso viejo/missing, ingreso y desconexión exterior; revisar fuentes/contexto del episodio.

**Área de propiedad propuesta:** `Tests/Phase03, validación de escenario/datos y reporte de build propios.`

**Entrega a integración:** Build con dos clientes que completa/cancela una salida; ningún pfu/clase X convertido en dosis humana ni daño clínico instantáneo.

## Integración y cierre obligatorio

Unir en `integration/phase-03`, conectar la misión exterior a la misma base/autoridad y verificar los dos recorridos: completar objetivo y regresar; o cancelar tras información nueva y entrar a protección. Evaluar decisiones según lo que el jugador sabía.

**La fase termina cuando:** preparar/salir/trabajar/retornar/ingresar consumen lo definido; el compañero y servicios siguen sincronizados; existe una alternativa opcional con costo; refugio y reintento funcionan fuera del Editor.

**Se aplaza:** dosimetría humana y curvas de blindaje sin transporte válido, rescate físico complejo, vehículos y un terreno planetario completo. El aviso no reduce automáticamente producción fotovoltaica.

## Regla de avance

No pasar a la fase siguiente hasta guardar el commit/build aceptados y los resultados reales de prueba. Se pueden explorar ideas futuras en una rama separada, pero no llamar completada una versión sin integrar. La fase 5 es el cierre de este roadmap; no se añade una fase extra de integración.
