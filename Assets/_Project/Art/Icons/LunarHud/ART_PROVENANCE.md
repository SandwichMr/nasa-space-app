# Procedencia del kit pixel art del HUD lunar

Veintiséis texturas originales generadas con `tools/art-pipeline/generate_hud_sprites.py` (Python puro, cuadrícula de píxeles ampliada ×2 por vecino más cercano, determinista). Sin IA de imágenes ni assets de terceros. El estilo se inspira en un dashboard de ejemplo y una hoja de iconos aportados por el equipo (no incluidos ni copiados): paneles azul marino con borde de píxel azul y esquinas biseladas, barras tipo píldora con brillo y símbolos de estado de alto contraste. Sin texto pintado.

- 9-slice (borde en píxeles de la textura): `frame-card` y `frame-panel` 48 × 48, borde 12; `button`, `button-hover`, `button-active` y el botón destacado `button-primary` / `button-primary-hover` (Iniciar misión, con brillo pulsante) 32 × 24, borde 6; `bar-track` y `bar-*` 24 × 12, borde 6.
- Símbolos: `status-ok`, `status-warn`, `status-crit`, `status-none` (28 × 28); iconos de recurso `icon-battery`, `icon-water`, `icon-food`, `icon-oxygen`, `icon-co2` (28 × 28); `trend-up`, `trend-down`, `trend-flat`; `badge-astronaut` (48 × 48).

Import: Sprite (referenciado como Texture2D), filtro Point, sin mipmaps, sin compresión, wrap Clamp. Se asignan en `Prefabs/UI/Phase04/BaseOperationsPanel.prefab` → `skin` (`HudSkin`), y la composición de la fase 4 pasa el mismo skin a `OutpostSessionControls`. Solo presentación: los valores, bandas y tendencias siguen viniendo del snapshot; O2 y CO2 de cabina siguen sin barra porque no tienen capacidad declarada. Sin skin, las fases 2 y 3 mantienen su aspecto anterior.
