using System.Threading.Tasks;
using Microsoft.Owin;

namespace ChequeAR.Api.Middleware
{
    public class AuditMiddleware : OwinMiddleware
    {
        public AuditMiddleware(OwinMiddleware next) : base(next) { }

        public override async Task Invoke(IOwinContext context)
        {
            // En MVP: pasar request, persistencia de AuditEvent es delegada a los services.
            // Acá se podría capturar correlation id, IP, user agent.
            context.Set("audit.ip", context.Request.RemoteIpAddress);
            await Next.Invoke(context);
        }
    }
}
