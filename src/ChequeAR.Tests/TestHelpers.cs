using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
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

        /// <summary>
        /// Variable del catálogo lista para usar. Los defaults son los de una medición
        /// numérica, que es el caso más común y el que no necesita opciones.
        /// </summary>
        public static ClinicalVariable Variable(string code, QuestionType type = QuestionType.Decimal,
            string name = null, Guid? id = null, bool isActive = true, int revision = 1)
        {
            return new ClinicalVariable
            {
                Id = id ?? Guid.NewGuid(),
                Code = code,
                Name = name ?? code,
                DefaultText = name ?? code,
                Type = type,
                IsActive = isActive,
                Revision = revision
            };
        }

        /// <summary>
        /// Repositorio de variables en memoria. GetManyAsync y GetVersionsAsync son los que usa
        /// QuestionnaireService al guardar, así que son los que más importa que respondan bien.
        /// Cada variable está publicada en la versión igual a su revisión, con su definición actual.
        /// </summary>
        public static IClinicalVariableRepository VariableRepoWith(params ClinicalVariable[] variables)
        {
            var seed = (variables ?? Array.Empty<ClinicalVariable>()).ToList();
            var versionIds = seed.ToDictionary(v => v.Id, v => v.CurrentVersionId ?? Guid.NewGuid());
            foreach (var v in seed) v.CurrentVersionId = versionIds[v.Id];
            var mock = new Mock<IClinicalVariableRepository>();
            mock.Setup(r => r.GetVersionsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) => (IReadOnlyList<ClinicalVariableVersion>)seed
                    .Where(v => ids.Contains(v.Id))
                    .Select(v => new ClinicalVariableVersion
                    {
                        Id = versionIds[v.Id], VariableId = v.Id, VersionNumber = v.Revision, SourceRevision = v.Revision,
                        DefinitionJson = VariableDefinition.From(v).ToJson()
                    }).ToList());
            mock.Setup(r => r.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => seed.FirstOrDefault(v => v.Id == id));
            mock.Setup(r => r.GetByCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((string code) => seed.FirstOrDefault(v => v.Code == code));
            mock.Setup(r => r.GetBySnomedCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((string code) => seed.FirstOrDefault(v => v.SnomedCode == code));
            mock.Setup(r => r.GetManyAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) =>
                    (IReadOnlyList<ClinicalVariable>)seed.Where(v => ids.Contains(v.Id)).ToList());
            mock.Setup(r => r.ListAsync(It.IsAny<string>(), It.IsAny<QuestionType?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((IReadOnlyList<ClinicalVariable>)seed);
            mock.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<QuestionType?>(), It.IsAny<bool>()))
                .ReturnsAsync(seed.Count);
            return mock.Object;
        }

        /// <summary>Copia la definición actual de la variable, como si estuviera publicada en la versión igual a su revisión.</summary>
        public static void ApplySnapshot(Question q, ClinicalVariable v)
            => ClinicalVariableSnapshot.Apply(q, VariableDefinition.From(v),
                new ClinicalVariableVersion { Id = v.CurrentVersionId ?? Guid.NewGuid(), VariableId = v.Id, VersionNumber = v.Revision });

        /// <summary>
        /// Pregunta que proviene de <paramref name="v"/>. Desde el catálogo de variables toda
        /// pregunta necesita VariableId o QuestionnaireService la rechaza, así que armarla a
        /// mano en cada test sería repetir lo mismo veinte veces. Trae versión, como una
        /// pregunta ya guardada, pero no pide ninguna: al guardar la resuelve el servicio.
        /// </summary>
        public static Question QuestionFrom(ClinicalVariable v, string linkId = null, string text = null)
            => new Question { LinkId = linkId ?? v.Code, Text = text ?? v.DefaultText, VariableId = v.Id, VariableVersionId = Guid.NewGuid() };

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
