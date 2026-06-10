# Integración Frontend ↔ Backend

> Mapeo central: pantalla MVC/Razor → endpoint backend → controller → servicio Application.
> Los endpoints completos están en cada `specs/00x/api.md`. Este archivo evita tener que cruzar specs para conectar la capa visual con el dominio.

## Convención de resolución de dependencias

`ChequeAR.Api/App_Start/ServiceComposition.cs` registra todas las factories de servicios. Implementa `System.Web.Mvc.IDependencyResolver` y `System.Web.Http.Dependencies.IDependencyResolver` sin agregar paquetes NuGet — es DI mínima propia, reemplazable por SimpleInjector/Autofac sin tocar controllers.

- **Web API controllers** se resuelven por `GlobalConfiguration.Configuration.DependencyResolver`.
- **MVC controllers** se resuelven por `DependencyResolver.SetResolver(...)`.
- **Scope**: singletons stateful (`AuditDispatcher`, `JwtIssuer`, `HttpClient`); todo lo demás transient.

## Patrón en MVC controllers

Cada controller MVC:
1. Recibe `Application services` por constructor.
2. Intenta resolver datos contra la DB / sistemas externos.
3. Si falla (ej. dev sin migración aplicada), cae a `MockSeed` y setea `ViewBag.DataMode = "demo"` para que la vista pueda mostrar un banner.

## Tabla de mapeo

| Pantalla | MVC view | MVC action | Endpoints backend consumidos | Application service / repo |
|---|---|---|---|---|
| **T2 Home** | `Home/Index.cshtml` | `HomeController.Index` | `GET /fhir/r5/QuestionnaireResponse?_count=5`, `GET /fhir/r5/Questionnaire?_count=0` (count), `GET /api/users?_count=0` (count) | `IQuestionnaireResponseRepository.SearchAsync`, `IQuestionnaireRepository.CountAsync`, `IUserRepository.CountAsync` |
| **T4 Error** | `Error/Index.cshtml` | `ErrorController.Index` | — | — |
| **T5 Profile** | `Account/Profile.cshtml` | `AccountController.Profile/SaveProfile` | — (sólo cookie de idioma) | — |
| **A1 Login** | `Account/Login.cshtml` | `AccountController.Login/DoLogin` | `POST /auth/login` (al hospital, vía cliente) | `IHospitalAuthClient.LoginAsync` |
| **A2 Sesión expirada** | `Account/SessionExpired.cshtml` | `AccountController.SessionExpired` | `POST /auth/refresh` o re-login | `RefreshTokenService.ValidateAsync` |
| **A4 Usuarios listado** | `Users/Index.cshtml` | `UsersController.Index` | `GET /api/users?q=...&role=...&specialty=...` | `IUserRepository.ListAsync` |
| **A5 Usuario detalle** | `Users/Details.cshtml` | `UsersController.Details/Save` | `GET /api/users/{id}`, `PUT /api/users/{id}` | `IUserRepository.GetAsync/UpsertAsync` |
| **A6 Auditoría listado** | `Audit/Index.cshtml` | `AuditController.Index` | `GET /fhir/r5/AuditEvent?agent.who=...&entity.what=...&_count=50` | `IAuditRepository.SearchAsync` |
| **A7 Auditoría detalle** | `Audit/Details.cshtml` | `AuditController.Details` | `GET /fhir/r5/AuditEvent/{id}` | `IAuditRepository.SearchAsync` (filtrado client-side; futuro: `GetAsync(id)`) |
| **Q1 Cuestionarios** | `Questionnaires/Index.cshtml` | `QuestionnairesController.Index` | `GET /fhir/r5/Questionnaire?status=&title:contains=&context=` | `IQuestionnaireRepository.SearchAsync/CountAsync` |
| **Q2 Editor (isla Vue)** | `Questionnaires/Edit.cshtml` | `QuestionnairesController.Edit` | `GET /fhir/r5/Questionnaire/{id}`, `POST /fhir/r5/Questionnaire`, `PUT /fhir/r5/Questionnaire/{id}` | `QuestionnaireService.CreateAsync/UpdateAsync` |
| **Q5 Vista previa** | `Questionnaires/Preview.cshtml` | `QuestionnairesController.Preview` | `GET /fhir/r5/Questionnaire/{id}` | `IQuestionnaireRepository.GetAsync` |
| **Q6 Publicar** | modal en Q1 | `QuestionnairesController.Publish` | `POST /fhir/r5/Questionnaire/{id}/$publish` | `QuestionnaireService.PublishAsync` |
| **Q7 Archivar** | modal en Q1 | `QuestionnairesController.Archive` | `POST /fhir/r5/Questionnaire/{id}/$archive` | `QuestionnaireService.ArchiveAsync` |
| **Q9 Historial** | `Questionnaires/History.cshtml` | `QuestionnairesController.History` | `GET /fhir/r5/Questionnaire/{id}` (versions in resource) | `IQuestionnaireRepository.GetAsync` |
| **R1 Buscador paciente** | `Encounters/Search.cshtml` | `EncountersController.Search` | `GET /api/patients?q=...` | `PatientService.SearchAsync` → `IOracleViewClient.QueryAsync(VW_PACIENTE_DATOS_BASICOS)` |
| **R2 Selector cuestionario** | modal en R1 | (sin POST) | `GET /fhir/r5/Questionnaire?specialty=...&status=active` | `IQuestionnaireRepository.SearchAsync` |
| **R3 Renderer (isla Vue)** | `Encounters/Load.cshtml` | `EncountersController.Load` | `GET /fhir/r5/Questionnaire/{id}`, `POST /fhir/r5/QuestionnaireResponse`, `POST /fhir/r5/QuestionnaireResponse/{id}/$finalize`, `POST /api/attachments` (Q tipo adjunto) | `QuestionnaireResponseService.CreateAsync/FinalizeAsync`, `IAttachmentStore.StoreAsync` |
| **R6 Lectura ficha** | `Encounters/View.cshtml` | `EncountersController.View` | `GET /fhir/r5/QuestionnaireResponse/{id}`, `GET /api/patients/{hospitalId}`, `GET /fhir/r5/Questionnaire/{id}` | `IQuestionnaireResponseRepository.GetAsync`, `PatientService.GetAsync` |
| **R7 Timeline paciente** | `Encounters/Timeline.cshtml` | `EncountersController.Timeline` | `GET /api/patients/{hospitalId}`, `GET /api/patients/{hospitalId}/encounters` | `PatientService.GetAsync/GetEncountersAsync` |
| **E1 Reportes guardados** | `Reports/Index.cshtml` | `ReportsController.Index` | `GET /api/reports/saved` | `ISavedReportRepository.ListForUserAsync` |
| **E2 Query Builder (isla)** | `Reports/Build.cshtml` | `ReportsController.Build` | `GET /api/reports/preview-columns?questionnaireId=...`, `POST /api/reports/query`, `POST /api/reports/saved` o `PUT /api/reports/saved/{id}` | `ReportExecutor.ExecuteAsync`, `ISavedReportRepository.AddAsync/UpdateAsync`, `IQuestionnaireRepository.GetAsync` |
| **E3 Preview cohorte** | `Reports/Preview.cshtml` | `ReportsController.Preview` | `POST /api/reports/query` | `ReportExecutor.ExecuteAsync` |
| **E4 Export (modal)** | en E3 | (sin acción MVC; POST directo desde JS) | `POST /api/reports/export` | `ReportExecutor.ExecuteAsync` + `CsvExporter`/`XlsxExporter` |
| **E5 Estadísticas** | `Reports/Stats.cshtml` | `ReportsController.Stats` | `POST /api/reports/query` (con `stats=true`) | `ReportExecutor.ExecuteAsync` (campo `stats` ya devuelto) |
| **F1 Salud integraciones** | `Admin/Integrations.cshtml` | `AdminController.Integrations` | `GET /api/integrations/status` | `IntegrationHealthService.ProbeAllAsync` |
| **F2 CapabilityStatement** | `Admin/Capability.cshtml` | `AdminController.Capability` | `GET /metadata` | `MetadataController` (estático) |

## Endpoints agregados en esta iteración

Estaban en el spec pero no implementados; ahora existen:

| Endpoint | Controller | Spec donde se documenta |
|---|---|---|
| `GET /api/patients?q=` | `PatientController.Search` | spec 003 §"Adaptador Oracle" + spec 002 §FR-005 |
| `GET /api/patients/{hospitalId}` | `PatientController.Get` | spec 003 §"Cliente: API FHIR del hospital" |
| `GET /api/patients/{hospitalId}/encounters` | `PatientController.Encounters` | spec 002 §User Story 4 (lectura histórica) |
| `GET /api/users` | `UserController.List` | spec 005 §A4 (UI nueva en 006) |
| `GET /api/users/{id}` | `UserController.Get` | spec 005 §A5 |
| `PUT /api/users/{id}` | `UserController.Upsert` | spec 005 §A5 |
| `GET /api/integrations/status` | `IntegrationStatusController.Status` | spec 003 §F1 + spec 006 §F1 |
| `GET /api/reports/saved` | `SavedReportController.List` | spec 004 §`/api/reports/saved` (ya documentado) |
| `GET /api/reports/saved/{id}` | `SavedReportController.Get` | spec 004 idem |
| `POST /api/reports/saved` | `SavedReportController.Create` | spec 004 idem |
| `PUT /api/reports/saved/{id}` | `SavedReportController.Update` | spec 004 idem |
| `DELETE /api/reports/saved/{id}` | `SavedReportController.Delete` | spec 004 idem |
| `GET /api/reports/preview-columns?questionnaireId=` | `SavedReportController.PreviewColumns` | spec 004 idem |
| `GET /fhir/r5/AuditEvent/{id}` | `AuditEventController.Get` | spec 005 §AuditEvent |

## Endpoints aún pendientes en el backend

Documentados en specs pero NO implementados todavía. La UI los necesita para llegar al 100%:

| Endpoint | Spec | Pantalla afectada | Mitigación actual |
|---|---|---|---|
| `GET /Questionnaire/{id}/_history/{vid}` | 001 | Q9 (comparar) | Q9 muestra lista; "Comparar" es stub |
| `POST /Questionnaire/{id}/$clone` | 001 | Q1 (acción "Duplicar") | Acción no expuesta en UI |
| `GET /api/reports/jobs/{jobId}` y `/artifacts/{artifactId}` | 004 | E4 modo asíncrono | E4 siempre síncrono |
| `POST /` (transaction Bundle) | 003 | — | No impacta UI directa |
| `GET /Observation` (passthrough) | 003 | R4 pre-carga | `PrefillService` ya tiene el helper |
| Refresco automático del CapabilityStatement con resources reales | 003 | F2 | Hoy estático |

## Tablas afectadas

**Ninguna nueva.** El schema existente (`ChequeARDbContext`) cubre todas las pantallas:

- `Questionnaires`, `QuestionnaireVersions`, `Questions`, `QuestionGroups`, `AnswerOptions` → Q1/Q2/Q5/Q6/Q7/Q9.
- `QuestionnaireResponses`, `QuestionnaireResponseItems`, `Attachments` → R3/R6/R7.
- `AuditEvents` → A6/A7.
- `Users`, `UserRoles`, `UserSpecialties`, `Specialties` → A4/A5.
- `SavedReports`, `ReportExecutions`, `ExportArtifacts`, `ExportJobs` → E1/E2/E3/E4.
- `RefreshTokens` → A1/A2.

Si futuras pantallas requieren columnas, **agregar en el `data-model.md` del módulo dueño** y migrar EF6.

## Modo demo (DataMode)

Cuando un MVC controller no puede resolver datos contra la DB (ej. dev sin migración aplicada, conexión Oracle ausente), setea `ViewBag.DataMode = "demo"` y sirve datos de `MockSeed.cs`. Los desarrolladores ven contenido coherente sin necesidad de migrar primero. En producción esta rama NO debería entrar — la presencia consistente del flag indica configuración rota.
