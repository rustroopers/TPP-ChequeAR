using System;
using System.Net;
using System.Net.Http;
using System.Web.Http.Filters;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;

namespace ChequeAR.Api.Filters
{
    /// <summary>
    /// Traducción única, global, de las excepciones de Application a respuestas HTTP de la
    /// API. Antes cada acción repetía su propio try/catch con el mismo mapeo (y con cuerpos
    /// distintos según el controller).
    ///
    /// Sólo se mapean las excepciones propias de Application, cuyo mensaje está pensado para
    /// el usuario. Una InvalidOperationException o ArgumentException cualquiera puede ser un
    /// bug: no se toca, sigue siendo un 500 genérico sin detalles hacia la red, y
    /// WebApiExceptionLogger la deja en el log.
    ///
    /// Una acción puede seguir capturando una excepción cuando para ESE endpoint significa
    /// otra cosa: las lecturas traducen SpecialtyForbiddenException a 404, para no confirmar
    /// que existe un recurso de otra especialidad.
    /// </summary>
    public class ApiExceptionFilter : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext context)
        {
            var response = ToResponse(context.Request, context.Exception);
            if (response != null) context.Response = response;
        }

        /// <summary>El 409 de lock necesita ir a la base para mostrar el nombre en vez del id.</summary>
        public override async System.Threading.Tasks.Task OnExceptionAsync(HttpActionExecutedContext context,
            System.Threading.CancellationToken cancellationToken)
        {
            if (context.Exception is LockConflictException l)
            {
                var name = l.LockedByUserId;
                try
                {
                    var users = context.Request.GetDependencyScope()?.GetService(typeof(UserService)) as UserService;
                    var user = users == null || string.IsNullOrEmpty(l.LockedByUserId) ? null : await users.GetAsync(l.LockedByUserId);
                    if (!string.IsNullOrWhiteSpace(user?.DisplayName)) name = user.DisplayName;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "No se pudo leer el nombre de quien tiene el lock de la ficha");
                }
                context.Response = context.Request.CreateResponse(HttpStatusCode.Conflict,
                    OperationOutcomes.LockConflict(l.Message, name, l.LockExpiresAt));
                return;
            }
            OnException(context);
        }

        /// <summary>
        /// Es un error del cliente (4xx) y no una falla del sistema. WebApiExceptionLogger lo usa
        /// para no registrar como error una regla de negocio que rechazó un pedido.
        /// </summary>
        public static bool IsClientError(Exception ex)
        {
            var status = StatusFor(ex);
            return status.HasValue && status.Value < 500;
        }

        public static int? StatusFor(Exception ex)
        {
            switch (ex)
            {
                case PermissionForbiddenException _: return 403;
                case SpecialtyForbiddenException _: return 403;
                case NotFoundException _: return 404;
                case ValidationFailedException _: return 422;
                case LockConflictException _: return 409;
                case ConcurrencyConflictException _: return 409;
                case BusinessRuleException _: return 422;
                case TerminologyException t: return t.StatusCode;
                default: return null;
            }
        }

        public static HttpResponseMessage ToResponse(HttpRequestMessage request, Exception ex)
        {
            var status = StatusFor(ex);
            if (status == null) return null;
            var code = (HttpStatusCode)status.Value;
            switch (ex)
            {
                case PermissionForbiddenException _:
                    return request.CreateResponse(code, OperationOutcomes.Forbidden());

                case SpecialtyForbiddenException _:
                    return request.CreateResponse(code,
                        OperationOutcomes.Forbidden("No tiene acceso a la especialidad de ese cuestionario."));

                case NotFoundException nf:
                    return request.CreateResponse(code, OperationOutcomes.NotFound(nf.Message));

                // Un issue por regla incumplida: con un mensaje concatenado, quien corrige
                // arregla la primera y recién al reintentar descubre la segunda.
                case ValidationFailedException v:
                    return request.CreateResponse(code, OperationOutcomes.ForValidationErrors(v.Errors));

                // Única respuesta que no es un OperationOutcome: FHIR no tiene dónde expresar
                // quién tiene el lock ni hasta cuándo, y el renderer necesita los dos datos
                // para mostrar "lo está completando X hasta las HH:mm".
                case LockConflictException l:
                    return request.CreateResponse(code,
                        OperationOutcomes.LockConflict(l.Message, l.LockedByUserId, l.LockExpiresAt));

                case ConcurrencyConflictException c:
                    return request.CreateResponse(code,
                        OperationOutcomes.Conflict(c.ExpectedVersion, c.ActualVersion));

                case BusinessRuleException br:
                    return request.CreateResponse(code, OperationOutcomes.Unprocessable(br.Message));

                case TerminologyException t:
                    return request.CreateResponse(code, OperationOutcomes.Transient(t.Message, t.RetryAfterSeconds));

                default:
                    return null;
            }
        }
    }
}
