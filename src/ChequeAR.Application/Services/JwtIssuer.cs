using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChequeAR.Application.Dtos;
using ChequeAR.Domain.Enums;
using Microsoft.IdentityModel.Tokens;

namespace ChequeAR.Application.Services
{
    public class JwtIssuer
    {
        private readonly string _issuer;
        private readonly string _audience;
        private readonly string _signingSecret;
        private readonly int _accessTtlMinutes;

        public JwtIssuer(string issuer, string audience, string signingSecret, int accessTtlMinutes)
        {
            _issuer = issuer;
            _audience = audience;
            _signingSecret = signingSecret;
            _accessTtlMinutes = accessTtlMinutes;
        }

        public string IssueAccessToken(UserInfo user)
        {
            // El dato guardado es un único rol (ver Domain.Enums.Role), pero el claim de rol
            // es ACUMULATIVO: RoleHierarchy.Expand("Jefe") -> [Jefe, Medico], para que
            // [Authorize(Roles = Role.Medico)] siga significando "Medico o superior". El
            // principal (sin expandir) viaja aparte para la UI (menú, "modo lectura" del
            // editor), que necesita saber el rol real, no el más bajo que también cumple.
            var principal = RoleHierarchy.Principal(user.Roles);
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim("name", user.DisplayName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("role_principal", principal)
            };
            foreach (var r in RoleHierarchy.Expand(principal))
                claims.Add(new Claim(ClaimTypes.Role, r));
            foreach (var s in user.Specialties ?? new List<string>())
                claims.Add(new Claim("specialty", s));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(_accessTtlMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public int AccessTtlSeconds => _accessTtlMinutes * 60;
    }
}
