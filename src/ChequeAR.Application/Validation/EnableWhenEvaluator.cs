using System;
using System.Collections.Generic;

namespace ChequeAR.Application.Validation
{
    public class EnableWhenEvaluator
    {
        public bool IsEnabled(EnableWhenRule rule, IReadOnlyDictionary<string, object> answers)
        {
            if (rule == null) return true;
            if (!answers.TryGetValue(rule.QuestionLinkId, out var actual)) return false;
            switch (rule.Operator)
            {
                case "exists": return actual != null;
                case "=":  return Equals(actual, rule.Answer);
                case "!=": return !Equals(actual, rule.Answer);
                case ">":  return Compare(actual, rule.Answer) > 0;
                case "<":  return Compare(actual, rule.Answer) < 0;
                case ">=": return Compare(actual, rule.Answer) >= 0;
                case "<=": return Compare(actual, rule.Answer) <= 0;
                default: throw new NotSupportedException("Op: " + rule.Operator);
            }
        }

        private static int Compare(object a, object b)
        {
            if (a is IComparable ca && b is IComparable cb) return ca.CompareTo(cb);
            return string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal);
        }
    }

    public class EnableWhenRule
    {
        public string QuestionLinkId { get; set; }
        public string Operator { get; set; }
        public object Answer { get; set; }
    }
}
