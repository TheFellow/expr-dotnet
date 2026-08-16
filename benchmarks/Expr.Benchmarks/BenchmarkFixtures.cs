using System;
using System.Collections.Generic;
using System.Linq;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;

namespace Expr.Benchmarks;

internal static class BenchmarkFixtures
{
    internal static PolicyEnvironment Environment { get; } = CreateEnvironment();

    internal static ExprConfiguration Configuration { get; } = ExprConfiguration.Default.WithEnvironment(
        new ExprEnvironmentSchemaBuilder<PolicyEnvironment>()
            .Member("Origin", static environment => environment.Origin, ExprTypes.String)
            .Member("Country", static environment => environment.Country, ExprTypes.String)
            .Member("Adults", static environment => environment.Adults, ExprTypes.Integer)
            .Member("Value", static environment => environment.Value, ExprTypes.Integer)
            .Member("Active", static environment => environment.Active, ExprTypes.Boolean)
            .Member("Email", static environment => environment.Email, ExprTypes.String)
            .Member("Pattern", static environment => environment.Pattern, ExprTypes.String)
            .Member("Values", static environment => environment.Values, ExprTypes.ArrayOf(ExprTypes.Integer))
            .Member(
                "Labels",
                static environment => environment.Labels,
                new MapTypeDescriptor([], ExprTypes.String, ExprTypes.String))
            .Member(
                "Price",
                static environment => environment.Price,
                new ObjectTypeDescriptor(typeof(PriceValue)))
            .Build());

    private static PolicyEnvironment CreateEnvironment()
    {
        long[] values = [.. Enumerable.Range(1, 1_000).Select(static value => (long)value)];
        var labels = new ExprMap(
        [
            new KeyValuePair<object?, object?>("region", "west"),
            new KeyValuePair<object?, object?>("tier", "gold"),
        ]);
        return new PolicyEnvironment(
            "MOW",
            "RU",
            1,
            100,
            true,
            "ada@example.com",
            "^[a-z]+@[a-z]+\\.[a-z]+$",
            ExprCollections.AsArray(values),
            labels,
            new PriceValue(125));
    }
}

internal sealed record PolicyEnvironment(
    string Origin,
    string Country,
    long Adults,
    long Value,
    bool Active,
    string Email,
    string Pattern,
    IExprArray Values,
    IExprMap Labels,
    PriceValue Price);

internal sealed record PriceValue(long Value);
