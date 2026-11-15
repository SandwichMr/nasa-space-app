# Base grande: distribución y paleta actual

El hábitat mide **68,4 × 40 m** (2736 m² de superficie de juego), frente a los 26,3 × 10 m del plano anterior: aproximadamente **10,4 veces el área**. La esclusa añade un módulo de 6 × 6 m al este. La escala y velocidad del astronauta se mantienen; el encuadre normal sigue mostrando 12,5 m de altura, de modo que las salas se recorren caminando. La vista general de evidencia usa un encuadre temporal más amplio.

`LunarBasePlan` define el plano autorado. `LunarBaseArtSetup.Run` lo instala en `Phase04_LunarInterior.asset`; la vista proyecta esa misma geometría.

- Norte: reciclaje de agua, soporte de aire y laboratorio de dos estaciones.
- Sur: almacén y logística, área común y refugio.
- Centro: corredor de 8 m; accesos de 4 m a las salas.
- Este: preparación EVA y esclusa con escotillas y paneles reubicados.
- Mesas, bancos y bloques de almacén tienen obstáculos físicos correspondientes.
- Sitios exteriores, antena, entrada de regreso y zonas se reubican junto al nuevo acceso.

El suelo usa los dos primeros códigos de la referencia del usuario: **cuadros #54565A**, **líneas #2B2C2E**. Cada cuadro mide 1 m. No hay damero blanco y negro. Las salas tienen rótulos y las estaciones conservan sus funciones.

La superficie de juego no se convierte automáticamente en un volumen de gas: el modelo de soporte vital continúa siendo el modelo reducido sintético con sus parámetros independientes y unidades explícitas. Se conservan consumos y stocks; no se agregan inventarios en las vistas. El terreno lunar repetido y sus coordenadas globales se mantienen.

Validación local en `Logs/lunar-base-large/`: instalador con rutas a todas las estaciones interiores, cuatro apariciones válidas, EditMode **551/551** y PlayMode **76/76**, exit 0. Las pruebas incluyen tamaño mínimo del hábitat, acceso al área común, posiciones exteriores y rutas de regreso. La sesión humana y una nueva build no forman parte de esta revisión.

Presentación productiva: **PASS, exit 0**. [Plano completo](large-base-overview.png), [sala de agua a escala normal](large-base-water-room.png), [preparación EVA y esclusa](large-base-eva.png). Las capturas generales amplían solo la cámara de evidencia; el encuadre de juego conserva su altura normal.

La paleta se actualizó después de las capturas enlazadas; esas imágenes muestran los tonos anteriores.
