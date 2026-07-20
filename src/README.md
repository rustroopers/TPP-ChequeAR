# ChequeAR — MVP (.NET Framework 4.7.1 / MVC + Web API 2)

Implementación inicial de los 5 módulos definidos en `/specs/`. Backend MVC 5 + Web API 2 sobre IIS 10 / Windows Server 2016.

## Estructura

```
src/
├── ChequeAR.sln
├── ChequeAR.Api/              # MVC + Web API 2 (presentación + OWIN)
├── ChequeAR.Application/      # Servicios, DTOs, interfaces
├── ChequeAR.Domain/           # Entidades y enums
└── ChequeAR.Infrastructure/   # EF6, repos, FHIR client, Oracle, Auth
```

Patrón **Package by Layer** según anteproyecto.

## Módulos cubiertos

| Spec | Componentes principales |
|---|---|
| 001 Gestor cuestionarios | `Questionnaire*` entities, `QuestionnaireService`, `QuestionnaireController` (CRUD + `$publish` + `$archive`) |
| 002 Motor renderizado | `QuestionnaireResponse*` entities, `QuestionnaireResponseService` (+ `$finalize`), `AttachmentController`, `PrefillService` |
| 003 Interoperabilidad FHIR | `MetadataController` (CapabilityStatement), `HospitalFhirClient`, `OracleViewClient`, `FhirValidatorService` |
| 004 Análisis y exportación | `ReportsController`, `QueryBuilder`, `ReportExecutor`, `CsvExporter`, `XlsxExporter` |
| 005 Seguridad y RBAC | `AuthController`, `JwtIssuer`, `RefreshTokenService`, `HospitalAuthClient`, `ChequeARSpecialtyAttribute`, `AuditDispatcher`, `AuditMiddleware`, `AuditEventController` |

## Para abrir el proyecto

1. Visual Studio 2022 con workload "ASP.NET y desarrollo web".
2. .NET Framework 4.7.1 Developer Pack instalado.
3. Abrir `src/ChequeAR.sln`.
4. Restaurar paquetes NuGet (`nuget restore`).
5. Crear base con `Update-Database` (Package Manager Console, proyecto Infrastructure).
6. F5 sobre `ChequeAR.Api`.

## Dependencias clave (NuGet)

- Microsoft.AspNet.Mvc 5.2.9
- Microsoft.AspNet.WebApi 5.2.9
- Microsoft.Owin.Security.Jwt 4.2.2
- System.IdentityModel.Tokens.Jwt 6.35.0
- EntityFramework 6.4.4
- Oracle.ManagedDataAccess 21.13.0
- Newtonsoft.Json 13.0.3

## Stubs que requieren implementación productiva

- `Infrastructure/Oracle/OracleViewClient.cs` — implementar queries reales contra vistas autorizadas del hospital.
- `Infrastructure/Fhir/FhirValidatorService.cs` — sustituir por Firely Validator (Hl7.Fhir.Specification).
- `Application/Exporters/XlsxExporter.cs` — usar ClosedXML para producir XLSX real.
- `Infrastructure/Auth/HospitalAuthClient.cs` — parser JSON con Newtonsoft + contrato real del hospital.
- Hangfire para exports asincrónicos > 10k filas (no incluido en MVP).
- Polly (retry + circuit breaker) en `HospitalFhirClient` y `HospitalAuthClient`.

## DI (no incluido)

El MVP define controllers con constructor injection. Para producción registrar contenedor (Autofac / Unity / SimpleInjector) en `Global.asax.cs::Application_Start` mapeando:

- `IQuestionnaireRepository` → `QuestionnaireRepository`
- `IQuestionnaireResponseRepository` → `QuestionnaireResponseRepository`
- `IAuditRepository` → `AuditRepository`
- `IUserRepository` → `UserRepository`
- `IRefreshTokenRepository` → `RefreshTokenRepository`
- `ISavedReportRepository` → `SavedReportRepository`
- `IHospitalFhirClient` → `HospitalFhirClient`
- `IHospitalAuthClient` → `HospitalAuthClient`
- `IOracleViewClient` → `OracleViewClient`
- `IAttachmentStore` → `FileShareAttachmentStore`
- `IFhirValidator` → `FhirValidatorService`
- Singletons: `JwtIssuer`, `AuditDispatcher`, `QueryBuilder`, `ReportExecutor`

## Trazabilidad spec → código

Ver `/specs/<modulo>/tasks.md` para el desglose por sprint del trabajo restante. Este MVP cubre las fases 0–2 de cada módulo con foco en estructura y endpoints; las fases 3+ (Hangfire, validador real, Polly, perfiles FHIR, librería JS de renderizado) se completan según roadmap.

## Mock del lado Hospital (desarrollo local)

Para desarrollar y testear contra una réplica provisoria del lado hospital
(Oracle XE en Docker), ver [`infra/mock-hospital/README.md`](../infra/mock-hospital/README.md).
Levantarlo es un `docker compose up -d --wait` desde esa carpeta. La spec
completa está en [`specs/008-mock-hospital-db/`](../specs/008-mock-hospital-db/).
