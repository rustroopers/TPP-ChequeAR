# Implementation Plan: Evolución longitudinal de parámetros por paciente

**Branch**: `010-evolucion-parametros-paciente` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-evolucion-parametros-paciente/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Desde el historial de un paciente (`EncountersController.Timeline`, R7), permitir elegir un cuestionario y uno o más parámetros numéricos/fecha de ese cuestionario, y mostrar un gráfico de evolución por parámetro (apilados sobre el mismo eje de fechas, con franja de rango normal), construido solo con las fichas **finalizadas** del paciente. El enfoque técnico reutiliza infraestructura ya existente: la vista Oracle `vw_FlatResponses` (misma fuente que usa el módulo de Reportes) para los puntos de datos, `Question.ValidationJson` para el rango normal, y el patrón de "isla Vue con SVG a mano, sin librería de gráficos externa" ya establecido en `Scripts/islands/report-stats`.

## Technical Context

**Language/Version**: C# sobre .NET Framework 4.7.1 (MVC 5 + Web API 2), JavaScript ES5 (Vue 3 global build vía CDN, sin build step) para el frontend.

**Primary Dependencies**: EF6 + Oracle.ManagedDataAccess (acceso a datos), Newtonsoft.Json (serialización), Vue 3 + Bootstrap (frontend, ya cargados globalmente en el layout). No se agrega ninguna dependencia nueva.

**Storage**: Oracle. Se reutiliza la vista `vw_FlatResponses` (formato largo: una fila por ficha+pregunta) ya usada por `Application/Reports/QueryBuilder.cs`; no se crea vista ni tabla nueva.

**Testing**: xUnit 2.7 + Moq 4.20 (`ChequeAR.Tests`), mismo patrón que `QuestionnaireResponseServiceTests.cs`.

**Target Platform**: IIS 10 / Windows Server 2016, en la red interna del Hospital Garrahan (sin garantía de salida a internet — condiciona no sumar CDNs nuevos).

**Project Type**: Web application, Package by Layer (`ChequeAR.Api` / `ChequeAR.Application` / `ChequeAR.Domain` / `ChequeAR.Infrastructure`).

**Performance Goals**: Uso interactivo dentro de la consulta de un único paciente — el volumen esperado son decenas de fichas finalizadas por paciente, no miles; no aplica el mismo cuidado de paginación que el módulo de Reportes a nivel cohorte.

**Constraints**: Cero librerías de gráficos externas (misma restricción ya documentada en `report-stats/index.js`: la intranet del hospital no garantiza acceso a un CDN de terceros). El endpoint no debe reusar la DSL de filtros de `QueryBuilder` (`ReportQuery`/`FilterNode`) tal cual, porque esa DSL está pensada para cohortes con filtros arbitrarios; acá el filtro es fijo (paciente + cuestionario + lista de linkIds + estado finalizada), así que conviene una consulta dedicada y más simple.

**Scale/Scope**: Un paciente y un cuestionario por vez. Sin comparación entre pacientes ni entre cuestionarios distintos (fuera de alcance, ver spec).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

El archivo `.specify/memory/constitution.md` del proyecto todavía tiene el contenido placeholder de la plantilla (no fue completado con principios concretos) — no hay gates formales que evaluar. Se sigue igualmente la convención ya establecida por las specs 001–009 del propio repo (Package by Layer, sin dependencias de frontend nuevas, tests xUnit), que cumple el mismo espíritu que un gate de "consistencia arquitectónica" hubiera exigido.

## Project Structure

### Documentation (this feature)

```text
specs/010-evolucion-parametros-paciente/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── patient-trend-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── ChequeAR.Domain/
│   └── (sin entidades nuevas — se reutilizan Question, QuestionnaireResponse, QuestionnaireResponseItem)
│
├── ChequeAR.Application/
│   ├── Dtos/
│   │   └── PatientTrendDtos.cs          # TrendParameterDto, TrendSeriesDto, TrendPointDto
│   ├── Interfaces/
│   │   └── IPatientTrendRepository.cs   # contrato de acceso a vw_FlatResponses acotado a esta feature
│   └── Services/
│       └── PatientTrendService.cs       # arma la lista de parámetros seleccionables y las series
│
├── ChequeAR.Infrastructure/
│   └── Oracle/
│       └── PatientTrendRepository.cs    # SELECT dedicado contra vw_FlatResponses (paciente+cuestionario+linkIds+finalizada)
│
├── ChequeAR.Api/
│   ├── Controllers/
│   │   └── Api/
│   │       └── PatientController.cs     # se agregan 2 acciones (GET trend-parameters, GET trend)
│   ├── Controllers/
│   │   └── Mvc/
│   │       └── EncountersController.cs  # se agrega acción Trend (nueva pantalla, análoga a Timeline)
│   ├── Views/
│   │   └── Encounters/
│   │       └── Trend.cshtml             # nueva vista, host del island
│   └── Scripts/islands/
│       └── patient-trend/
│           └── index.js                 # isla Vue: selector de parámetros + gráficos SVG apilados
│
└── ChequeAR.Tests/
    └── Application/
        └── PatientTrendServiceTests.cs  # selección de parámetros, armado de series, edge cases
```

**Structure Decision**: Se extiende el módulo de Análisis/Interoperabilidad existente en vez de crear un módulo nuevo. El backend sigue el mismo Package by Layer que el resto del proyecto (nueva pieza por capa: DTO en Application, repo dedicado en Infrastructure, endpoints en el controller de Api que ya expone Patient). El frontend se resuelve como una pantalla nueva (`Trend`) accesible desde el Historial del paciente (R7), en vez de sobrecargar `Timeline.cshtml`, siguiendo el mismo patrón de navegación que ya usan `Search → Load → View → Timeline` en `EncountersController`.

## Complexity Tracking

*Sin violaciones que justificar — no se agregan proyectos, dependencias ni patrones nuevos fuera de los ya establecidos en el repo.*
