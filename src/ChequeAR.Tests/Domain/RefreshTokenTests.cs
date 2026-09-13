using System;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    public class RefreshTokenTests
    {
        [Fact]
        public void IsActive_SinRevocarYSinExpirar_ReturnsTrue()
        {
            var t = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddHours(1) };

            Assert.True(t.IsActive());
        }

        [Fact]
        public void IsActive_Revocado_ReturnsFalse()
        {
            var t = new RefreshToken
            {
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                RevokedAt = DateTime.UtcNow
            };

            Assert.False(t.IsActive());
        }

        [Fact]
        public void IsActive_Expirado_ReturnsFalse()
        {
            var t = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddSeconds(-1) };

            Assert.False(t.IsActive());
        }

        // ExpiresAt > UtcNow es estricto: el instante exacto de vencimiento ya no es valido.
        [Fact]
        public void IsActive_ExpiraJustoAhora_ReturnsFalse()
        {
            var t = new RefreshToken { ExpiresAt = DateTime.UtcNow };

            Assert.False(t.IsActive());
        }
    }
}
