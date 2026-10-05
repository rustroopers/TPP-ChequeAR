# Material para el Informe Final — ChequeAR

Insumos de gestión del Trabajo Profesional, listos para llevar al informe.
Reconstruidos del historial de commits y del anteproyecto (`EntregaInicial_equipo201`).

- **Marzo 2026** — anteproyecto (aprobación del informe pre-proyecto; acta del 20-abr).
- **Abril 2026** — inicio del desarrollo (Scrum, sprints de 2 semanas).
- **1 de noviembre de 2026** — presentación.
- De la fase de integración en adelante: **integración real con el Hospital Garrahan** (épica E12).

## Entregable principal

**[`ChequeAR_backlog_cronograma_riesgos.xlsx`](ChequeAR_backlog_cronograma_riesgos.xlsx)** — 3 hojas:

| Hoja | Contenido |
|------|-----------|
| **Backlog** | 12 épicas (E1–E5 = los 5 módulos del anteproyecto) y **64 User Stories** con prioridad, estimación en SP (Planning Poker), sprint y estado. Incluye resumen por épica con totales. |
| **Cronograma** | Anteproyecto + 14 sprints (S1–S14), fase, foco, épicas, SP e hitos. Con el Gantt embebido. |
| **Riesgos** | Matriz P × I: los 4 riesgos del anteproyecto + 5 de la fase de integración, ordenados por criticidad. |

## Scripts (reproducibles)

| Archivo | Qué hace |
|---------|----------|
| `generar_graficos.py` | Genera los PNG del cronograma y del esfuerzo (`python docs/informe/generar_graficos.py`) |
| `build_xlsx.py` | Genera el `.xlsx` (correr **después** de los gráficos, porque embebe el Gantt) |

Los datos (épicas, US, sprints, riesgos) están como listas al inicio de `build_xlsx.py`: editá ahí y volvé a correr.

## Números de referencia

- **Duración**: marzo (anteproyecto) → 1-nov (presentación); desarrollo desde abril.
- **Backlog**: 64 US · **340 SP entregados** de ~400 (~85 %); los 60 SP restantes son la integración (E12).
- **Épicas E1–E5**: mapean 1:1 con los 5 módulos de la solución propuesta del anteproyecto.
- **Conformidad**: FHIR R5 validado contra el validador oficial de HL7 (02-ago-2026).

> Las estimaciones en SP son una reconstrucción a posteriori con fines del informe; ajustá
> los valores en `build_xlsx.py` si tenés los registros reales de Planning Poker.
