using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Moq;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ClinicalVariableServiceTests
    {
        private readonly Mock<IClinicalVariableRepository> _repo = new Mock<IClinicalVariableRepository>();
        private readonly ClinicalVariableService _sut;

        public ClinicalVariableServiceTests()
        {
            _sut = new ClinicalVariableService(_repo.Object, TestHelpers.NoopAuditDispatcher(), TestHelpers.UnrestrictedAccess());
        }

        private static ClinicalVariable Nueva(string code = "peso", QuestionType type = QuestionType.Decimal)
            => new ClinicalVariable { Code = code, Name = "Peso", DefaultText = "Peso corporal", Type = type };

        private ClinicalVariableService SutPara(string role)
            => new ClinicalVariableService(_repo.Object, TestHelpers.NoopAuditDispatcher(), TestHelpers.AccessGuardFor(role));

        // El Jefe arma cuestionarios pero no define el vocabulario clínico: centralizar eso en
        // el Administrador es lo que hace comparables los datos entre especialidades.
        [Fact]
        public async Task CreateAsync_ComoJefe_Lanza()
        {
            await Assert.ThrowsAsync<PermissionForbiddenException>(
                () => SutPara(Role.Jefe).CreateAsync(Nueva(), "jefe.cardio"));
        }

        // Leer el catálogo sí: sin eso el Jefe no podría elegir variables en el editor.
        [Fact]
        public async Task ListAsync_ComoJefe_NoLanza()
        {
            _repo.Setup(r => r.ListAsync(It.IsAny<string>(), It.IsAny<QuestionType?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>()))
                 .ReturnsAsync((IReadOnlyList<ClinicalVariable>)new List<ClinicalVariable>());
            _repo.Setup(r => r.CountAsync(It.IsAny<string>(), It.IsAny<QuestionType?>(), It.IsAny<bool?>())).ReturnsAsync(0);

            var pagina = await SutPara(Role.Jefe).ListAsync(null, null, true, 0, 50);

            Assert.Equal(0, pagina.Total);
        }

        // El código es la semilla del LinkId de cada pregunta, la clave sobre la que pivotean
        // los reportes: dos variables con el mismo código harían colisionar dos conceptos
        // distintos en la misma columna.
        [Fact]
        public async Task CreateAsync_CodigoDuplicado_Falla()
        {
            _repo.Setup(r => r.GetByCodeAsync("peso")).ReturnsAsync(new ClinicalVariable { Id = Guid.NewGuid(), Code = "peso" });

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.CreateAsync(Nueva(), "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("peso"));
        }

        // Espacios, acentos y mayúsculas tendrían que normalizarse en cada consulta de reporte.
        [Fact]
        public async Task CreateAsync_CodigoNoSlug_Falla()
        {
            var v = Nueva("Peso Corporal!");

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.CreateAsync(v, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("minúsculas"));
        }

        // Una pregunta de opción única sin opciones no se puede responder: el renderer
        // dibujaría un desplegable vacío y el error recién aparecería al completar la ficha.
        [Fact]
        public async Task CreateAsync_ChoiceSinOpciones_Falla()
        {
            var v = Nueva("severidad", QuestionType.Choice);

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.CreateAsync(v, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("dos opciones"));
        }

        // Un rango imposible dejaría una variable que ninguna respuesta puede satisfacer.
        [Fact]
        public async Task CreateAsync_MinMayorQueMax_Falla()
        {
            var v = Nueva();
            v.MinValue = 300m;
            v.MaxValue = 10m;

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.CreateAsync(v, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("mínimo"));
        }

        // Dos variables para el mismo concepto SNOMED partirían en dos columnas datos que
        // clínicamente son lo mismo — justo lo que la feature viene a evitar.
        [Fact]
        public async Task CreateAsync_SnomedDuplicado_Falla()
        {
            _repo.Setup(r => r.GetBySnomedCodeAsync("27113001"))
                 .ReturnsAsync(new ClinicalVariable { Id = Guid.NewGuid(), Name = "Peso viejo" });
            var v = Nueva("peso-2");
            v.SnomedCode = "27113001";

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.CreateAsync(v, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("Peso viejo"));
        }

        // Un código sin sistema no es interoperable con nadie: si el admin eligió un concepto,
        // se asume SNOMED salvo que declare otro.
        [Fact]
        public async Task CreateAsync_SnomedSinSistema_CompletaSnomedPorOmision()
        {
            var v = Nueva();
            v.SnomedCode = "27113001";

            var creada = await _sut.CreateAsync(v, "admin");

            Assert.Equal(TerminologyService.SnomedSystem, creada.SnomedSystem);
        }

        // Unidad y rango en un tipo no numérico son residuos que viajarían al ValidationJson de
        // cada pregunta. Se limpian en vez de rechazarse: el formulario los esconde al cambiar
        // de tipo, y el admin no tiene por qué saber que quedaron ahí.
        [Fact]
        public async Task CreateAsync_UnidadEnTipoNoNumerico_SeLimpia()
        {
            var v = Nueva("observacion", QuestionType.Text);
            v.Unit = "kg";
            v.MaxValue = 10m;

            var creada = await _sut.CreateAsync(v, "admin");

            Assert.Null(creada.Unit);
            Assert.Null(creada.MaxValue);
        }

        // Revision es lo único que permite después avisar "esta pregunta usa una versión
        // anterior de la variable".
        [Fact]
        public async Task UpdateAsync_IncrementaRevision()
        {
            var id = Guid.NewGuid();
            var existente = new ClinicalVariable { Id = id, Code = "peso", Name = "Peso", Type = QuestionType.Decimal, Revision = 4 };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(existente);

            var actualizada = await _sut.UpdateAsync(id, Nueva(), "admin");

            Assert.Equal(5, actualizada.Revision);
        }

        // Editar el catálogo NO reescribe lo ya armado: los cuestionarios publicados tienen que
        // ser reproducibles, y los borradores sólo deben avisar, no cambiar solos.
        [Fact]
        public async Task UpdateAsync_NoTocaLasPreguntasExistentes()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(new ClinicalVariable { Id = id, Code = "peso", Name = "Peso" });

            await _sut.UpdateAsync(id, Nueva(), "admin");

            _repo.Verify(r => r.CountQuestionsUsingAsync(It.IsAny<Guid>()), Times.Never);
        }

        // Borrarla de verdad dejaría una versión publicada nombrando un concepto que el
        // catálogo ya no puede explicar. Se desactiva: lo armado sigue andando, lo nuevo no.
        [Fact]
        public async Task DeleteAsync_EnUso_DesactivaEnVezDeBorrar()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.CountQuestionsUsingAsync(id)).ReturnsAsync(3);
            var v = new ClinicalVariable { Id = id, Code = "peso", Name = "Peso", IsActive = true };
            _repo.Setup(r => r.GetAsync(id)).ReturnsAsync(v);

            var outcome = await _sut.DeleteAsync(id, "admin");

            Assert.Equal(VariableDeleteOutcome.DeactivatedBecauseInUse, outcome);
            Assert.False(v.IsActive);
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeleteAsync_SinUso_Borra()
        {
            var id = Guid.NewGuid();
            _repo.Setup(r => r.CountQuestionsUsingAsync(id)).ReturnsAsync(0);

            var outcome = await _sut.DeleteAsync(id, "admin");

            Assert.Equal(VariableDeleteOutcome.Deleted, outcome);
            _repo.Verify(r => r.DeleteAsync(id), Times.Once);
        }

        // Las opciones se guardan con Code == SnomedCode: R5 admite un solo Coding por
        // answerOption.value, y unificarlos evita que el código que guardan las respuestas
        // (ValueCode, sobre el que agrupan los reportes) difiera del que viaja en el recurso.
        [Fact]
        public async Task CreateAsync_OpcionCodificada_UnificaCodeConSnomedCode()
        {
            var v = Nueva("severidad", QuestionType.Choice);
            v.Options.Add(new VariableOption { Display = "Leve", SnomedCode = "255604002" });
            v.Options.Add(new VariableOption { Display = "Severa", SnomedCode = "24484000" });

            var creada = await _sut.CreateAsync(v, "admin");

            Assert.Equal("255604002", creada.Options.First().Code);
            Assert.Equal(TerminologyService.SnomedSystem, creada.Options.First().SnomedSystem);
        }

        // Toda escritura del catálogo es auditable: es vocabulario clínico compartido por todas
        // las especialidades, y tiene que poder rastrearse quién lo cambió.
        [Fact]
        public async Task CreateAsync_EncolaAuditoria()
        {
            var auditRepo = new Mock<IAuditRepository>();
            var dispatcher = new AuditDispatcher(auditRepo.Object, flushIntervalMs: 600000);
            var sut = new ClinicalVariableService(_repo.Object, dispatcher, TestHelpers.UnrestrictedAccess());

            var creada = await sut.CreateAsync(Nueva(), "admin");
            await dispatcher.FlushAsync();

            auditRepo.Verify(r => r.AddManyAsync(It.Is<IEnumerable<AuditEvent>>(
                evts => evts.Any(e => e.EntityRef == "ClinicalVariable/" + creada.Id))), Times.Once);
        }
    }
}
