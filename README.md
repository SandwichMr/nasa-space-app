# Space Station Breakdown

**Mantén la base. Cuida las reservas. Haz ciencia. Regresa con tu equipo.**

Prototipo educativo cooperativo de gestión de una **base lunar**, desarrollado en **Unity Universal 2D** para el reto *Build a Junior Astronaut Mission Trainer* de **NASA Space Apps Challenge 2026**.

La idea es sencilla: la base sigue funcionando mientras el equipo juega. Repartirse mantenimiento, cuidar suministros y decidir cuándo hacer ciencia o volver de una expedición importa más que completar una lista de tareas.

![Vista de desarrollo de la base lunar](docs/phase-04/art/large-base-overview.png)

*Captura de desarrollo de la distribución lunar. El encuadre general es de evidencia y la paleta puede diferir de la escena actual; no representa toda la validación del juego.*

> **Estado:** prototipo en desarrollo, fase 4 en consolidación. Este repositorio incluye trabajo de simulación, presentación, red, integración y pruebas. No es una versión final ni un simulador certificado para operaciones espaciales reales.

## Qué propone el juego

Los jugadores controlan astronautas en una base cenital 2D y comparten recursos, equipos y decisiones. Mientras una persona atiende el reciclador, otra puede revisar el aire o preparar una salida científica. Una reparación cuesta tiempo y recursos; terminarla no devuelve lo que ya se perdió.

El ciclo buscado es:

**Observar → priorizar → repartirse el trabajo → intervenir → recuperar margen → continuar.**

Las tareas científicas y las expediciones son actividades dentro de la sesión. El modo continuo está diseñado para que completarlas no cierre ni reinicie toda la base. Los escenarios anteriores de entrenamiento conservan sus objetivos y resultados finitos.

## Sistemas incluidos en el prototipo

- **Cooperación en red:** arquitectura host/cliente sobre Unity Transport y WebSocket, con una única autoridad de simulación.
- **Astronautas y navegación:** movimiento 2D, presencia de compañeros, estaciones por proximidad, habitaciones, esclusa y exterior lunar.
- **Recursos compartidos:** energía y batería, agua por calidad, comida y servicios de aire con O2 y CO2 diferenciados.
- **Estaciones:** tratamiento de agua, decisiones de servicio de aire, ciencia y logística, con estados confirmados por la autoridad.
- **Mantenimiento:** degradación sintética del reciclador, diagnóstico, aislamiento, intervención, prueba y restitución.
- **Reposición:** pedidos, espera y descarga con las reglas y límites del escenario.
- **EVA:** preparación del traje, recursos propios, margen de retorno, esclusa, ciencia exterior y regreso/refugio en los escenarios correspondientes.
- **Interfaz:** barras, unidades, tendencias, ocupación, resultados de acciones y estados desconocidos o antiguos.

La implementación y sus límites están documentados en los [handoffs de fase 4](docs/phase-04/handoffs/) y en el [informe de integración](docs/phase-04/INTEGRATION.md). La presencia de un sistema no equivale a que todos sus recorridos y plataformas estén aceptados.

## Abrir el proyecto

### Requisitos

- **Unity Hub** y **Unity Editor 6000.3.28f1**, versión declarada en [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt).
- Git.
- Módulo de build del target que se quiera generar. La evidencia de ejecución disponible se concentra en **Linux**; no se promete compatibilidad final con otros targets sin pruebas específicas.

### Pasos

```sh
git clone https://github.com/SandwichMr/space-station-breakdown.git
cd space-station-breakdown
```

1. En Unity Hub, añade la carpeta clonada como proyecto existente.
2. Ábrela con la versión indicada y deja que Unity resuelva los paquetes/importación.
3. Abre `Assets/_Project/Scenes/Phase04_LivingBase.unity`.
4. Entra en Play Mode. En la configuración de autoridad jugable, el anfitrión también controla un astronauta.
5. Usa **Iniciar** en los controles de sesión para pasar de Ready a la simulación activa.

Las fases anteriores siguen disponibles en `Assets/_Project/Scenes/` como escenarios de desarrollo y regresión.

### Controles principales

- **WASD / flechas:** moverse.
- **E:** interactuar con la estación o panel cercano; en las estaciones de fase 4 abre la interfaz correspondiente.
- **R:** declarar el regreso en el flujo EVA cuando corresponda.
- **Interfaz:** decisiones, detalles de recursos y controles de iniciar, pausa y reinicio.
- **Escape / cerrar panel:** salir de la interfaz según su estado.

Las acciones pueden rechazarse por distancia, ocupación, falta de recursos o requisitos del procedimiento. La interfaz debe mostrar el estado confirmado, no dar por terminada una solicitud solo por enviarla.

## Multiplayer local y desarrollo

Para ejecutar varios procesos, genera primero una build del proyecto actual. No ejecutes Unity CLI sobre el mismo proyecto mientras esté abierto en otro Editor.

Ejemplo con la salida Linux del tooling de fase 4, desde la raíz del repositorio:

```sh
P="Builds/Phase4LivingBase/StandaloneLinux64/Phase4LivingBase.x86_64"

# Ventana del anfitrión: autoridad y jugador local.
"$P" -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port 7777

# En otra terminal: cliente de la misma build.
"$P" -trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect
```

Para una autoridad dedicada se añaden `-batchmode -nographics` al proceso autoridad. En LAN privada, utiliza una dirección de escucha apropiada y la IP del host en los clientes, con acceso permitido por el firewall. **Dos procesos en localhost no prueban LAN, Internet, crossplay ni WebGL/WSS.**

Este README no configura hosting público ni un túnel. No añadas credenciales o direcciones privadas de servicios al repositorio.

## Pruebas, builds y evidencia

El proyecto contiene pruebas EditMode/PlayMode, validadores de escenario/composición, verificaciones visuales y herramientas para ejecutar autoridad y clientes. Cada tipo de evidencia tiene un alcance diferente.

Ejemplo de Test Runner nativo en Linux, con el Editor del proyecto cerrado:

```sh
UNITY="/ruta/a/Unity/Hub/Editor/6000.3.28f1/Editor/Unity"
mkdir -p Logs/local-checks

"$UNITY" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/Logs/local-checks/editmode.xml" \
  -logFile "$PWD/Logs/local-checks/editmode.log"

"$UNITY" -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform PlayMode \
  -testResults "$PWD/Logs/local-checks/playmode.xml" \
  -logFile "$PWD/Logs/local-checks/playmode.log"
```

Una ejecución sin gráficos no valida legibilidad, teclado físico o experiencia de usuarios. Revisa los XML, el exit code y las pruebas omitidas/inconclusas, no solo si el proceso terminó.

El tooling de fase 4 está en `Assets/_Project/Editor/Build/Phase04/Phase04BuildChecks.cs`. Para generar una build Linux con la escena productiva:

```sh
"$UNITY" -batchmode -nographics -projectPath "$PWD" -buildTarget Linux64 \
  -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.BuildFromCommandLine \
  -logFile "$PWD/Logs/local-checks/build.log"
```

No uses una build forzada/diagnóstica como prueba de aceptación. Los logs y las builds se generan localmente y no se versionan.

### Estado de validación

Los XML locales de la revisión de base grande registran **551/551 EditMode** y **76/76 PlayMode**, sin fallos ni inconclusas. **Después hubo retoques de escena/configuración:** esas cifras no certifican por sí solas cada cambio posterior de este commit. Esta publicación no volvió a ejecutar Unity porque el proyecto estaba abierto en el Editor.

Existen pruebas automatizadas de continuidad y ejecuciones de builds anteriores; sus versiones, duración, límites e interrupciones están documentados. **No se atribuye la validación de una build anterior al rediseño visual posterior.** Sigue pendiente la aceptación prolongada con personas.

Referencias:
- [Integración y correcciones](docs/phase-04/INTEGRATION.md).
- [Resultados registrados de ejecución](docs/phase-04/integration-evidence/RUN_RESULTS_2026-11-15.md).
- [Pruebas y límites pendientes](docs/phase-04/integration-evidence/PENDING_TESTS.md).
- [Protocolo de sesión humana](docs/phase-04/integration-evidence/HUMAN_SESSION.md).
- [Distribución de la base y validación del rediseño](docs/phase-04/art/LARGE_BASE.md).

## Ciencia y datos NASA

El proyecto busca enseñar relaciones entre sistemas: gastar energía en ciencia puede reducir margen para otro servicio; tratar agua tiene entradas, salidas y pérdidas; regresar a tiempo puede ser una decisión correcta.

Los escenarios actuales usan **parámetros sintéticos identificados** y modelos reducidos. No son predicciones, tasas universales, umbrales médicos ni una reconstrucción certificada de una base real.

La integración de registros de clima espacial de **NASA/CCMC DONKI** pertenece a la **fase 5**. No se presenta como una conexión productiva ya terminada. Se prevén episodios históricos contextualizados y fallback sintético rotulado, sin hacer depender la partida de un evento solar real.

- Una llamarada, una CME y un evento de partículas no son equivalentes.
- Clase X, Kp o flujo de protones no se convierten directamente en dosis de un astronauta lunar.
- La ausencia de datos no significa ausencia de peligro.
- Esta versión no necesita configurar una API key NASA para abrir los escenarios sintéticos. Nunca incluyas claves en clientes, assets, Git o logs.

Más contexto: [reglas científicas](docs/SCIENTIFIC_RULES.md) y [DONKI oficial](https://ccmc.gsfc.nasa.gov/tools/DONKI/).

## Roadmap

- **Fases 1–3:** energía cooperativa, gestión interior y expediciones con regreso/refugio.
- **Fase 4:** base viva, recursos, mantenimiento, reposición e integración; consolidación actual.
- **Fase 5:** variedad de contratiempos, datos NASA contextualizados y actividades renovables.
- **Fase 6:** balance, guardado/reanudación, accesibilidad y validación de entrega.

Los planes no son funciones ya disponibles. Marte es una posible ampliación posterior, no un escenario implementado en esta versión.

Consulta los documentos vigentes en [docs/roadmap](docs/roadmap/), especialmente la [fase 4](docs/roadmap/04_MISSION_OPERATIONS_PROTOTYPE.md), [fase 5](docs/roadmap/05_RELEASE_CANDIDATE.md) y [fase 6](docs/roadmap/06_RELEASE_CANDIDATE.md). El archivo de fase 5 conserva un nombre histórico; su contenido vigente es el de contratiempos y rejugabilidad.

## Arquitectura y colaboración

```text
Unity / presentación / red
            ↓
       Application
            ↓
        Simulation
```

La autoridad procesa intenciones y avanza la simulación; los clientes reciben snapshots. Las vistas no mantienen inventarios ni controladores de movimiento alternativos.

Código y assets del proyecto: `Assets/_Project/`. Reparto y contratos: [OWNERSHIP de fase 4](docs/phase-04/OWNERSHIP.md) y [contrato de sesión](docs/phase-04/SESSION_CONTRACT.md). Antes de contribuir, lee [AGENTS.md](AGENTS.md), conserva metadatos/GUIDs y no publiques caches ni cambios de settings generados accidentalmente.

## Créditos y licencia

Desarrollo por el equipo del proyecto. [Contribuidores en GitHub](https://github.com/SandwichMr/space-station-breakdown/graphs/contributors).

La procedencia de los recursos visuales y sus prompts se documenta en [Art de fase 4](Assets/_Project/Art/Phase04/ART_PROVENANCE.md) y [arte lunar](docs/phase-04/art/LUNAR_BASE.md). Usar datos o referencias de NASA no implica aval o certificación de este prototipo por NASA.

El repositorio todavía no declara una licencia general mediante un archivo LICENSE. No asumas una licencia MIT u otra autorización que no esté publicada; consulta a los autores y la procedencia de cada recurso antes de redistribuirlo.
