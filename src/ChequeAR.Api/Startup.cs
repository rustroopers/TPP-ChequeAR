using System.Configuration;
using System.Text;
using ChequeAR.Api.Middleware;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Security.Jwt;
using Owin;

[assembly: OwinStartup(typeof(ChequeAR.Api.Startup))]

namespace ChequeAR.Api
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var issuer = ConfigurationManager.AppSettings["Jwt:Issuer"] ?? "https://chequear.garrahan";
            var audience = ConfigurationManager.AppSettings["Jwt:Audience"] ?? "chequear-api";
            var secret = ConfigurationManager.AppSettings["Jwt:SigningSecret"] ?? "dev-secret-please-rotate-dev-secret-please-rotate";

            app.UseJwtBearerAuthentication(new JwtBearerAuthenticationOptions
            {
                AuthenticationMode = Microsoft.Owin.Security.AuthenticationMode.Active,
                TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = System.TimeSpan.FromSeconds(30)
                }
            });

            app.Use<AuditMiddleware>();
        }
    }
}
