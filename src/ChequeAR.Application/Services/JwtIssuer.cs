using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChequeAR.Application.Dtos;
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
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim("name", user.DisplayName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };
            foreach (var r in user.Roles ?? new List<string>())
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
