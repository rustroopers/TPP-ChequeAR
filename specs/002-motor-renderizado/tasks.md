# Tasks: Motor de Renderizado Dinámico

## Fase 0 – Spike y bases

- [ ] T001 [S2] Spike de `$populate` y `QuestionnaireResponse` con HAPI sample data. *(fuera de alcance (2026-10-15))*
- [ ] T002 [S2][P] Definir contrato TypeScript de la librería JS (`ChequeAR.render(...)`). *(las islas son Vue sin TypeScript)*

## Fase 1 – Backend QR

- [x] T010 [S3] Entidades `QuestionnaireResponse`, `QuestionnaireResponseItem`, `Attachment` + migración.
- [x] T011 [S3] `QuestionnaireResponseController` con POST/PUT/GET. *(POST/GET + $save-progress y $finalize en lugar de PUT)*
- [x] T012 [S3] `QuestionnaireResponseService` con reglas de transición (`in-progress` → `completed` → `amended`).
- [x] T013 [S3] Repositorio + queries paginadas por paciente y cuestionario.
- [x] T014 [S3][P] Tests unit/integration de CRUD.

## Fase 2 – Validación server-side

- [x] T020 [S3] `EnableWhenEvaluator` con soporte de operadores (`=`, `!=`, `>`, `<`, `>=`, `<=`, `exists`).
- [x] T021 [S3] Validación de reglas (`required`, rango, regex, longitud, extensiones de adjunto).
- [x] T022 [S3][P] `$finalize` con validación completa.
- [x] T023 [S3][P] Tests con casos límite (condicionales anidadas, valores faltantes).

## Fase 3 – Adjuntos

- [x] T030 [S4] `AttachmentController` con upload `multipart/form-data`.
- [x] T031 [S4] `FileShareAttachmentStore` (path config + sanitización filename).
- [x] T032 [S4][P] Validación tamaño + MIME real (no sólo extensión).
- [x] T033 [S4] Referenciar `Attachment` desde `QuestionnaireResponse.item.answer.valueAttachment`.

## Fase 4 – Librería JS

- [ ] T040 [S4] Setup proyecto TS (tsconfig, bundler, Jest). *(sin bundler ni TypeScript: islas Vue servidas como módulos)*
- [x] T041 [S4] `QuestionnaireParser` (FHIR JSON → modelo interno). *(en la isla questionnaire-renderer)*
- [x] T042 [S4] `FieldFactory` para los tipos del MVP.
- [x] T043 [S4] `EnableWhenEvaluator` cliente (compartido conceptualmente con el server).
- [ ] T044 [S4] `AutosaveQueue` con IndexedDB. *(fuera de alcance (2026-10-15): autosave cada 2,5 s sin cola offline)*
- [ ] T045 [S5][P] Integración en template del hospital + smoke E2E con Playwright. *(fuera de alcance (2026-10-15))*

## Fase 5 – Pre-carga

- [ ] T050 [S5] `PrefillService` con resolvers `fhir-observation` y `oracle-view` (consume módulo 003). *(fuera de alcance (2026-10-15): PrefillService no se usa)*
- [ ] T051 [S5] Operation `$populate` en `QuestionnaireController`. *(fuera de alcance (2026-10-15))*
- [ ] T052 [S5][P] UI marca campos pre-cargados y "editados". *(fuera de alcance (2026-10-15))*

## DoD

- Tests unit ≥75%, integración para endpoints críticos, E2E para el flujo completo.
- Performance: GET ficha < 200 ms p95 medido con k6.
- Auditoría emite `AuditEvent` en `$finalize` y en upload de adjuntos.
