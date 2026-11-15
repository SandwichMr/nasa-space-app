# Fase 6 — Balance, continuidad y entrega verificable

**Estado:** nueva fase de planificación; no implementación ni lanzamiento.
**Objetivo:** entregar el juego de base continua de fases 4–5, comprensible, coherente y probado. Esta fase no es un cajón donde dejar la integración pendiente de todas las anteriores.

## Entrada

Exigir una build integrada de fase 5: recursos reales, interacciones, contratiempos, datos contextualizados/fallback y dos clientes. Aquí se estabiliza y entrega esa experiencia; no se inventa por primera vez su ciclo de juego.

D2 registra base, SDK/Editor/target, capacidad anunciada y topología acordada antes de repartir correcciones. Dos clientes son el mínimo de desarrollo, no prueba automática para cinco personas. No cambiar SDK/renderer ni crear hosting público sin aprobación.

## D1 — Balance y estabilidad de sesiones largas

Propiedad: simulaciones/pruebas existentes asignadas en lista exacta, sin reescritura general.

Ajustar ritmos para alternar preparación, presión y recuperación; no una derrota inevitable por stocks finitos ni un equilibrio en el que nada importe. Verificar costos de reposición, oportunidades y mejoras sin ciclos de recursos infinitos gratuitos. Conservar tasas físicas/contextos y etiquetar escalas de juego sintéticas. No maquillar balances para pasar una demo.

Ejecutar pruebas aceleradas para límites numéricos y desbordamientos; no confundirlas con una sesión humana divertida ni con duración real del test. Registrar memoria, costo de simulación y tolerancias en la configuración acordada.

## D2 — Guardado/reanudación y flujo de entrega

Propiedad propuesta: `Runtime/Application/Persistence/**`, adaptador de guardado en ruta acordada, bootstrap/escenas asignados y tests propios.

Guardar/reanudar una sesión del host sin reiniciar los stocks. Definir versión, integridad, escritura atómica, manejo de archivo incompleto y estado de reservas/tareas tras carga. Guardar reloj/semilla/estado necesario de incidentes; no reejecutar transferencias ya aplicadas ni conceder recompensas duplicadas. No guardar claves NASA.

El guardado es del host: no implica nube, migración de host ni progreso consumiéndose fuera de sesión. Caída/pérdida de host debe tener un mensaje y camino de recuperación comprobados, sin prometer reconstrucción de datos nunca guardados.

Pulir entrada, tutorial breve dentro de la base, pausa autorizada, salida voluntaria, resumen no terminal de progreso y reinicio explícito. Mantener un solo runner y montar las correcciones de los demás en la escena real.

## D3 — Conectividad y límites anunciados

Propiedad: red/adaptadores y pruebas asignados, sin reemplazar transporte.

Probar el grupo soportado en la topología/target anunciados: unión tardía, dos jugadores en la misma estación, desconexión en tarea/expedición, reconexión solo si existe, guardado/carga y pérdida de host. Distinguir dos procesos locales, LAN, Internet y crossplay; no vender uno como otro.

Revisar autorización, límites de payload, duplicación, reintentos y estados obsoletos. El host mantiene autoridad de guardado y simulación. No abrir túneles o desplegar LXC sin autorización.

## D4 — Cohesión visual, feedback y accesibilidad

Propiedad: assets/prefabs/presentación asignados y pruebas UI, sin tocar reglas de dominio.

Unificar interior y exterior para que no parezcan dos juegos distintos. Corregir base vacía o ilegible, no solo decorar el exterior. Mantener estaciones distinguibles y astronautas/control claros. Pulir barras, tendencias, alertas, estados de equipos e interacciones; explicar el porqué de una acción y su efecto.

Texto legible, escala de UI, contraste, icono+texto además de color, control sin micrófono y audio opcional. Fuentes, procedencia y licencias accesibles. No abrir una producción artística enorme ni ocultar recursos esenciales por minimalismo.

## D5 — Aceptación, build y paquete final

Propiedad: validadores/build tooling/tests y docs de release según manifiesto.

- Regresión nativa EditMode/PlayMode, validadores, build del target correcto y ejecución fuera del Editor.
- Sesión propuesta de al menos 60 minutos de tiempo real con el grupo anunciado, registrando incidentes, recuperación y problemas de comprensión; documentar si no se pudo ejecutar. Pruebas aceleradas son evidencia distinta.
- Comprobar completar varios encargos sin cierre de sesión, reservas que bajan y se recuperan, un incidente de equipo, replay de clima espacial y fallback sin API. No afirmar que online funciona sin red: fallback de datos y conectividad de jugadores son independientes.
- Guardar, cerrar host y cargar: estado coherente y sin duplicación. Simular archivo corrupto y mensaje recuperable, sin perder silenciosamente el último guardado válido.
- Pruebas de comprensión con participantes apropiados/autorizados cuando sea viable, sin datos personales innecesarios ni inventar aprobación de niños.
- Entregar build identificada por commit, controles, acceso host/cliente, compatibilidad, créditos, fuentes, modos de datos y limitaciones.

## Preparación común e integración

D2 prepara únicamente contratos adicionales necesarios (por ejemplo persistencia) y un manifiesto real `docs/phase-06/{SESSION_CONTRACT,OWNERSHIP,COMMON_BASE}.md`. Publicar SHA compartido antes de consumidores. No regenerar contratos/fakes previos ni solapar editores de escena.

Reunir cambios en `integration/phase-06`, revisar diff y generar la build desde un commit identificado. Cada cambio conserva regresiones; no aplazar el primer ensamblaje a esta fase.

**Cierre obligatorio:** una persona ajena al desarrollo puede entrar con su equipo, entender necesidades, mantener la base, reaccionar a incidentes, hacer expediciones/encargos, guardar y continuar sin ayuda permanente. La base no termina por una lista de objetivos. Existen evidencias reales de estabilidad/conectividad, instrucciones y límites honestos.

Publicación, envío al challenge o despliegue solo con autorización expresa. No se añade una fase 7 automática: lo no esencial se documenta como trabajo futuro.

**Exclusiones:** mundo abierto planetario, construcción libre, migración automática de host, servicio persistente 24/7, agricultura autosuficiente, dosimetría clínica y funcionalidades online no probadas.
