using System.Collections.Generic;
using System.Linq;

namespace MicroRuleEngine
{
    public class Rule
    {
        public Rule()
        {
            Inputs = Enumerable.Empty<object>();
        }

        public string MemberName { get; set; }
        public string Operator { get; set; }
        public string TargetValue { get; set; }
        public IEnumerable<Rule> Rules { get; set; }
        public IEnumerable<object> Inputs { get; set; }

        public string Name { get; set; }
        public string Message { get; set; }
        public bool? Result { get; set; }

        public override string ToString()
        {
            return RuleResults(this);
        }

        public void ClearResult()
        {
            RuleClear(this);
        }

        private static string RuleResults(Rule rule, string result = "", int level = 0)
        {
            result += $"{new string('|', level)}{rule.Name} --> {rule.Result.ToString() ?? "Not Evaluted"}" +
                            (string.IsNullOrWhiteSpace(rule.Message) ? "\r\n" : $" : {rule.Message}\r\n");
            if (rule.Rules != null)
                foreach (var r in rule.Rules)
                    result = RuleResults(r, result, level + 1);
            return result;
        }

        private static void RuleClear(Rule rule)
        {
            if (rule.Rules != null)
                foreach (var r in rule.Rules)
                    RuleClear(r);
            rule.Result = null;
        }
    }
}