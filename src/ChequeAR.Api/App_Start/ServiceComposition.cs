using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Http.Dependencies;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Reports;
using ChequeAR.Application.Services;
using ChequeAR.Infrastructure.Auth;
using ChequeAR.Infrastructure.EF;
using ChequeAR.Infrastructure.Fhir;
using ChequeAR.Infrastructure.Oracle;
using ChequeAR.Infrastructure.Repositories;
using ChequeAR.Infrastructure.Storage;

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
        private static System.Net.Http.HttpClient _fhirHttp;
        private static System.Net.Http.HttpClient _authHttp;

        public static void Configure()
        {
            // ===== Config =====
            var jwtIssuer = ConfigurationManager.AppSettings["Jwt:Issuer"] ?? "https://chequear.garrahan";
            var jwtAudience = ConfigurationManager.AppSettings["Jwt:Audience"] ?? "chequear-api";
            var jwtSecret = ConfigurationManager.AppSettings["Jwt:SigningSecret"] ?? "dev-secret-please-rotate-dev-secret-please-rotate";
            var jwtTtlMinutes = int.Parse(ConfigurationManager.AppSettings["Jwt:AccessTtlMinutes"] ?? "60");
            var refreshTtlHours = int.Parse(ConfigurationManager.AppSettings["Jwt:RefreshTtlHours"] ?? "8");

            var hospitalAuthUrl = ConfigurationManager.AppSettings["HospitalAuth:LoginUrl"] ?? "";
            var hospitalFhirUrl = ConfigurationManager.AppSettings["HospitalFhir:BaseUrl"] ?? "";
            var oracleConn = ConfigurationManager.AppSettings["Oracle:ConnectionString"] ?? "";
            var attachmentsRoot = ConfigurationManager.AppSettings["Attachments:Root"] ?? @"C:\ChequeAR\Attachments";
            var sqlConn = ConfigurationManager.ConnectionStrings["ChequeAR"]?.ConnectionString ?? "";

            // ===== Singletons =====
            _jwt = new JwtIssuer(jwtIssuer, jwtAudience, jwtSecret, jwtTtlMinutes);
            // Un HttpClient singleton por integración, con su Timeout fijado una sola vez.
            // Los clients NO mutan estas instancias (evita InvalidOperationException).
            _fhirHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _authHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            // AuditDispatcher requiere repo; usamos una factory que crea su propio contexto.
            _audit = new AuditDispatcher(NewAuditRepository(), 1000);

            // ===== Factories =====
            Register<ChequeARDbContext>(() => new ChequeARDbContext());

            Register<IQuestionnaireRepository>(() => new QuestionnaireRepository(Resolve<ChequeARDbContext>()));
            Register<IQuestionnaireResponseRepository>(() => new QuestionnaireResponseRepository(Resolve<ChequeARDbContext>()));
            Register<IAuditRepository>(() => new AuditRepository(Resolve<ChequeARDbContext>()));
            Register<IUserRepository>(() => new UserRepository(Resolve<ChequeARDbContext>()));
            Register<IRefreshTokenRepository>(() => new RefreshTokenRepository(Resolve<ChequeARDbContext>()));
            Register<ISavedReportRepository>(() => new SavedReportRepository(Resolve<ChequeARDbContext>()));

            Register<IHospitalFhirClient>(() => new HospitalFhirClient(_fhirHttp, hospitalFhirUrl));
            Register<IHospitalAuthClient>(() => new HospitalAuthClient(_authHttp, hospitalAuthUrl));

            // Allowlist de vistas autorizadas. Se carga una sola vez al arrancar la app.
            // El archivo se copia a la salida con CopyToOutputDirectory=PreserveNewest;
            // la ubicación final en runtime es bin/Oracle/allowlist.yaml junto al ensamblado.
            var allowlistPath = Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory, "Oracle", "allowlist.yaml");
            var allowedViews = File.Exists(allowlistPath)
                ? AllowlistLoader.Load(allowlistPath)
                : null; // En tests sin archivo: comportamiento fallback (sólo VW_*).
            Register<IOracleViewClient>(() => new OracleViewClient(oracleConn, allowedViews));
            Register<IFhirValidator>(() => new FhirValidatorService());
            Register<IAttachmentStore>(() => new FileShareAttachmentStore(attachmentsRoot));

            Register<JwtIssuer>(() => _jwt);
            Register<AuditDispatcher>(() => _audit);
            Register<RefreshTokenService>(() => new RefreshTokenService(Resolve<IRefreshTokenRepository>(), refreshTtlHours));

            Register<QuestionnaireService>(() => new QuestionnaireService(
                Resolve<IQuestionnaireRepository>(), Resolve<IQuestionnaireResponseRepository>(),
                Resolve<IFhirValidator>(), _audit));
            Register<QuestionnaireResponseService>(() => new QuestionnaireResponseService(
                Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>(),
                Resolve<IFhirValidator>(), _audit));
            Register<PrefillService>(() => new PrefillService(Resolve<IHospitalFhirClient>(), Resolve<IOracleViewClient>()));
            Register<QueryBuilder>(() => new QueryBuilder());
            Register<ReportExecutor>(() => new ReportExecutor(sqlConn, Resolve<QueryBuilder>(), Resolve<IQuestionnaireRepository>()));

            Register<IntegrationHealthService>(() => new IntegrationHealthService(
                Resolve<IHospitalFhirClient>(), Resolve<IOracleViewClient>(), hospitalAuthUrl));
            Register<PatientService>(() => new PatientService(
                Resolve<IHospitalFhirClient>(), Resolve<IOracleViewClient>(),
                Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>()));

            // Controllers de Web API: SimpleInjector los registra automáticamente;
            // nosotros declaramos los pocos que tenemos.
            Register<Controllers.Api.QuestionnaireController>(() => new Controllers.Api.QuestionnaireController(
                Resolve<IQuestionnaireRepository>(), Resolve<QuestionnaireService>()));
            Register<Controllers.Api.QuestionnaireResponseController>(() => new Controllers.Api.QuestionnaireResponseController(
                Resolve<IQuestionnaireResponseRepository>(), Resolve<QuestionnaireResponseService>()));
            Register<Controllers.Api.AuthController>(() => new Controllers.Api.AuthController(
                Resolve<IHospitalAuthClient>(), Resolve<JwtIssuer>(), Resolve<RefreshTokenService>()));
            Register<Controllers.Api.AuditEventController>(() => new Controllers.Api.AuditEventController(Resolve<IAuditRepository>()));
            Register<Controllers.Api.AttachmentController>(() => new Controllers.Api.AttachmentController(Resolve<IAttachmentStore>()));
            Register<Controllers.Api.ReportsController>(() => new Controllers.Api.ReportsController(Resolve<ReportExecutor>(), Resolve<AuditDispatcher>()));
            Register<Controllers.Api.MetadataController>(() => new Controllers.Api.MetadataController());
            Register<Controllers.Api.PatientController>(() => new Controllers.Api.PatientController(Resolve<PatientService>()));
            Register<Controllers.Api.UserController>(() => new Controllers.Api.UserController(Resolve<IUserRepository>()));
            Register<Controllers.Api.IntegrationStatusController>(() => new Controllers.Api.IntegrationStatusController(Resolve<IntegrationHealthService>()));
            Register<Controllers.Api.SavedReportController>(() => new Controllers.Api.SavedReportController(Resolve<ISavedReportRepository>(), Resolve<IQuestionnaireRepository>()));

            // Controllers MVC
            Register<Controllers.Mvc.HomeController>(() => new Controllers.Mvc.HomeController(
                Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>(), Resolve<IUserRepository>()));
            Register<Controllers.Mvc.AccountController>(() => new Controllers.Mvc.AccountController(
                Resolve<IHospitalAuthClient>(), Resolve<IUserRepository>()));
            Register<Controllers.Mvc.QuestionnairesController>(() => new Controllers.Mvc.QuestionnairesController(
                Resolve<IQuestionnaireRepository>(), Resolve<QuestionnaireService>()));
            Register<Controllers.Mvc.EncountersController>(() => new Controllers.Mvc.EncountersController(
                Resolve<PatientService>(), Resolve<IQuestionnaireResponseRepository>(), Resolve<IQuestionnaireRepository>()));
            Register<Controllers.Mvc.ReportsController>(() => new Controllers.Mvc.ReportsController(
                Resolve<ISavedReportRepository>(), Resolve<ReportExecutor>(), Resolve<IQuestionnaireRepository>()));
            Register<Controllers.Mvc.AuditController>(() => new Controllers.Mvc.AuditController(Resolve<IAuditRepository>()));
            Register<Controllers.Mvc.UsersController>(() => new Controllers.Mvc.UsersController(Resolve<IUserRepository>()));
            Register<Controllers.Mvc.AdminController>(() => new Controllers.Mvc.AdminController(Resolve<IntegrationHealthService>()));
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
