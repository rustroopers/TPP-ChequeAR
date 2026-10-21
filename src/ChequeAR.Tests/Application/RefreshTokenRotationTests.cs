using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class RefreshTokenRotationTests
    {
        private readonly FakeClock _clock = new FakeClock(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc));
        private readonly Dictionary<string, RefreshToken> _porHash = new Dictionary<string, RefreshToken>();
        private readonly Mock<IRefreshTokenRepository> _repo = new Mock<IRefreshTokenRepository>();
        private readonly RefreshTokenService _sut;

        public RefreshTokenRotationTests()
        {
            _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>())).Callback<RefreshToken>(t => _porHash[t.TokenHash] = t).Returns(Task.CompletedTask);
            _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync((string h) => _porHash.TryGetValue(h, out var t) ? t : null);
            _repo.Setup(r => r.RevokeAsync(It.IsAny<string>())).Callback<string>(h => _porHash[h].RevokedAt = _clock.UtcNow).Returns(Task.CompletedTask);
            _repo.Setup(r => r.RevokeAllForUserAsync(It.IsAny<string>(), It.IsAny<DateTime>()))
                .Callback<string, DateTime>((u, now) => { foreach (var t in _porHash.Values.Where(x => x.UserId == u && x.RevokedAt == null)) t.RevokedAt = now; })
                .Returns(Task.CompletedTask);
            _sut = new RefreshTokenService(_repo.Object, ttlHours: 8, clock: _clock);
        }

        [Fact]
        public async Task Rotar_DevuelveUnoNuevo_YElViejoDejaDeServir()
        {
            var viejo = await _sut.IssueAsync("m1", "ua", "ip");

            var rotacion = await _sut.RotateAsync(viejo, "ua", "ip");

            Assert.Equal("m1", rotacion.UserId);
            Assert.NotEqual(viejo, rotacion.RefreshToken);
            Assert.Null(await _sut.ValidateAsync(viejo));
            Assert.NotNull(await _sut.ValidateAsync(rotacion.RefreshToken));
        }

        [Fact]
        public async Task Rotar_ConservaElVencimientoOriginal()
        {
            var primero = await _sut.IssueAsync("m1", "ua", "ip");
            var vence = _porHash.Values.Single().ExpiresAt;
            _clock.Advance(TimeSpan.FromHours(3));

            var rotacion = await _sut.RotateAsync(primero, "ua", "ip");

            Assert.Equal(vence, (await _sut.ValidateAsync(rotacion.RefreshToken)).ExpiresAt);
        }

        [Fact]
        public async Task ReusarUnoYaCanjeado_RevocaTodosLosDelUsuario()
        {
            var robado = await _sut.IssueAsync("m1", "ua", "ip");
            var legitimo = (await _sut.RotateAsync(robado, "ua", "ip")).RefreshToken;

            Assert.Null(await _sut.RotateAsync(robado, "otro-ua", "otra-ip"));

            Assert.Null(await _sut.ValidateAsync(legitimo));
            _repo.Verify(r => r.RevokeAllForUserAsync("m1", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task Rotar_Vencido_DevuelveNullSinRevocarNada()
        {
            var raw = await _sut.IssueAsync("m1", "ua", "ip");
            _clock.Advance(TimeSpan.FromHours(8));

            Assert.Null(await _sut.RotateAsync(raw, "ua", "ip"));
            _repo.Verify(r => r.RevokeAllForUserAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }
    }
}
