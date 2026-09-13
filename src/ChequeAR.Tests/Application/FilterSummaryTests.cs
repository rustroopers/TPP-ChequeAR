using System.Collections.Generic;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    /// <summary>
    /// FilterSummary vuelca un FilterNode a lenguaje natural para el Excel exportado.
    /// Antes salía tipo SQL ("Peso &gt;= 10 OR ..."); acá se verifica que los operadores,
    /// el AND/OR y los valores booleanos/de opción se traduzcan.
    /// </summary>
    public class FilterSummaryTests
    {
        private static ReportSchema Schema()
        {
            var q = new Questionnaire();
            q.Questions.Add(new Question { LinkId = "peso", Text = "Peso", Type = QuestionType.Decimal, OrderIndex = 0 });
            q.Questions.Add(new Question { LinkId = "dolor", Text = "¿Tiene dolor?", Type = QuestionType.Boolean, OrderIndex = 1 });
            var especialidad = new Question { LinkId = "especialidad", Text = "Especialidad", Type = QuestionType.Choice, OrderIndex = 2 };
            especialidad.Options.Add(new AnswerOption { Code = "card", Value = "card", Display = "Cardiología", OrderIndex = 0 });
            q.Questions.Add(especialidad);
            return ReportSchema.From(q);
        }

        [Fact]
        public void SinFiltros_DevuelveMensajeExplicito()
        {
            Assert.Equal("Sin filtros", FilterSummary.Describe(null, Schema()));
        }

        [Fact]
        public void TraduceOperadorYUsaElTextoDeLaPregunta()
        {
            var node = new FilterNode
            {
                Logic = "AND",
                Predicates = new List<FilterPredicate> { new FilterPredicate { LinkId = "peso", Op = ">=", Value = 10 } }
            };
            Assert.Equal("Peso mayor o igual a 10", FilterSummary.Describe(node, Schema()));
        }

        [Fact]
        public void BooleanoSaleComoSiNo_NoComoTrueFalse()
        {
            var node = new FilterNode
            {
                Logic = "AND",
                Predicates = new List<FilterPredicate> { new FilterPredicate { LinkId = "dolor", Op = "=", Value = true } }
            };
            Assert.Equal("¿Tiene dolor? es Sí", FilterSummary.Describe(node, Schema()));
        }

        [Fact]
        public void OpcionSaleConSuEtiqueta_NoConElCodigo()
        {
            var node = new FilterNode
            {
                Logic = "AND",
                Predicates = new List<FilterPredicate> { new FilterPredicate { LinkId = "especialidad", Op = "=", Value = "card" } }
            };
            Assert.Equal("Especialidad es Cardiología", FilterSummary.Describe(node, Schema()));
        }

        [Fact]
        public void ConectorOR_SaleEnMinusculaEnEspanol()
        {
            var node = new FilterNode
            {
                Logic = "OR",
                Predicates = new List<FilterPredicate>
                {
                    new FilterPredicate { LinkId = "peso", Op = ">", Value = 5 },
                    new FilterPredicate { LinkId = "peso", Op = "<", Value = 2 }
                }
            };
            Assert.Equal("Peso mayor a 5 o Peso menor a 2", FilterSummary.Describe(node, Schema()));
        }

        [Fact]
        public void GrupoAnidado_QuedaEntreParentesis()
        {
            var interno = new FilterNode
            {
                Logic = "OR",
                Predicates = new List<FilterPredicate>
                {
                    new FilterPredicate { LinkId = "dolor", Op = "=", Value = true },
                    new FilterPredicate { LinkId = "dolor", Op = "=", Value = false }
                }
            };
            var node = new FilterNode
            {
                Logic = "AND",
                Predicates = new List<FilterPredicate>
                {
                    new FilterPredicate { LinkId = "peso", Op = ">=", Value = 10 },
                    new FilterPredicate { Group = interno }
                }
            };
            Assert.Equal("Peso mayor o igual a 10 y (¿Tiene dolor? es Sí o ¿Tiene dolor? es No)",
                FilterSummary.Describe(node, Schema()));
        }

        [Fact]
        public void Between_MuestraLosDosValoresConY()
        {
            var node = new FilterNode
            {
                Logic = "AND",
                Predicates = new List<FilterPredicate>
                {
                    new FilterPredicate { LinkId = "peso", Op = "between", Value = new object[] { 10, 20 } }
                }
            };
            Assert.Equal("Peso entre 10 y 20", FilterSummary.Describe(node, Schema()));
        }
    }
}
