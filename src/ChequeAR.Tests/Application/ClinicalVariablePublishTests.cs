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
    public class ClinicalVariablePublishTests
    {
        private readonly Mock<IClinicalVariableRepository> _repo = new Mock<IClinicalVariableRepository>();
        private readonly List<AuditEvent> _auditados = new List<AuditEvent>();
        private readonly AuditDispatcher _audit;
        private readonly ClinicalVariableService _sut;

        public ClinicalVariablePublishTests()
        {
            _audit = TestHelpers.CapturingAuditDispatcher(_auditados);
            _sut = new ClinicalVariableService(_repo.Object, _audit, TestHelpers.UnrestrictedAccess());
        }

        private ClinicalVariable Severidad(int revision = 1, bool publicada = false)
        {
            var v = new ClinicalVariable
            {
                Id = Guid.NewGuid(), Code = "severidad", Name = "Severidad", Type = QuestionType.Choice, IsActive = true,
                Revision = revision, PublishedAt = publicada ? new DateTime(2026, 1, 1) : (DateTime?)null,
                Options =
                {
                    new VariableOption { Id = Guid.NewGuid(), Value = "leve", Display = "Leve", Code = "leve", OrderIndex = 0 },
                    new VariableOption { Id = Guid.NewGuid(), Value = "grave", Display = "Grave", Code = "grave", OrderIndex = 1 }
                }
            };
            _repo.Setup(r => r.GetAsync(v.Id)).ReturnsAsync(v);
            return v;
        }

        // Activar o desactivar no es un cambio de definición: antes reemplazaba las opciones
        // (ORA-00001) y subía la revisión, y todos los borradores avisaban un cambio falso.
        [Fact]
        public async Task SetActive_SoloGuardaLaCabecera()
        {
            var v = Severidad(revision: 3);

            await _sut.SetActiveAsync(v.Id, false, "admin");

            Assert.False(v.IsActive);
            Assert.Equal(3, v.Revision);
            _repo.Verify(r => r.UpdateHeaderAsync(v), Times.Once);
            _repo.Verify(r => r.UpdateWithOptionsAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ICollection<VariableOption>>()), Times.Never);
        }

        private void CapturarPublicacion(ClinicalVariable v, Action<ClinicalVariable> baja = null)
        {
            _repo.Setup(r => r.PublishAsync(v, It.IsAny<ClinicalVariable>()))
                 .Callback<ClinicalVariable, ClinicalVariable>((_, d) => baja?.Invoke(d))
                 .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task Publish_LaPrimeraVez_MarcaLaFecha()
        {
            var v = Severidad(revision: 2);
            CapturarPublicacion(v);

            var result = await _sut.PublishAsync(v.Id, "admin");

            Assert.True(result.IsPublished);
            _repo.Verify(r => r.PublishAsync(v, null), Times.Once);
        }

        // Una publicada no se vuelve a publicar: sus textos se corrigen en el lugar y lo
        // clínico se cambia creando otra variable.
        [Fact]
        public async Task Publish_YaPublicada_NoVuelveAPublicar()
        {
            var v = Severidad(revision: 2, publicada: true);
            var fecha = v.PublishedAt;

            var result = await _sut.PublishAsync(v.Id, "admin");

            Assert.Equal(fecha, result.PublishedAt);
            Assert.Equal(2, result.Revision);
            _repo.Verify(r => r.PublishAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ClinicalVariable>()), Times.Never);
        }

        [Fact]
        public async Task Publish_ConRevisionVieja_Rechaza()
        {
            var v = Severidad(revision: 4);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _sut.PublishAsync(v.Id, "admin", expectedRevision: 3));

            _repo.Verify(r => r.PublishAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ClinicalVariable>()), Times.Never);
        }

        // Mover la revisión al publicar es lo que hace chocar dos publicaciones simultáneas.
        [Fact]
        public async Task Publish_MueveLaRevision()
        {
            var v = Severidad(revision: 4);
            CapturarPublicacion(v);

            await _sut.PublishAsync(v.Id, "admin");

            Assert.Equal(5, v.Revision);
        }

        [Theory]
        [InlineData("otro-codigo", false)]
        [InlineData("severidad", true)]
        public async Task Update_CodigoORepeticionEnUnaPublicada_Rechaza(string code, bool repeats)
        {
            var v = Severidad(publicada: true);
            var patch = new ClinicalVariable { Code = code, Name = "Severidad", Type = QuestionType.Choice, RepeatsAllowed = repeats, Options = v.Options.ToList() };

            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.UpdateAsync(v.Id, patch, "admin"));
        }

        [Fact]
        public async Task Delete_SinPublicarPeroEnUso_Desactiva()
        {
            var v = Severidad();
            _repo.Setup(r => r.CountQuestionsUsingAsync(v.Id)).ReturnsAsync(2);

            var outcome = await _sut.DeleteAsync(v.Id, "admin");

            Assert.Equal(VariableDeleteOutcome.DeactivatedBecauseInUse, outcome);
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Update_ConRevisionVieja_RechazaSinGuardar()
        {
            var v = Severidad(revision: 5);
            var patch = new ClinicalVariable { Code = "severidad", Name = "Severidad", Type = QuestionType.Choice, Options = v.Options.ToList() };

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _sut.UpdateAsync(v.Id, patch, "admin", expectedRevision: 4));

            _repo.Verify(r => r.UpdateWithOptionsAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ICollection<VariableOption>>()), Times.Never);
        }

        private static ClinicalVariable MismaDefinicion(ClinicalVariable v) => new ClinicalVariable
        {
            Code = v.Code, Name = v.Name, Type = v.Type,
            Options = v.Options.Select(o => new VariableOption { Value = o.Value, Display = o.Display, Code = o.Code, OrderIndex = o.OrderIndex }).ToList()
        };

        [Fact]
        public async Task Update_PublicadaSoloTextos_SeGuarda()
        {
            var v = Severidad(publicada: true);
            var patch = MismaDefinicion(v);
            patch.DefaultText = "Severidad del cuadro";
            patch.Description = "Corregida";

            var updated = await _sut.UpdateAsync(v.Id, patch, "admin");

            Assert.Equal("Severidad del cuadro", updated.DefaultText);
            _repo.Verify(r => r.UpdateWithOptionsAsync(v, It.IsAny<ICollection<VariableOption>>()), Times.Once);
        }

        [Fact]
        public async Task Update_SinCambios_NoAudita()
        {
            var v = Severidad(publicada: true);

            await _sut.UpdateAsync(v.Id, MismaDefinicion(v), "admin");
            await _audit.FlushAsync();

            Assert.Empty(_auditados);
        }

        [Fact]
        public async Task Update_AuditaQueCambio()
        {
            var v = Severidad(publicada: true);
            var patch = MismaDefinicion(v);
            patch.Description = "Corregida";

            await _sut.UpdateAsync(v.Id, patch, "admin");
            await _audit.FlushAsync();

            var cambio = Assert.Single(AuditChangeSet.Read(Assert.Single(_auditados).FhirJson).Changes);
            Assert.Equal("Descripción", cambio.Field);
            Assert.Null(cambio.Before);
            Assert.Equal("Corregida", cambio.After);
        }

        // El formulario manda "" donde lo publicado quedó sin valor (término, nombre del concepto):
        // no es un cambio clínico, y antes rechazaba corregir sólo la descripción.
        [Fact]
        public async Task Update_PublicadaConCamposVaciosDelFormulario_NoEsCambioClinico()
        {
            var v = Severidad(publicada: true);
            var patch = MismaDefinicion(v);
            patch.SnomedDisplay = "";
            patch.Description = "Corregida";
            foreach (var o in patch.Options) o.SnomedDisplay = "";

            var updated = await _sut.UpdateAsync(v.Id, patch, "admin");

            Assert.Equal("Corregida", updated.Description);
        }

        [Theory]
        [InlineData("nombre")]
        [InlineData("opcion")]
        [InlineData("tipo")]
        public async Task Update_PublicadaCambioClinico_RechazaYMandaACrearOtra(string cambio)
        {
            var v = Severidad(publicada: true);
            var patch = MismaDefinicion(v);
            if (cambio == "nombre") patch.Name = "Severidad clínica";
            if (cambio == "opcion") patch.Options.Add(new VariableOption { Value = "moderada", Display = "Moderada", Code = "moderada", OrderIndex = 2 });
            if (cambio == "tipo") { patch.Type = QuestionType.String; patch.Options.Clear(); }

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.UpdateAsync(v.Id, patch, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("cree una variable nueva"));
            _repo.Verify(r => r.UpdateWithOptionsAsync(It.IsAny<ClinicalVariable>(), It.IsAny<ICollection<VariableOption>>()), Times.Never);
        }

        [Fact]
        public async Task Update_CambioDeTipoSinPublicar_SePermite()
        {
            var v = Severidad();
            var patch = new ClinicalVariable { Code = "severidad", Name = "Severidad", Type = QuestionType.String };

            var updated = await _sut.UpdateAsync(v.Id, patch, "admin");

            Assert.Equal(QuestionType.String, updated.Type);
        }

        [Fact]
        public async Task Delete_Publicada_DesactivaEnVezDeBorrar()
        {
            var v = Severidad(publicada: true);

            var outcome = await _sut.DeleteAsync(v.Id, "admin");

            Assert.Equal(VariableDeleteOutcome.DeactivatedBecauseInUse, outcome);
            Assert.False(v.IsActive);
            _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Delete_SinPublicarNiUsar_Borra()
        {
            var v = Severidad();

            var outcome = await _sut.DeleteAsync(v.Id, "admin");

            Assert.Equal(VariableDeleteOutcome.Deleted, outcome);
            _repo.Verify(r => r.DeleteAsync(v.Id), Times.Once);
        }

        // ── Reemplazo: un cambio clínico crea otra variable ─────────────────────────────

        private ClinicalVariable PublicadaConConcepto(string snomed = "246112005")
        {
            var v = Severidad(publicada: true);
            v.SnomedCode = snomed;
            return v;
        }

        private ClinicalVariable Derivada(ClinicalVariable fuente, string nombre, bool cambiarOpciones = true)
        {
            var d = new ClinicalVariable
            {
                Id = Guid.NewGuid(), Code = "severidad-2", Name = nombre, Type = QuestionType.Choice, IsActive = true, Revision = 1,
                SnomedCode = fuente.SnomedCode, ReplacesId = fuente.Id,
                Options = fuente.Options.Select(o => new VariableOption { Id = Guid.NewGuid(), Value = o.Value, Display = o.Display, Code = o.Code, OrderIndex = o.OrderIndex }).ToList()
            };
            if (cambiarOpciones)
                d.Options.Add(new VariableOption { Id = Guid.NewGuid(), Value = "moderada", Display = "Moderada", Code = "moderada", OrderIndex = 2 });
            _repo.Setup(r => r.GetAsync(d.Id)).ReturnsAsync(d);
            return d;
        }

        [Fact]
        public async Task Derive_SugiereCodigoLibreYCopiaLaDefinicionPublicada()
        {
            var fuente = PublicadaConConcepto();
            _repo.Setup(r => r.GetByCodeAsync("severidad-2")).ReturnsAsync(new ClinicalVariable { Id = Guid.NewGuid() });

            var draft = await _sut.DeriveAsync(fuente.Id);

            Assert.Equal("severidad-3", draft.Code);
            Assert.Null(draft.Name);
            Assert.Equal(fuente.Id, draft.ReplacesId);
            Assert.False(draft.DeactivateReplacedOnPublish);
            Assert.Equal(2, draft.Options.Count);
            Assert.Equal("246112005", draft.SnomedCode);
        }

        [Fact]
        public async Task Derive_DeUnaSinPublicar_Rechaza()
        {
            var v = Severidad();

            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.DeriveAsync(v.Id));
        }

        [Fact]
        public async Task Create_ReemplazandoAUnaSinPublicar_Rechaza()
        {
            var fuente = Severidad();
            var nueva = new ClinicalVariable { Code = "severidad-2", Name = "Severidad pediátrica", Type = QuestionType.Decimal, ReplacesId = fuente.Id };

            await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.CreateAsync(nueva, "admin"));
        }

        [Fact]
        public async Task PublicarDerivada_MismoNombre_Rechaza()
        {
            var fuente = PublicadaConConcepto();
            var d = Derivada(fuente, " severidad ");

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.PublishAsync(d.Id, "admin", deactivateReplaced: true));

            Assert.Contains(ex.Errors, e => e.Contains("nombre"));
        }

        [Fact]
        public async Task PublicarDerivada_SinCambioClinico_Rechaza()
        {
            var fuente = PublicadaConConcepto();
            var d = Derivada(fuente, "Severidad pediátrica", cambiarOpciones: false);

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.PublishAsync(d.Id, "admin", deactivateReplaced: true));

            Assert.Contains(ex.Errors, e => e.Contains("No hay cambios"));
        }

        [Fact]
        public async Task PublicarDerivada_MismoConceptoSinDarDeBaja_Rechaza()
        {
            var fuente = PublicadaConConcepto();
            _repo.Setup(r => r.GetPublishedActiveBySnomedCodeAsync("246112005")).ReturnsAsync(new List<ClinicalVariable> { fuente });
            var d = Derivada(fuente, "Severidad pediátrica");

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.PublishAsync(d.Id, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("246112005"));
        }

        [Fact]
        public async Task PublicarDerivada_MismoConceptoDandoDeBaja_PublicaYDaDeBajaLaAnterior()
        {
            var fuente = PublicadaConConcepto();
            _repo.Setup(r => r.GetPublishedActiveBySnomedCodeAsync("246112005")).ReturnsAsync(new List<ClinicalVariable> { fuente });
            var d = Derivada(fuente, "Severidad pediátrica");
            ClinicalVariable dadaDeBaja = null;
            CapturarPublicacion(d, x => dadaDeBaja = x);

            await _sut.PublishAsync(d.Id, "admin", deactivateReplaced: true);

            Assert.True(d.IsPublished);
            Assert.Equal(fuente.Id, dadaDeBaja?.Id);
        }

        [Fact]
        public async Task Reactivar_ConElConceptoTomadoPorOtra_Rechaza()
        {
            var vieja = PublicadaConConcepto();
            vieja.IsActive = false;
            var nueva = new ClinicalVariable { Id = Guid.NewGuid(), Name = "Severidad pediátrica", SnomedCode = "246112005", IsActive = true };
            _repo.Setup(r => r.GetPublishedActiveBySnomedCodeAsync("246112005")).ReturnsAsync(new List<ClinicalVariable> { nueva });

            var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.SetActiveAsync(vieja.Id, true, "admin"));

            Assert.Contains(ex.Errors, e => e.Contains("Severidad pediátrica"));
            Assert.False(vieja.IsActive);
        }
    }
}
