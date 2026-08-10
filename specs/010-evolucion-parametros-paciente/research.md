# Research: Evolución longitudinal de parámetros por paciente

Todas las decisiones de esta feature fueron acordadas con el usuario en conversación previa a la especificación (no quedaron `[NEEDS CLARIFICATION]` en el spec). Este documento consolida el research técnico sobre **cómo** implementarlas reutilizando lo que ya existe en el repo.

## 1. Fuente de datos para los puntos del gráfico

**Decisión**: Consultar directamente la vista Oracle `vw_FlatResponses`, filtrando por `PatientHospitalId`, `QuestionnaireId`, `LinkId IN (...)` y estado finalizada, ordenado por `AuthoredAt`.

**Rationale**: Es la misma vista que ya usa `Application/Reports/QueryBuilder.cs` para el módulo de cohortes — expone exactamente las columnas necesarias (`ValueDecimal`, `ValueInteger`, `ValueDate`, `AuthoredAt`, `LinkId`, `PatientHospitalId`) sin necesidad de tocar el esquema ni crear una vista nueva.

**Alternatives considered**:
- Reusar `QueryBuilder`/`ReportQuery` tal cual (la DSL de filtros de cohortes): descartado porque esa DSL está pensada para condiciones arbitrarias con paginación por ficha — para un filtro fijo de 4 campos (paciente, cuestionario, lista de linkIds, estado) agrega complejidad innecesaria (construcción de `FilterNode`, `ReportSchema`, paginación).
- Traer todas las `QuestionnaireResponse` del paciente vía `IQuestionnaireResponseRepository` y filtrar los `Items` en memoria (capa Application, sin SQL dedicado): descartado por volumen — un paciente con historial largo traería de vuelta el `FhirJson` completo y todos los items de cada ficha, cuando solo se necesitan 2-3 columnas de las fichas finalizadas de un cuestionario puntual. La vista ya resuelve ese filtrado en la base.

## 2. Qué preguntas son seleccionables como "parámetro"

**Decisión**: Solo preguntas de tipo numérico (entero/decimal) o fecha, con `RepeatsAllowed = false`. Se listan a partir del `Questionnaire` (entidad ya cargada vía `IQuestionnaireRepository`), no de la vista de reportes.

**Rationale**: El tipo de cada pregunta ya vive en `Question.Type` (`QuestionType` enum) — no hace falta inferir el tipo desde los datos cargados como sí lo hace `ReportSchema` para tipar columnas de la vista. `RepeatsAllowed` está en la misma entidad. Filtrar ahí es más simple y evita depender de que existan datos ya cargados para saber qué se puede seleccionar (un cuestionario recién publicado, sin fichas todavía, igual debe poder mostrar su lista de parámetros graficables, aunque el gráfico salga vacío).

**Alternatives considered**: Derivar los parámetros seleccionables a partir de qué `LinkId`s aparecen efectivamente en `vw_FlatResponses` para ese paciente: descartado porque mezclaría "qué se puede seleccionar" con "qué tiene datos" — el spec (edge case) pide que el sistema distinga explícitamente "sin datos" de "no es un parámetro válido".

## 3. Rango normal (franja de referencia)

**Decisión**: Se lee `min`/`max` del mismo `ValidationJson` que hoy usa el editor de cuestionarios y el builder FHIR (`FhirQuestionnaireBuilder.cs`, extensions `minValue`/`maxValue`). Se toma el valor vigente en la pregunta actual, no un valor histórico por versión.

**Rationale**: Es el único lugar donde ese dato existe hoy; no hay versión histórica del rango normal, y el spec ya asume esto explícitamente (ver sección Assumptions).

**Alternatives considered**: Ninguna — no existe otra fuente de rango normal en el sistema.

## 4. Charting sin librerías externas

**Decisión**: Un componente Vue (isla) nuevo, `Scripts/islands/patient-trend/index.js`, que dibuja línea + puntos + franja de rango normal con SVG armado a mano (`<svg>`, `<path>`, `<rect>` para la franja), mismo enfoque que ya usa `report-stats/index.js` para su histograma.

**Rationale**: El propio código de `report-stats/index.js` documenta la razón (comentario en el archivo): la app carga Vue y Bootstrap desde CDN y eso ya es "un problema abierto para una intranet sin salida a internet" — sumar una librería de charts agregaría otra dependencia externa al mismo problema, para un caso de uso (línea + puntos + banda) que no lo justifica.

**Alternatives considered**: Chart.js / D3 vía CDN — descartado por la razón de arriba. Renderizar con `<canvas>` en vez de SVG — descartado porque SVG es más simple de inspeccionar/testear y de estilizar con las variables CSS que ya usa el resto de la UI (`var(--cq-success)`, etc., visto en `report-stats`).

## 5. Dónde vive la pantalla nueva

**Decisión**: Nueva acción `Trend` en `EncountersController.cs` + vista `Views/Encounters/Trend.cshtml`, accesible desde un botón "Ver evolución" en `Timeline.cshtml` (R7).

**Rationale**: `EncountersController` ya sigue el patrón `Search → Load → View → Timeline`, cada uno una pantalla con su propio rol (R1, R3, R6, R7). Agregar la evolución como una pantalla más (en vez de incrustarla dentro de `Timeline.cshtml`) mantiene esa consistencia y la hace bookmarkeable/navegable por separado.

**Alternatives considered**: Modal/panel embebido directamente en `Timeline.cshtml`: descartado porque `Timeline` ya maneja varios filtros (cuestionario, especialidad, autor, fecha) y agregar ahí selección de parámetros + varios gráficos sobrecargaría la pantalla y su estado en la URL.

## 6. Endpoints nuevos

**Decisión**: Dos acciones nuevas en `PatientController.cs` (API), bajo el mismo `[RoutePrefix("api/patients")]` ya existente:

- `GET api/patients/{hospitalId}/questionnaires/{questionnaireId}/trend-parameters` — lista de parámetros seleccionables (linkId, texto, tipo, unidad, rango normal si existe).
- `GET api/patients/{hospitalId}/trend?questionnaireId=..&linkId=a&linkId=b` — series de puntos por cada linkId pedido.

**Rationale**: `PatientController` ya concentra los endpoints de paciente usados por R1/R2/R7 (`Search`, `Get`, `Encounters`) inyectando `PatientService`; agregar acá dos endpoints más que delegan a un `PatientTrendService` nuevo mantiene la misma superficie de API en vez de crear un controller adicional para dos rutas.

**Alternatives considered**: Un `PatientTrendController` separado — descartado por ser prematuro para solo 2 endpoints acotados a la misma entidad (`Patient`) que ya cubre el controller existente.
