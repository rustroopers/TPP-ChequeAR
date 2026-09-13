using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ChequeAR.Application.Dtos;

namespace ChequeAR.Application.Reports
{
    public class QueryBuilder
    {
        // Marcador del alias de la vista dentro del WHERE. El mismo bloque de condiciones se
        // emite dos veces —una para traer las filas y otra para paginar/contar— con alias
        // distintos, pero los parámetros se registran una sola vez (BindByName).
        private const string Alias = "{A}";

        private static readonly HashSet<string> ValidOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "=","!=",">","<",">=","<=","in","between","exists","contains"
        };

        private const string Projection =
            "v.\"ResponseId\", v.\"PatientHospitalId\", v.\"AuthoredAt\", v.\"QuestionnaireVersion\", " +
            "v.\"AuthorUserId\", v.\"LinkId\", v.\"ValueDecimal\", v.\"ValueInteger\", v.\"ValueString\", " +
            "v.\"ValueDate\", v.\"ValueBoolean\", v.\"ValueCode\", v.\"ValueText\"";

        /// <param name="paginate">
        /// La previsualización pagina; la exportación no (si no, el archivo saldría cortado
        /// en la primera página sin que nadie lo note).
        /// </param>
        // No se marca static (aunque no toque estado de instancia): QueryBuilder se registra
        // e inyecta por DI en ReportExecutor (ver ServiceComposition.cs) precisamente para
        // poder sustituirse; forzar static rompería esa inyección.
#pragma warning disable CA1822
        public BuiltQuery Build(ReportQuery q, ReportSchema schema, bool paginate = true)
        {
#pragma warning restore CA1822
            if (q == null) throw new ArgumentNullException(nameof(q));
            if (q.QuestionnaireId == Guid.Empty) throw new ArgumentException("QuestionnaireId required");
            if (schema == null) throw new ArgumentException("Falta la metadata del cuestionario.");

            var ps = new List<QueryParameter> { GuidParameter("QuestionnaireId", q.QuestionnaireId) };
            var where = BuildWhere(q, schema, ps);

            var sql = new StringBuilder();
            sql.AppendLine("SELECT " + Projection);
            sql.AppendLine("FROM \"vw_FlatResponses\" v");
            sql.AppendLine("WHERE " + where.Replace(Alias, "v"));

            if (paginate)
            {
                // Se pagina por FICHA, no por fila: el resultado viene en formato largo (una
                // fila por respuesta-pregunta), asi que un OFFSET plano cortaria una ficha al
                // medio y el pivot devolveria una fila incompleta.
                var take = q.PageSize > 0 ? q.PageSize : 50;
                var skip = (q.Page > 0 ? q.Page - 1 : 0) * take;
                sql.AppendLine("  AND v.\"ResponseId\" IN (");
                sql.AppendLine("    SELECT \"ResponseId\" FROM (");
                sql.AppendLine("      SELECT DISTINCT p.\"ResponseId\", p.\"AuthoredAt\"");
                sql.AppendLine("      FROM \"vw_FlatResponses\" p");
                sql.AppendLine("      WHERE " + where.Replace(Alias, "p"));
                sql.AppendLine("      ORDER BY p.\"AuthoredAt\" DESC, p.\"ResponseId\"");
                sql.AppendLine("      OFFSET :Skip ROWS FETCH NEXT :Take ROWS ONLY");
                sql.AppendLine("    )");
                sql.AppendLine("  )");
                ps.Add(QueryParameter.Int("Skip", skip));
                ps.Add(QueryParameter.Int("Take", take));
            }

            // Orden estable: sin ORDER BY explicito Oracle no garantiza nada, y el pivot
            // conserva el orden de aparicion, asi que la tabla se reordenaba sola entre
            // ejecuciones identicas.
            sql.AppendLine("ORDER BY v.\"AuthoredAt\" DESC, v.\"ResponseId\", v.\"LinkId\"");

            // Total real de fichas de la cohorte, independiente de la pagina traida. Antes
            // se informaba rows.Count del formato largo, o sea fichas x preguntas.
            var countSql = "SELECT COUNT(DISTINCT v.\"ResponseId\") FROM \"vw_FlatResponses\" v WHERE "
                           + where.Replace(Alias, "v");

            return new BuiltQuery { Sql = sql.ToString(), CountSql = countSql, Parameters = ps };
        }

        private static string BuildWhere(ReportQuery q, ReportSchema schema, List<QueryParameter> ps)
        {
            var sb = new StringBuilder();
            sb.Append(Alias).Append(".\"QuestionnaireId\" = :QuestionnaireId");

            // Si "desde" quedó después de "hasta" (rango invertido), se intercambian acá:
            // nunca debe llegar a ejecutarse un rango que estructuralmente no puede traer filas.
            if (q.DateRange != null && q.DateRange.From.HasValue && q.DateRange.To.HasValue
                && q.DateRange.From.Value > q.DateRange.To.Value)
            {
                var tmp = q.DateRange.From;
                q.DateRange.From = q.DateRange.To;
                q.DateRange.To = tmp;
            }

            if (q.DateRange?.From != null)
            {
                sb.Append(" AND ").Append(Alias).Append(".\"AuthoredAt\" >= :DateFrom");
                ps.Add(QueryParameter.Date("DateFrom", q.DateRange.From.Value));
            }
            if (q.DateRange?.To != null)
            {
                sb.Append(" AND ").Append(Alias).Append(".\"AuthoredAt\" <= :DateTo");
                ps.Add(QueryParameter.Date("DateTo", q.DateRange.To.Value));
            }

            // Version del cuestionario con la que se completo la ficha. La vista ya expone la
            // columna; hasta ahora el campo viajaba en el DTO y no lo leia nadie.
            var versions = (q.QuestionnaireVersions ?? new List<int>()).Distinct().ToList();
            if (versions.Count > 0)
            {
                sb.Append(" AND ").Append(Alias).Append(".\"QuestionnaireVersion\" IN (");
                for (int i = 0; i < versions.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(":qv").Append(i);
                    ps.Add(QueryParameter.Int("qv" + i, versions[i]));
                }
                sb.Append(')');
            }

            var authors = (q.Authors ?? new List<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
            if (authors.Count > 0)
            {
                sb.Append(" AND ").Append(Alias).Append(".\"AuthorUserId\" IN (");
                for (int i = 0; i < authors.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(":au").Append(i);
                    ps.Add(QueryParameter.Text("au" + i, authors[i]));
                }
                sb.Append(')');
            }

            if (q.Filters?.Predicates != null && q.Filters.Predicates.Count > 0)
            {
                sb.Append(" AND ");
                BuildNode(q.Filters, schema, sb, ps);
            }
            return sb.ToString();
        }

        private static void BuildNode(FilterNode node, ReportSchema schema, StringBuilder sql, List<QueryParameter> ps)
        {
            var logic = string.Equals(node.Logic, "OR", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
            sql.Append('(');
            bool first = true;
            foreach (var p in node.Predicates)
            {
                if (!first) sql.Append(logic);
                first = false;

                if (p.Group != null) { BuildNode(p.Group, schema, sql, ps); continue; }

                if (!ValidOps.Contains(p.Op))
                    throw new ArgumentException("Operador inválido: " + p.Op);

                // El tipo sale de la PREGUNTA, no del valor que mando el cliente. Tambien
                // valida que el linkId pertenezca al cuestionario.
                var col = schema.ColumnFor(p.LinkId);
                var idx = ps.Count;

                sql.Append("EXISTS (SELECT 1 FROM \"QuestionnaireResponseItem\" qri")
                   .Append(" WHERE qri.\"ResponseId\" = ").Append(Alias).Append(".\"ResponseId\"")
                   .Append(" AND qri.\"LinkId\" = :li").Append(idx);
                ps.Add(QueryParameter.Text("li" + idx, p.LinkId));

                sql.Append(" AND ").Append(Predicate(p, schema, col, idx, ps)).Append(')');
            }
            sql.Append(')');
        }

        /// <summary>Condición sobre la columna tipada, según el operador.</summary>
        private static string Predicate(FilterPredicate p, ReportSchema schema, string col, int idx, List<QueryParameter> ps)
        {
            var target = "qri.\"" + col + "\"";

            if (string.Equals(p.Op, "exists", StringComparison.OrdinalIgnoreCase))
                return target + " IS NOT NULL";   // "la pregunta fue respondida"

            if (string.Equals(p.Op, "contains", StringComparison.OrdinalIgnoreCase))
            {
                ps.Add(QueryParameter.Text("p" + idx, "%" + Convert.ToString(p.Value, CultureInfo.InvariantCulture) + "%"));
                return "UPPER(" + target + ") LIKE UPPER(:p" + idx + ")";
            }

            if (string.Equals(p.Op, "in", StringComparison.OrdinalIgnoreCase))
            {
                var values = AsList(p.Value);
                if (values.Count == 0) throw new ArgumentException("El operador 'in' necesita al menos un valor.");
                var names = new List<string>();
                for (int i = 0; i < values.Count; i++)
                {
                    var name = "p" + idx + "_" + i;
                    names.Add(":" + name);
                    ps.Add(QueryParameter.Text(name, schema.Coerce(p.LinkId, values[i]) ?? DBNull.Value));
                }
                return target + " IN (" + string.Join(", ", names) + ")";
            }

            if (string.Equals(p.Op, "between", StringComparison.OrdinalIgnoreCase))
            {
                var values = AsList(p.Value);
                if (values.Count != 2) throw new ArgumentException("El operador 'between' necesita exactamente dos valores.");
                ps.Add(QueryParameter.Text("p" + idx + "_lo", schema.Coerce(p.LinkId, values[0]) ?? DBNull.Value));
                ps.Add(QueryParameter.Text("p" + idx + "_hi", schema.Coerce(p.LinkId, values[1]) ?? DBNull.Value));
                return target + " BETWEEN :p" + idx + "_lo AND :p" + idx + "_hi";
            }

            ps.Add(QueryParameter.Text("p" + idx, schema.Coerce(p.LinkId, p.Value) ?? DBNull.Value));
            return target + " " + p.Op + " :p" + idx;
        }

        /// <summary>
        /// Los valores de 'in'/'between' llegan como array JSON. Newtonsoft los materializa
        /// en un JArray cuando el destino es object, no en un List.
        /// </summary>
        private static List<object> AsList(object value)
        {
            if (value == null) return new List<object>();
            if (value is string s) return new List<object> { s };
            if (value is IEnumerable en) return en.Cast<object>().ToList();
            return new List<object> { value };
        }

        /// <summary>
        /// El identificador viaja como los 16 bytes crudos porque es así como está guardado
        /// (EF los persiste como RAW(16)); convertirlo a texto haría que la comparación nunca
        /// matchee. El driver además no sabe bindear un Guid directamente: tiraba
        /// ArgumentException ("El valor no está dentro del intervalo esperado") y toda
        /// consulta de reportes fallaba antes de llegar a la base.
        ///
        /// Esa segunda razón es del motor y ya no se nombra acá: la traducción concreta la
        /// hace la capa de infraestructura. Lo que queda es la decisión de negocio —comparar
        /// contra la representación real del identificador— expresada con un tipo neutro.
        /// </summary>
        private static QueryParameter GuidParameter(string name, Guid value)
            => QueryParameter.Id(name, value);
    }

    public class BuiltQuery
    {
        public string Sql { get; set; }
        /// <summary>Cuenta de fichas distintas que matchean, sin paginar.</summary>
        public string CountSql { get; set; }
        public List<QueryParameter> Parameters { get; set; }
    }
}
