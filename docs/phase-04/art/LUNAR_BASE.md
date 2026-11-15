# Base y terreno lunar

**Interior vigente:** [base grande, nueva distribución y cuadrícula gris](LARGE_BASE.md). Las capturas y el arte gris descritos a continuación son la versión anterior; se conservan como referencia. El terreno repetido y las coordenadas globales siguen vigentes.

La escena de fase 4 usa `LunarBaseOptimized.png`, generada con imagegen a partir de la paleta gris, blanca y grafito de la referencia original. El interior mide 18 × 10 m, con divisiones montadas sobre la geometría autoritativa, pasos de 3 m y radio de colisión original de 0,35 m. Los astronautas aparecen en la zona central; agua, aire, ciencia y suministros mantienen sus funciones. La preparación EVA y la esclusa siguen conectadas a la derecha.

Arte: `Assets/_Project/Art/Sprites/Environment/LunarBase/`. Escenario propio: `Assets/_Project/Data/Scenarios/Phase04/Phase04_LunarInterior.asset`; fase 3 conserva su asset. Instalador: `Trainer.Tooling.Art.LunarBaseArtSetup.Run`. [Prompt y procedencia](GENERATION_PROMPT.md).

## Repetición del suelo

`LunarRegolith.png` es la imagen del usuario sin modificar. `LunarSurfaceView` mantiene 49 sprites, en celdas de 24 × 16 m, alrededor de la cámara. Cada celda tiene su posición global fija; al reciclar un sprite se lo coloca exactamente en otra celda entera. Los dos clientes asignan la misma textura a las mismas coordenadas. No se teletransporta al astronauta ni se modifica su reloj, inventario o posición autoritativa. La imagen fuente permanece intacta; celdas vecinas se reflejan alternadamente en X/Y según la paridad de sus coordenadas globales, para que compartan exactamente los mismos bordes sin cortes de color. La orientación también coincide entre clientes.

La representación se repite sin crecer en memoria. El área transitable tiene límites técnicos de −100 000 a +100 000 m en ambos ejes; no se presenta como un espacio matemáticamente infinito. Oxígeno, batería, agua del traje, reservas y margen de regreso siguen vigentes.

El campo de retorno calcula una cuadrícula detallada alrededor de los obstáculos y conecta puntos lejanos con su perímetro mediante segmentos libres de obstáculos ampliados por el radio de la tripulación. El piloto de validación y el validador acotan sus cuadrículas a la zona autorada para evitar asignaciones de tamaño planetario. El validador comprueba además que la región exterior cubra los límites extendidos.

## Comprobaciones

Evidencia: `Logs/lunar-base-v2/`. Las pruebas nuevas comprueban rutas interiores, posiciones transitables fuera del mapa anterior, retorno desde puntos lejanos y coordenadas de textura compartidas entre dos vistas, con reciclaje de objetos y vuelta a las mismas celdas.

El runner gráfico con `-lunar-render` captura la escena y desplaza solo la cámara de evidencia a (70,20), (94,36) y de vuelta a (9,5). Eso comprueba presentación; no constituye una salida EVA jugada ni teletransporta a la tripulación. La build y la continuidad certificadas antes del rediseño corresponden al plano antiguo.

Resultados de Unity 6000.3.28f1: EditMode completo **551/551**, PlayMode completo **76/76**, exit 0; runner gráfico productivo **PASS, exit 0**. Los intentos que exigían resolución fija fallaron por cambios de tamaño de Game View. El modo de evidencia lunar registra la resolución nativa real (1440×900 en las capturas exteriores finales), conserva las comprobaciones de controles/reloj/pausa y no certifica otros formatos.

[Vista de la base](lunar-base-v2-preview.png) · [Terreno exterior](lunar-exterior-preview.png). No se generó una nueva build ni se repitió la sesión humana.
