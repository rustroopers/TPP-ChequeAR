using System;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class LoginLockoutTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData(0, 1, false)]
        [InlineData(3, 4, false)]
        [InlineData(4, 5, true)]
        [InlineData(5, 6, false)]
        public void AfterFailure_BloqueaRecienAlLlegarAlUmbral(int antes, int despues, bool bloquea)
        {
            var (count, until) = LoginLockoutPolicy.AfterFailure(antes, Ahora);

            Assert.Equal(despues, count);
            Assert.Equal(bloquea, until.HasValue);
        }

        [Theory]
        [InlineData(4, 5)]
        [InlineData(9, 10)]
        [InlineData(14, 20)]
        [InlineData(19, 40)]
        [InlineData(24, 60)]
        [InlineData(99, 60)]
        public void AfterFailure_ElBloqueoSeDuplicaConTope(int antes, int minutos)
        {
            var (_, until) = LoginLockoutPolicy.AfterFailure(antes, Ahora);

            Assert.Equal(Ahora.AddMinutes(minutos), until);
        }

        private static User Usuario(string password, int fallos = 0, DateTime? hasta = null)
        {
            var u = new User
            {
                Id = "u1", DisplayName = "U1", IsActive = true, PasswordHash = PasswordHasher.Hash(password),
                FailedLoginCount = fallos, LockoutUntil = hasta
            };
            u.Roles.Add(new UserRole { UserId = "u1", Role = Role.Medico });
            return u;
        }

        [Fact]
        public async Task Login_Bloqueado_RechazaAunConLaContraseñaCorrecta_YNoTocaElContador()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(Usuario("correcta", 5, Ahora.AddMinutes(3)));
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.Null(await sut.LoginAsync("u1", "correcta"));
            repo.Verify(r => r.SetLoginStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
            repo.Verify(r => r.IncrementFailedLoginAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Login_BloqueoVencido_DejaEntrarYReiniciaElContador()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(Usuario("correcta", 5, Ahora.AddMinutes(-1)));
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.NotNull(await sut.LoginAsync("u1", "correcta"));
            repo.Verify(r => r.SetLoginStateAsync("u1", 0, null), Times.Once);
        }

        [Fact]
        public async Task Login_QuintoFallo_Bloquea()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(Usuario("correcta", 4));
            repo.Setup(r => r.IncrementFailedLoginAsync("u1")).ReturnsAsync(5);
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.Null(await sut.LoginAsync("u1", "incorrecta"));
            repo.Verify(r => r.SetLockoutUntilAsync("u1", Ahora.AddMinutes(5)), Times.Once);
        }

        // dos intentos en paralelo leen 3; la base devuelve 4 y 5, y el segundo bloquea
        [Fact]
        public async Task Login_FalloConcurrente_BloqueaSegunElTotalDeLaBase()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(Usuario("correcta", 3));
            repo.Setup(r => r.IncrementFailedLoginAsync("u1")).ReturnsAsync(5);
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.Null(await sut.LoginAsync("u1", "incorrecta"));
            repo.Verify(r => r.SetLockoutUntilAsync("u1", Ahora.AddMinutes(5)), Times.Once);
        }

        [Fact]
        public async Task Login_FalloSinLlegarAlUmbral_NoBloquea()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(Usuario("correcta", 1));
            repo.Setup(r => r.IncrementFailedLoginAsync("u1")).ReturnsAsync(2);
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.Null(await sut.LoginAsync("u1", "incorrecta"));
            repo.Verify(r => r.SetLockoutUntilAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public async Task Login_OkSinFallosPrevios_NoEscribeEstado()
        {
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(Usuario("correcta"));
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.NotNull(await sut.LoginAsync("u1", "correcta"));
            repo.Verify(r => r.SetLoginStateAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
            repo.Verify(r => r.SetPasswordHashAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Login_HashConParametrosViejos_SeRegeneraAlEntrar()
        {
            var u = Usuario("correcta");
            // hash válido con 1000 iteraciones, como uno de una versión anterior
            u.PasswordHash = HashCon("correcta", 1000);
            var repo = new Mock<IUserRepository>();
            repo.Setup(r => r.GetAsync("u1")).ReturnsAsync(u);
            var sut = new LocalCredentialAuthenticator(repo.Object, () => Ahora);

            Assert.NotNull(await sut.LoginAsync("u1", "correcta"));
            repo.Verify(r => r.SetPasswordHashAsync("u1", It.Is<string>(h => !PasswordHasher.NeedsRehash(h))), Times.Once);
        }

        [Fact]
        public void Verify_SaltDeMenosDe8Bytes_DevuelveFalseSinLanzar()
        {
            var hash = "pbkdf2-sha1$1000$" + Convert.ToBase64String(new byte[4]) + "$" + Convert.ToBase64String(new byte[32]);

            Assert.False(PasswordHasher.Verify("x", hash));
        }

        private static string HashCon(string password, int iteraciones)
        {
            var salt = new byte[16];
            new Random(1).NextBytes(salt);
#pragma warning disable CA5379, CA5387 // replica a propósito el formato del hasher con otro conteo
            using (var kdf = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, iteraciones))
#pragma warning restore CA5379, CA5387
            {
                return "pbkdf2-sha1$" + iteraciones + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(kdf.GetBytes(32));
            }
        }
    }
}
