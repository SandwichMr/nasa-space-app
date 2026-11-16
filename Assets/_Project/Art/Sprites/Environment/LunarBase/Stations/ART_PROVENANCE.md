# Procedencia de los sprites de estaciones lunares

Trece sprites originales, uno por estación interactiva, generados con `tools/art-pipeline/generate_station_sprites.py` (Python puro, formas por distancia con signo y antialiasing, determinista). Sin IA de imágenes ni assets de terceros. Sin texto pintado: los rótulos siguen siendo texto en ejecución.

Estilo (equipos interiores): vista cenital 3/4 inspirada en una imagen de referencia aportada por el equipo (no incluida ni copiada): equipo sobre plataforma azul marino con borde de color, cilindros de vidrio con tapas metálicas, tuberías grises con collarines, LEDs luminosos y sombra proyectada abajo a la derecha. El color del borde indica la función: agua azul, aire/CO2 verde, energía amarillo, O2+agua y sellos cian, refugio ámbar, ciencia morado, logística naranja. Fondo transparente: sin placa oscura detrás; en el juego solo quedan pequeñas placas translúcidas bajo los rótulos para que el texto blanco se lea sobre el piso gris.

| Estación | Sprite | PPU | Tamaño en mundo |
|---|---|---|---|
| suit-rack-a / suit-rack-b | traje en su soporte sobre plataforma (cian / naranja) | 100 | 1.28 m (escala 0.5 del prefab) |
| suit-charger | dos celdas de batería con nivel de carga y poste de carga con rayo y cable | 100 | 1.28 m |
| consumables-panel | tanque de O2 (burbujas) y de agua, válvula con manómetro y manguera | 100 | 1.28 m |
| suit-check | cámara de vidrio con casco, anillo de escaneo del sello y pantalla de verificación | 100 | 1.28 m |
| shelter-panel | emisor con holograma de escudo y pilares de control | 100 | 1.28 m |
| science-lab | microscopio, tubos de muestra y matraz | 100 | 1.28 m |
| sample-analyzer | cámara de vidrio con roca lunar y terminal con espectro | 100 | 1.28 m |
| water-recycler / air-scrubber / logistics-console | agua usada → filtro → agua limpia; ventilador + cartuchos de CO2; cajas + terminal | 256 | 1.5 m (escala 1.5 del prefab D4) |
| regolith-site / crater-rim | pala y recipiente de muestra, perforadora de núcleo con estrella opcional | 128 | 2 m |

PNG RGBA 256 × 256, pivote central, FullRect, bilinear, mipmaps, sin compresión. Se asignan en `Phase04_LivingBase` → `BaseOperationsSceneComposition.lunarInteriorSkin.stations` por id de estación. Solo cambian el sprite del cuerpo: posición, alcance, identidad y estado vienen del escenario y del snapshot. Sin sprite asignado se mantiene el del prefab.
