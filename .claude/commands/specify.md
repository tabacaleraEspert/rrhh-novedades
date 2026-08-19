---
description: Crear una nueva spec (qué y por qué) para una feature, desde el template SDD
argument-hint: <descripción corta de la feature>
---

Vas a crear una nueva **especificación** para esta feature: "$ARGUMENTS".

Pasos:
1. Mirá las carpetas en `specs/` y determiná el próximo número de 4 dígitos (`NNNN`). Si no hay ninguna feature todavía, empezá en `0001`.
2. Generá un slug corto en kebab-case a partir de la descripción.
3. Creá `specs/NNNN-<slug>/spec.md` tomando como base `.specify/templates/spec-template.md`, completándola con lo que sepas de "$ARGUMENTS". Poné la fecha de hoy y el número en el título.
4. Respetá la Constitución (`.specify/constitution.md`): esto es **qué y por qué**, NO el cómo técnico. Marcá explícitamente lo que falte definir (preguntas abiertas y decisiones de negocio con Yanina si corresponde).
5. Si hay ambigüedad que cambie el alcance, preguntá antes de fijarla.

Al terminar: mostrame un resumen del spec, el path creado, y recordame que el siguiente paso es `/plan`.
