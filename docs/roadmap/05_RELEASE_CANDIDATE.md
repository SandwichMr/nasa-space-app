# Fase 5 — Contratiempos, datos NASA y rejugabilidad

**Estado:** planificación revisada, no implementación. Sustituye la antigua fase 5 de release; el cierre pasa a fase 6.
**Nota de ruta:** se conserva el nombre histórico `05_RELEASE_CANDIDATE.md` para no romper enlaces existentes; su contenido vigente es este, no una fase de pulido.
**Objetivo:** que la base continua de fase 4 genere decisiones variadas mediante contratiempos, avisos contextualizados y oportunidades renovables, sin depender de un suceso solar real en ese momento.

## Qué podrá jugar el equipo

Gestionar una base que alterna preparación, incidentes y recuperación. Llegan avisos, fallan/degradan equipos según escenarios declarados y aparecen encargos científicos/expediciones opcionales. El grupo decide posponer ciencia, repartir mantenimiento, solicitar suministros, retirar a compañeros del exterior y preparar protección. Completar el episodio no cierra la partida: vuelve a un estado de operación y nuevas oportunidades.

La variedad no consiste en acelerar infinitamente las barras o apilar emergencias imposibles. Debe haber descansos, anticipación y más de una estrategia viable.

## Entrada y preparación común

Partir de una build aceptada de fase 4 y conservar la expedición/refugio de fase 3. D2 coordina la actualización común de contratos de incidente, observación, fuente/validez, presupuesto de eventos y encargos. D5 coordina el contrato de datos científicos. Se preparan juntos una sola vez, con implementaciones/fake y SHA completo antes de consumidores: `docs/phase-05/{SESSION_CONTRACT,OWNERSHIP,COMMON_BASE}.md`.

Las rutas abajo son propuestas; el manifiesto previo debe concretar archivos exclusivos. Paquetes/settings, API/hosting o cambios de contrato previos requieren coordinación, nunca cinco modificaciones paralelas.

## D1 — Consecuencias físicas y recuperación

**Propiedad propuesta:** `Runtime/Simulation/Incidents/**`, extensiones de equipo explícitamente asignadas y `Tests/EditMode/Phase05/Simulation/**`.

Definir cómo afecta cada incidente a capacidades, consumos, rendimiento o disponibilidad. El director solicita activar una condición; no modifica stocks por fuera del modelo. Mantener balances y efectos reversibles/irreversibles declarados. Probar acumulación y eliminación de modificadores sin restaurar masa perdida ni cobrar dos veces.

Primer catálogo acotado: degradación de reciclador, fallo de un servicio eléctrico/equipo y episodio de clima espacial con respuesta EVA/refugio. No añadir un modelo térmico nuevo completo si no es necesario para una decisión demostrable. Reutilizar lo implementado, sin daños clínicos ficticios.

## D2 — Director de eventos y actividades renovables

**Propiedad propuesta:** `Runtime/Application/Incidents/**`, `Runtime/Application/Operations/**`, escenario y composición asignados.

1. Director autoritativo reproducible por semilla con estados aviso → preparación → incidente → intervención → recuperación. Reglas de activación consideran estado de base, tripulación y recursos; incluir enfriamiento y presupuesto de simultaneidad.
2. Separar emergencias derivadas del modelo (agua realmente escasa) de incidentes authored (equipo que degrada). No generar agua escasa por azar sin descontarla/trazarla.
3. Elegir intensidad y frecuencia como parámetros de juego sintéticos, nunca como probabilidades NASA ni frecuencia médica real.
4. Encargos renovables con condiciones, costo, variedad y recompensa acotada; oportunidades y mejoras concretas de equipo, no tecnologías/árboles infinitos. Evitar la repetición obligatoria del mismo botón y las recompensas que eliminan todos los límites.
5. Conectar aviso de partículas con planificación, retorno y protección reales de fase 3. Evaluar decisiones con información disponible en ese momento, no conocimiento futuro.
6. Conservar operación tras resolver eventos y cerrar encargos. Agotar un catálogo finito no equivale a acabar la sesión: puede reutilizar variantes con reglas antirrepetición y límites explícitos.

**Entrega:** incidentes con decisiones/consecuencias y un ciclo renovable, no una lista de escenas terminales.

## D3 — Adquisición externa y sincronización

**Propiedad propuesta:** adaptadores en `Runtime/Unity/Integration/SpaceWeather/**`, red de incidentes en `Runtime/Unity/Networking/Incidents/**` y pruebas propias.

Con D5, verificar endpoints/esquemas oficiales vigentes antes de programar; no inventar una URL ni asumir que toda NASA usa la misma key. La autoridad/servicio autorizado descarga, valida, cachea y publica información normalizada; cada cliente no consulta ni sortea sus propios incidentes. No incrustar la API key en Git, assets, WebGL, logs o prefabs. Si WebGL requiere proxy secreto, proponer arquitectura antes de desplegar un servicio; no abrir infraestructura automáticamente.

Diseñar timeout, rate limits, cache/versiones, deduplicación y revisión de registros. Una caída, respuesta vacía o API lenta no bloquea el reloj ni la partida. Un dato externo informa el director mediante política explícita; no ejecuta comandos arbitrarios ni impone automáticamente daño.

Sincronizar semilla/estado confirmado, fuente del aviso, revisiones, expiración y tareas. Late join obtiene incidentes activos sin duplicar consecuencias. Separar hora científica, reloj de sesión y latencia de red.

## D4 — Avisos, consecuencias visibles y variedad

**Propiedad propuesta:** `Art/Phase05/**`, `Audio/Phase05/**`, `Prefabs/UI/Phase05/**`, `Runtime/Unity/Presentation/Incidents/**` y pruebas UI propias.

Mostrar qué está ocurriendo, qué se sabe, desde cuándo, su vigencia y qué acciones están disponibles. Señalar ruta/refugio, servicios degradados, ocupación y costos; no una pantalla de alarma constante. Información histórica, actual y sintética claramente diferenciada. API caída no se dibuja como «peligro cero».

Mantener visibles las barras esenciales de fase 4. Explicar cambios mediante el mundo, señales, animaciones y texto; sonido opcional, no único canal. Encargos y mejoras tienen efecto perceptible. Entregar prefabs/receta a D2 para montaje.

## D5 — Datos NASA, ciencia y escenarios

**Propiedad propuesta:** fixtures/cache de referencia en ruta acordada, `Editor/Validation/Phase05/**`, `Editor/Build/Phase05/**`, `Tests/EditMode/Phase05/Validation/**` y handoff propio.

- Verificar una integración real con una fuente NASA adecuada, por ejemplo DONKI para registros/eventos, según documentación y acceso comprobados. Guardar URL, tipo, identificador, unidades, tiempo de evento, disponibilidad/revisión, recuperación y procedencia/licencia. No afirmar que un registro detecta las condiciones exactas en nuestra base lunar ficticia.
- Entregar un replay histórico contextualizado y congelado; además, un generador sintético etiquetado para flujo continuo y fallback. Modo de observación actual solo si está implementado y probado, nunca requisito para poder jugar.
- Separar flare, CME y partículas energéticas. No convertir clase X, Kp, pfu ni dosis orbital directamente en dosis humana lunar. Una alerta no demuestra un impacto local ni una caída de producción solar. Sin transporte válido, enseñar respuesta cualitativa de protección, sin umbrales clínicos inventados.
- Probar API vacía/caída, datos viejos/futuros, revisiones y duplicados; no filtrar información disponible después de la decisión del jugador.
- Replays deterministas y tests de combinación: incidente con mantenimiento, desconexión, reinicio y reservas bajas. Verificar que director no cree estados sin salida por presupuesto/configuración.

## Integración y aceptación

D2 monta en `integration/phase-05` con el resto, usando el mismo mundo y una autoridad. La fase no se acepta por descargar JSON o mostrar una alerta.

**Cierre:** build con dos clientes reales que completa un tramo de operación normal, un contratiempo de equipo con recuperación y un episodio NASA histórico contextualizado con decisión de retorno/refugio; luego continúa jugando. Repetir con datos externos indisponibles usando fallback rotulado, sin romper multiplayer. Demostrar variedad entre semillas y reproducción de un mismo caso con la misma semilla. Registrar límites y evidencia por modo de datos.

**P1/P2, no bloqueantes del núcleo:** mayor catálogo, planificación alimentaria calibrada o nuevas mejoras. No cosechas instantáneas, MTBF inventados, pronóstico solar entrenado sin validación, dosimetría humana ni explotación de ISRU. No inflar la fase con todo el dossier científico.
