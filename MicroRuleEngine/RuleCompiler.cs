using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace MicroRuleEngine
{
    public static class RuleCompiler
    {
        public static Func<T, bool> Compile<T>(Rule rule, bool withValue = false)
        {
            var expressionParameter = Expression.Parameter(typeof(T));
            var expression = ExpressionBuilder.Build<T>(rule, expressionParameter, withValue);
            return Expression.Lambda<Func<T, bool>>(expression, expressionParameter).Compile();
        }

        public static Func<T, bool> Compile<T>(IEnumerable<Rule> rules, bool withValue = false)
        {
            var expressionParameter = Expression.Parameter(typeof(T));
            var expression = ExpressionBuilder.Build<T>(rules, expressionParameter, ExpressionType.And, withValue);
            return Expression.Lambda<Func<T, bool>>(expression, expressionParameter).Compile();
        }
    }
}