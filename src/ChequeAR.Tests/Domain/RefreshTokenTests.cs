using System;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    public class RefreshTokenTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void IsActive_SinRevocarYSinExpirar_ReturnsTrue()
        {
            var t = new RefreshToken { ExpiresAt = Ahora.AddHours(1) };

            Assert.True(t.IsActive(Ahora));
        }

        [Fact]
        public void IsActive_Revocado_ReturnsFalse()
        {
            var t = new RefreshToken
            {
                ExpiresAt = Ahora.AddHours(1),
                RevokedAt = Ahora
            };

            Assert.False(t.IsActive(Ahora));
        }

        [Fact]
        public void IsActive_Expirado_ReturnsFalse()
        {
            var t = new RefreshToken { ExpiresAt = Ahora.AddSeconds(-1) };

            Assert.False(t.IsActive(Ahora));
        }

        // ExpiresAt > ahora es estricto: el instante exacto de vencimiento ya no es valido.
        [Fact]
        public void IsActive_ExpiraJustoAhora_ReturnsFalse()
        {
            var t = new RefreshToken { ExpiresAt = Ahora };

            Assert.False(t.IsActive(Ahora));
        }
    }
}
