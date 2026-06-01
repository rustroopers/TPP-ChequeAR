# Implementation Plan: Capa de Interoperabilidad y Almacenamiento

**Branch**: `003-interoperabilidad-fhir` | **Date**: 2026-06-01 | **Spec**: ./spec.md

## Summary

Capa base que provee: (a) servidor FHIR R5 propio de ChequeAR, (b) cliente FHIR para consumir la API del hospital, (c) adaptador Oracle de solo lectura para datos legacy, (d) pipeline de validación contra el validador HL7 oficial.

## Technical Context

**Language/Version**: .NET Framework 4.7.1 (C#)
**Primary Dependencies**: Hl7.Fhir.R5 (Firely .NET SDK), Oracle.ManagedDataAccess, Polly (retry/circuit-breaker), YamlDotNet (mapeos), Serilog
**Storage**: SQL Server 2016+ (recursos FHIR propios) + Oracle (lectura solamente, infra del hospital)
**Testing**: xUnit, Moq, validador HAPI oficial en CI, contenedor Oracle XE en tests de integración
**Target Platform**: IIS 10 / Windows Server 2016
**Project Type**: web-service (capa de infraestructura compartida por los demás módulos)
**Performance Goals**: GET propio < 200 ms p95; GET con hop al hospital < 1 s p95 (con caché)
**Constraints**: la vista Oracle sólo se consulta read-only; circuit-breaker para no propagar caídas; sin almacenar credenciales del hospital en claro (DPAPI o secrets manager local)
**Scale/Scope**: ~50k recursos en el primer año, ~10 req/s pico

## Constitution Check

- ✅ Validador FHIR oficial corrido en CI sobre todos los recursos del repo.
- ✅ `CapabilityStatement` publicado y verificado.
- ✅ Cliente del hospital con circuit-breaker (mitigación de "Interoperabilidad fallida", criticidad 10 del anteproyecto).
- ✅ Auditoría de accesos al hospital y Oracle (módulo 005).
- ✅ Cero PHI almacenada en logs.

## Project Structure

```text
src/
├── ChequeAR.Api/
│   ├── Controllers/MetadataController.cs            # CapabilityStatement
│   └── Controllers/FhirTransactionController.cs     # bundle transaction
├── ChequeAR.Infrastructure/
│   ├── Fhir/
│   │   ├── HospitalFhirClient.cs
│   │   ├── FhirValidatorService.cs
│   │   └── Profiles/                                # perfiles ChequeAR
│   ├── Oracle/
│   │   ├── OracleViewClient.cs
│   │   └── Mapping/
│   │       ├── YamlMappingLoader.cs
│   │       └── OracleToFhirMapper.cs
│   ├── Repositories/                                # PatientCache, ObservationStore, etc
│   └── Caching/HospitalFhirCache.cs

src/ChequeAR.Infrastructure/OracleMappings/
├── patient.yaml
├── condition.yaml
└── observation.yaml

tests/
├── unit/Infrastructure.Fhir.Tests/
├── integration/HospitalClient.Tests/                # mock server
└── integration/Oracle.Tests/                        # Oracle XE en Docker
```

**Structure Decision**: la capa de interoperabilidad vive enteramente en `ChequeAR.Infrastructure`, expuesta vía interfaces consumidas por los servicios de aplicación de los demás módulos.

## Fases

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Spike validador HL7 + perfiles ChequeAR | S1 |
| 1 | Servidor FHIR base + `CapabilityStatement` | S2 |
| 2 | `HospitalFhirClient` con resiliencia (Polly) | S3 |
| 3 | `OracleViewClient` + mapeos YAML | S3-S4 |
| 4 | Caché + Observation derivada al finalizar QR | S4 |
| 5 | Bundle transaction + hardening | S5 |

## Complexity Tracking

| Violación | Por qué | Alternativa descartada |
|---|---|---|
| Mapeos YAML externos | Cambios frecuentes en la vista Oracle sin redeploy | Hardcoded: requeriría release por cada ajuste |
| Cache propia en SQL | No hay Redis en infra del hospital | In-memory: no sobrevive reciclo de IIS |
