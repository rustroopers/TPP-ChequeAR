using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Web.Http;
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
using SimpleInjector;
using SimpleInjector.Integration.Web;
using SimpleInjector.Integration.Web.Mvc;
using SimpleInjector.Integration.WebApi;
using SimpleInjector.Lifestyles;

namespace ChequeAR.Api.App_Start
{
    /// <summary>
    /// Raíz de composición: el único lugar que conoce las implementaciones concretas.
    ///
    /// Ciclos de vida:
    ///  - Por request (scoped): el DbContext y los repositorios. Todos los repositorios de un
    ///    mismo request comparten el contexto, que es la unidad de trabajo, y SimpleInjector lo
    ///    libera al terminar. Antes cada repositorio tenía su propio contexto (sin transacciones
    ///    posibles entre ellos) y ninguno se liberaba.
    ///  - Singleton: lo que tiene estado de aplicación o es caro de construir (JwtIssuer, el
    ///    validador FHIR, AuditDispatcher, el catálogo de especialidades). Los que necesitan base
    ///    usan su PROPIO contexto por operación, nunca el del request: viven más que él.
    ///  - Transient: el resto (servicios y clientes sin estado).
    ///
    /// MVC y Web API conviven en la app, así que el scope es híbrido: el de Web API cuando hay
    /// uno activo (lo abre el dependency resolver de Web API por request) y, si no, el del
    /// request web (MVC). Es la configuración documentada por SimpleInjector para este caso.
    ///
    /// Verify() corre al arrancar: un registro faltante o un ciclo de vida inconsistente (un
    /// singleton que depende de algo por request) falla acá y no en medio de un request.
    /// </summary>
    public static class ServiceComposition
    {
        private static AuditDispatcher _audit;
        private static OrphanAttachmentSweeper _orphans;

        public static Container Container { get; private set; }

        public static Container Configure(HttpConfiguration webApiConfig)
        {
            var container = new Container();
            container.Options.DefaultScopedLifestyle = Lifestyle.CreateHybrid(
                defaultLifestyle: new AsyncScopedLifestyle(),
                fallbackLifestyle: new WebRequestLifestyle());

            // ===== Config =====
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
            // Snowstorm Lite propio en OCI (infra/snowstorm-lite/): exige una key compartida y
            // sirve TLS con cert autofirmado, que se valida por huella SHA-256 (pinning). Ambos
            // vacíos = server sin auth (Snowstorm local en Docker). Van en App Settings, no acá.
            var terminologyApiKey = ConfigurationManager.AppSettings["Terminology:ApiKey"] ?? "";
            var terminologyCertSha256 = ConfigurationManager.AppSettings["Terminology:CertSha256"] ?? "";
            var oracleConn = ConfigurationManager.AppSettings["Oracle:ConnectionString"] ?? "";
            var attachmentsRoot = Environment.ExpandEnvironmentVariables(
                ConfigurationManager.AppSettings["Attachments:Root"] ?? @"C:\ChequeAR\Attachments");
            var sqlConn = ConfigurationManager.ConnectionStrings["ChequeAR"]?.ConnectionString ?? "";

            // ===== Singletons =====
            container.RegisterInstance(new JwtIssuer(JwtSettings.Issuer, JwtSettings.Audience, JwtSettings.SigningKey, jwtTtlMinutes));
            // Singleton: abre un contexto propio por consulta.
            container.RegisterInstance(new SessionValidator(async userId =>
            {
                using (var ctx = new ChequeARDbContext())
                {
                    return await new UserRepository(ctx).GetAsync(userId).ConfigureAwait(false);
                }
            }, TimeSpan.FromSeconds(60), (ex, message) => Log.Warning(ex, "{SessionMessage}", message)));
            container.RegisterInstance<IFhirValidator>(CreateFhirValidator());

            // Un HttpClient por integración, con su Timeout fijado una sola vez. Los clients
            // NO mutan estas instancias (evita InvalidOperationException).
            var fhirHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var authHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // El terminológico puede ser un server externo (más lento que el hospital local):
            // le damos bastante más aire. En arranque en frío, la PRIMERA expansión de los ECL
            // grandes de la config (<<404684003 "Clinical finding" entero, etc.) se pasaba de 8s
            // hasta que Elasticsearch calienta sus cachés, y el autocomplete del editor moría en
            // 502 justo al empezar a usarlo. Ya caliente, la misma consulta tarda ~700 ms.
            var termHttp = TerminologyHttp.Create(TimeSpan.FromSeconds(25), terminologyCertSha256);

            // Singleton con su propia factory de repositorio: pide un contexto nuevo por
            // operación y lo libera. No puede usar el del request: vive toda la aplicación y
            // persiste desde el timer, fuera de cualquier request.
            _audit = new AuditDispatcher(
                () => new AuditRepository(new ChequeARDbContext(), ownsContext: true), 1000,
                (ex, message) => Log.Error(ex, "{AuditMessage}", message));
            container.RegisterInstance(_audit);

            // Mismo criterio: contexto propio por recarga. El catálogo cambia rarísimo, así que
            // un TTL de minutos alcanza sin pegarle a la base en cada request.
            container.RegisterInstance(new SpecialtyCatalogService(
                () => new SpecialtyRepository(new ChequeARDbContext(), ownsContext: true), specialtiesCacheTtlMinutes));

            // ===== Por request: unidad de trabajo =====
            container.Register<ChequeARDbContext>(() => new ChequeARDbContext(), Lifestyle.Scoped);
            container.RegisterInstance<ISchemaMigrator>(new ChequeAR.Infrastructure.Migrations.SchemaMigrator());

            container.Register<IQuestionnaireRepository, QuestionnaireRepository>(Lifestyle.Scoped);
            container.Register<IQuestionnaireResponseRepository, QuestionnaireResponseRepository>(Lifestyle.Scoped);
            container.Register<IUserRepository, UserRepository>(Lifestyle.Scoped);
            container.Register<IRefreshTokenRepository, RefreshTokenRepository>(Lifestyle.Scoped);
            container.Register<ISavedReportRepository, SavedReportRepository>(Lifestyle.Scoped);
            container.Register<ISpecialtyRepository>(() => new SpecialtyRepository(container.GetInstance<ChequeARDbContext>(), ownsContext: false), Lifestyle.Scoped);
            container.Register<IClinicalVariableRepository, ClinicalVariableRepository>(Lifestyle.Scoped);
            // No es dueño del contexto: el del request lo libera SimpleInjector.
            container.Register<IAuditRepository>(
                () => new AuditRepository(container.GetInstance<ChequeARDbContext>(), ownsContext: false), Lifestyle.Scoped);
            container.Register<IAttachmentStore>(
                () => new FileShareAttachmentStore(attachmentsRoot, container.GetInstance<ChequeARDbContext>()), Lifestyle.Scoped);
            _orphans = new OrphanAttachmentSweeper(async cutoff =>
                {
                    using (var ctx = new ChequeARDbContext())
                        return await new FileShareAttachmentStore(attachmentsRoot, ctx).PurgeOrphansAsync(cutoff);
                },
                TimeSpan.FromMinutes(10), TimeSpan.FromHours(12),
                n => Log.Information("Adjuntos sin uso borrados: {Count}", n),
                ex => Log.Error(ex, "No se pudieron borrar los adjuntos sin uso"));

            // ===== Integraciones externas =====
            // Allowlist de vistas autorizadas, copiada a la salida (bin/Oracle/allowlist.yaml).
            var allowlistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Oracle", "allowlist.yaml");
            var allowedViews = File.Exists(allowlistPath)
                ? AllowlistLoader.Load(allowlistPath)
                : null; // Sin archivo: comportamiento fallback (sólo VW_*).
            // Sin conexion configurada no hay base del hospital que consultar. El mock de
            // spec 008 es un contenedor Oracle, que no existe en App Service, asi que alla
            // las vistas se sirven desde memoria respetando la misma allowlist.
            if (string.IsNullOrWhiteSpace(oracleConn))
            {
                Log.Warning("Oracle:ConnectionString vacio: las vistas VW_* se sirven desde "
                            + "MockOracleViewClient (datos de demostracion, no del hospital).");
                container.Register<IOracleViewClient>(() => new MockOracleViewClient(allowedViews));
            }
            else
            {
                // Sin el archivo se aceptaría cualquier VW_* de la base del hospital.
                if (allowedViews == null)
                    throw new InvalidOperationException("Falta " + allowlistPath + ": no se consultan vistas del hospital sin allowlist.");
                container.Register<IOracleViewClient>(() => new OracleViewClient(oracleConn, allowedViews));
            }

            // Sin servidor FHIR del hospital, se proyecta el mismo dataset que las vistas
            // VW_* (ver MockHospitalFhirClient): una sola fuente, dos protocolos.
            if (string.IsNullOrWhiteSpace(hospitalFhirUrl))
            {
                Log.Warning("HospitalFhir:BaseUrl vacio: se usa MockHospitalFhirClient sobre "
                            + "el dataset de las vistas (datos de demostracion).");
                container.Register<IHospitalFhirClient>(() => new MockHospitalFhirClient(container.GetInstance<IOracleViewClient>()));
            }
            else
            {
                container.Register<IHospitalFhirClient>(() => new HospitalFhirClient(fhirHttp, hospitalFhirUrl));
            }

            // Sin URL configurada no hay login del hospital que llamar: se autentica local,
            // contra AppUser.PasswordHash (ver LocalCredentialAuthenticator/PasswordHasher).
            // Cuando exista el servicio real del hospital, basta configurar la URL para que
            // ese modo tome precedencia -- el hash local queda sin uso, no hace falta borrarlo.
            if (string.IsNullOrWhiteSpace(hospitalAuthUrl))
            {
                Log.Warning("HospitalAuth:LoginUrl vacio: se usa autenticacion local contra "
                            + "AppUser.PasswordHash (LocalCredentialAuthenticator).");
                container.Register<ICredentialAuthenticator>(() => new LocalCredentialAuthenticator(container.GetInstance<IUserRepository>()));
            }
            else
            {
                container.Register<ICredentialAuthenticator>(() => new HospitalAuthClient(authHttp, hospitalAuthUrl));
            }

            container.Register<ITerminologyClient>(() => new TerminologyClient(termHttp, terminologyUrl, terminologyEcl, terminologyLang, terminologyApiKey));
            container.Register<IReportQueryRunner>(() => new OracleReportQueryRunner(sqlConn));
            // Evolución longitudinal de parámetros (spec 010): consulta dedicada contra
            // vw_FlatResponses, misma conexión Oracle que los reportes.
            container.Register<IPatientTrendRepository>(() => new PatientTrendRepository(sqlConn));

            // ===== Usuario actual y autorización =====
            // Adaptador ClaimsPrincipal -> ICurrentUser (Application no conoce System.Web).
            container.Register<ICurrentUser, Auth.HttpCurrentUser>();
            container.Register<ISpecialtyGuard>(() => new SpecialtyGuard(container.GetInstance<ICurrentUser>(), enforceSpecialtyScope));
            // Eje rol (paralelo al de especialidad): RolePolicy es la matriz explícita, no
            // depende del flag de despliegue de arriba.
            container.Register<IAccessGuard, AccessGuard>();

            // ===== Servicios de aplicación =====
            container.Register<RefreshTokenService>(() => new RefreshTokenService(container.GetInstance<IRefreshTokenRepository>(), refreshTtlHours));
            container.Register<IntegrationHealthService>(() => new IntegrationHealthService(
                container.GetInstance<IHospitalFhirClient>(), container.GetInstance<IOracleViewClient>(), hospitalAuthUrl,
                new ChequeAR.Infrastructure.Fhir.HttpProbe(), container.GetInstance<IFhirValidator>()));
            container.Register<QuestionnaireService>();
            container.Register<QuestionnaireResponseService>();
            container.Register<AuditQueryService>();
            container.Register<AccessAuditService>();
            container.Register<SavedReportService>();
            container.Register<UserService>();
            container.Register<ClinicalVariableService>();
            container.Register<TerminologyService>();
            container.Register<HomeDashboardService>();
            container.Register<QueryBuilder>();
            container.Register<ReportExecutor>();
            container.Register<PatientTrendService>();
            container.Register<PatientService>();
            container.Register<IPatientDemographicsSource, PatientService>();
            container.Register<AttachmentService>();

            // ===== Controllers =====
            container.RegisterMvcControllers(Assembly.GetExecutingAssembly());
            container.RegisterWebApiControllers(webApiConfig);

            container.Verify();
            Container = container;
            return container;
        }

        /// <summary>
        /// Instancia única: el validador lee y cachea la especificación FHIR completa (~10 MB de
        /// definiciones). Los perfiles propios se copian a la salida: bin/Fhir/Profiles.
        /// </summary>
        private static IFhirValidator CreateFhirValidator()
        {
            var profilesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fhir", "Profiles");
            // El arranque NO se puede caer por el validador: una excepción acá viaja como
            // HttpException desde Application_Start y deja TODA la aplicación en 500, con el
            // detalle sólo visible en el registro de eventos de Windows. Ya pasó una vez.
            try
            {
                var validador = new FhirValidatorService(profilesPath);
                // Calentamiento: sin esto la primera publicación del día paga la carga de
                // toda la especificación y parece que el sistema se colgó.
                validador.Warmup();
                return validador;
            }
            catch (Exception ex)
            {
                // Degradado: sin especificación ni perfiles, las reglas del dominio se siguen
                // aplicando con el conjunto de tipos por omisión.
                Log.Error(ex, "No se pudo cargar la especificación FHIR desde {Path}: validador en modo degradado.", profilesPath);
                return new FhirValidatorService();
            }
        }

        /// <summary>Vacía la cola de auditoría y libera los singletons antes de que el proceso termine (Application_End).</summary>
        public static void Shutdown()
        {
            _orphans?.Dispose();
            _audit?.Dispose();
            Container?.Dispose();
        }

        public static System.Web.Mvc.IDependencyResolver MvcResolver(Container container)
            => new SimpleInjectorDependencyResolver(container);

        public static System.Web.Http.Dependencies.IDependencyResolver WebApiResolver(Container container)
            => new SimpleInjectorWebApiDependencyResolver(container);
    }
}
