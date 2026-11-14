# Phase 01 — arte básico original de D4

Los seis PNG de esta carpeta y los cinco PNG de `Art/Icons/Phase01/` son
geometría vectorial original creada para esta entrega: sin fotos, marcas,
logos, tipografías ni recursos de terceros. No se descargaron bancos de arte.
Su uso sigue la licencia del proyecto; esta entrega no modifica la licencia
del repositorio ni incorpora obligaciones de atribución de terceros.

Las fuentes editables están en las subcarpetas `Source/`. Los PNG se rasterizaron
con `rsvg-convert`; esa herramienta no se distribuye ni se necesita en el juego.
Para regenerar uno: `rsvg-convert Source/battery.svg -o battery.png` desde esta
carpeta. El fondo fuera de cada silueta es transparente. No son imágenes vacías.

- `power-room.png`: 1200 × 800; suelo, paredes, ventanas, bus y tres zócalos.
- `battery.png`, `essential-service.png`, `science-instrument.png`: 256 × 256.
- `battery-charge.png`: 128 × 24; relleno variable, solo con snapshot vigente.
- `station-selection.png`: 280 × 280; esquinas de selección.
- Iconos: 96 × 96; batería, servicio, ciencia, conexión y solicitud pendiente.

Importación: Sprite/Single, 100 px/unidad, alpha, sin mipmaps ni compresión,
filtro bilineal. Unity genera los `.meta`; conservarlos con los assets.
`Phase01Sprite.mat` utiliza el shader unlit del Universal 2D instalado, sin
necesitar luces nuevas. No cambiar renderer/settings del proyecto.

El servicio esencial representa una carga eléctrica prioritaria abstracta;
no representa oxígeno, atmósfera ni umbrales clínicos. El instrumento y la sala
son provisionales, sin fijar ambientación/cámara definitiva. Los colores ayudan
a identificar estaciones, pero sus nombres, lecturas y selección también
aparecen como texto en la interfaz.
