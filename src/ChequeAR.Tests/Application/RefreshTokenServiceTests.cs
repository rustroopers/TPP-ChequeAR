using System;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// Emision/validacion/revocacion de refresh tokens. No tenia ninguna prueba pese a
    /// manejar la sesion de login: ninguno de estos casos (revocado, vencido, hash
    /// compartido entre validar y revocar) estaba verificado.
    /// </summary>
    public class RefreshTokenServiceTests
    {
        private readonly Mock<IRefreshTokenRepository> _repo = new Mock<IRefreshTokenRepository>();
        private readonly RefreshTokenService _sut;

        public RefreshTokenServiceTests()
        {
            _sut = new RefreshTokenService(_repo.Object, ttlHours: 8);
        }

        [Fact]
        public async Task IssueAsync_PersisteElTokenConDatosDeSesion()
        {
            RefreshToken guardado = null;
            _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>()))
                 .Callback<RefreshToken>(t => guardado = t)
                 .Returns(Task.CompletedTask);

            var raw = await _sut.IssueAsync("dr.gomez", "Mozilla/5.0", "10.0.0.1");

            Assert.NotNull(guardado);
            Assert.Equal("dr.gomez", guardado.UserId);
            Assert.Equal("Mozilla/5.0", guardado.UserAgent);
            Assert.Equal("10.0.0.1", guardado.Ip);
            Assert.NotEqual(Guid.Empty, guardado.Id);
            Assert.True(guardado.ExpiresAt > guardado.IssuedAt);
            // El hash guardado no es el token crudo devuelto al cliente: si se filtra la
            // base, nadie puede reconstruir un refresh token valido a partir del hash.
            Assert.NotEqual(raw, guardado.TokenHash);
            Assert.NotEmpty(raw);
        }

        [Fact]
        public async Task ValidateAsync_VenceAlCumplirseElTtl()
        {
            var clock = new FakeClock(new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
            var sut = new RefreshTokenService(_repo.Object, ttlHours: 8, clock: clock);
            RefreshToken guardado = null;
            _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>())).Callback<RefreshToken>(t => guardado = t).Returns(Task.CompletedTask);
            var raw = await sut.IssueAsync("dr.gomez", "ua", "ip");
            _repo.Setup(r => r.GetByHashAsync(guardado.TokenHash)).ReturnsAsync(() => guardado);

            clock.Advance(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1));
            Assert.NotNull(await sut.ValidateAsync(raw));
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Null(await sut.ValidateAsync(raw));
        }

        [Fact]
        public async Task IssueAsync_RespetaElTtlConfigurado()
        {
            var sut = new RefreshTokenService(_repo.Object, ttlHours: 1);
            RefreshToken guardado = null;
            _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>()))
                 .Callback<RefreshToken>(t => guardado = t)
                 .Returns(Task.CompletedTask);

            await sut.IssueAsync("dr.gomez", "ua", "ip");

            var vidaUtil = guardado.ExpiresAt - guardado.IssuedAt;
            Assert.True(Math.Abs((vidaUtil - TimeSpan.FromHours(1)).TotalSeconds) < 1);
        }

        // Sin limpieza, la tabla crecía con cada login.
        [Fact]
        public async Task Issue_PurgaLosVencidos()
        {
            await _sut.IssueAsync("u1", "ua", "1.1.1.1");

            _repo.Verify(r => r.PurgeExpiredAsync(It.Is<DateTime>(d => d <= DateTime.UtcNow && d > DateTime.UtcNow.AddMinutes(-1))), Times.Once);
        }

        [Fact]
        public async Task ValidateAsync_TokenActivo_LoDevuelve()
        {
            var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddHours(1) };
            _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(token);

            var resultado = await _sut.ValidateAsync("cualquier-raw");

            Assert.Same(token, resultado);
        }

        [Fact]
        public async Task ValidateAsync_TokenInexistente_DevuelveNull()
        {
            _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync((RefreshToken)null);

            Assert.Null(await _sut.ValidateAsync("no-existe"));
        }

        [Fact]
        public async Task ValidateAsync_TokenRevocado_DevuelveNullAunqueNoHayaExpirado()
        {
            var token = new RefreshToken
            {
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                RevokedAt = DateTime.UtcNow.AddMinutes(-1)
            };
            _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(token);

            Assert.Null(await _sut.ValidateAsync("revocado"));
        }

        [Fact]
        public async Task ValidateAsync_TokenExpirado_DevuelveNull()
        {
            var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddSeconds(-1) };
            _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>())).ReturnsAsync(token);

            Assert.Null(await _sut.ValidateAsync("vencido"));
        }

        [Fact]
        public async Task RevokeAsync_UsaElMismoHashQueValidateAsync()
        {
            // Emitir/validar/revocar con el mismo raw debe pegarle al mismo hash: es la unica
            // forma en que el server encuentra o tumba el registro correcto sin guardar el
            // token en texto plano.
            string hashValidado = null;
            string hashRevocado = null;
            _repo.Setup(r => r.GetByHashAsync(It.IsAny<string>()))
                 .Callback<string>(h => hashValidado = h)
                 .ReturnsAsync((RefreshToken)null);
            _repo.Setup(r => r.RevokeAsync(It.IsAny<string>()))
                 .Callback<string>(h => hashRevocado = h)
                 .Returns(Task.CompletedTask);

            await _sut.ValidateAsync("mismo-raw");
            await _sut.RevokeAsync("mismo-raw");

            Assert.Equal(hashValidado, hashRevocado);
        }
    }
}
