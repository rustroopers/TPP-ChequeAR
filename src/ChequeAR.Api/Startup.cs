using ChequeAR.Api.App_Start;
using ChequeAR.Api.Auth;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Security.Jwt;
using Microsoft.Owin.Security.OAuth;
using Owin;

[assembly: OwinStartup(typeof(ChequeAR.Api.Startup))]

namespace ChequeAR.Api
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseJwtBearerAuthentication(new JwtBearerAuthenticationOptions
            {
                AuthenticationMode = Microsoft.Owin.Security.AuthenticationMode.Active,
                // Mismo criterio que la cookie: el JWT se revalida contra la base.
                Provider = new OAuthBearerAuthenticationProvider
                {
                    OnValidateIdentity = async context =>
                    {
                        if (!await SessionRevalidation.IsStillValidAsync(context.Ticket?.Identity).ConfigureAwait(false))
                            context.Rejected();
                    }
                },
                TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = JwtSettings.Issuer,
                    ValidAudience = JwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(JwtSettings.SigningKey),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = System.TimeSpan.FromSeconds(30)
                }
            });
        }
    }
}
