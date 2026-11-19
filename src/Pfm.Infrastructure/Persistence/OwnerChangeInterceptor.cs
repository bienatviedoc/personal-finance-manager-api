using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pfm.Domain;

namespace Pfm.Infrastructure.Persistence;

/// <summary>
/// Bulk updates (ExecuteUpdate) never pass through SaveChanges, so the owner check there cannot see
/// them. This refuses, before the query is compiled, any bulk update that sets the owner of
/// user-owned rows. The query filter already limits which rows a bulk update reaches.
/// </summary>
internal sealed class OwnerChangeInterceptor : ExpressionVisitor, IQueryExpressionInterceptor
{
    public static readonly OwnerChangeInterceptor Instance = new();

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        Visit(queryExpression);

    protected override Expression VisitMethodCall(MethodCallExpression call)
    {
        if (call.Method.Name == "ExecuteUpdate")
        {
            // EF 10 passes the setters as `new Tuple(row => row.Property, value)` entries.
            var setters = call.Arguments.OfType<NewArrayExpression>().SelectMany(array => array.Expressions).OfType<NewExpression>();
            foreach (var setter in setters)
            {
                if (SetsOwner(setter.Arguments[0]))
                {
                    throw new InvalidOperationException("User-owned data cannot be changed: its owner cannot change.");
                }
            }
        }

        return base.VisitMethodCall(call);
    }

    private static bool SetsOwner(Expression propertySelector)
    {
        while (propertySelector is UnaryExpression { NodeType: ExpressionType.Quote or ExpressionType.Convert } wrapped)
        {
            propertySelector = wrapped.Operand;
        }

        if (propertySelector is not LambdaExpression { Body: var body, Parameters: [var row] }) return false;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } converted) body = converted.Operand;

        return typeof(IUserOwned).IsAssignableFrom(row.Type)
            && body is MemberExpression { Member.Name: nameof(IUserOwned.UserId) };
    }
}
