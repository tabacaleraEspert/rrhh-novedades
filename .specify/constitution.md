# Constitución — RRHHNovedades

Principios **no negociables** que gobiernan toda spec, plan e implementación del proyecto.
Cualquier propuesta que los viole debe revisarse y aprobarse antes de avanzar.
Destilado de `CLAUDE.md` (referencia operativa completa) para el flujo SDD.

## Propósito del producto
El corazón del sistema es **el bot**: 2 partes diarios de asistencia por WhatsApp (07:00 / 14:00,
parametrizables) al equipo de RRHH, a partir de datos de Humand. El dashboard es secundario.
Toda feature se evalúa por cuánto sirve a ese propósito; lo que no, va al backlog.

## Artículo 1 — Datos y acceso
- **Siempre `IDbContextFactory<AppDbContext>`**; nunca inyectar `DbContext` directo.
- Ingesta **idempotente** (reproceso sin duplicar; clave empleado + fecha).
- Toda fecha/hora en **TZ Argentina**. Las horas literales de Humand (`timeSlots`) NO se convierten
  de zona; los timestamps ISO completos (`entries`) sí.

## Artículo 2 — Parámetros, no constantes
- Horarios, tolerancias y umbrales son **configuración** (`Options` + `appsettings`), nunca
  hardcodeados en el código.

## Artículo 3 — El bot no daña a ChatbotCobros
- Twilio es **outbound only**; reutiliza la cuenta sin tocar el webhook ni el flujo inbound del
  otro proyecto.

## Artículo 4 — Verificación obligatoria (harness)
- **Todo bug encontrado en vivo se convierte en test** antes de darse por cerrado.
- Correr los tests después de cada cambio; el **smoke E2E** (`bash tools/smoke-test.sh`) antes de
  dar por buena una feature que cruce capas.
- Los casos reales se **congelan** como tests (no se borran al "arreglar").

## Artículo 5 — El manual vive con el código
- **Todo cambio funcional visible para el usuario** se refleja en `Components/Pages/Ayuda.razor`
  en el mismo cambio, actualizando la constante `UltimaActualizacion`. Lenguaje simple, sin jerga.

## Artículo 6 — UI / Marca ESPERT
- Colores: gold #A48242, gold-light #C4A866, gray-dark #53565A. Fuente Inter.
- **Sin emojis en UI** (usar `MudIcon`). Nulos se muestran como **"—"** (em dash).
- Páginas interactivas: `@rendermode InteractiveServer`. Feedback de error con `ISnackbar`.

## Artículo 7 — Git y secretos
- Conventional commits (`feat:` / `fix:` / `chore:`).
- **NUNCA** commitear ni pushear sin aprobación explícita del usuario (él pushea).
- **Cero secretos en el repo**: van en `appsettings.secrets.local.json` (gitignored) o en las
  App Settings de Azure.

## Artículo 8 — Registro de servicios y endpoints
- Servicios se registran en `Extensions/ServiceCollectionExtensions.cs`.
- Endpoints se registran en `Extensions/EndpointExtensions.cs`.

---

## Cómo se trabaja (flujo SDD)
Cada feature recorre 4 pasos; cada uno produce un archivo en `specs/NNNN-<slug>/`:

1. **`/specify <feature>`** → `spec.md` — *qué* y *por qué* (sin el cómo técnico).
2. **`/plan`** → `plan.md` — *cómo*: archivos, datos, tests, impacto en Ayuda.
3. **`/tasks`** → `tasks.md` — checklist ordenado y verificable.
4. **`/implement`** → ejecuta las tareas respetando esta Constitución.

Detalle del flujo y convenciones: `specs/README.md`. Templates: `.specify/templates/`.
