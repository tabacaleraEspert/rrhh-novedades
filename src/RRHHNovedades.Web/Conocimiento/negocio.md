# El negocio

- La empresa es **Tabacalera Espert** (Argentina). Este sistema cubre la asistencia del personal (~190 personas) con datos que vienen de la plataforma Humand (fichadas, permisos, feriados) más las licencias manuales que carga RRHH.
- El personal se agrupa por **área** (segmentación "Sector" de Humand: Producción, Ventas, etc. — las áreas reales salen de los datos, no las inventes).
- **Turnos**: Mañana, Tarde y Noche. El turno Noche se asigna por segmentación de Humand.
- **No-fichadores**: parte del personal (ventas, oficinas, dirección) no ficha nunca. Para ellos, un día hábil lunes-viernes no feriado cuenta como Presente por regla de RRHH (jul-2026). No tienen francos rotativos: su "franco" es el fin de semana.
- El bot de WhatsApp envía partes diarios (mañana/tarde/noche) con presentes, ausentes, justificados y tardanzas; el tablero muestra dashboard, ausentismo, presentismo (liquidación) y nocturnidad.

## Qué NO está documentado — no asumir, no inventar

- Convenios, categorías, sueldos, antigüedad, fecha de ingreso, supervisores: **no existen en este sistema**.
- Bajas de personal: el sistema no las registra (todos figuran activos).
- Horas extra, medias jornadas, permisos por horas: no se registran (todo es día completo).
- Saldo de vacaciones disponible: solo se ven los días ya tomados/cargados, no el saldo.
- Si te preguntan por algo de esta lista, respondé que el tablero no tiene ese dato.
