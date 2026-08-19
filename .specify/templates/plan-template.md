# Plan técnico — <FEATURE> (spec NNNN)

> Deriva de `spec.md`. Acá va el *cómo*. Respeta la Constitución (`.specify/constitution.md`).

## Enfoque
Resumen de la solución técnica en 2-4 frases.

## Archivos afectados
| Archivo | Cambio |
|---|---|
| `src/RRHHNovedades.Web/...` | ... |

## Modelo de datos
¿Nuevas entidades o columnas? ¿Hace falta **migración EF**? (hoy se usa `EnsureCreated` — ver
Constitución / `docs/DEPLOY.md`). Idempotencia / índices afectados.

## Servicios / endpoints
Qué services o endpoints se tocan o agregan. Recordar registrarlos en las Extensions (Artículo 8).

## Configuración (Artículo 2)
¿Nuevos parámetros en `Options` / `appsettings`? Nada hardcodeado.

## Integraciones (Humand / Twilio)
Impacto en la API de Humand o en el envío Twilio. Cuidar: outbound only (Artículo 3), TZ
(Artículo 1), paginación (límite 50 / `totalPages` no confiable).

## Tests (Artículo 4 — obligatorio)
Qué tests unitarios/smoke se agregan o tocan. Casos reales a congelar.

## Manual de usuario (Artículo 5)
Qué se actualiza en `Ayuda.razor` + `UltimaActualizacion`. Si no aplica, justificar.

## Riesgos de implementación / rollback
- ...
