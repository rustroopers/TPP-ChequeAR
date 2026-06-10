# Implementation Plan: Motor de Renderizado Dinámico

**Branch**: `002-motor-renderizado` | **Date**: 2026-06-01 | **Spec**: ./spec.md

## Summary

Backend que sirve `Questionnaire` para renderizar y recibe `QuestionnaireResponse` con autosave, validación server-side de `enableWhen` y reglas, y soporte de adjuntos. La capa cliente es una **isla Vue 3** (`questionnaire-renderer`, pantalla R3 — spec 006) que interpreta el JSON FHIR y se monta dentro de una vista Razor propia.

## Technical Context

**Language/Version**: .NET Framework 4.7.1 (backend); JavaScript ES2020 + Vue 3 (isla `questionnaire-renderer`)
**Primary Dependencies**: ASP.NET Web API 2, Hl7.Fhir.R5, EF6, FluentValidation; en el cliente: Vue 3 (global build) + parser de `enableWhen` propio
**Storage**: SQL Server (mismo schema que módulo 001) + file share UNC para adjuntos
**Testing**: xUnit + Moq; Playwright para E2E sobre la pantalla R3; Vitest para la isla Vue
**Target Platform**: IIS 10 sobre Windows Server 2016; navegadores Chrome/Edge ≥ 110
**Project Type**: web-service + isla Vue embebida en `ChequeAR.Api`
**Performance Goals**: GET ficha < 200 ms p95; POST QR < 500 ms p95
**Constraints**: la isla Vue debe poder montarse en cualquier vista Razor; assets vendored (sin CDN)
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

src/ChequeAR.Api/Scripts/islands/questionnaire-renderer/
├── index.js                                       # entrypoint Vue island
├── parser/QuestionnaireParser.js
├── parser/EnableWhenEvaluator.js
├── renderer/FieldFactory.js
└── autosave/AutosaveQueue.js

tests/
├── unit/
├── integration/
└── e2e/                                           # Playwright sobre pantalla R3 (spec 006)
```

**Structure Decision**: backend y cliente viven en la misma solución `ChequeAR.sln`. La isla Vue del renderizador se publica en `ChequeAR.Api/Scripts/islands/questionnaire-renderer/` y se monta desde la vista Razor `Views/Encounters/R3.cshtml` (ver spec 006).

## Fases

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Spike de `Questionnaire` + `$populate` | S2 |
| 1 | Endpoints CRUD de `QuestionnaireResponse` | S3 |
| 2 | Validación server (enableWhen, reglas) + autosave | S3 |
| 3 | Adjuntos + storage | S4 |
| 4 | Isla Vue `questionnaire-renderer` integrada en vista Razor R3 | S4-S5 |
| 5 | Pre-carga `$populate` integrada con módulo 003 | S5 |

## Complexity Tracking

| Violación | Por qué | Alternativa descartada |
|---|---|---|
| Doble validación (cliente + servidor) | Reglas críticas no pueden confiar en cliente | Sólo servidor: UX pobre por roundtrips |
