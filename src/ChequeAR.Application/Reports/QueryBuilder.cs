using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using ChequeAR.Application.Dtos;

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

            var sql = new StringBuilder();
            sql.AppendLine("SELECT v.ResponseId, v.PatientHospitalId, v.AuthoredAt, v.LinkId, v.ValueDecimal, v.ValueString, v.ValueDate, v.ValueBoolean, v.ValueCode, v.ValueText");
            sql.AppendLine("FROM vw_FlatResponses v");
            sql.AppendLine("WHERE v.QuestionnaireId = @QuestionnaireId");

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@QuestionnaireId", q.QuestionnaireId)
            };

            if (q.DateRange?.From != null)
            {
                sql.AppendLine(" AND v.AuthoredAt >= @DateFrom");
                parameters.Add(new SqlParameter("@DateFrom", q.DateRange.From.Value));
            }
            if (q.DateRange?.To != null)
            {
                sql.AppendLine(" AND v.AuthoredAt <= @DateTo");
                parameters.Add(new SqlParameter("@DateTo", q.DateRange.To.Value));
            }

            if (q.Filters != null && q.Filters.Predicates != null && q.Filters.Predicates.Any())
            {
                sql.Append(" AND ");
                BuildNode(q.Filters, sql, parameters);
            }

            return new BuiltQuery { Sql = sql.ToString(), Parameters = parameters };
        }

        private void BuildNode(FilterNode node, StringBuilder sql, List<SqlParameter> ps)
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
                    var pName = "@p" + ps.Count;
                    sql.Append("EXISTS (SELECT 1 FROM QuestionnaireResponseItems qri WHERE qri.ResponseId = v.ResponseId");
                    sql.Append(" AND qri.LinkId = ").Append("@li").Append(ps.Count);
                    ps.Add(new SqlParameter("@li" + (ps.Count), p.LinkId));
                    var col = ResolveColumn(p.Value);
                    sql.Append(" AND qri.").Append(col).Append(" ").Append(p.Op).Append(" ").Append(pName).Append(")");
                    ps.Add(new SqlParameter(pName, p.Value ?? DBNull.Value));
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
        public List<SqlParameter> Parameters { get; set; }
    }
}
