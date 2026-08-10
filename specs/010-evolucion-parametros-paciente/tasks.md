---

description: "Task list for: Evolución longitudinal de parámetros por paciente"

---

# Tasks: Evolución longitudinal de parámetros por paciente

**Input**: Design documents from `/specs/010-evolucion-parametros-paciente/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/patient-trend-api.md, quickstart.md

**Tests**: Incluidos — el proyecto ya tiene convención de tests unitarios xUnit por servicio (ver `QuestionnaireResponseServiceTests.cs` de la spec 009) y `quickstart.md` pide casos concretos para `PatientTrendServiceTests.cs`.

**Organization**: Tareas agrupadas por historia de usuario (spec.md: US1 P1, US2 P2, US3 P3) para poder implementar y demostrar cada una por separado.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Puede correr en paralelo (archivos distintos, sin dependencias pendientes)
- **[Story]**: A qué historia de usuario pertenece (US1, US2, US3)
- Cada tarea incluye la ruta de archivo exacta

---

## Phase 1: Setup

**Purpose**: Andamiaje mínimo — no hace falta ninguna dependencia NuGet/JS nueva (research.md §4).

- [X] T001 [P] Crear el stub de la isla Vue en `src/ChequeAR.Api/Scripts/islands/patient-trend/index.js` (IIFE + objeto `PatientTrend` con `props`/`setup`/`template` vacíos), mismo andamiaje que `src/ChequeAR.Api/Scripts/islands/report-stats/index.js`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Backend completo (DTOs, repo, service, endpoints) y la pantalla `Trend` vacía. Ambos endpoints ya soportan múltiples `linkId` desde el arranque (el contrato no distingue 1 vs. N parámetros — la diferencia entre US1/US2 es solo de frontend).

**⚠️ CRITICAL**: Ninguna historia de usuario puede arrancar hasta que esta fase esté completa.

- [X] T002 [P] Crear los DTOs `TrendParameterOptionDto`, `NormalRangeDto`, `TrendSeriesDto`, `TrendPointDto` en `src/ChequeAR.Application/Dtos/PatientTrendDtos.cs`, según `data-model.md`.
- [X] T003 [P] Crear la interfaz `IPatientTrendRepository` en `src/ChequeAR.Application/Interfaces/IPatientTrendRepository.cs` con `Task<List<TrendSeriesDto>> GetSeriesAsync(string patientHospitalId, Guid questionnaireId, IEnumerable<string> linkIds)`.
- [X] T004 Implementar `PatientTrendRepository` en `src/ChequeAR.Infrastructure/Oracle/PatientTrendRepository.cs`: SELECT dedicado contra `vw_FlatResponses` filtrando por `PatientHospitalId` + `QuestionnaireId` + `LinkId IN (...)` + estado finalizada, ordenado por `AuthoredAt`; mismo patrón de conexión/ejecución que `src/ChequeAR.Application/Reports/ReportExecutor.cs`, **sin** reusar la DSL de `QueryBuilder`/`FilterNode` (research.md §1). (depende de T003)
- [X] T005 Implementar `PatientTrendService` en `src/ChequeAR.Application/Services/PatientTrendService.cs`:
  - `GetSelectableParametersAsync(Guid questionnaireId)` — lee `Questionnaire.Questions` vía `IQuestionnaireRepository` (ya existente), filtra `Type` ∈ {Decimal, Integer, Date} y `RepeatsAllowed == false`, arma `NormalRange`/`Unit` desde `Question.ValidationJson` (tolerante a JSON corrupto o ausente, igual criterio que `FhirQuestionnaireBuilder`).
  - `GetTrendAsync(string patientHospitalId, Guid questionnaireId, IEnumerable<string> linkIds)` — descarta `linkId`s que no pertenecen al cuestionario o no son graficables, delega el resto en `IPatientTrendRepository`.
  (depende de T002, T003, T004)
- [X] T006 Registrar `IPatientTrendRepository` → `PatientTrendRepository` y `PatientTrendService` en `src/ChequeAR.Api/App_Start/ServiceComposition.cs`, mismo patrón que el registro existente de `ReportExecutor`/`QueryBuilder`. (depende de T004, T005)
- [X] T007 [P] Agregar 2 acciones a `src/ChequeAR.Api/Controllers/Api/PatientController.cs`: `GET {hospitalId}/questionnaires/{questionnaireId}/trend-parameters` y `GET {hospitalId}/trend`, según `contracts/patient-trend-api.md` (400 si falta `questionnaireId`/ningún `linkId`, 404 si el cuestionario no existe, `[Authorize]` igual que el resto del controller). (depende de T005, T006)
- [X] T008 [P] Agregar la acción `Trend(string patientId, Guid questionnaireId)` a `src/ChequeAR.Api/Controllers/Mvc/EncountersController.cs` + vista `src/ChequeAR.Api/Views/Encounters/Trend.cshtml` (layout, breadcrumbs, host `data-vue-island="patient-trend"`, carga de scripts vía `@section scripts`), mismo patrón que `src/ChequeAR.Api/Views/Reports/Stats.cshtml`. (depende de T006)
- [X] T009 [P] Agregar el botón/link "Ver evolución" en `src/ChequeAR.Api/Views/Encounters/Timeline.cshtml` que navega a `Trend` para el paciente actual. (depende de T008)
- [X] T010 [P] Tests unitarios en `src/ChequeAR.Tests/Application/PatientTrendServiceTests.cs`: selección de parámetros (excluye repetibles, excluye tipos no numéricos/fecha), armado de series (solo fichas `Completed`, orden cronológico, `Points: []` sin datos), rango normal presente/ausente/`ValidationJson` corrupto, `linkId`s inválidos ignorados sin romper el resto de la respuesta. (depende de T005)

**Checkpoint**: Backend completo y pantalla `Trend` accesible desde el Historial — falta solo la UI de selección/gráficos, que arman las historias de usuario.

---

## Phase 3: User Story 1 - Ver la evolución de un parámetro clínico (Priority: P1) 🎯 MVP

**Goal**: Elegir un cuestionario y un parámetro numérico/fecha, y ver sus valores históricos (solo fichas finalizadas) en un gráfico ordenado por fecha.

**Independent Test**: Paciente con ≥2 fichas finalizadas del mismo cuestionario y un parámetro con valores distintos en cada una → aparece un gráfico con esos puntos, ordenados cronológicamente.

### Implementation for User Story 1

- [X] T011 [US1] En `EncountersController.Trend` y `Views/Encounters/Trend.cshtml`: selector de cuestionario vía formulario GET, poblado con los cuestionarios que el paciente completó al menos una vez (mismo criterio que ya arma `Model.AvailableQuestionnaires` en `Timeline`). Sin cuestionario elegido, no hay nada más que mostrar (edge case del spec).
- [X] T012 [US1] En `Scripts/islands/patient-trend/index.js`: al montar con un `questionnaireId` ya elegido, pedir `GET .../trend-parameters` y renderizar la lista de parámetros seleccionables (checkboxes).
- [X] T013 [US1] En `Scripts/islands/patient-trend/index.js`: al seleccionar un parámetro, pedir `GET .../trend?questionnaireId=..&linkId=..` y dibujar un `<svg>` (línea + puntos) para esa serie, puntos ordenados cronológicamente (FR-008).
- [X] T014 [US1] En `Scripts/islands/patient-trend/index.js`: manejar explícitamente el estado "sin datos suficientes" — cuestionario sin fichas finalizadas, o parámetro con `Points: []` — mostrando un mensaje en vez de un gráfico vacío sin explicación (FR-011, SC-005).

**Checkpoint**: User Story 1 funciona de punta a punta y es demostrable por sí sola (MVP).

---

## Phase 4: User Story 2 - Comparar varios parámetros a la vez (Priority: P2)

**Goal**: Seleccionar más de un parámetro del mismo cuestionario y ver un gráfico por cada uno, apilados y alineados por fecha, sin límite de cantidad.

**Independent Test**: Seleccionar 2 (o más de 4) parámetros del mismo cuestionario y verificar que aparece un `<svg>` independiente por cada uno, todos compartiendo el mismo rango de fechas en el eje X.

### Implementation for User Story 2

- [X] T015 [US2] En `Scripts/islands/patient-trend/index.js`: permitir selección múltiple de parámetros (los checkboxes de T012 ya lo soportan) y pedir todos los `linkId` seleccionados en una sola llamada a `GET .../trend` (query param repetido), sin tope de cantidad (FR-004).
- [X] T016 [US2] En `Scripts/islands/patient-trend/index.js`: renderizar un `<svg>` independiente por cada serie devuelta, apilados verticalmente, calculando un dominio de fechas común (mín./máx. de `AuthoredAt` entre todas las series seleccionadas) para que los ejes X queden alineados entre gráficos (FR-005, FR-006).

**Checkpoint**: User Stories 1 y 2 funcionan juntas — selección simple y múltiple conviven en la misma pantalla.

---

## Phase 5: User Story 3 - Distinguir valores dentro o fuera de lo esperado (Priority: P3)

**Goal**: Mostrar en cada gráfico la franja de rango normal de la pregunta, cuando exista.

**Independent Test**: Graficar un parámetro cuya pregunta tiene `min`/`max` configurado y verificar que aparece la franja de referencia; graficar uno sin rango y verificar que el gráfico se muestra igual, sin la franja ni error.

### Implementation for User Story 3

- [X] T017 [US3] En `Scripts/islands/patient-trend/index.js`: dibujar, dentro de cada `<svg>` de T013/T016, un `<rect>` semitransparente entre `normalRange.min` y `normalRange.max` cuando la serie lo tenga, detrás de la línea de evolución (FR-009).
- [X] T018 [US3] En `Scripts/islands/patient-trend/index.js`: cuando `normalRange` es `null` (o viene con un solo extremo), omitir la franja o dibujarla solo del lado disponible, sin romper el resto del gráfico (FR-010).

**Checkpoint**: Las 3 historias de usuario funcionan juntas — selección simple, múltiple, y franja de referencia.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Ejecutar la guía manual de `quickstart.md` contra Oracle real (mismo enfoque de validación manual que usó la spec 009), y documentar cualquier bug real encontrado en el round-trip HTTP completo (los tests unitarios de T010 no lo cubren, igual que pasó con el bug de `ConcurrencyVersion` en spec 009).
- [X] T020 [P] Revisar accesibilidad básica del selector de parámetros y de los gráficos (labels, contraste de la franja de rango normal) reusando las variables `--cq-*` ya definidas para el resto de la UI (vistas en `report-stats/index.js`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sin dependencias.
- **Foundational (Phase 2)**: depende de Setup — bloquea todas las historias de usuario.
- **User Story 1 (Phase 3)**: depende de Foundational. Es el MVP.
- **User Story 2 (Phase 4)**: depende de Foundational; reutiliza T012 (checkboxes) de US1, pero el backend (T002–T010) ya soporta múltiples `linkId` desde Foundational, así que no depende funcionalmente de que US1 esté "terminada", solo de que exista el componente base.
- **User Story 3 (Phase 5)**: depende de Foundational (necesita `NormalRange` en la respuesta, ya presente desde T005/T007); se apoya visualmente en los `<svg>` que crean US1 (T013) y US2 (T016).
- **Polish (Phase 6)**: depende de que al menos US1 esté completa; idealmente después de las 3.

### Parallel Opportunities

- T002 y T003 (DTOs e interfaz) pueden ir en paralelo.
- T007, T008, T009, T010 pueden ir en paralelo una vez completos T005/T006 (tocan archivos distintos).
- T019 y T020 pueden ir en paralelo al cierre.

---

## Parallel Example: Foundational

```bash
# Una vez T004 y T005 están listos, estas 4 tareas tocan archivos distintos:
Task: "Agregar endpoints trend-parameters/trend en PatientController.cs"
Task: "Agregar acción Trend + vista Trend.cshtml en EncountersController"
Task: "Agregar botón 'Ver evolución' en Timeline.cshtml"
Task: "Tests unitarios de PatientTrendService"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Completar Phase 1: Setup
2. Completar Phase 2: Foundational (backend completo + pantalla vacía)
3. Completar Phase 3: User Story 1
4. **Parar y validar**: correr `quickstart.md` pasos 1-4 y 6-7 contra un paciente real
5. Demo: un profesional ve la evolución de un parámetro desde el Historial

### Incremental Delivery

1. Setup + Foundational → backend listo, sin UI de selección todavía
2. + User Story 1 → demo del caso de uso central (MVP)
3. + User Story 2 → demo de comparación multi-parámetro
4. + User Story 3 → demo con franja de rango normal
5. Cada historia suma valor sin romper la anterior

---

## Notes

- [P] = archivos distintos, sin dependencias pendientes entre sí.
- El backend (Foundational) ya soporta la funcionalidad completa de US1+US2 desde el arranque (múltiples `linkId`, `NormalRange` incluido) — lo que separa las historias es exclusivamente el frontend, para poder demostrar valor incremental sin re-trabajar la API.
- Confirmar que los tests fallan antes de implementar (T010 antes de que T005 esté terminado, si se sigue TDD estricto).
- Commitear después de cada tarea o grupo lógico.
