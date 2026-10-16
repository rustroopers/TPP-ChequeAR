# Tasks: Versionado de variables clínicas

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Fase 1: Modelo y migración

- [x] T001 Entidad `ClinicalVariableVersion` (con `SourceRevision`) y configuración EF: FK explícita sin cascade, `DefinitionJson` como CLOB, índice único (variable, número) con nombre corto.
- [x] T002 `ClinicalVariable.CurrentVersionId`; `Revision` como token de concurrencia.
- [x] T003 `Question.VariableVersionId`.
- [x] T004 Migración EF generada con `ef6 migrations add` (snapshot `.resx` incluido).
- [x] T005 DTO `VariableDefinition` en Application: armado desde la entidad, serialización y comparación por valor.
- [x] T006 Backfill en el Seed: versión 1 por variable y re-atado de **todas** sus preguntas comparando contenido (v1 o sin versión con número 0).

## Fase 2: Servicio y repositorio de variables (US1, US3, US4)

- [x] T007 `UpdateHeaderAsync` en el repositorio; `SetActiveAsync` lo usa y no toca `Revision`.
- [x] T008 `UpdateAsync(id, patch, userId, expectedRevision)`: 409 si no coincide, bloqueo de código, tipo y repetición con versiones; el repositorio traduce `DbUpdateConcurrencyException`.
- [x] T009 `PublishAsync(id, userId, note, expectedRevision)`: sin versión si no hay cambios, auditoría.
- [x] T010 `DeleteAsync`: borra sólo sin versión y sin preguntas que la usen.
- [x] T011 Búsqueda "sólo publicadas y activas" con el número vigente; versiones en lote por (variable, número); versión por número.
- [x] T012 Tests de servicio. Actualizar `UpdateAsync_IncrementaRevision`, `DeleteAsync_EnUso_DesactivaEnVezDeBorrar`, `DeleteAsync_SinUso_Borra` y los helpers `TestHelpers.Variable`/`VariableRepoWith`.

## Fase 3: Cuestionarios (US2)

- [x] T013 `ClinicalVariableSnapshot` aplica un `VariableDefinition` y guarda `VariableVersionId` y el número; `KeepSnapshot` copia la versión.
- [x] T014 `BindVariablesAsync` según la regla de versión pedida del plan; 422 para versión inexistente o variable sin publicar.
- [x] T015 `PublishAsync` del cuestionario rechaza preguntas sin versión.
- [x] T016 `CopyQuestion` (clonar) copia la versión.
- [x] T017 Extensión `question-variable-version` en builder y parser (sólo si hay versión) y su StructureDefinition.
- [x] T018 Tests de binding, clonado y round trip FHIR. Actualizar `Apply_CongelaLaRevisionDeLaVariable`, `CloneAsync_CopiaVariableIdYRevision`, `CreateAsync_ClienteMandaOtroTipo_GanaLaVariable`.

## Fase 4: Interfaz

- [x] T019 API: `POST api/variables/{id}/$publish`, `GET api/variables/{id}/versions`, `GET api/variables/{id}/versions/{n}` (lectura con `QuestionnaireRead`); `If-Match` en el `PUT`.
- [x] T020 MVC de variables: publicar con nota (antiforgery), historial, badge de versión y de cambios sin publicar, conflicto de concurrencia visible sin reenviar la revisión vieja.
- [x] T021 Formulario de variable: código, tipo y repetición deshabilitados con versiones.
- [x] T022 `QuestionnairesController.ToQuestionView`: `VariableCurrentRevision` = número de la versión vigente.
- [x] T023 Editor: buscador con publicadas; insertar y "Actualizar a vN" con la definición de la versión; re-evaluar condiciones; `normQ` con `variableRevision`; el import conserva la versión del origen.

## Fase 5: Cierre

- [x] T024 Prueba de punta a punta en la app: crear, publicar, usar, publicar v2, actualizar la pregunta, desactivar y reactivar una variable con opciones, conflicto entre dos administradores.
- [x] T025 Auditoría independiente del cambio.
