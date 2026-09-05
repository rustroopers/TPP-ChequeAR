using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Web;
using System.Web.Security;
using ChequeAR.Application.Dtos;
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
        }

        public static void Issue(HttpContextBase context, UserInfo user, bool persistent)
        {
            if (context == null || user == null) return;

            var payload = JsonConvert.SerializeObject(new Payload
            {
                DisplayName = user.DisplayName,
                Roles = user.Roles ?? new List<string>(),
                Specialties = user.Specialties ?? new List<string>()
            });

            var timeout = FormsAuthentication.Timeout;
            var ticket = new FormsAuthenticationTicket(
                version: 2,
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
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, ticket.Name),
                new Claim("sub", ticket.Name),
                new Claim("name", payload.DisplayName ?? ticket.Name)
            };
            claims.AddRange((payload.Roles ?? new List<string>()).Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange((payload.Specialties ?? new List<string>()).Select(s => new Claim("specialty", s)));

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
                Specialties = CurrentUser.Specialties(principal).ToList()
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
