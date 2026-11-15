# Fase 4: sesión humana pendiente

Estado: **no ejecutada**. El usuario indicó el 2026-11-15 que no hay dos participantes disponibles. Los pilotos automáticos no cuentan como personas ni prueban comprensión, diversión o manejo físico del teclado.

## Preparar dos jugadores

Usar la build candidata de las pruebas automáticas, con gráficos y sin piloto. Desde la raíz del proyecto, abrir un anfitrión que también juega y un cliente:

```sh
P="$PWD/Builds/Phase4LivingBase/StandaloneLinux64/Phase4LivingBase.x86_64"
L="$PWD/Logs/phase04-integration/human"
mkdir -p "$L"
"$P" -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7799 -logFile "$L/host.log" &
"$P" -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7799 -trainer-connect -logFile "$L/client.log" &
```

El anfitrión comienza en `LISTA`. Pulsar **Iniciar misión**. Deben aparecer dos miembros. Mantener la sesión al menos **30 minutos reales**, sin reinicios durante el tramo de continuidad. Registrar aparte cualquier prueba deliberada de reinicio. El puerto 7799 evita mezclar esta sesión con las ejecuciones automáticas en 7795/7797/7798.

## Recorrido y registro

1. Ambos jugadores caminan, se ven y usan estaciones por proximidad. Abrir una estación con E o «Recursos y actividad»: comprobar que WASD, E y R no actúan mientras el panel tiene el foco. Escape y «×» deben liberarlos.
2. Agua: inspeccionar, procesar un lote, decidir potable o reprocesar usando la conductividad y observar el efecto. Distinguir unidades, tendencias y «sin lectura»/«dato antiguo» de un cero válido.
3. Aire: inspeccionar, cambiar de lecho o regenerar y observar la tendencia del CO2. Los límites son los del escenario sintético, no umbrales clínicos.
4. Reciclador: dejar llegar la degradación a 240 s simulados; diagnosticar, aislar, cambiar filtro con un repuesto y tiempo, probar y restituir. El agua perdida no debe reaparecer. Si se deja sin reparar, el fallo está programado para 540 s simulados.
5. Reposición: pedir, esperar el plazo y descargar una sola vez. Comprobar el límite de pedidos y la recuperación de existencias.
6. EVA: equipar y comprobar el traje, salir, completar ciencia o cancelarla y regresar. La base debe continuar; observar margen y servicios del traje. En un regreso sin ciencia, no se concede ciencia completada.
7. Los avisos de refugio **no se emiten en este escenario** (`keepPhase3Episode = false`). No marcar su recepción como probada. Activar un episodio requeriría una decisión explícita de escenario y otra validación.

Guardar capturas y logs en `Logs/phase04-integration/human/`. Registrar participantes como P1/P2, sin nombres ni datos personales:

| Dato | Valor observado |
| --- | --- |
| Build y hashes de `build-info.txt` / manifiesto | |
| Inicio y fin, con zona horaria | |
| Duración real medida, ≥ 30 min | |
| Tiempo simulado inicial/final y época | |
| Acciones realizadas por P1 y P2 | |
| Recursos antes/después; reparación y pedido | |
| Incidencias de teclado, foco, red o lectura de textos | |
| ¿Se entendieron la decisión de agua, el servicio de aire y el coste de reparar? | |

Una tabla vacía no es evidencia. Tras la sesión, contrastar estos datos con los logs y completar la puerta 7 de `PENDING_TESTS.md`.
