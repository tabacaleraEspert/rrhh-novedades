---
description: Generar el plan técnico (cómo) a partir de la spec activa
argument-hint: "[NNNN o slug del spec; vacío = el más reciente]"
---

Generá el **plan técnico** de una feature ya especificada.

Pasos:
1. Identificá el spec objetivo en `specs/`: si "$ARGUMENTS" trae número o slug, usá ese; si está vacío, usá la carpeta con mayor `NNNN`.
2. Leé su `spec.md` completo y la Constitución (`.specify/constitution.md`).
3. Antes de escribir, **explorá el código real** afectado (no asumas): services, models, endpoints, Options, tests existentes.
4. Creá `plan.md` en la misma carpeta usando `.specify/templates/plan-template.md`. Sé concreto: archivos exactos, si hace falta migración EF, qué tests se agregan (Artículo 4), qué se toca en `Ayuda.razor` (Artículo 5), qué services/endpoints registrar (Artículo 8).
5. Si el spec tiene preguntas abiertas que bloquean el plan, marcalas y preguntá.

Al terminar: resumí el enfoque y recordame que el siguiente paso es `/tasks`.
