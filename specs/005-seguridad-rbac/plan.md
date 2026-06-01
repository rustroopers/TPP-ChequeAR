# Implementation Plan: Seguridad y Control de Accesos

**Branch**: `005-seguridad-rbac` | **Date**: 2026-06-01 | **Spec**: ./spec.md

## Summary

Auth delegada a la API del Hospital Garrahan, emisión de JWT propio para uso interno, autorización por rol y especialidad, y auditoría en formato FHIR `AuditEvent`. Es un módulo transversal usado por todos los demás.

## Technical Context

**Language/Version**: .NET Framework 4.7.1
**Primary Dependencies**: System.IdentityModel.Tokens.Jwt, ASP.NET Identity Core (no User Store), Owin Bearer Auth, Polly, Serilog
**Storage**: SQL Server (tablas listadas en data-model.md)
**Testing**: xUnit, Moq, WireMock.Net (simular hospital), ITestHarness para integración Owin
**Target Platform**: IIS 10 / Windows Server 2016
**Project Type**: web-service (transversal)
**Performance Goals**: validación JWT < 5 ms; `AuditEvent` no debe agregar > 50 ms p95 a operaciones (escritura asincrónica)
**Constraints**: cero almacenamiento de contraseñas; retención auditoría ≥ 6 años; logs sin PHI cruda
**Scale/Scope**: 200 usuarios concurrentes pico; ~5k AuditEvent/día

## Constitution Check

- ✅ JWT firmado, TTL corto, refresh seguro.
- ✅ `[Authorize]` declarativo en todos los endpoints — audit en CI.
- ✅ AuditEvent append-only, validado contra perfil FHIR.
- ✅ Sin secretos hardcoded — escaneo con `git-secrets` en CI.
- ✅ Filtros por especialidad aplicados a nivel servicio (no sólo UI).

## Project Structure

```text
src/
├── ChequeAR.Api/
│   ├── Auth/
│   │   ├── JwtBearerConfig.cs
│   │   ├── ChequeARSpecialtyAttribute.cs    # filtro por especialidad
│   │   └── AuthController.cs
│   └── Middleware/AuditMiddleware.cs        # captura request/response
├── ChequeAR.Application/
│   ├── Auth/
│   │   ├── HospitalAuthClient.cs
│   │   ├── JwtIssuer.cs
│   │   └── RefreshTokenService.cs
│   └── Audit/
│       ├── AuditEventBuilder.cs
│       └── AuditDispatcher.cs               # cola asincrónica
├── ChequeAR.Infrastructure/
│   ├── Repositories/UserRepository.cs
│   ├── Repositories/RefreshTokenRepository.cs
│   └── Audit/AuditRepository.cs

tests/
├── unit/Auth.Tests/
├── unit/Audit.Tests/
└── integration/
    ├── Auth.IntegrationTests/               # WireMock hospital
    └── Audit.IntegrationTests/
```

**Structure Decision**: módulo embebido como capa transversal. Filtros y atributos aplicados a controllers de los demás módulos vía DI.

## Fases

| Fase | Entregable | Sprint |
|---|---|---|
| 0 | Diseño claims + acuerdo con sistemas del hospital sobre auth | S1 |
| 1 | `HospitalAuthClient`, `JwtIssuer`, `/auth/login` | S2 |
| 2 | Atributos de autorización + filtro por especialidad | S2 |
| 3 | `AuditEvent` + dispatcher asincrónico | S3 |
| 4 | Refresh tokens + logout + sincronización roles | S3 |
| 5 | Hardening: rate limiting, escaneo secretos, partitioning auditoría | S5 |

## Complexity Tracking

| Violación | Por qué | Alternativa descartada |
|---|---|---|
| Cache local de usuarios | Permite operar si el hospital cae brevemente | Consulta en cada request: latencia 100ms+ |
| Dispatcher async para AuditEvent | Performance de operaciones críticas | Sync escritura: agrega ~30-80ms a cada request |
| Partitioning auditoría | Retención 6+ años sin degradar | Sin partitions: queries de auditoría lentas con > 1M filas |
