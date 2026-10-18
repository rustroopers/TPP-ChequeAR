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
    internal sealed class FakeClock : IClock
    {
        public FakeClock(DateTime utcNow) { UtcNow = utcNow; }
        public DateTime UtcNow { get; private set; }
        public void Advance(TimeSpan by) => UtcNow = UtcNow + by;
    }

    internal static class TestHelpers
    {
        /// <summary>
        /// AuditDispatcher with a far-future flush interval so the background timer
        /// never races with assertions during a unit test.
        /// </summary>
        // Una sola instancia: cada dispatcher arranca un Timer, y los tests no los liberaban.
        private static readonly AuditDispatcher Noop = new AuditDispatcher(Mock.Of<IAuditRepository>(), flushIntervalMs: 600000);

        public static AuditDispatcher NoopAuditDispatcher() => Noop;

        /// <summary>Junta en <paramref name="sink"/> lo que se persiste (FlushAsync para la cola).</summary>
        public static AuditDispatcher CapturingAuditDispatcher(List<AuditEvent> sink)
        {
            var repo = new Mock<IAuditRepository>();
            repo.Setup(r => r.AddManyAsync(It.IsAny<IEnumerable<AuditEvent>>()))
                .Callback<IEnumerable<AuditEvent>>(sink.AddRange).Returns(Task.CompletedTask);
            repo.Setup(r => r.AddAsync(It.IsAny<AuditEvent>()))
                .Callback<AuditEvent>(sink.Add).Returns(Task.CompletedTask);
            return new AuditDispatcher(repo.Object, flushIntervalMs: 600000);
        }

        public static QuestionnaireService QuestionnaireService(
            Mock<IQuestionnaireRepository> repo, Mock<IQuestionnaireResponseRepository> responses, Mock<IFhirValidator> validator,
            IClinicalVariableRepository variables, ISpecialtyGuard guard = null, SpecialtyCatalogService catalog = null,
            IAccessGuard access = null, AuditDispatcher audit = null)
            => new QuestionnaireService(repo.Object, responses.Object, validator.Object, audit ?? Noop,
                guard ?? UnrestrictedGuard(), catalog ?? CatalogWith(), access ?? UnrestrictedAccess(), variables);

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
        /// Repositorio de variables en memoria. GetManyAsync es el que usa QuestionnaireService
        /// al guardar, así que es el que más importa que responda bien. Cada variable queda
        /// publicada, salvo que el test le saque la fecha después.
        /// </summary>
        public static IClinicalVariableRepository VariableRepoWith(params ClinicalVariable[] variables)
        {
            var seed = (variables ?? Array.Empty<ClinicalVariable>()).ToList();
            foreach (var v in seed) v.PublishedAt = v.PublishedAt ?? new DateTime(2026, 1, 1);
            var mock = new Mock<IClinicalVariableRepository>();
            mock.Setup(r => r.GetAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => seed.FirstOrDefault(v => v.Id == id));
            mock.Setup(r => r.GetByCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((string code) => seed.FirstOrDefault(v => v.Code == code));
            mock.Setup(r => r.GetPublishedActiveBySnomedCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((string code) => (IReadOnlyList<ClinicalVariable>)seed
                    .Where(v => v.SnomedCode == code && v.IsActive && v.PublishedAt != null).ToList());
            mock.Setup(r => r.GetPublishedActiveNamesAsync())
                .ReturnsAsync(() => (IReadOnlyList<ClinicalVariable>)seed.Where(v => v.IsActive && v.PublishedAt != null).ToList());
            mock.Setup(r => r.GetSuccessorsAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) => (IReadOnlyList<ClinicalVariable>)seed
                    .Where(v => v.ReplacesId.HasValue && ids.Contains(v.ReplacesId.Value) && v.PublishedAt != null).ToList());
            mock.Setup(r => r.GetManyAsync(It.IsAny<IEnumerable<Guid>>()))
                .ReturnsAsync((IEnumerable<Guid> ids) =>
                    (IReadOnlyList<ClinicalVariable>)seed.Where(v => ids.Contains(v.Id)).ToList());
            mock.Setup(r => r.ListAsync(It.IsAny<string>(), It.IsAny<QuestionType?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((IReadOnlyList<ClinicalVariable>)seed);
            mock.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<QuestionType?>(), It.IsAny<bool>()))
                .ReturnsAsync(seed.Count);
            return mock.Object;
        }

        /// <summary>Copia la definición de la variable a la pregunta.</summary>
        public static void ApplySnapshot(Question q, ClinicalVariable v) => ClinicalVariableSnapshot.Apply(q, v);

        /// <summary>
        /// Pregunta que proviene de <paramref name="v"/>. Desde el catálogo de variables toda
        /// pregunta necesita VariableId o QuestionnaireService la rechaza, así que armarla a
        /// mano en cada test sería repetir lo mismo veinte veces.
        /// </summary>
        public static Question QuestionFrom(ClinicalVariable v, string linkId = null, string text = null)
            => new Question { LinkId = linkId ?? v.Code, Text = text ?? v.DefaultText, VariableId = v.Id };

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
