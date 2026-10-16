# Tasks: Gestor y Editor de Cuestionarios

> Convención: `[P]` = paralelizable; `[Sx]` = sprint sugerido.

## Fase 0 – Setup

- [x] T001 [S1] Inicializar solución `ChequeAR.sln` con proyectos `Api`, `Application`, `Domain`, `Infrastructure`, `Shared`. *(sin proyecto Shared; se sumaron ChequeAR.Tests y ChequeAR.Api.Tests)*
- [x] T002 [S1][P] Configurar pipeline CI en GitHub Actions: build, unit tests, validador FHIR. *(build + tests; el validador HL7 se corre a mano)*
- [x] T003 [S1][P] Definir perfil FHIR `ChequeARQuestionnaire` en `src/ChequeAR.Infrastructure/Fhir/Profiles/`.

## Fase 1 – Modelo de datos

- [x] T010 [S1] Crear entidades en `ChequeAR.Domain.Entities`: `Questionnaire`, `QuestionnaireVersion`, `QuestionGroup`, `Question`, `AnswerOption`.
- [x] T011 [S1] Configurar EF6 mapping y migración inicial (`Initial`). *(Oracle, no SQL Server)*
- [x] T012 [S1][P] Definir DTOs y mappers Domain ↔ FHIR (`QuestionnaireMapper`). *(FhirQuestionnaireBuilder / FhirQuestionnaireParser)*

## Fase 2 – CRUD básico

- [x] T020 [S2] Implementar `QuestionnaireController` con `GET`, `POST`, `PUT`, `DELETE`.
- [x] T021 [S2] Implementar `QuestionnaireService` con reglas de negocio (transiciones de estado).
- [x] T022 [S2] Implementar `QuestionnaireRepository` con queries optimizadas (índices ya definidos).
- [x] T023 [S2][P] Tests unitarios `Domain` (entidades, value objects).
- [x] T024 [S2][P] Tests de integración `Api` con SQL Server en Docker. *(tests/integration/ChequeAR.Infrastructure.Tests contra Oracle local, a mano)*

## Fase 3 – Versionado y operaciones FHIR

- [x] T030 [S2] Implementar `$publish` (validación FHIR + creación de `QuestionnaireVersion`).
- [x] T031 [S2] Implementar `$archive` (transición a `retired`).
- [x] T032 [S3] Implementar `$clone`.
- [x] T033 [S3] Implementar versionado automático al editar `active`.
- [x] T034 [S3][P] Implementar `If-Match` y respuesta 409 en conflictos.

## Fase 4 – Funcionalidad avanzada

- [x] T040 [S3] Soporte `enableWhen` (parsing + serialización FHIR).
- [x] T041 [S3] Soporte preguntas tipo `attachment` (validación extensión/tamaño, integración blob).
- [ ] T042 [S3][P] Soporte agrupamiento jerárquico (subsecciones). *(fuera de alcance (2026-10-15): el editor arma grupos de un nivel)*

## Fase 5 – Hardening y entrega

- [x] T050 [S4] Auditoría: emitir `AuditEvent` en todas las escrituras (delega en módulo 005).
- [ ] T051 [S4] Performance: índices, paginación cursor-based, tests de carga (200 RPS). *(paginación por _offset/_count; sin tests de carga)*
- [x] T052 [S4][P] Documentación OpenAPI / FHIR CapabilityStatement. *(CapabilityStatement en /fhir/r5/metadata)*
- [ ] T053 [S4][P] Validar contra el validador oficial HL7 en CI. *(validado a mano contra el validador oficial (02-ago-2026), no en CI)*
- [x] T054 [S4] Coverage ≥ 75% en `Domain` + `Application`. *(umbral de cobertura en ChequeAR.Tests: 85 % líneas / 75 % ramas)*

## DoD (por tarea)

- Criterios de aceptación cumplidos.
- Tests unitarios y/o de integración.
- PR aprobado por otro integrante (Calderan / Chaia / Crudo / Merlinsky).
- Pipeline CI verde.
