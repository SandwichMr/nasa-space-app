# Interior con damero blanco y negro

Versión anterior. El interior vigente es la [base grande con cuadrícula gris](LARGE_BASE.md).

El rediseño actual usa `LunarInteriorArchitecture`: un suelo de baldosas blancas y negras de 0,8 m, juntas finas, paredes modulares claras con contorno grafito y placas oscuras para los equipos. El damero cubre el hábitat, la preparación EVA, el refugio y la cámara de la esclusa. La geometría visual se construye sobre los rectángulos del escenario; ya no se estiran recortes de la imagen generada.

Las rocas exteriores se dibujan únicamente fuera de la estructura de la base. Los grandes rectángulos bloqueados debajo/encima de la esclusa no se representan como rocas gigantes. Los títulos de estaciones interiores se centran y reducen, y los estados se colocan en placas inferiores oscuras. El terreno lunar repetido conserva su implementación anterior.

El bloqueo rectangular debajo y encima de la esclusa se reduce a sus bordes de pared para permitir caminar por el terreno exterior visible. La prueba de mundo incluye transitabilidad y ruta de regreso desde (27,7; 2). No cambian los inventarios, los consumos ni las reglas de proximidad. Los recursos gráficos del damero y las superficies de pared se generan como primitivas en Unity y se liberan al destruir la vista.

Evidencia de esta revisión: `Logs/lunar-base-checker/`. EditMode completo: **551/551**, PlayMode completo: **76/76**, exit 0. La prueba de presentación usa la escena productiva; las vistas de cerca desplazan solo la cámara de evidencia, sin teletransportar a la tripulación. No es una sesión humana ni una nueva build.

Presentación productiva: **PASS, exit 0**, en `render-final/verification.txt`. Capturas finales: [hábitat](checker-base-preview.png) y [ala EVA y esclusa](checker-eva-preview.png).
