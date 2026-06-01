# Tasks: Motor de Renderizado Dinámico

## Fase 0 – Spike y bases

- [ ] T001 [S2] Spike de `$populate` y `QuestionnaireResponse` con HAPI sample data.
- [ ] T002 [S2][P] Definir contrato TypeScript de la librería JS (`ChequeAR.render(...)`).

## Fase 1 – Backend QR

- [ ] T010 [S3] Entidades `QuestionnaireResponse`, `QuestionnaireResponseItem`, `Attachment` + migración.
- [ ] T011 [S3] `QuestionnaireResponseController` con POST/PUT/GET.
- [ ] T012 [S3] `QuestionnaireResponseService` con reglas de transición (`in-progress` → `completed` → `amended`).
- [ ] T013 [S3] Repositorio + queries paginadas por paciente y cuestionario.
- [ ] T014 [S3][P] Tests unit/integration de CRUD.

## Fase 2 – Validación server-side

- [ ] T020 [S3] `EnableWhenEvaluator` con soporte de operadores (`=`, `!=`, `>`, `<`, `>=`, `<=`, `exists`).
- [ ] T021 [S3] Validación de reglas (`required`, rango, regex, longitud, extensiones de adjunto).
- [ ] T022 [S3][P] `$finalize` con validación completa.
- [ ] T023 [S3][P] Tests con casos límite (condicionales anidadas, valores faltantes).

## Fase 3 – Adjuntos

- [ ] T030 [S4] `AttachmentController` con upload `multipart/form-data`.
- [ ] T031 [S4] `FileShareAttachmentStore` (path config + sanitización filename).
- [ ] T032 [S4][P] Validación tamaño + MIME real (no sólo extensión).
- [ ] T033 [S4] Referenciar `Attachment` desde `QuestionnaireResponse.item.answer.valueAttachment`.

## Fase 4 – Librería JS

- [ ] T040 [S4] Setup proyecto TS (tsconfig, bundler, Jest).
- [ ] T041 [S4] `QuestionnaireParser` (FHIR JSON → modelo interno).
- [ ] T042 [S4] `FieldFactory` para los tipos del MVP.
- [ ] T043 [S4] `EnableWhenEvaluator` cliente (compartido conceptualmente con el server).
- [ ] T044 [S4] `AutosaveQueue` con IndexedDB.
- [ ] T045 [S5][P] Integración en template del hospital + smoke E2E con Playwright.

## Fase 5 – Pre-carga

- [ ] T050 [S5] `PrefillService` con resolvers `fhir-observation` y `oracle-view` (consume módulo 003).
- [ ] T051 [S5] Operation `$populate` en `QuestionnaireController`.
- [ ] T052 [S5][P] UI marca campos pre-cargados y "editados".

## DoD

- Tests unit ≥75%, integración para endpoints críticos, E2E para el flujo completo.
- Performance: GET ficha < 200 ms p95 medido con k6.
- Auditoría emite `AuditEvent` en `$finalize` y en upload de adjuntos.
