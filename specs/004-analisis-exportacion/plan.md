# Implementation Plan: Módulo de Análisis y Exportación

**Branch**: `004-analisis-exportacion` | **Date**: 2026-06-01 | **Spec**: ./spec.md

## Summary

API de consultas estructuradas sobre respuestas finalizadas con exportación a Excel y CSV. Consultas grandes se procesan en background. Permite guardar y compartir consultas a nivel especialidad.

## Technical Context

**Language/Version**: .NET Framework 4.7.1
**Primary Dependencies**: ASP.NET Web API 2, EF6, EPPlus (XLSX), CsvHelper, Hangfire (jobs en background), Serilog
**Storage**: SQL Server (mismo schema que módulos 001-003) + file share para artifacts
**Testing**: xUnit, Moq, snapshot tests del XLSX/CSV generado
**Target Platform**: IIS 10 / Windows Server 2016
**Project Type**: web-service
**Performance Goals**: query con 5 filtros sobre 10k respuestas < 3 s p95; export 5k filas < 10 s
**Constraints**: licencia EPPlus comercial — coordinar con el hospital o usar versión LGPL antigua o ClosedXML
**Scale/Scope**: ~100 reportes ejecutados por semana, picos de 1M filas en queries de investigación

## Constitution Check

- ✅ Auditoría obligatoria en cada export (módulo 005).
- ✅ Permisos por rol y especialidad respetados (delega en módulo 005).
- ✅ Sin PII en logs (sólo IDs).
- ✅ Resultados deterministas: misma consulta → mismo XLSX byte-equivalente (tests de snapshot).
- ⚠️ Decisión de licencia para EPPlus a confirmar antes de S3.

## Project Structure

```text
src/
├── ChequeAR.Api/
│   └── Controllers/ReportsController.cs
├── ChequeAR.Application/
│   ├── Reports/
│   │   ├── QueryBuilder.cs              # JSON → SQL parametrizado
│   │   ├── ReportExecutor.cs
│   │   ├── ColumnResolver.cs
│   │   └── Statistics/StatsCalculator.cs
│   └── Exporters/
│       ├── XlsxExporter.cs
│       └── CsvExporter.cs
├── ChequeAR.Infrastructure/
│   ├── Repositories/SavedReportRepository.cs
│   ├── Jobs/ExportJobWorker.cs          # Hangfire job
│   └── Storage/FileShareArtifactStore.cs

tests/
├── unit/
└── integration/
    └── Reports.IntegrationTests/        # dataset semilla + asserts deterministas
```

**Structure Decision**: nuevo namespace `Reports` dentro de `Application`. Workers Hangfire en `Infrastructure.Jobs`. La vista `vw_FlatResponses` se crea por migración EF.

## Fases

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Spike de generación XLSX y dataset semilla | S4 |
| 1 | `QueryBuilder` + endpoint `/query` síncrono | S5 |
| 2 | Exporters XLSX y CSV sincrónicos | S5 |
| 3 | Hangfire + exports asincrónicos | S6 |
| 4 | `SavedReport` y permisos por especialidad | S6 |
| 5 | Estadísticas básicas y previsualización | S7 |

## Complexity Tracking

| Violación | Por qué | Alternativa descartada |
|---|---|---|
| SQL dinámico parametrizado | Filtros arbitrarios con N predicados | LINQ: explota en queries pivot con 50 columnas |
| Vista materializada | Performance en queries con muchos filtros | Joins on-the-fly: lentos con > 5k respuestas |
