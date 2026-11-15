# Procedencia del arte D4 de fase 4

Ocho SVG originales creados para esta entrega y rasterizados localmente con librsvg (`rsvg-convert`). Sin herramientas externas, IA de imágenes ni assets de terceros. Continúan los SVG propios de Phase02/Phase03: azul pizarra, bisel claro, agua cian, aire verde y logística ámbar. No se genera otro astronauta ni se copia Among Us. Aportados bajo las mismas condiciones del proyecto.

Estaciones: PNG RGBA 256 × 256, 256 PPU, pivote central (0.5, 0.5); iconos: 64 × 64, 64 PPU, mismo pivote. FullRect, bilinear, sin compresión ni mipmaps, alpha transparency. Sin texto pintado. Los rótulos y estados son texto en ejecución. Estaciones con escala visual 1.5; no alteran geometría ni alcance. Material URP unlit existente Phase01Sprite. Sorting Default: señal de suelo -780; cuerpo 1000 − round(20y); progreso 2500; texto 3000. No luces, capas, colisiones o renderer nuevos.

El importador y los cinco prefabs se generan con `Tools > Trainer > Phase 04 > Build D4 visual prefabs`. PNG regenerable con `rsvg-convert Source/nombre.svg -o nombre.png`. Las capturas del handoff verifican el render con el fake común, sin acreditar autoridad ni red.

El aro de piso reutiliza `Phase02/crew-marker.png` con tamaño visual 1.8 × 0.65 m. Es una placa decorativa, no el radio autoritativo de interacción. La proximidad viene del escenario y el snapshot; el aro solo cambia de intensidad al estar cerca.
