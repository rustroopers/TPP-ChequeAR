using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Dtos;
using ChequeAR.Application.Reports;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class ReportSchemaTests
    {
        // El bug de fondo: la columna se deducia del tipo CLR del valor que mandaba el
        // cliente. Una opcion unica se guarda en ValueCode, pero el filtro llegaba como
        // string y consultaba ValueString -> NULL para esas respuestas -> cero filas y
        // ningun error. El tipo tiene que salir de la PREGUNTA.
        [Theory]
        [InlineData(QuestionType.Choice, "ValueCode")]
        [InlineData(QuestionType.OpenChoice, "ValueCode")]
        [InlineData(QuestionType.Integer, "ValueInteger")]
        [InlineData(QuestionType.Decimal, "ValueDecimal")]
        [InlineData(QuestionType.Scale, "ValueDecimal")]
        [InlineData(QuestionType.Boolean, "ValueBoolean")]
        [InlineData(QuestionType.Date, "ValueDate")]
        [InlineData(QuestionType.DateTime, "ValueDate")]
        [InlineData(QuestionType.String, "ValueString")]
        [InlineData(QuestionType.Text, "ValueString")]
        public void ColumnFor_SaleDelTipoDeLaPregunta(QuestionType tipo, string columna)
        {
            var schema = SchemaCon(("q1", tipo));
            Assert.Equal(columna, schema.ColumnFor("q1"));
        }

        [Fact]
        public void ColumnFor_Adjunto_NoEsFiltrable()
        {
            var schema = SchemaCon(("q1", QuestionType.Attachment));
            Assert.Throws<ArgumentException>(() => schema.ColumnFor("q1"));
        }

        // Un linkId que no pertenece al cuestionario era una cohorte vacia silenciosa.
        [Fact]
        public void LinkIdDesconocido_Explota()
        {
            var schema = SchemaCon(("q1", QuestionType.String));
            Assert.False(schema.Knows("inventado"));
            Assert.Throws<ArgumentException>(() => schema.ColumnFor("inventado"));
        }

        // Todo lo que sale de un <input> llega como string, aunque la pregunta sea numerica.
        [Fact]
        public void Coerce_NumeroComoTexto_SeConvierte()
        {
            var schema = SchemaCon(("edad", QuestionType.Integer));
            Assert.Equal(12m, schema.Coerce("edad", "12"));
        }

        // Coma decimal: es lo que tipea un usuario es-AR.
        [Fact]
        public void Coerce_DecimalConComa_SeConvierte()
        {
            var schema = SchemaCon(("ta", QuestionType.Decimal));
            Assert.Equal(3.5m, schema.Coerce("ta", "3,5"));
        }

        [Fact]
        public void Coerce_NumeroInvalido_Explota()
        {
            var schema = SchemaCon(("edad", QuestionType.Integer));
            Assert.Throws<ArgumentException>(() => schema.Coerce("edad", "doce"));
        }

        // Oracle no tiene BOOLEAN en tablas: ValueBoolean es NUMBER(1).
        [Fact]
        public void Coerce_Booleano_QuedaEnUnoOCero()
        {
            var schema = SchemaCon(("fuma", QuestionType.Boolean));
            Assert.Equal(1, schema.Coerce("fuma", true));
            Assert.Equal(0, schema.Coerce("fuma", false));
            Assert.Equal(1, schema.Coerce("fuma", "true"));
        }

        [Fact]
        public void Columns_RespetaOrderIndex()
        {
            var q = new Questionnaire();
            q.Questions.Add(new Question { LinkId = "b", Text = "Segunda", Type = QuestionType.String, OrderIndex = 1 });
            q.Questions.Add(new Question { LinkId = "a", Text = "Primera", Type = QuestionType.String, OrderIndex = 0 });

            var cols = ReportSchema.From(q).Columns;

            Assert.Equal(new[] { "a", "b" }, cols.Select(c => c.Key));
            Assert.Equal(new[] { "Primera", "Segunda" }, cols.Select(c => c.Value));
        }

        // Segun quien haya cargado la ficha, la respuesta guardo el codigo de la opcion o su
        // texto. Sin normalizar, la misma categoria aparece como dos barras distintas.
        [Fact]
        public void LabelFor_CodigoYTextoLlevanALaMismaEtiqueta()
        {
            var schema = SchemaConOpciones("subtipo",
                ("oligoarticular", "Oligoarticular"),
                ("entesitis", "Entesitis"));

            Assert.Equal("Oligoarticular", schema.LabelFor("subtipo", "oligoarticular"));
            Assert.Equal("Oligoarticular", schema.LabelFor("subtipo", "Oligoarticular"));
            Assert.Equal("Entesitis", schema.LabelFor("subtipo", "entesitis"));
        }

        // El seed slugificaba: 'Poliarticular FR+' y 'Poliarticular FR-' daban los dos
        // 'poliarticular_fr_'. Resolver ese codigo a cualquiera de las dos etiquetas seria
        // inventar la mitad de las veces, asi que se muestra crudo y el problema se ve.
        [Fact]
        public void LabelFor_CodigoAmbiguo_NoInventa()
        {
            var schema = SchemaConOpciones("subtipo",
                ("poliarticular_fr_", "Poliarticular FR+"),
                ("poliarticular_fr_", "Poliarticular FR-"));

            Assert.Equal("poliarticular_fr_", schema.LabelFor("subtipo", "poliarticular_fr_"));
        }

        [Fact]
        public void LabelFor_ValorDesconocido_PasaCrudo()
        {
            var schema = SchemaConOpciones("subtipo", ("a", "Alfa"));
            Assert.Equal("otra cosa", schema.LabelFor("subtipo", "otra cosa"));
        }

        private static ReportSchema SchemaConOpciones(string linkId, params (string code, string display)[] opciones)
        {
            var q = new Questionnaire();
            var pregunta = new Question { LinkId = linkId, Text = linkId, Type = QuestionType.Choice, OrderIndex = 0 };
            var i = 0;
            foreach (var (code, display) in opciones)
                pregunta.Options.Add(new AnswerOption { Code = code, Value = display, Display = display, OrderIndex = i++ });
            q.Questions.Add(pregunta);
            return ReportSchema.From(q);
        }

        internal static ReportSchema SchemaCon(params (string linkId, QuestionType tipo)[] preguntas)
        {
            var q = new Questionnaire();
            var i = 0;
            foreach (var (linkId, tipo) in preguntas)
                q.Questions.Add(new Question { LinkId = linkId, Text = linkId, Type = tipo, OrderIndex = i++ });
            return ReportSchema.From(q);
        }
    }

    public class QueryBuilderTests
    {
        private static ReportQuery Query(FilterNode filtros = null) => new ReportQuery
        {
            QuestionnaireId = Guid.NewGuid(),
            Filters = filtros
        };

        private static FilterNode Un(string linkId, string op, object value) => new FilterNode
        {
            Logic = "AND",
            Predicates = new List<FilterPredicate> { new FilterPredicate { LinkId = linkId, Op = op, Value = value } }
        };

        // Estos tres operadores estaban declarados en ValidOps pero BuildNode los emitia
        // como `col <op> :param`, o sea `ValueDecimal in :p0` y `ValueString exists :p0`:
        // SQL invalido que moria en Oracle.
        [Fact]
        public void In_GeneraUnBindPorValor()
        {
            var schema = ReportSchemaTests.SchemaCon(("dx", QuestionType.Choice));
            var built = new QueryBuilder().Build(Query(Un("dx", "in", new List<object> { "A", "B", "C" })), schema);

            Assert.Contains("IN (:p1_0, :p1_1, :p1_2)", built.Sql);
            Assert.Equal(3, built.Parameters.Count(p => p.Name.StartsWith("p1_", StringComparison.Ordinal)));
        }

        [Fact]
        public void Between_GeneraDosBinds()
        {
            var schema = ReportSchemaTests.SchemaCon(("edad", QuestionType.Integer));
            var built = new QueryBuilder().Build(Query(Un("edad", "between", new List<object> { 5, 12 })), schema);

            Assert.Contains("BETWEEN :p1_lo AND :p1_hi", built.Sql);
        }

        [Fact]
        public void Between_ConCantidadDistintaDeDos_Explota()
        {
            var schema = ReportSchemaTests.SchemaCon(("edad", QuestionType.Integer));
            Assert.Throws<ArgumentException>(() =>
                new QueryBuilder().Build(Query(Un("edad", "between", new List<object> { 5 })), schema));
        }

        // "exists" pregunta si la variable fue respondida, no compara contra un valor.
        [Fact]
        public void Exists_NoBindeaValor()
        {
            var schema = ReportSchemaTests.SchemaCon(("dx", QuestionType.Choice));
            var built = new QueryBuilder().Build(Query(Un("dx", "exists", null)), schema);

            Assert.Contains("qri.\"ValueCode\" IS NOT NULL", built.Sql);
            Assert.DoesNotContain(":p1", built.Sql);
        }

        [Fact]
        public void OperadorInvalido_Explota()
        {
            var schema = ReportSchemaTests.SchemaCon(("dx", QuestionType.Choice));
            Assert.Throws<ArgumentException>(() =>
                new QueryBuilder().Build(Query(Un("dx", "; DROP TABLE", "x")), schema));
        }

        // Los dos filtros de metadata que el DTO transportaba y nadie leia (FR-004).
        [Fact]
        public void FiltraPorVersionDelCuestionario()
        {
            var q = Query();
            q.QuestionnaireVersions = new List<int> { 1, 3 };
            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.Contains("\"QuestionnaireVersion\" IN (:qv0, :qv1)", built.Sql);
        }

        [Fact]
        public void FiltraPorAutor()
        {
            var q = Query();
            q.Authors = new List<string> { "dr.perez" };
            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.Contains("\"AuthorUserId\" IN (:au0)", built.Sql);
        }

        // El total tiene que ser de FICHAS. Antes se informaba rows.Count del formato largo,
        // o sea fichas x preguntas.
        [Fact]
        public void CountEsPorFichaDistinta()
        {
            var built = new QueryBuilder().Build(Query(), ReportSchemaTests.SchemaCon(("a", QuestionType.String)));
            Assert.Contains("COUNT(DISTINCT v.\"ResponseId\")", built.CountSql);
        }

        // Se pagina por ficha: un OFFSET plano sobre el formato largo cortaria una ficha al
        // medio y el pivot devolveria una fila incompleta.
        [Fact]
        public void PaginaPorFicha()
        {
            var q = Query();
            q.Page = 2; q.PageSize = 25;
            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.Contains("OFFSET :Skip ROWS FETCH NEXT :Take ROWS ONLY", built.Sql);
            Assert.Contains("v.\"ResponseId\" IN (", built.Sql);
            Assert.Equal(25, built.Parameters.Single(p => p.Name == "Skip").Value);
            Assert.Equal(25, built.Parameters.Single(p => p.Name == "Take").Value);
        }

        [Fact]
        public void PageSize_SeAcotaAlMaximo()
        {
            var q = Query();
            q.PageSize = 1000000;
            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.Equal(QueryBuilder.MaxPageSize, built.Parameters.Single(p => p.Name == "Take").Value);
        }

        [Fact]
        public void PaginaAbsurda_NoDesbordaElOffset()
        {
            var q = Query();
            q.Page = int.MaxValue; q.PageSize = 50;
            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.True((int)built.Parameters.Single(p => p.Name == "Skip").Value >= 0);
        }

        // La exportacion no pagina: un archivo cortado en la primera pagina seria peor que
        // un error, porque nadie lo notaria.
        [Fact]
        public void ExportacionNoPagina()
        {
            var built = new QueryBuilder().Build(Query(), ReportSchemaTests.SchemaCon(("a", QuestionType.String)), paginate: false);
            Assert.DoesNotContain("OFFSET", built.Sql);
        }

        [Fact]
        public void SinQuestionnaireId_Explota()
        {
            Assert.Throws<ArgumentException>(() =>
                new QueryBuilder().Build(new ReportQuery(), ReportSchemaTests.SchemaCon(("a", QuestionType.String))));
        }

        // Un rango invertido (desde > hasta) nunca debe ejecutarse tal cual: filtraria una
        // cohorte estructuralmente vacia sin avisar. Se intercambian los extremos antes de
        // armar el WHERE.
        [Fact]
        public void RangoDeFechasInvertido_SeIntercambia()
        {
            var q = Query();
            var tarde = new DateTime(2026, 6, 1);
            var temprano = new DateTime(2026, 1, 1);
            q.DateRange = new DateRange { From = tarde, To = temprano };

            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.Equal(new DateTime(2026, 1, 1, 3, 0, 0), built.Parameters.Single(p => p.Name == "DateFrom").Value);
            Assert.Equal(new DateTime(2026, 6, 2, 3, 0, 0), built.Parameters.Single(p => p.Name == "DateTo").Value);
        }

        // El rango son días de Argentina contra una columna en UTC. La isla manda
        // T00:00:00 / T23:59:59 sin zona; lo que cuenta es el día.
        [Fact]
        public void RangoDeFechas_SeInterpretaComoDiasDeArgentina()
        {
            var q = Query();
            q.DateRange = new DateRange
            {
                From = new DateTime(2026, 8, 10, 0, 0, 0),
                To = new DateTime(2026, 8, 10, 23, 59, 59)
            };

            var built = new QueryBuilder().Build(q, ReportSchemaTests.SchemaCon(("a", QuestionType.String)));

            Assert.Contains("\"AuthoredAt\" >= :DateFrom", built.Sql);
            Assert.Contains("\"AuthoredAt\" < :DateTo", built.Sql);
            Assert.Equal(new DateTime(2026, 8, 10, 3, 0, 0), built.Parameters.Single(p => p.Name == "DateFrom").Value);
            Assert.Equal(new DateTime(2026, 8, 11, 3, 0, 0), built.Parameters.Single(p => p.Name == "DateTo").Value);
        }

        // '%' y '_' son comodines de LIKE: buscar "50%" tiene que buscar ese texto, no "50" + lo que sea.
        [Theory]
        [InlineData("50%", "%50\\%%")]
        [InlineData("a_b", "%a\\_b%")]
        [InlineData("c\\d", "%c\\\\d%")]
        [InlineData("dolor", "%dolor%")]
        public void Contains_EscapaLosComodinesDeLike(string buscado, string esperado)
        {
            var schema = ReportSchemaTests.SchemaCon(("nota", QuestionType.String));

            var built = new QueryBuilder().Build(Query(Un("nota", "contains", buscado)), schema);

            Assert.Contains("ESCAPE '\\'", built.Sql);
            Assert.Contains(built.Parameters, p => Equals(p.Value, esperado));
        }
    }
}
