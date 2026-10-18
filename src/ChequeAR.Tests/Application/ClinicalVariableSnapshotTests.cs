using System;
using System.Linq;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ClinicalVariableSnapshotTests
    {
        private static ClinicalVariable Peso()
            => new ClinicalVariable
            {
                Id = Guid.NewGuid(),
                Code = "peso",
                Name = "Peso",
                DefaultText = "Peso corporal",
                Type = QuestionType.Decimal,
                Unit = "kg",
                MinValue = 0m,
                MaxValue = 300m,
                SnomedSystem = "http://snomed.info/sct",
                SnomedCode = "27113001",
                SnomedDisplay = "Body weight",
                Term = "peso corporal",
                Revision = 3
            };

        // Todo lo que define la variable se COPIA a la pregunta. Si no se copiara, el recurso
        // FHIR que se publica no sería autocontenido: un consumidor externo tendría que
        // conocer el catálogo de ChequeAR para saber en qué unidad viene un peso.
        [Fact]
        public void Apply_CopiaTipoUnidadRangoYCoding()
        {
            var v = Peso();
            var q = new Question { LinkId = "peso-1" };

            TestHelpers.ApplySnapshot(q, v);

            Assert.Equal(QuestionType.Decimal, q.Type);
            Assert.Equal(v.Id, q.VariableId);
            var validacion = QuestionMetadataReader.ReadValidation(q.ValidationJson);
            Assert.Equal("kg", validacion.Unit);
            Assert.Equal(0m, validacion.Min);
            Assert.Equal(300m, validacion.Max);
            var coding = QuestionMetadataReader.ReadCoding(q.CodingJson);
            Assert.Equal("27113001", coding.Code);
            Assert.Equal("http://snomed.info/sct", coding.System);
        }

        // Enunciado, obligatoriedad y condición son lo único que el jefe decide por
        // cuestionario: pisarlos convertiría "Peso al nacer" en "Peso corporal" en cada guardado.
        [Fact]
        public void Apply_RespetaTextRequiredYEnableWhen()
        {
            var q = new Question
            {
                Text = "Peso al nacer",
                Required = true,
                EnableWhenJson = "{\"question\":\"prematuro\",\"op\":\"=\",\"value\":true}"
            };

            TestHelpers.ApplySnapshot(q, Peso());

            Assert.Equal("Peso al nacer", q.Text);
            Assert.True(q.Required);
            Assert.Equal("prematuro", QuestionMetadataReader.ReadCondition(q.EnableWhenJson).Question);
        }

        // Insertar una variable sin escribir nada tiene que dejar la pregunta con su enunciado
        // por defecto, no en blanco: en blanco el cuestionario se publicaría sin texto visible.
        [Fact]
        public void Apply_TextVacio_CaeAlEnunciadoPorDefecto()
        {
            var q = new Question { Text = "   " };

            TestHelpers.ApplySnapshot(q, Peso());

            Assert.Equal("Peso corporal", q.Text);
        }

        // El cliente no puede contradecir al catálogo: si manda otro tipo u otra unidad, gana
        // la variable. Es lo que hace que bloquear esos campos en la interfaz sea una decisión
        // de presentación y no la única defensa.
        [Fact]
        public void Apply_PisaLoQueElClienteMandoEnLosCamposBloqueados()
        {
            var q = new Question
            {
                Type = QuestionType.Text,
                ValidationJson = "{\"unit\":\"lb\",\"max\":9999}",
                CodingJson = "{\"code\":\"000\"}"
            };

            TestHelpers.ApplySnapshot(q, Peso());

            Assert.Equal(QuestionType.Decimal, q.Type);
            Assert.Equal("kg", QuestionMetadataReader.ReadValidation(q.ValidationJson).Unit);
            Assert.Equal("27113001", QuestionMetadataReader.ReadCoding(q.CodingJson).Code);
        }

        // Las opciones se copian con su coding. Sin SnomedCode el builder emitiría el Coding sin
        // system y la respuesta no sería resoluble fuera de ChequeAR.
        [Fact]
        public void Apply_CopiaOpcionesConSuCoding()
        {
            var v = new ClinicalVariable { Id = Guid.NewGuid(), Type = QuestionType.Choice, Name = "Severidad" };
            v.Options.Add(new VariableOption { Display = "Leve", Value = "leve", Code = "255604002", SnomedCode = "255604002", SnomedDisplay = "Mild", OrderIndex = 0 });
            v.Options.Add(new VariableOption { Display = "Severa", Value = "severa", Code = "24484000", SnomedCode = "24484000", SnomedDisplay = "Severe", OrderIndex = 1 });
            var q = new Question();

            TestHelpers.ApplySnapshot(q, v);

            Assert.Equal(2, q.Options.Count);
            var leve = q.Options.First();
            Assert.Equal("Leve", leve.Display);
            Assert.Equal("255604002", leve.SnomedCode);
            Assert.Equal("Mild", leve.SnomedDisplay);
            Assert.Equal(new[] { 0, 1 }, q.Options.Select(o => o.OrderIndex).ToArray());
        }

        // Null y no "{}": un objeto vacío ocuparía la columna sin significar nada, y
        // QuestionMetadataReader tendría que distinguir "sin restricciones" de "restricciones
        // vacías". Es el mismo criterio que ya usa el parser FHIR.
        [Fact]
        public void ToValidationJson_SinRestricciones_DevuelveNull()
        {
            var v = new ClinicalVariable { Type = QuestionType.Boolean, Name = "Fuma" };

            Assert.Null(ClinicalVariableSnapshot.ToValidationJson(v));
        }

        // Una variable puede existir antes de que se le encuentre el concepto correcto.
        [Fact]
        public void ToCodingJson_SinCodigoNiTermino_DevuelveNull()
        {
            var v = new ClinicalVariable { Type = QuestionType.String, Name = "Observación" };

            Assert.Null(ClinicalVariableSnapshot.ToCodingJson(v));
        }

        // Sin código pero con término: se guarda igual, porque el término es la semilla que
        // permite retomar la codificación después.
        [Fact]
        public void ToCodingJson_SoloTermino_LoConserva()
        {
            var v = new ClinicalVariable { Type = QuestionType.String, Name = "Dolor", Term = "dolor torácico" };

            Assert.Equal("dolor torácico", QuestionMetadataReader.ReadCoding(ClinicalVariableSnapshot.ToCodingJson(v)).Term);
        }
    }
}
