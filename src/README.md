# ChequeAR — .NET Framework 4.7.1 / MVC 5 + Web API 2

Cuestionarios clínicos para el Hospital Garrahan con interfaz HL7 FHIR R5. Corre en Azure App
Service (Windows) sobre Oracle Autonomous Database; en desarrollo, IIS Express y Oracle XE 21c.

## Estructura

```
src/
├── ChequeAR.sln
├── ChequeAR.Domain/           # Entidades y enums, sin paquetes
├── ChequeAR.Application/      # Servicios, interfaces, mapeo FHIR, reportes y exportadores
├── ChequeAR.Infrastructure/   # EF6 + Oracle, repositorios, clientes HTTP, auth, adjuntos
├── ChequeAR.Api/              # Web API 2 + MVC 5 (Razor) + islas Vue en Scripts/islands/
├── ChequeAR.Tests/            # xUnit + Moq; sólo referencia Domain y Application
└── ChequeAR.Api.Tests/        # Pipeline OWIN, filtros y controladores
```

Las dependencias van en un solo sentido (`Domain ← Application ← Infrastructure ← Api`); los
repositorios implementan interfaces de `Application/Interfaces`. `LayerDependencyTests` lo
verifica sobre los ensamblados y los csproj.

Fuera de `src/`: `tests/integration/` (tests contra Oracle, se corren a mano),
`infra/mock-hospital/` (vistas del hospital para desarrollo) e `infra/snowstorm*/`
(servidor de terminología SNOMED CT).

## Módulos

| Spec | Componentes principales |
|---|---|
| 001 Gestor de cuestionarios | `QuestionnaireService`, `QuestionnaireController` (CRUD + `$publish`, `$archive`, `$clone`), isla `questionnaire-editor` |
| 002 Motor de renderizado | `QuestionnaireResponseService`, `PrefillService`, `AttachmentController`, isla `questionnaire-renderer` |
| 003 Interoperabilidad FHIR | `MetadataController` + `CapabilityCatalog`, `FhirValidatorService`, `OracleViewClient` |
| 004 Análisis y exportación | `ReportsController`, `QueryBuilder`, `ReportExecutor`, `CsvExporter`, `XlsxExporter`, islas `report-builder` y `report-stats` |
| 005 Seguridad y RBAC | `AuthController`, `JwtIssuer`, `RefreshTokenService`, `LocalCredentialAuthenticator`, `AuditDispatcher` |
| 006 Terminología | `TerminologyService`, `TerminologyController` (`ValueSet/$expand` contra Snowstorm) |
| 008 Mock del hospital | `infra/mock-hospital/`, `MockOracleViewClient` |
| 009–014 | Conformidad FHIR, variables clínicas, tendencias por paciente y ajustes (ver `specs/`) |

El hospital no expone API: el login es local (`LocalCredentialAuthenticator`) y los datos de
pacientes salen de las vistas `VW_*` (`MockHospitalFhirClient` arma los recursos FHIR desde
ahí). `HospitalFhirClient` y `HospitalAuthClient` se registran sólo si se configuran
`HospitalFhir:BaseUrl` y `HospitalAuth:LoginUrl`.

## Desarrollo local

Requisitos: Visual Studio 2022 o posterior (o Build Tools) con el workload de desarrollo web,
.NET Framework 4.7.1 Developer Pack, SDK de .NET 8 (para `dotnet test`) y Oracle XE 21c con
la PDB `XEPDB1`.

1. Crear el usuario `CHEQUEAR` en `XEPDB1` (`infra/chequear-db/00-create-user.sql`, como SYSDBA).
2. Copiar `ChequeAR.Api/Web.ConnectionStrings.config.example` y `Web.Secrets.config.example`
   sin el `.example` y completarlos. Ninguno de los dos se versiona.
3. Compilar y levantar IIS Express en `https://localhost:44301` (la skill `/run-app` hace
   todo: listener de Oracle, MSBuild encontrado con `vswhere`, IIS Express).
4. Las migraciones pendientes se aplican solas la primera vez que se usa la base
   (`Migrations:Automatic`, activo por omisión). En producción está apagado y las aplica el
   deploy por `/api/deploy/migrate`, después de un export Data Pump.

Con `Oracle:ConnectionString` vacío, las vistas del hospital se sirven desde memoria
(`MockOracleViewClient`). Para usar el mock en Oracle, ver
[`infra/mock-hospital/README.md`](../infra/mock-hospital/README.md).

### Build y tests

```bash
msbuild ChequeAR.sln -p:Configuration=Release -warnaserror
dotnet test ChequeAR.Tests/ChequeAR.Tests.csproj -c Release --no-build
dotnet test ChequeAR.Api.Tests/ChequeAR.Api.Tests.csproj -c Release --no-build
```

El CI (`.github/workflows/build-test.yml`) corre lo mismo más los tests de integración que no
necesitan Oracle, con umbral de cobertura en `ChequeAR.Tests`. `deploy.yml` publica en Azure
desde `main`.

## Dependencias clave (NuGet)

- Microsoft.AspNet.Mvc / WebApi 5.2.9, Microsoft.Owin.Security.Jwt 4.2.2
- SimpleInjector 5.5 (contenedor, armado en `App_Start/ServiceComposition.cs`)
- EntityFramework 6.5.1, Oracle.ManagedDataAccess(.EntityFramework) 21.17
- Hl7.Fhir.R5 5.13.4
- Serilog 4.2
- System.IdentityModel.Tokens.Jwt 6.35, Newtonsoft.Json 13.0.4

## Interfaz FHIR

Ver `specs/009-conformidad-fhir-capas/contracts/fhir-api.md` para el contrato completo.

### Qué es FHIR y qué no

Todo lo que cuelga de `/fhir/r5` devuelve recursos HL7 FHIR R5 reales, con
`Content-Type: application/fhir+json` **por omisión** (no hace falta mandar `Accept`), Bundle
conformes y `OperationOutcome` en los errores.

| Recurso | Interacciones |
|---|---|
| `Questionnaire` | read, search, create, update, delete + `$publish`, `$archive`, `$clone` |
| `QuestionnaireResponse` | read, search, create + `$save-progress`, `$finalize`, `$amend`, `$amend-commit`, `$abandon`, `$lock`, `$renew-lock`, `$unlock`, `$retarget-preview`, `$retarget-version` |
| `AuditEvent` | read, search |
| `Patient` | read, search — **sólo lectura**: ChequeAR no posee la entidad |
| `Encounter` | search — **sólo lectura**: derivado de las fichas cargadas |
| `ValueSet` | `$expand` (autocompletado SNOMED CT; devuelve un formato propio, no un ValueSet) |
| `Observation` | **no soportado**, declarado explícitamente en el CapabilityStatement |

**Fuera de la interfaz FHIR, a propósito**: `/auth`, `/api/reports`, `/api/reports/saved`,
`/api/users`, `/api/attachments`, `/api/variables`, `/api/specialties`, `/api/patients`
(tendencias), `/api/integrations`, `/api/health` y `/api/deploy`. No tienen recurso natural en
el estándar. Los adjuntos en particular deben seguir existiendo: las fichas los referencian por
dirección desde `valueAttachment.url`.

### Extensiones propias

Definidas en `Application/Fhir/ChequeARExtensions.cs`, bajo
`https://chequear.garrahan/StructureDefinition/`. Cada una es algo que un consumidor externo no
entiende sin leer esta documentación, así que se agregan sólo cuando no hay campo nativo.

| Extensión | Aplica a | Para qué |
|---|---|---|
| `question-term` | `Questionnaire.item` | Término de autoría: lo que quiso decir quien diseñó la pregunta, aunque todavía no haya elegido un código SNOMED |
| `questionnaire-term` | `Questionnaire` | Lo mismo, para el cuestionario entero |
| `questionnaire-version-id` | `QuestionnaireResponse` | Versión congelada con la que se completó la ficha |
| `question-variable-id`, `question-variable-version` | `Questionnaire.item` | Variable clínica de la que sale la pregunta |
| `option-snomed-display` | `answerOption.valueCoding` | Término SNOMED de la opción cuando la etiqueta visible es otra |
| `lock-holder`, `lock-expires` | `OperationOutcome` (409) | Quién tiene la sesión de edición y hasta cuándo |

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
