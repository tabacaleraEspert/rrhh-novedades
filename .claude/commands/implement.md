---
description: Ejecutar las tareas del spec activo respetando la Constitución
argument-hint: "[NNNN o slug; vacío = el más reciente]"
---

Implementá la feature siguiendo su checklist.

Pasos:
1. Ubicá el spec objetivo y abrí su `tasks.md` (si no existe, avisá que falta `/tasks`).
2. Leé `spec.md`, `plan.md`, `tasks.md` y la Constitución (`.specify/constitution.md`).
3. Ejecutá las tareas **en orden**. Después de cada cambio que lo amerite, corré los tests; marcá `[x]` la tarea en `tasks.md` al completarla.
4. Respetá sí o sí: `IDbContextFactory` (Art. 1), TZ Argentina (Art. 1), parámetros no constantes (Art. 2), Twilio outbound-only (Art. 3), registrar services/endpoints en las Extensions (Art. 8), "—" para nulos y sin emojis en UI (Art. 6).
5. Si una tarea revela algo no contemplado en el plan, **pará y avisá** antes de improvisar.
6. NO commitees ni pushees (Art. 7). Al cerrar: build sin warnings + tests verdes + (si cruza capas) smoke E2E, y entregá el **mensaje de commit propuesto** para que yo lo aplique.

Al terminar: resumí qué se hizo, el estado de los tests y el mensaje de commit.
