using System;
using System.Threading.Tasks;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class SessionValidatorTests
    {
        private User _user;
        private int _consultas;
        private DateTime _ahora = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc);

        private SessionValidator Sut(TimeSpan? ttl = null) => new SessionValidator(
            id => { _consultas++; return Task.FromResult(_user); }, ttl ?? TimeSpan.FromSeconds(60), null, () => _ahora);

        private static User Medico(params string[] especialidades)
        {
            var u = new User { Id = "med", IsActive = true, SecurityStamp = "s1" };
            u.Roles.Add(new UserRole { UserId = "med", Role = Role.Medico });
            foreach (var e in especialidades) u.Specialties.Add(new UserSpecialty { UserId = "med", SpecialtyCode = e });
            return u;
        }

        [Fact]
        public async Task SesionVigente_EsValida()
        {
            _user = Medico("cardio", "neumo");
            Assert.True(await Sut().IsValidAsync("med", "s1", Role.Medico, new[] { "NEUMO", "cardio" }));
        }

        [Fact]
        public async Task SelloRotado_PorLogout_NoEsValida()
        {
            _user = Medico("cardio");
            _user.SecurityStamp = "s2";
            Assert.False(await Sut().IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" }));
        }

        [Fact]
        public async Task SinSello_TicketViejo_NoEsValida()
        {
            _user = Medico("cardio");
            Assert.False(await Sut().IsValidAsync("med", null, Role.Medico, new[] { "cardio" }));
        }

        [Fact]
        public async Task UsuarioDesactivado_NoEsValida()
        {
            _user = Medico("cardio");
            _user.IsActive = false;
            Assert.False(await Sut().IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" }));
        }

        [Fact]
        public async Task LeSacaronUnaEspecialidad_NoEsValida()
        {
            _user = Medico("cardio");
            Assert.False(await Sut().IsValidAsync("med", "s1", Role.Medico, new[] { "cardio", "onco" }));
        }

        [Fact]
        public async Task LeCambiaronElRol_NoEsValida()
        {
            _user = Medico("cardio");
            Assert.False(await Sut().IsValidAsync("med", "s1", Role.Jefe, new[] { "cardio" }));
        }

        [Fact]
        public async Task UsuarioInexistente_NoEsValida()
        {
            _user = null;
            Assert.False(await Sut().IsValidAsync("med", "s1", Role.Medico, Array.Empty<string>()));
        }

        [Fact]
        public async Task CacheaDentroDelTtl_EInvalidateLaDescarta()
        {
            _user = Medico("cardio");
            var sut = Sut();

            await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" });
            await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" });
            Assert.Equal(1, _consultas);

            _user.SecurityStamp = "s2";
            sut.Invalidate("med");
            Assert.False(await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" }));
            Assert.Equal(2, _consultas);
        }

        [Fact]
        public async Task VencidoElTtl_VuelveAConsultar()
        {
            _user = Medico("cardio");
            var sut = Sut(TimeSpan.FromSeconds(60));

            await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" });
            _ahora = _ahora.AddSeconds(61);
            await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" });

            Assert.Equal(2, _consultas);
        }

        [Fact]
        public async Task BaseCaida_UsaElUltimoDatoConocido()
        {
            _user = Medico("cardio");
            var falla = false;
            var sut = new SessionValidator(id => falla ? throw new InvalidOperationException("caída") : Task.FromResult(_user),
                TimeSpan.FromSeconds(1), null, () => _ahora);

            Assert.True(await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" }));
            falla = true;
            _ahora = _ahora.AddSeconds(5);
            Assert.True(await sut.IsValidAsync("med", "s1", Role.Medico, new[] { "cardio" }));
            Assert.False(await sut.IsValidAsync("med", "s9", Role.Medico, new[] { "cardio" }));
        }
    }
}
