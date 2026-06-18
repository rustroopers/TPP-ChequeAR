using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChequeAR.Application.Dtos;
using Oracle.ManagedDataAccess.Client;

namespace ChequeAR.Application.Reports
{
    public class QueryBuilder
    {
        private static readonly HashSet<string> ValidOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "=","!=",">","<",">=","<=","in","between","exists","contains"
        };

        public BuiltQuery Build(ReportQuery q)
        {
            if (q.QuestionnaireId == Guid.Empty) throw new ArgumentException("QuestionnaireId required");

            // Identificadores entrecomillados (case-sensitive) para matchear las tablas/columnas
            // que crea EF/ODP.NET con el mismo casing que los nombres .NET. La vista
            // "vw_FlatResponses" se define en db/oracle/schema.sql con estas mismas columnas.
            var sql = new StringBuilder();
            sql.AppendLine("SELECT v.\"ResponseId\", v.\"PatientHospitalId\", v.\"AuthoredAt\", v.\"LinkId\", v.\"ValueDecimal\", v.\"ValueString\", v.\"ValueDate\", v.\"ValueBoolean\", v.\"ValueCode\"");
            sql.AppendLine("FROM \"vw_FlatResponses\" v");
            sql.AppendLine("WHERE v.\"QuestionnaireId\" = :QuestionnaireId");

            var parameters = new List<OracleParameter>
            {
                new OracleParameter("QuestionnaireId", q.QuestionnaireId)
            };

            if (q.DateRange?.From != null)
            {
                sql.AppendLine(" AND v.\"AuthoredAt\" >= :DateFrom");
                parameters.Add(new OracleParameter("DateFrom", q.DateRange.From.Value));
            }
            if (q.DateRange?.To != null)
            {
                sql.AppendLine(" AND v.\"AuthoredAt\" <= :DateTo");
                parameters.Add(new OracleParameter("DateTo", q.DateRange.To.Value));
            }

            if (q.Filters != null && q.Filters.Predicates != null && q.Filters.Predicates.Any())
            {
                sql.Append(" AND ");
                BuildNode(q.Filters, sql, parameters);
            }

            return new BuiltQuery { Sql = sql.ToString(), Parameters = parameters };
        }

        private void BuildNode(FilterNode node, StringBuilder sql, List<OracleParameter> ps)
        {
            var logic = string.Equals(node.Logic, "OR", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
            sql.Append("(");
            bool first = true;
            foreach (var p in node.Predicates)
            {
                if (!first) sql.Append(logic);
                first = false;
                if (p.Group != null)
                {
                    BuildNode(p.Group, sql, ps);
                }
                else
                {
                    if (!ValidOps.Contains(p.Op))
                        throw new ArgumentException("Invalid operator: " + p.Op);
                    var idx = ps.Count;
                    sql.Append("EXISTS (SELECT 1 FROM \"QuestionnaireResponseItem\" qri WHERE qri.\"ResponseId\" = v.\"ResponseId\"");
                    sql.Append(" AND qri.\"LinkId\" = :li").Append(idx);
                    ps.Add(new OracleParameter("li" + idx, p.LinkId));
                    var col = ResolveColumn(p.Value);
                    sql.Append(" AND qri.\"").Append(col).Append("\" ").Append(p.Op).Append(" :p").Append(idx).Append(")");
                    // Oracle no tiene BOOLEAN en tablas: ValueBoolean es NUMBER(1), bindeamos 1/0.
                    object pval = p.Value is bool b ? (b ? 1 : 0) : p.Value;
                    ps.Add(new OracleParameter("p" + idx, pval ?? DBNull.Value));
                }
            }
            sql.Append(")");
        }

        private string ResolveColumn(object value)
        {
            if (value is decimal || value is double || value is float || value is int) return "ValueDecimal";
            if (value is DateTime) return "ValueDate";
            if (value is bool) return "ValueBoolean";
            return "ValueString";
        }
    }

    public class BuiltQuery
    {
        public string Sql { get; set; }
        public List<OracleParameter> Parameters { get; set; }
    }
}
