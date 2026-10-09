using System;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    /// <summary>Reglas del lock de edición de una ficha.</summary>
    public class QuestionnaireResponseLockTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 10, 6, 13, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

        [Fact]
        public void SinLock_EstaLibre_YCualquieraPuedeTomarlo()
        {
            var r = new QuestionnaireResponse();
            Assert.True(r.IsLockFree(Ahora));
            Assert.True(r.CanBeLockedBy("dr.gomez", Ahora));
        }

        [Fact]
        public void LockVigenteDeOtro_NoSePuedeTomar()
        {
            var r = new QuestionnaireResponse();
            r.AcquireLock("dra.perez", Ahora, Ttl);

            Assert.False(r.CanBeLockedBy("dr.gomez", Ahora.AddMinutes(5)));
            Assert.True(r.CanBeLockedBy("dra.perez", Ahora.AddMinutes(5)));
        }

        [Fact]
        public void LockVencido_QuedaLibreParaOtro()
        {
            var r = new QuestionnaireResponse();
            r.AcquireLock("dra.perez", Ahora, Ttl);

            Assert.True(r.CanBeLockedBy("dr.gomez", Ahora.Add(Ttl).AddSeconds(1)));
        }

        [Fact]
        public void AcquireLock_GeneraTokenNuevoCadaVez()
        {
            var r = new QuestionnaireResponse();
            r.AcquireLock("dr.gomez", Ahora, Ttl);
            var primero = r.LockToken;
            r.AcquireLock("dr.gomez", Ahora, Ttl);

            Assert.NotEqual(primero, r.LockToken);
            Assert.Equal(Ahora.Add(Ttl), r.LockExpiresAt);
        }

        /// <summary>
        /// Período de gracia: el dueño puede seguir escribiendo con su token aunque el lock haya
        /// vencido, mientras nadie más lo haya tomado.
        /// </summary>
        [Fact]
        public void IsLockHeldBy_ExigeMismoTokenYUsuario_SinImportarElVencimiento()
        {
            var r = new QuestionnaireResponse();
            r.AcquireLock("dr.gomez", Ahora, Ttl);
            var token = r.LockToken.Value;
            r.LockExpiresAt = Ahora.AddMinutes(-1);

            Assert.True(r.IsLockHeldBy("dr.gomez", token));
            Assert.False(r.IsLockHeldBy("dra.perez", token));
            Assert.False(r.IsLockHeldBy("dr.gomez", Guid.NewGuid()));
        }

        [Fact]
        public void ReleaseLock_LimpiaLaSesion()
        {
            var r = new QuestionnaireResponse();
            r.AcquireLock("dr.gomez", Ahora, Ttl);
            var token = r.LockToken.Value;

            r.ReleaseLock();

            Assert.True(r.IsLockFree(Ahora));
            Assert.Null(r.LockedByUserId);
            Assert.False(r.IsLockHeldBy("dr.gomez", token));
        }

        [Fact]
        public void RenewLock_ExtiendeElVencimiento()
        {
            var r = new QuestionnaireResponse();
            r.AcquireLock("dr.gomez", Ahora, Ttl);

            r.RenewLock(Ahora.AddMinutes(10), Ttl);

            Assert.Equal(Ahora.AddMinutes(10).Add(Ttl), r.LockExpiresAt);
        }
    }
}
