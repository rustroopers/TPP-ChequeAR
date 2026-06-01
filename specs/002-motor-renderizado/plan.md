# Implementation Plan: Motor de Renderizado Dinámico

**Branch**: `002-motor-renderizado` | **Date**: 2026-06-01 | **Spec**: ./spec.md

## Summary

Backend que sirve `Questionnaire` para renderizar y recibe `QuestionnaireResponse` con autosave, validación server-side de `enableWhen` y reglas, y soporte de adjuntos. Una librería JS auxiliar se integra al template del hospital para interpretar el JSON FHIR.

## Technical Context

**Language/Version**: .NET Framework 4.7.1 (backend); TypeScript 5.x ES2020 (librería JS para el template)
**Primary Dependencies**: ASP.NET Web API 2, Hl7.Fhir.R5, EF6, FluentValidation; en el cliente: vanilla TS + un parser de `enableWhen` propio
**Storage**: SQL Server (mismo schema que módulo 001) + file share UNC para adjuntos
**Testing**: xUnit + Moq; Playwright para E2E sobre el template; Jest para la librería JS
**Target Platform**: IIS 10 sobre Windows Server 2016; navegadores Chrome/Edge actuales (los desplegados en el Hospital)
**Project Type**: web-service + librería JS embeddable
**Performance Goals**: GET ficha < 200 ms p95; POST QR < 500 ms p95
**Constraints**: el template no puede ser modificado en su core; la librería JS debe ser drop-in
**Scale/Scope**: ~500 fichas/día en pico de uso

## Constitution Check

- ✅ FHIR R5 estricto; `$populate` y `QuestionnaireResponse` validados.
- ✅ Sin almacenamiento de PII libre fuera de los campos clínicos esperados.
- ✅ Auditoría de altas/finalizaciones obligatoria.
- ✅ Pre-carga sin sobrescribir respuestas explícitas del usuario.

## Project Structure

```text
src/
├── ChequeAR.Api/
│   └── Controllers/
│       ├── QuestionnaireResponseController.cs
│       └── AttachmentController.cs
├── ChequeAR.Application/
│   ├── Services/QuestionnaireResponseService.cs
│   ├── Services/PrefillService.cs               # delega a módulo 003
│   └── Validation/EnableWhenEvaluator.cs
├── ChequeAR.Infrastructure/
│   ├── Repositories/QuestionnaireResponseRepository.cs
│   ├── Storage/FileShareAttachmentStore.cs
│   └── Fhir/ResponseMapper.cs

frontend/chequear-renderer/
├── src/
│   ├── parser/QuestionnaireParser.ts
│   ├── parser/EnableWhenEvaluator.ts
│   ├── renderer/FieldFactory.ts
│   └── autosave/AutosaveQueue.ts
├── dist/                                          # bundle drop-in para el template
└── tests/

tests/
├── unit/
├── integration/
└── e2e/                                           # Playwright sobre template
```

**Structure Decision**: backend reusa la solución `ChequeAR.sln`; el cliente vive en un sub-proyecto frontend independiente que se publica como bundle único integrable al template.

## Fases

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Spike de `Questionnaire` + `$populate` | S2 |
| 1 | Endpoints CRUD de `QuestionnaireResponse` | S3 |
| 2 | Validación server (enableWhen, reglas) + autosave | S3 |
| 3 | Adjuntos + storage | S4 |
| 4 | Librería JS de renderizado integrada al template | S4-S5 |
| 5 | Pre-carga `$populate` integrada con módulo 003 | S5 |

## Complexity Tracking

| Violación | Por qué | Alternativa descartada |
|---|---|---|
| Doble validación (cliente + servidor) | Reglas críticas no pueden confiar en cliente | Sólo servidor: UX pobre por roundtrips |
