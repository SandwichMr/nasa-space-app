# Arte del menú

Se inspeccionaron visualmente las 85 imágenes PNG/JPG existentes en Assets mediante una hoja de contacto (`Logs/main-menu/sprite-contact-sheet.jpg`). También se revisaron las procedencias de fases 2/4, estaciones, interiores y HUD. Sin generación de imágenes nuevas, assets externos ni cambios a importadores existentes. `Art/MainMenu/{Panel,Button,Primary}.asset` contiene tres vistas Sprite con bordes 9-slice (12/6/6 px) que referencian las texturas originales del HUD; no duplica ni retoca sus píxeles.

Referencias reutilizadas, sin copiar ni modificar sus archivos:

- `Art/Icons/LunarHud/`: frame-panel, button, button-active, icon-water, icon-oxygen, icon-battery. Pixel art procedural original; procedencia en `Art/Icons/LunarHud/ART_PROVENANCE.md`.
- `Art/Sprites/Phase02/astronaut-sheet.png`: sprite existente de la tripulación generado con OpenAI image_gen; procedencia en `Art/Sprites/Phase02/ART_PROVENANCE.md`. Se usa una celda de su importación existente.
- `Art/Sprites/Environment/LunarBase/LunarBaseInterior.png` y `LunarRegolith.png`: referencias aportadas por el usuario, ya presentes en el proyecto; documentadas en `docs/phase-04/art/GENERATION_PROMPT.md`. No se atribuyen a NASA ni se inventa licencia de terceros. Derechos/licencia de las imágenes aportadas por el equipo: verificar antes de distribución pública comercial.
- Fuente `LegacyRuntime.ttf` builtin Unity; no se redistribuye una fuente descargada.

El menú conserva azul marino, biseles del HUD, gris lunar y acento cian. Los iconos de recursos son decorativos, sin valores ficticios. No se incluyeron logos NASA, imágenes de Among Us ni datos científicos nuevos. No se modifica la licencia del repositorio.
