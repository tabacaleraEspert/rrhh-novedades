---
description: Desglosar el plan técnico en un checklist de tareas verificables
argument-hint: "[NNNN o slug; vacío = el más reciente]"
---

Convertí el plan técnico en una lista de **tareas** ejecutables.

Pasos:
1. Ubicá el spec objetivo (igual que `/plan`: "$ARGUMENTS" o el más reciente). Debe existir `plan.md`; si no, avisá que primero hay que correr `/plan`.
2. Leé `plan.md` y la Constitución (`.specify/constitution.md`).
3. Creá `tasks.md` con `.specify/templates/tasks-template.md`: tareas chicas, ordenadas y verificables. Incluí SIEMPRE las tareas de test (Artículo 4) y de manual de usuario (Artículo 5, si aplica), más build verde y mensaje de commit sin commitear (Artículo 7).
4. Cada tarea tiene que poder marcarse `[x]` sin ambigüedad.

Al terminar: mostrá el checklist y recordame que el siguiente paso es `/implement`.
