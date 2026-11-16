# Procedencia del arte interior de la base lunar

Seis texturas originales generadas proceduralmente con `tools/art-pipeline/generate_lunar_interior_textures.py` (Python puro, sin dependencias, determinista). Sin IA de imágenes ni assets de terceros. Regenerables con `python3 tools/art-pipeline/generate_lunar_interior_textures.py`.

- Pisos (`floor-*.png`): 256 × 256 RGBA, 128 PPU (repiten cada 2 m), sin costuras, mipmaps, wrap Repeat, FullRect, sin compresión. El pasillo (metal gris claro con remaches) se usa en todo el hábitat y en todas las salas; solo la salida hacia la Luna conserva piso propio: Preparación EVA (antideslizante con marcas amarillas) y la esclusa (rejilla con franjas de peligro).
- 9-slice: `wall-panel.png` y `furniture-top.png` (64 × 64, 256 PPU, borde 16 px), `edge-shadow.png` (64 × 64, 64 PPU, borde 24 px, oclusión suave junto a los muros).

Solo presentación: se asignan en `Phase04_LivingBase` → `BaseOperationsSceneComposition.lunarInteriorSkin` y los dibuja `LunarInteriorArchitecture` sobre los mismos rectángulos de la autoridad. No cambian geometría, colisiones, estado ni red. Si falta un sprite, se usa la cuadrícula gris procedural anterior.
