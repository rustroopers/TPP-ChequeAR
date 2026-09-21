using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Moq;

namespace ChequeAR.Tests
{
    internal static class TestHelpers
    {
        /// <summary>
        /// AuditDispatcher with a far-future flush interval so the background timer
        /// never races with assertions during a unit test.
        /// </summary>
        public static AuditDispatcher NoopAuditDispatcher()
            => new AuditDispatcher(Mock.Of<IAuditRepository>(), flushIntervalMs: 600000);

        public static ValidationResult ValidOk()
            => new ValidationResult { IsValid = true, Errors = Array.Empty<string>(), Warnings = Array.Empty<string>() };

        public static ValidationResult ValidFail(params string[] errors)
            => new ValidationResult { IsValid = false, Errors = errors, Warnings = Array.Empty<string>() };

        /// <summary>
        /// Guard sin restricción (equivalente a Administrador, o al flag de enforcement
        /// apagado): es el default para los tests que no ejercitan la autorización por
        /// especialidad y sólo quieren que el resto del comportamiento no cambie.
        /// </summary>
        public static ISpecialtyGuard UnrestrictedGuard()
        {
            var mock = new Mock<ISpecialtyGuard>();
            mock.SetupGet(g => g.Scope).Returns(SpecialtyScope.Unrestricted());
            return mock.Object;
        }

        /// <summary>Guard restringido a los códigos dados (simula Jefe/Medico).</summary>
        public static ISpecialtyGuard GuardFor(params string[] allowedCodes)
        {
            var scope = SpecialtyScope.Limited(allowedCodes);
            var mock = new Mock<ISpecialtyGuard>();
            mock.SetupGet(g => g.Scope).Returns(scope);
            mock.Setup(g => g.EnsureAllows(It.IsAny<string>()))
                .Callback<string>(code => { if (!scope.Allows(code)) throw new ChequeAR.Application.Exceptions.SpecialtyForbiddenException(code); });
            return mock.Object;
        }

        /// <summary>IAccessGuard que otorga cualquier permiso, para tests que no ejercitan el eje rol.</summary>
        public static IAccessGuard UnrestrictedAccess()
        {
            var mock = new Mock<IAccessGuard>();
            mock.Setup(g => g.Can(It.IsAny<string>())).Returns(true);
            return mock.Object;
        }

        /// <summary>IAccessGuard que sólo otorga los permisos de un rol concreto (vía RolePolicy real).</summary>
        public static IAccessGuard AccessGuardFor(string role)
        {
            var mock = new Mock<IAccessGuard>();
            mock.Setup(g => g.Can(It.IsAny<string>()))
                .Returns((string permission) => RolePolicy.Allows(new[] { role }, permission));
            mock.Setup(g => g.EnsureCan(It.IsAny<string>()))
                .Callback<string>(permission =>
                {
                    if (!RolePolicy.Allows(new[] { role }, permission))
                        throw new ChequeAR.Application.Exceptions.PermissionForbiddenException(permission);
                });
            return mock.Object;
        }

        /// <summary>Catálogo en memoria con los 5 códigos base, para tests que no necesitan tocar la DB.</summary>
        public static SpecialtyCatalogService CatalogWith(params Specialty[] items)
        {
            var seed = items != null && items.Length > 0
                ? items
                : new[]
                {
                    new Specialty { Code = "general", Name = "General", IsActive = true, IsDefault = true, SortOrder = 0 },
                    new Specialty { Code = "cardio", Name = "Cardiología", IsActive = true, SortOrder = 10 },
                    new Specialty { Code = "neumo", Name = "Neumonología", IsActive = true, SortOrder = 20 },
                    new Specialty { Code = "onco", Name = "Oncología", IsActive = true, SortOrder = 30 },
                    new Specialty { Code = "reuma", Name = "Reumatología", IsActive = true, SortOrder = 40 }
                };
            var repo = new Mock<ISpecialtyRepository>();
            repo.Setup(r => r.ListAsync(It.IsAny<bool>())).ReturnsAsync((IReadOnlyList<Specialty>)seed.ToList());
            return new SpecialtyCatalogService(() => repo.Object, ttlMinutes: 60);
        }
    }
}
