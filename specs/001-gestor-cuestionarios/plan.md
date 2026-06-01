# Implementation Plan: Gestor y Editor de Cuestionarios

**Branch**: `001-gestor-cuestionarios` | **Date**: 2026-06-01 | **Spec**: ./spec.md

## Summary

Backend .NET que expone una API REST FHIR R5 para CRUD y publicación de `Questionnaire`. Persiste en SQL Server con un modelo relacional optimizado para edición + snapshots inmutables al publicar. La UI la provee el Hospital Garrahan (template).

## Technical Context

**Language/Version**: .NET Framework 4.7.1 (C# 7.3)
**Primary Dependencies**: ASP.NET Web API 2, HAPI-FHIR .NET (Hl7.Fhir.R5), Entity Framework 6, FluentValidation, Serilog
**Storage**: SQL Server 2016+ (instancia provista por el Hospital)
**Testing**: xUnit, Moq, WebApi.TestHost, Playwright (E2E vía template), Testcontainers para integración
**Target Platform**: IIS 10 sobre Windows Server 2016 (on-premise)
**Project Type**: web-service (API REST)
**Performance Goals**: p95 < 500 ms para CRUD; soporta cuestionarios de hasta 200 ítems
**Constraints**: sin acceso a internet en runtime; validador HL7 embebido; logs sin PII directa
**Scale/Scope**: 4 especialidades pediátricas en MVP, ~50 cuestionarios totales, ~200 usuarios concurrentes pico

## Constitution Check

- ✅ Estándar FHIR R5 obligatorio (validador en CI).
- ✅ Package by Layer (controllers / services / repositories / entities).
- ✅ Pruebas unitarias ≥ 75% sobre lógica de dominio.
- ✅ Auditoría obligatoria (módulo 005).
- ✅ Sin almacenamiento de datos clínicos de pacientes reales en este módulo.

## Project Structure

```text
specs/001-gestor-cuestionarios/
├── spec.md
├── api.md
├── data-model.md
├── plan.md
├── quickstart.md
├── research.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── ChequeAR.Api/                    # Web API (presentación)
│   └── Controllers/QuestionnaireController.cs
├── ChequeAR.Application/            # Servicios de aplicación
│   └── Services/QuestionnaireService.cs
├── ChequeAR.Domain/                 # Entidades y reglas
│   ├── Entities/Questionnaire.cs
│   ├── Entities/Question.cs
│   └── ValueObjects/
├── ChequeAR.Infrastructure/         # EF6, mapping FHIR, HAPI validator
│   ├── Repositories/QuestionnaireRepository.cs
│   ├── Fhir/QuestionnaireMapper.cs
│   └── EF/ChequeARDbContext.cs
└── ChequeAR.Shared/

tests/
├── unit/ChequeAR.Domain.Tests/
├── integration/ChequeAR.Api.IntegrationTests/   # Docker SQL Server
└── e2e/                                          # template del hospital + Playwright
```

**Structure Decision**: Package by Layer dentro de una solución única .NET. La capa Domain es independiente de FHIR; la capa Infrastructure traduce hacia/desde recursos FHIR.

## Fases de implementación

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Investigación FHIR + scaffold solución | S1 |
| 1 | Modelo de datos + migraciones EF | S1 |
| 2 | CRUD básico (POST/GET/PUT/DELETE) | S2 |
| 3 | Versionado + `$publish` + `$archive` | S2-S3 |
| 4 | Lógica condicional + tipo `attachment` | S3 |
| 5 | Hardening, validador FHIR, performance | S4 |

## Complexity Tracking

| Violación | Por qué | Alternativa descartada |
|---|---|---|
| Doble representación (relacional + JSON snapshot) | Versiones inmutables requeridas por trazabilidad clínica | Sólo relacional: pierde fidelidad FHIR exacta |
