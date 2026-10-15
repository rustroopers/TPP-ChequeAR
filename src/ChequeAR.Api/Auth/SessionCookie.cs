using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Web;
using System.Web.Security;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;

namespace ChequeAR.Api.Auth
{
    /// Puente entre el login MVC (cookie) y la API (Bearer). FormsAuthentication.SetAuthCookie
    /// guarda SOLO el nombre de usuario, con lo cual los roles que devuelve el hospital se
    /// perdian apenas terminaba DoLogin y no habia forma de firmar un token honesto en el
    /// render siguiente. Aca el ticket se arma a mano para poder llevar los roles en UserData.
    ///
    /// UserData viaja dentro del ticket cifrado y firmado (protection="All" en Web.config),
    /// asi que el cliente no lo puede leer ni modificar.
    public static class SessionCookie
    {
        private class Payload
        {
            public string DisplayName { get; set; }
            public List<string> Roles { get; set; }
            public List<string> Specialties { get; set; }
            public string Stamp { get; set; }
        }

        public static void Issue(HttpContextBase context, UserInfo user, bool persistent)
        {
            if (context == null || user == null) return;

            // Un único rol (ver Domain.Enums.Role/RoleHierarchy): aunque `user.Roles` traiga
            // más de uno (dato heredado, o un sync a mitad de migrar), acá se colapsa al más
            // alto ANTES de guardarlo. ToPrincipal es quien lo vuelve a expandir para los
            // claims -- el mismo patrón que JwtIssuer.
            var payload = JsonConvert.SerializeObject(new Payload
            {
                DisplayName = user.DisplayName,
                Roles = new List<string> { RoleHierarchy.Principal(user.Roles) },
                Specialties = user.Specialties ?? new List<string>(),
                Stamp = user.SecurityStamp
            });

            var timeout = FormsAuthentication.Timeout;
            var ticket = new FormsAuthenticationTicket(
                // 3, no 2: marca que este ticket ya asume el modelo de rol único (Observador
                // eliminado). No hay chequeo automático de versión -un ticket viejo con
                // version=2 se sigue aceptando-, pero da igual: ToPrincipal degrada
                // cualquier rol desconocido o heredado a Medico (RoleHierarchy.Principal),
                // así que un ticket de antes de esta feature nunca queda con más privilegio
                // del que debería, sin necesidad de forzar un re-login.
                version: 3,
                name: user.Id,
                issueDate: DateTime.Now,
                expiration: DateTime.Now.Add(timeout),
                isPersistent: persistent,
                userData: payload,
                cookiePath: FormsAuthentication.FormsCookiePath);

            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true,
                Secure = FormsAuthentication.RequireSSL,
                Path = FormsAuthentication.FormsCookiePath
            };
            // Solo las persistentes llevan Expires: sin el atributo, la cookie muere
            // al cerrar el navegador, que es lo que se espera de una sesion clinica.
            if (persistent) cookie.Expires = ticket.Expiration;

            context.Response.Cookies.Remove(FormsAuthentication.FormsCookieName);
            context.Response.Cookies.Add(cookie);
        }

        /// Reconstruye el principal con los roles del ticket. El FormsAuthenticationModule
        /// por si solo arma un principal sin roles (UserData lo ignora), asi que sin esto
        /// User.IsInRole(...) daria false para todo el mundo.
        public static ClaimsPrincipal ToPrincipal(FormsAuthenticationTicket ticket)
        {
            if (ticket == null) return null;

            var payload = Deserialize(ticket.UserData);
            // Igual criterio que JwtIssuer: el dato es un único rol, pero el claim de rol es
            // ACUMULATIVO (Jefe -> Jefe+Medico), y un ticket heredado con un rol desconocido
            // (p. ej. "Observador") degrada a Medico en vez de perder el principal.
            var principal = RoleHierarchy.Principal(payload.Roles);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, ticket.Name),
                // NameIdentifier no es decorativo: @Html.AntiForgeryToken() exige este claim
                // (o el de identityprovider de ACS) en cualquier identidad claims-based
                // autenticada, y sin el tira InvalidOperationException al renderizar el
                // token, tumbando toda vista con formulario. Ver AntiForgeryConfig.
                new Claim(ClaimTypes.NameIdentifier, ticket.Name),
                new Claim("sub", ticket.Name),
                new Claim("name", payload.DisplayName ?? ticket.Name),
                new Claim("role_principal", principal)
            };
            claims.AddRange(RoleHierarchy.Expand(principal).Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange((payload.Specialties ?? new List<string>()).Select(s => new Claim("specialty", s)));
            if (!string.IsNullOrEmpty(payload.Stamp))
                claims.Add(new Claim(JwtIssuer.SecurityStampClaim, payload.Stamp));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Forms", ClaimTypes.Name, ClaimTypes.Role));
        }

        public static UserInfo ToUserInfo(ClaimsPrincipal principal)
        {
            if (principal == null || principal.Identity == null || !principal.Identity.IsAuthenticated) return null;

            return new UserInfo
            {
                Id = CurrentUser.Id(principal) ?? principal.Identity.Name,
                DisplayName = principal.FindFirst("name")?.Value ?? principal.Identity.Name,
                Roles = CurrentUser.Roles(principal).ToList(),
                Specialties = CurrentUser.Specialties(principal).ToList(),
                SecurityStamp = principal.FindFirst(JwtIssuer.SecurityStampClaim)?.Value
            };
        }

        private static Payload Deserialize(string userData)
        {
            // Un ticket viejo (emitido por SetAuthCookie, sin UserData) o corrupto no
            // deberia tirar abajo la request: degrada a sesion sin roles, y la API
            // responde 403 en vez de 500.
            if (string.IsNullOrEmpty(userData)) return new Payload();
            try
            {
                return JsonConvert.DeserializeObject<Payload>(userData) ?? new Payload();
            }
            catch (JsonException)
            {
                return new Payload();
            }
        }
    }
}
