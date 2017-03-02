using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace MicroRuleEngine
{
    internal static class ExpressionBuilder
    {
        private const string StrIsMatch = "IsMatch";
        private const string StrNull = "null";
        private static readonly string[] StrEnumerableAggregateMethodes = new[] { "Average", "Max", "Min", "Sum" };
        private static readonly string[] StrEnumerableGenericMethodes = new[] { "Count", "LongCount", "First", "FirstOrDefault"
                                                                                , "Last", "LastOrDefault", "Single", "SingleOrDefault" };
        private static readonly string[] StrEnumerableItemMethodes = new[] { "All", "Any" };
        private static readonly Type typeOfNullReferenceException = typeof(NullReferenceException);
        private static readonly Type typeOfBool = typeof(bool);
        private static readonly Type typeOfRegex = typeof(Regex);
        private static readonly Type typeOfString = typeof(string);
        private static readonly Type typeOfRegexOptions = typeof(RegexOptions);

        private static readonly ExpressionType[] NestedOperators =
        {
            ExpressionType.And,
            ExpressionType.AndAlso,
            ExpressionType.Or,
            ExpressionType.OrElse,
        };

        public static Expression Build(Type type, Rule rule, ParameterExpression parameterExpression, bool withValue)
        {
            ExpressionType nestedOperator;
            var isComposite = Enum.TryParse(rule.Operator, out nestedOperator)
                                && NestedOperators.Contains(nestedOperator)
                                && rule.Rules != null
                                && rule.Rules.Any();
            return isComposite
                       ? withValue
                            ? BuildRuleSetValueExpression(rule, Build(type, rule.Rules, parameterExpression, nestedOperator, withValue))
                            : Build(type, rule.Rules, parameterExpression, nestedOperator, withValue)
                       : withValue
                            ? BuildRuleSetValueExpression(rule, BuildExpression(type, rule, parameterExpression))
                            : BuildExpression(type, rule, parameterExpression);
        }

        public static Expression Build(Type type, IEnumerable<Rule> rules, ParameterExpression parameterExpression, ExpressionType operation, bool withValue)
        {
            var expressions = rules.Select(r => Build(type, r, parameterExpression, withValue));
            return Build(expressions, operation);
        }


        private static Expression Build(IEnumerable<Expression> expressions, ExpressionType operationType)
        {
            Func<Expression, Expression, Expression> expressionAggregateMethod;
            switch (operationType)
            {
                case ExpressionType.Or:
                    expressionAggregateMethod = Expression.Or;
                    break;
                case ExpressionType.OrElse:
                    expressionAggregateMethod = Expression.OrElse;
                    break;
                case ExpressionType.AndAlso:
                    expressionAggregateMethod = Expression.AndAlso;
                    break;
                default:
                    expressionAggregateMethod = Expression.And;
                    break;
            }
            return BuildExpression(expressions, expressionAggregateMethod);
        }

        private static Expression BuildExpression(IEnumerable<Expression> expressions, Func<Expression, Expression, Expression> expressionAggregateMethod)
        {
            return expressions.Aggregate<Expression, Expression>(null,
                (current, expression) => current == null
                    ? expression
                    : expressionAggregateMethod(current, expression)
            );
        }


        private static Expression BuildExpression(Type type, Rule rule, Expression expression)
        {
            Expression propExpression = PropertyExpression(type, rule, expression);
            Type propType = propExpression.Type;

            propExpression = Expression.TryCatch(
                Expression.Block(propExpression.Type, propExpression),
                Expression.Catch(typeOfNullReferenceException, Expression.Default(propExpression.Type))
            );

            ExpressionType tBinary;
            // is the operator a known .NET operator?
            if (Enum.TryParse(rule.Operator, out tBinary))
            {
                var right = StringToExpression(rule.TargetValue, propType);
                return Expression.MakeBinary(tBinary, propExpression, right);
            }
            // is the operator a RegEx IsMatch operator?
            if (rule.Operator == StrIsMatch)
            {
                return Expression.Call(
                    typeOfRegex.GetMethod(StrIsMatch,
                        new[]
                        {
                            typeOfString,
                            typeOfString,
                            typeOfRegexOptions
                        }
                    ),
                    propExpression,
                    Expression.Constant(rule.TargetValue, typeOfString),
                    Expression.Constant(RegexOptions.IgnoreCase, typeOfRegexOptions)
                );
            }
            // is the operator a collection item operator?
            if (StrEnumerableItemMethodes.Any(m => m == rule.Operator))
            {
                var elementType = ElementType(propType);
                var lambdaParam = Expression.Parameter(elementType, "lambdaParam");
                return rule?.Rules?.Any() == true
                    ? Expression.Call(
                        typeof(Enumerable).GetMethods().First(m =>
                                    m.Name == rule.Operator
                                    && m.GetParameters().Count() == 2
                        ).MakeGenericMethod(elementType),
                        propExpression,
                        Expression.Lambda(Build(elementType, rule.Rules, lambdaParam, ExpressionType.AndAlso, true), lambdaParam)
                    )
                    : Expression.Call(
                        typeof(Enumerable).GetMethods().First(m =>
                                    m.Name == rule.Operator
                                    && m.GetParameters().Count() == 1
                        ).MakeGenericMethod(elementType),
                        propExpression
                    );
            }
            // Invoke a method on the Property
            var inputs = rule.Inputs.Select(x => x.GetType()).ToArray();
            var methodInfo = propType.GetMethod(rule.Operator, inputs);
            if (!methodInfo.IsGenericMethod)
                inputs = null; // Only pass in type information to a Generic Method
            var expressions = rule.Inputs.Select(Expression.Constant).ToArray();

            return Expression.TryCatch(
                Expression.Block(typeOfBool, Expression.Call(propExpression, rule.Operator, inputs, expressions)),
                Expression.Catch(typeOfNullReferenceException, Expression.Constant(false))
            );
        }

        private static Expression BuildRuleSetValueExpression(Rule rule, Expression exp)
        {
            return rule != null
                ? Expression.Convert(Expression.Assign(
                      Expression.Property(Expression.Constant(rule), "Result"),
                      Expression.Convert(exp, typeof(bool?))), typeof(bool))
                : exp;
        }

        private static Expression PropertyExpression(Type type, Rule rule, Expression expression)
        {
            // Object itself
            if (string.IsNullOrEmpty(rule.MemberName))
            {
                return expression;
            }
            //Child property
            else if (rule.MemberName.Contains('.'))
            {
                var childProperties = rule.MemberName.Split('.');
                var property = type.GetProperty(childProperties[0]);

                Expression propExpression = Expression.PropertyOrField(expression, childProperties[0]);
                for (var i = 1; i < childProperties.Length; i++)
                {
                    if (property == null) continue;
                    property = property.PropertyType.GetProperty(childProperties[i]);
                    if (property == null)
                    {
                        propExpression = EnumarableMethodToExpression(propExpression, childProperties[i]);
                        continue;
                    }
                    propExpression = Expression.PropertyOrField(propExpression, childProperties[i]);
                }
                return propExpression;
            }
            // Property
            else
            {
                if (type.GetProperty(rule.MemberName) == null)
                    return EnumarableMethodToExpression(expression, rule.MemberName);

                return Expression.PropertyOrField(expression, rule.MemberName);
            }
        }

        private static Expression EnumarableMethodToExpression(Expression propExpression, string methodName)
        {
            var elementType = ElementType(propExpression.Type);
            if (propExpression.Type.GetInterface("IEnumerable") != null)
                // Check if it is a collection aggregate method
                if (StrEnumerableAggregateMethodes.Any(m => m == methodName))
                {
                    propExpression = Expression.Call(
                        typeof(Enumerable),
                        methodName,
                        null,
                        propExpression
                    );
                }
                // Check if it is a collection generic method
                else if (StrEnumerableGenericMethodes.Any(m => m == methodName))
                {
                    propExpression = Expression.Call(
                        typeof(Enumerable),
                        methodName,
                        new[] { elementType },
                        propExpression
                    );
                }

            return propExpression;
        }

        private static Expression StringToExpression(string value, Type propType)
        {
            return value.ToLower() == StrNull
                ? Expression.Constant(null)
                : Expression.Constant(propType.IsEnum
                    ? Enum.Parse(propType, value)
                    : Convert.ChangeType(value, propType));
        }

        private static Type ElementType(Type seqType)
        {
            Type ienum = FindIEnumerable(seqType);
            if (ienum == null) return seqType;
            return ienum.GetGenericArguments()[0];
        }

        private static Type FindIEnumerable(Type seqType)
        {
            if (seqType == null || seqType == typeof(string))
                return null;
            if (seqType.IsArray)
                return typeof(IEnumerable<>).MakeGenericType(seqType.GetElementType());
            if (seqType.IsGenericType)
            {
                foreach (Type arg in seqType.GetGenericArguments())
                {
                    Type ienum = typeof(IEnumerable<>).MakeGenericType(arg);
                    if (ienum.IsAssignableFrom(seqType))
                    {
                        return ienum;
                    }
                }
            }
            Type[] ifaces = seqType.GetInterfaces();
            if (ifaces != null && ifaces.Length > 0)
            {
                foreach (Type iface in ifaces)
                {
                    Type ienum = FindIEnumerable(iface);
                    if (ienum != null) return ienum;
                }
            }
            if (seqType.BaseType != null && seqType.BaseType != typeof(object))
            {
                return FindIEnumerable(seqType.BaseType);
            }
            return null;
        }
    }
}
