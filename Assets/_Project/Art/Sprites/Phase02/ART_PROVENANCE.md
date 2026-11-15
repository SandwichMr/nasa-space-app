# Procedencia de arte D4 · fase 2

Arte original preparado para este proyecto. No se descargaron ni copiaron sprites,
mapas, nombres de salas, marcas o recursos de Among Us. La referencia de dirección
es la navegación cenital entre habitaciones y estaciones, con tripulación legible.

| Asset | Procedencia | Consumo en Unity |
| --- | --- | --- |
| `astronaut-sheet.png` | Generado con la herramienta integrada OpenAI `image_gen`, una generación; prompt íntegro en `Source/astronaut-prompt.txt`. Guardado sin retoques de píxeles, recortes ni recoloreado del archivo. | Sprite/Multiple, 4 × 4 cortes; cuatro orientaciones, dos poses de reposo y dos de marcha. El importador recorta la cuadrícula y el renderer aplica un tinte leve por hueco. |
| `floor-tile.png`, `wall-panel.png`, `white-pixel.png`, `crew-marker.png`, `water-station.png`, `air-station.png` | Geometría SVG original creada en esta entrega, siguiendo el sistema editable de fase 1; fuentes en `Source/`. Rasterizado con `rsvg-convert` ya instalado. | PNG con metadata generada por Unity. SVG solo como fuente, sin paquete/importador vectorial nuevo. |
| Material `Phase01Sprite.mat`, sprites `science-instrument`, `battery`, `station-selection`, iconos `battery-icon`, `science-icon` | Recursos originales existentes de fase 1, reutilizados por referencia sin modificar sus archivos o GUID. | Material unlit del Universal 2D instalado. Batería conserva significado eléctrico; instrumento identifica ciencia; selección indica alcance. No se reetiqueta el servicio eléctrico esencial como oxígeno. |
| Texto de mundo | `LegacyRuntime.ttf`, fuente builtin de Unity, referenciada por el Editor. | No se copia una tipografía externa al repositorio. |

No se incorpora una licencia de banco de assets ni se declara exclusividad sobre
salidas generadas por IA. El uso del contenido nuevo sigue las condiciones
aplicables a la herramienta y la política/licencia que establezca el proyecto;
esta entrega no cambia la licencia del repositorio ni concede derechos de terceros.
No se usaron logos de NASA ni afirmaciones de certificación.

Los ocho colores de identificación también llevan número de hueco; «TÚ» identifica
solo al miembro local. Los huecos se reutilizan; nunca sustituyen al PlayerId.
Los terminales nuevos son placas de piso transitables: su dibujo no promete un
obstáculo inexistente. Si D2 define mobiliario bloqueante, su huella debe entrar
en `OutpostLayout.Obstacles`; D4 dibuja esos rectángulos exactamente.

Regenerar un PNG vectorial: `rsvg-convert Source/water-station.svg -o water-station.png`
desde esta carpeta. Reimportar/reconstruir solo mediante el menú explícito D4;
no reconstruir assets con ajustes manuales posteriores sin revisar el diff.
