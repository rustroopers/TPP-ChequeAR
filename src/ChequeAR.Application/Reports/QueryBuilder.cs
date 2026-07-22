using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using ChequeAR.Application.Dtos;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

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
            sql.AppendLine("SELECT v.\"ResponseId\", v.\"PatientHospitalId\", v.\"AuthoredAt\", v.\"LinkId\", v.\"ValueDecimal\", v.\"ValueString\", v.\"ValueDate\", v.\"ValueBoolean\", v.\"ValueCode\", v.\"ValueText\"");
            sql.AppendLine("FROM \"vw_FlatResponses\" v");
            sql.AppendLine("WHERE v.\"QuestionnaireId\" = :QuestionnaireId");

            var parameters = new List<OracleParameter>
            {
                GuidParameter("QuestionnaireId", q.QuestionnaireId)
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

                    if (string.Equals(p.Op, "contains", StringComparison.OrdinalIgnoreCase))
                    {
                        sql.Append(" AND qri.\"ValueString\" LIKE :p").Append(idx).Append(")");
                        ps.Add(new OracleParameter("p" + idx, "%" + (p.Value?.ToString() ?? string.Empty) + "%"));
                    }
                    else
                    {
                        var col = ResolveColumn(p.Value);
                        sql.Append(" AND qri.\"").Append(col).Append("\" ").Append(p.Op).Append(" :p").Append(idx).Append(")");
                        // Oracle no tiene BOOLEAN en tablas: ValueBoolean es NUMBER(1), bindeamos 1/0.
                        object pval = p.Value is bool b ? (b ? 1 : 0) : p.Value;
                        ps.Add(new OracleParameter("p" + idx, pval ?? DBNull.Value));
                    }
                }
            }
            sql.Append(")");
        }

        /// <summary>
        /// ODP.NET no sabe bindear un Guid: new OracleParameter(name, guid) tira
        /// ArgumentException ("El valor no está dentro del intervalo esperado") y toda
        /// consulta de reportes fallaba antes de llegar a la base. EF guarda los Guid como
        /// RAW(16), así que se bindea explícitamente como bytes.
        /// </summary>
        private static OracleParameter GuidParameter(string name, Guid value)
        {
            return new OracleParameter(name, OracleDbType.Raw, 16, value.ToByteArray(), ParameterDirection.Input);
        }

        private string ResolveColumn(object value)
        {
            // JSON deserializa enteros como long y reales como double: contemplarlos todos.
            if (value is decimal || value is double || value is float ||
                value is int || value is long || value is short || value is byte) return "ValueDecimal";
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
