# Specs — flujo Spec-Driven Development (SDD)

Acá vive el trabajo de cada feature, especificado **antes** de tocar código. Una carpeta por
feature: `specs/NNNN-<slug>/`, numerada en orden (`0001`, `0002`, ...).

## El flujo

```
/specify <feature>   →  spec.md    Qué y por qué (sin cómo técnico)
        ↓
/plan                →  plan.md     Cómo: archivos, datos, tests, manual
        ↓
/tasks               →  tasks.md    Checklist ordenado y verificable
        ↓
/implement           →  ejecuta las tareas respetando la Constitución
```

Cada comando trabaja sobre el spec indicado por número/slug, o sobre el **más reciente** si no se
le pasa argumento.

## Reglas del juego
- Toda feature pasa por `spec.md` antes de codear. Si es un cambio chico/mecánico, igual conviene
  un spec breve.
- El **qué** (spec) no mezcla el **cómo** (plan). Eso mantiene el spec legible para el negocio.
- Todo plan e implementación respeta la **Constitución** (`.specify/constitution.md`): tests
  obligatorios, manual de usuario actualizado, TZ Argentina, `IDbContextFactory`, sin secretos,
  no commitear sin aprobación.
- Los specs son artefactos del proyecto: **se commitean** (no son secretos).

## Estructura de una feature
```
specs/0001-mi-feature/
├── spec.md     # qué y por qué        (lo crea /specify)
├── plan.md     # cómo                  (lo crea /plan)
└── tasks.md    # checklist             (lo crea /tasks)
```

## Estado
- Plantillas: `.specify/templates/`
- Principios: `.specify/constitution.md`
- Referencia operativa del proyecto: `CLAUDE.md`
- Backlog / pendientes priorizados: `docs/PENDIENTES.md`

> Candidatos para el primer spec (del audit de pendientes): el **fallback DEV silencioso de Twilio**
> y el **doble-envío del scheduler** (dedup en memoria). Ver `docs/PENDIENTES.md`.
