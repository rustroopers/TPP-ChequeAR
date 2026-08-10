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

Ver `/specs/<modulo>/tasks.md` para el desglose por sprint del trabajo restante. Este MVP cubre las fases 0–2 de cada módulo con foco en estructura y endpoints; las fases 3+ (Hangfire, Polly, librería JS de renderizado) se completan según roadmap.

La conformidad FHIR y el saneamiento de capas los cierra la spec **009**: validador real, perfiles publicados y recursos conformes en toda la interfaz.

## Mock del lado Hospital (desarrollo local)

Para desarrollar y testear contra una réplica provisoria del lado hospital
(Oracle XE en Docker), ver [`infra/mock-hospital/README.md`](../infra/mock-hospital/README.md).
Levantarlo es un `docker compose up -d --wait` desde esa carpeta. La spec
completa está en [`specs/008-mock-hospital-db/`](../specs/008-mock-hospital-db/).

## Interfaz FHIR

Ver `specs/009-conformidad-fhir-capas/contracts/fhir-api.md` para el contrato completo.

### Qué es FHIR y qué no

Todo lo que cuelga de `/fhir/r5` devuelve recursos HL7 FHIR R5 reales, con
`Content-Type: application/fhir+json` **por omisión** (no hace falta mandar `Accept`), Bundle
conformes y `OperationOutcome` en los errores.

| Recurso | Interacciones |
|---|---|
| `Questionnaire` | read, search, create, update, delete + `$publish`, `$archive`, `$clone` |
| `QuestionnaireResponse` | read, search, create + `$finalize` |
| `AuditEvent` | read, search |
| `Patient` | read, search — **sólo lectura**: ChequeAR no posee la entidad |
| `Encounter` | search — **sólo lectura**: derivado de las fichas cargadas |
| `ValueSet` | `$expand` (autocompletado SNOMED CT) |
| `Observation` | **no soportado**, declarado explícitamente en el CapabilityStatement |

**Fuera de la interfaz FHIR, a propósito**: `/auth`, `/api/reports`, `/api/reports/saved`,
`/api/users`, `/api/attachments` y `/api/integrations`. No tienen recurso natural en el
estándar. Los adjuntos en particular deben seguir existiendo: las fichas los referencian por
dirección desde `valueAttachment.url`.

### Extensiones propias

Dos, y sólo dos. Cada extensión propia es algo que un consumidor externo no entiende sin leer
esta documentación, así que se pagan de a una.

| URL | Aplica a | Para qué |
|---|---|---|
| `https://chequear.garrahan/StructureDefinition/question-term` | `Questionnaire.item` | Término clínico de autoría: lo que quiso decir quien diseñó la pregunta, aunque todavía no haya elegido un código SNOMED |
| `https://chequear.garrahan/StructureDefinition/questionnaire-version-id` | `QuestionnaireResponse` | Versión congelada con la que se completó la ficha |

### Perfiles de conformidad

Artefactos versionados en `ChequeAR.Infrastructure/Fhir/Profiles/`, copiados a la salida de
compilación. El sistema los declara en `meta.profile`.

- `ChequeARQuestionnaire` — título, `subjectType = Patient`, especialidad obligatoria,
  `linkId` obligatorio y único, y tipos acotados a los que el renderizador sabe dibujar.
- `ChequeARQuestionnaireResponse` — paciente, cuestionario y anclaje de versión obligatorios.

⚠️ El motor que interpreta StructureDefinition automáticamente (`Firely.Fhir.Validation.*`)
exige `netstandard2.1` y **no corre sobre .NET Framework 4.7.1**. La validación combina el
parser estricto del SDK con las reglas de `Application/Fhir/ProfileRules`, que deben
mantenerse sincronizadas a mano con los perfiles. Ver `research.md` (R9) de la spec 009.

### Vocabulario: FHIR vs. interno

Las islas Vue hablan su propio vocabulario de tipos (`choice`, `openChoice`, `scale`,
`dateTime`) y **no tiene por qué coincidir** con el del estándar. La traducción vive en la capa
de negocio. En R5 las preguntas de opción son `coding` y se distinguen por `answerConstraint`;
`choice` y `open-choice` eran de R4 y ya no existen.
