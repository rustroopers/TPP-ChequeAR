using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Http.Dependencies;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Infrastructure.Auth;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Fhir;
using ChequeAR.Infrastructure.Oracle;
using ChequeAR.Infrastructure.Repositories;
using ChequeAR.Infrastructure.Storage;
using Serilog;

namespace ChequeAR.Api.App_Start
{
    /// Custom DI sin dependencias externas. Soporta MVC (System.Web.Mvc.IDependencyResolver)
    /// y Web API (System.Web.Http.Dependencies.IDependencyResolver).
    ///
    /// Estrategia:
    ///  - Singletons stateful (AuditDispatcher, JwtIssuer, RefreshTokenService) viven el ciclo de la app.
    ///  - DbContext y repos son transient (uno por resolución; en Web API se libera con el scope).
    ///
    /// Reemplazable por SimpleInjector / Autofac sin tocar la lógica de los controllers (todos
    /// declaran sus dependencias por constructor estándar).
    public static class ServiceComposition
    {
        private static readonly Dictionary<Type, Func<object>> Factories = new Dictionary<Type, Func<object>>();
        private static readonly object _lock = new object();

        private static AuditDispatcher _audit;
        private static JwtIssuer _jwt;
        private static SpecialtyCatalogService _specialties;
        private static IFhirValidator _fhirValidator;
        private static System.Net.Http.HttpClient _fhirHttp;
        private static System.Net.Http.HttpClient _authHttp;
        private static System.Net.Http.HttpClient _termHttp;

        public static void Configure()
        {
            // ===== Config =====
            var jwtIssuer = ConfigurationManager.AppSettings["Jwt:Issuer"] ?? "https://chequear.garrahan";
            var jwtAudience = ConfigurationManager.AppSettings["Jwt:Audience"] ?? "chequear-api";
            var jwtSecret = ConfigurationManager.AppSettings["Jwt:SigningSecret"] ?? "dev-secret-please-rotate-dev-secret-please-rotate";
            var jwtTtlMinutes = int.Parse(ConfigurationManager.AppSettings["Jwt:AccessTtlMinutes"] ?? "60");
            var refreshTtlHours = int.Parse(ConfigurationManager.AppSettings["Jwt:RefreshTtlHours"] ?? "8");
            var specialtiesCacheTtlMinutes = int.Parse(ConfigurationManager.AppSettings["Specialties:CacheTtlMinutes"] ?? "10");
            // Puerta de escape para desplegar el enforcement sin cambio de comportamiento
            // mientras se puebla el espejo de usuarios (ver feature 011): el default es
            // SIEMPRE true -- sólo el string "false" explícito lo apaga. Un appSetting
            // ausente no puede dejar el sistema abierto.
            var enforceSpecialtyScope = !string.Equals(
                ConfigurationManager.AppSettings["Authz:EnforceSpecialtyScope"], "false",
                StringComparison.OrdinalIgnoreCase);
            if (!enforceSpecialtyScope)
                Log.Warning("Authz:EnforceSpecialtyScope=false: el filtro por especialidad esta DESACTIVADO.");

            var hospitalAuthUrl = ConfigurationManager.AppSettings["HospitalAuth:LoginUrl"] ?? "";
            var hospitalFhirUrl = ConfigurationManager.AppSettings["HospitalFhir:BaseUrl"] ?? "";
            // Servidor terminológico SNOMED CT (Snowstorm). Vacío en config => el endpoint
            // de autocomplete responde 503 sin romper el resto de la app. ECL opcional acota
            // el dominio de sugerencias; displayLanguage por defecto 'es'.
            var terminologyUrl = ConfigurationManager.AppSettings["Terminology:BaseUrl"] ?? "";
            var terminologyEcl = ConfigurationManager.AppSettings["Terminology:Ecl"] ?? "";
            var terminologyLang = ConfigurationManager.AppSettings["Terminology:DisplayLanguage"] ?? "es";
            var oracleConn = ConfigurationManager.AppSettings["Oracle:ConnectionString"] ?? "";
            var attachmentsRoot = Environment.ExpandEnvironmentVariables(
                ConfigurationManager.AppSettings["Attachments:Root"] ?? @"C:\ChequeAR\Attachments");
            var sqlConn = ConfigurationManager.ConnectionStrings["ChequeAR"]?.ConnectionString ?? "";

            // ===== Singletons =====
            _jwt = new JwtIssuer(jwtIssuer, jwtAudience, jwtSecret, jwtTtlMinutes);

            // Instancia única, no transitoria: el validador lee y cachea la especificación
            // FHIR completa (~10 MB de definiciones). Crear uno por request sería inviable.
            // Mientras fue un sustituto que no hacía nada, ser transitorio no costaba nada.
            //
            // Los perfiles propios se copian a la salida de compilación, igual que la
            // allowlist de vistas: la ubicación en runtime es bin/Fhir/Profiles.
            var profilesPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Fhir", "Profiles");
            // El arranque NO se puede caer por el validador: una excepción acá viaja como
            // HttpException desde Application_Start y deja TODA la aplicación en 500, con el
            // detalle sólo visible en el registro de eventos de Windows. Ya pasó una vez.
            try
            {
                var validador = new FhirValidatorService(profilesPath);
                // Calentamiento: sin esto la primera publicación del día paga la carga de
                // toda la especificación y parece que el sistema se colgó.
                validador.Warmup();
                _fhirValidator = validador;
            }
            catch (Exception)
            {
                // Degradado: sin especificación ni perfiles, las reglas del dominio se siguen
                // aplicando con el conjunto de tipos por omisión.
                _fhirValidator = new FhirValidatorService();
            }
            // Un HttpClient singleton por integración, con su Timeout fijado una sola vez.
            // Los clients NO mutan estas instancias (evita InvalidOperationException).
            _fhirHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _authHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // El terminológico puede ser un server externo (más lento que el hospital local):
            // le damos bastante más aire. En arranque en frío, la PRIMERA expansión de los ECL
            // grandes de la config (<<404684003 "Clinical finding" entero, etc.) se pasaba de 8s
            // hasta que Elasticsearch calienta sus cachés, y el autocomplete del editor moría en
            // 502 justo al empezar a usarlo. Ya caliente, la misma consulta tarda ~700 ms.
            _termHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(25) };

            // AuditDispatcher requiere repo; usamos una factory que crea su propio contexto.
            _audit = new AuditDispatcher(NewAuditRepository(), 1000);

            // Mismo criterio: singleton con su propia factory de repositorio (DbContext
            // nuevo por recarga, descartado al terminar). El catálogo cambia rarísimo, así
            // que un TTL de minutos alcanza sin pegarle a la base en cada request.
            _specialties = new SpecialtyCatalogService(
                () => new SpecialtyRepository(new ChequeARDbContext()), specialtiesCacheTtlMinutes);

            // ===== Factories =====
            Register<ChequeARDbContext>(() => new ChequeARDbContext());

            Register<IQuestionnaireRepository>(() => new QuestionnaireRepository(Resolve<ChequeARDbContext>()));
            Register<IQuestionnaireResponseRepository>(() => new QuestionnaireResponseRepository(Resolve<ChequeARDbContext>()));
            Register<IAuditRepository>(() => new AuditRepository(Resolve<ChequeARDbContext>()));
            Register<IUserRepository>(() => new UserRepository(Resolve<ChequeARDbContext>()));
            Register<IRefreshTokenRepository>(() => new RefreshTokenRepository(Resolve<ChequeARDbContext>()));
            Register<ISavedReportRepository>(() => new SavedReportRepository(Resolve<ChequeARDbContext>()));
            Register<ISpecialtyRepository>(() => new SpecialtyRepository(Resolve<ChequeARDbContext>()));
            Register<IClinicalVariableRepository>(() => new ClinicalVariableRepository(Resolve<ChequeARDbContext>()));

            // Sin servidor FHIR del hospital, se proyecta el mismo dataset que las vistas
            // VW_* (ver MockHospitalFhirClient): una sola fuente, dos protocolos.
            if (string.IsNullOrWhiteSpace(hospitalFhirUrl))
            {
                Log.Warning("HospitalFhir:BaseUrl vacio: se usa MockHospitalFhirClient sobre "
                            + "el dataset de las vistas (datos de demostracion).");
                Register<IHospitalFhirClient>(() => new MockHospitalFhirClient(Resolve<IOracleViewClient>()));
            }
            else
            {
                Register<IHospitalFhirClient>(() => new HospitalFhirClient(_fhirHttp, hospitalFhirUrl));
            }
            // Sin URL configurada no hay login del hospital que llamar: se autentica local,
            // contra AppUser.PasswordHash (ver LocalCredentialAuthenticator/PasswordHasher).
            // Cuando exista el servicio real del hospital, basta configurar la URL para que
            // ese modo tome precedencia -- el hash local queda sin uso, no hace falta borrarlo.
            if (string.IsNullOrWhiteSpace(hospitalAuthUrl))
            {
                Log.Warning("HospitalAuth:LoginUrl vacio: se usa autenticacion local contra "
                            + "AppUser.PasswordHash (LocalCredentialAuthenticator).");
                Register<ICredentialAuthenticator>(() => new LocalCredentialAuthenticator(Resolve<IUserRepository>()));
            }
            else
            {
                Register<ICredentialAuthenticator>(() => new HospitalAuthClient(_authHttp, hospitalAuthUrl));
            }
            Register<ITerminologyClient>(() => new TerminologyClient(_termHttp, terminologyUrl, terminologyEcl, terminologyLang));

            // Allowlist de vistas autorizadas. Se carga una sola vez al arrancar la app.
            // El archivo se copia a la salida con CopyToOutputDirectory=PreserveNewest;
            // la ubicación final en runtime es bin/Oracle/allowlist.yaml junto al ensamblado.
            var allowlistPath = Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "Oracle", "allowlist.yaml");
            var allowedViews = File.Exists(allowlistPath)
                ? AllowlistLoader.Load(allowlistPath)
                : null; // En tests sin archivo: comportamiento fallback (sólo VW_*).
            // Sin conexion configurada no hay base del hospital que consultar. El mock de
            // spec 008 es un contenedor Oracle, que no existe en App Service, asi que alla
            // las vistas se sirven desde memoria respetando la misma allowlist.
            if (string.IsNullOrWhiteSpace(oracleConn))
            {
                Log.Warning("Oracle:ConnectionString vacio: las vistas VW_* se sirven desde "
                            + "MockOracleViewClient (datos de demostracion, no del hospital).");
                Register<IOracleViewClient>(() => new MockOracleViewClient(allowedViews));
            }
            else
            {
                Register<IOracleViewClient>(() => new OracleViewClient(oracleConn, allowedViews));
            }
            Register<IFhirValidator>(() => _fhirValidator);
            Register<IAttachmentStore>(() => new FileShareAttachmentStore(attachmentsRoot, Resolve<ChequeARDbContext>()));

            Register<JwtIssuer>(() => _jwt);
            Register<AuditDispatcher>(() => _audit);
            Register<SpecialtyCatalogService>(() => _specialties);
            Register<RefreshTokenService>(() => new RefreshTokenService(Resolve<IRefreshTokenRepository>(), refreshTtlHours));

            // Adaptador de ClaimsPrincipal -> ICurrentUser (Application no conoce System.Web)
            // y el guard que decide el scope. SpecialtyGuard.Scope se recalcula en cada
            // acceso, así que no importa que ICurrentUser/ISpecialtyGuard no tengan scope
            // por request en este DI.
            Register<ICurrentUser>(() => new ChequeAR.Api.Auth.HttpCurrentUser());
            Register<ISpecialtyGuard>(() => new SpecialtyGuard(Resolve<ICurrentUser>(), enforceSpecialtyScope));
            // Eje rol (paralelo al de especialidad): RolePolicy es la matriz explícita, no
            // depende del flag de despliegue de arriba -sacarle auditoría/usuarios a Jefe no
            // es algo que se pueda "apagar" para volver al comportamiento anterior.
            Register<IAccessGuard>(() => new AccessGuard(Resolve<ICurrentUser>()));

            Register<QuestionnaireService>(() => new QuestionnaireService(
                Resolve<IQuestionnaireRepository>(), Resolve<IQuestionnaireResponseRepository>(),
                Resolve<IFhirValidator>(), _audit, Resolve<ISpecialtyGuard>(), Resolve<SpecialtyCatalogService>(),
                Resolve<IAccessGuard>(), Resolve<IClinicalVariableRepository>()));
            Register<QuestionnaireResponseService>(() => new QuestionnaireResponseService(
                Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>(),
                Resolve<IFhirValidator>(), _audit, Resolve<ISpecialtyGuard>(), Resolve<IAccessGuard>()));
            Register<AuditQueryService>(() => new AuditQueryService(Resolve<IAuditRepository>()));
            Register<SavedReportService>(() => new SavedReportService(Resolve<ISavedReportRepository>(), Resolve<AuditDispatcher>(), Resolve<ISpecialtyGuard>()));
            Register<UserService>(() => new UserService(Resolve<IUserRepository>(), Resolve<SpecialtyCatalogService>()));
            // Sin caché, a diferencia de SpecialtyCatalogService: el Administrador edita el
            // catálogo en vivo y un alta recién hecha tiene que aparecer en el editor ya.
            Register<ClinicalVariableService>(() => new ClinicalVariableService(
                Resolve<IClinicalVariableRepository>(), _audit, Resolve<IAccessGuard>()));
            Register<TerminologyService>(() => new TerminologyService(Resolve<ITerminologyClient>()));
            Register<HomeDashboardService>(() => new HomeDashboardService(
                Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>(),
                Resolve<IUserRepository>(), Resolve<SpecialtyCatalogService>(),
                Resolve<ISpecialtyGuard>()));
            Register<PrefillService>(() => new PrefillService(Resolve<IHospitalFhirClient>(), Resolve<IOracleViewClient>()));
            Register<QueryBuilder>(() => new QueryBuilder());
            Register<IReportQueryRunner>(() => new OracleReportQueryRunner(sqlConn));
            Register<ReportExecutor>(() => new ReportExecutor(
                Resolve<IReportQueryRunner>(), Resolve<QueryBuilder>(), Resolve<IQuestionnaireRepository>(), Resolve<ISpecialtyGuard>()));

            // Evolución longitudinal de parámetros (spec 010): consulta dedicada contra
            // vw_FlatResponses (misma conexión Oracle que los reportes) + service que arma las
            // series desde la definición del cuestionario.
            Register<IPatientTrendRepository>(() => new PatientTrendRepository(sqlConn));
            Register<PatientTrendService>(() => new PatientTrendService(
                Resolve<IQuestionnaireRepository>(), Resolve<IPatientTrendRepository>()));

            Register<IntegrationHealthService>(() => new IntegrationHealthService(
                Resolve<IHospitalFhirClient>(), Resolve<IOracleViewClient>(), hospitalAuthUrl));
            Register<PatientService>(() => new PatientService(
                Resolve<IHospitalFhirClient>(), Resolve<IOracleViewClient>(),
                Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>(),
                Resolve<SpecialtyCatalogService>()));

            // Controllers de Web API: SimpleInjector los registra automáticamente;
            // nosotros declaramos los pocos que tenemos.
            Register<Controllers.Api.QuestionnaireController>(() => new Controllers.Api.QuestionnaireController(
                Resolve<QuestionnaireService>()));
            Register<Controllers.Api.QuestionnaireResponseController>(() => new Controllers.Api.QuestionnaireResponseController(
                Resolve<QuestionnaireResponseService>()));
            Register<Controllers.Api.AuthController>(() => new Controllers.Api.AuthController(
                Resolve<ICredentialAuthenticator>(), Resolve<JwtIssuer>(), Resolve<RefreshTokenService>(), Resolve<UserService>()));
            Register<Controllers.Api.AuditEventController>(() => new Controllers.Api.AuditEventController(Resolve<AuditQueryService>()));
            Register<Controllers.Api.AttachmentController>(() => new Controllers.Api.AttachmentController(Resolve<IAttachmentStore>()));
            Register<Controllers.Api.ReportsController>(() => new Controllers.Api.ReportsController(Resolve<ReportExecutor>(), Resolve<AuditDispatcher>()));
            Register<Controllers.Api.MetadataController>(() => new Controllers.Api.MetadataController());
            Register<Controllers.Api.TerminologyController>(() => new Controllers.Api.TerminologyController(Resolve<ITerminologyClient>()));
            Register<Controllers.Api.PatientController>(() => new Controllers.Api.PatientController(Resolve<PatientService>()));
            Register<Controllers.Api.PatientTrendController>(() => new Controllers.Api.PatientTrendController(Resolve<PatientTrendService>()));
            Register<Controllers.Api.EncounterController>(() => new Controllers.Api.EncounterController(Resolve<PatientService>()));
            Register<Controllers.Api.UserController>(() => new Controllers.Api.UserController(Resolve<UserService>()));
            Register<Controllers.Api.ClinicalVariableController>(() => new Controllers.Api.ClinicalVariableController(Resolve<ClinicalVariableService>()));
            Register<Controllers.Api.IntegrationStatusController>(() => new Controllers.Api.IntegrationStatusController(Resolve<IntegrationHealthService>()));
            Register<Controllers.Api.SavedReportController>(() => new Controllers.Api.SavedReportController(Resolve<SavedReportService>(), Resolve<QuestionnaireService>()));
            Register<Controllers.Api.SpecialtyController>(() => new Controllers.Api.SpecialtyController(Resolve<SpecialtyCatalogService>(), Resolve<ISpecialtyGuard>()));

            // Controllers MVC
            Register<Controllers.Mvc.HomeController>(() => new Controllers.Mvc.HomeController(Resolve<HomeDashboardService>(), Resolve<SpecialtyCatalogService>()));
            Register<Controllers.Mvc.AccountController>(() => new Controllers.Mvc.AccountController(
                Resolve<ICredentialAuthenticator>(), Resolve<UserService>(), Resolve<SpecialtyCatalogService>()));
            Register<Controllers.Mvc.QuestionnairesController>(() => new Controllers.Mvc.QuestionnairesController(
                Resolve<QuestionnaireService>(), Resolve<SpecialtyCatalogService>(), Resolve<ClinicalVariableService>()));
            Register<Controllers.Mvc.EncountersController>(() => new Controllers.Mvc.EncountersController(
                Resolve<PatientService>(), Resolve<QuestionnaireResponseService>(), Resolve<QuestionnaireService>(), Resolve<AuditQueryService>(),
                Resolve<SpecialtyCatalogService>()));
            Register<Controllers.Mvc.ReportsController>(() => new Controllers.Mvc.ReportsController(
                Resolve<SavedReportService>(), Resolve<ReportExecutor>(), Resolve<QuestionnaireService>()));
            Register<Controllers.Mvc.AuditController>(() => new Controllers.Mvc.AuditController(
                Resolve<AuditQueryService>(), Resolve<QuestionnaireService>()));
            Register<Controllers.Mvc.UsersController>(() => new Controllers.Mvc.UsersController(Resolve<UserService>(), Resolve<SpecialtyCatalogService>()));
            Register<Controllers.Mvc.VariablesController>(() => new Controllers.Mvc.VariablesController(Resolve<ClinicalVariableService>()));
            Register<Controllers.Mvc.AdminController>(() => new Controllers.Mvc.AdminController());
            Register<Controllers.Mvc.ErrorController>(() => new Controllers.Mvc.ErrorController());
        }

        public static void Register<TService>(Func<object> factory)
        {
            lock (_lock) { Factories[typeof(TService)] = factory; }
        }

        public static T Resolve<T>() => (T)Resolve(typeof(T));

        public static object Resolve(Type t)
        {
            Func<object> f;
            lock (_lock) { Factories.TryGetValue(t, out f); }
            return f != null ? f() : null;
        }

        public static System.Web.Mvc.IDependencyResolver MvcResolver { get; } = new MvcResolverImpl();
        public static IDependencyResolver WebApiResolver { get; } = new WebApiResolverImpl();

        private static IAuditRepository NewAuditRepository() => new AuditRepository(new ChequeARDbContext());

        private class MvcResolverImpl : System.Web.Mvc.IDependencyResolver
        {
            public object GetService(Type serviceType) => ServiceComposition.Resolve(serviceType);
            public IEnumerable<object> GetServices(Type serviceType)
            {
                var r = Resolve(serviceType);
                return r != null ? new[] { r } : Enumerable.Empty<object>();
            }
        }

        private class WebApiResolverImpl : IDependencyResolver
        {
            public object GetService(Type serviceType) => ServiceComposition.Resolve(serviceType);
            public IEnumerable<object> GetServices(Type serviceType)
            {
                var r = Resolve(serviceType);
                return r != null ? new[] { r } : Enumerable.Empty<object>();
            }
            public IDependencyScope BeginScope() => new Scope();
            public void Dispose() { }

            private class Scope : IDependencyScope
            {
                private readonly ConcurrentBag<IDisposable> _disposables = new ConcurrentBag<IDisposable>();
                public object GetService(Type serviceType)
                {
                    var r = ServiceComposition.Resolve(serviceType);
                    if (r is IDisposable d) _disposables.Add(d);
                    return r;
                }
                public IEnumerable<object> GetServices(Type serviceType)
                {
                    var r = GetService(serviceType);
                    return r != null ? new[] { r } : Enumerable.Empty<object>();
                }
                public void Dispose()
                {
                    foreach (var d in _disposables)
                    {
                        try { d.Dispose(); } catch { /* swallow */ }
                    }
                }
            }
        }
    }
}
