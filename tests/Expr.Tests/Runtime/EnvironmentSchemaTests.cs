using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Runtime;

public sealed class EnvironmentSchemaTests
{
    [Fact]
    [RequiresDynamicCode("Exercises the explicitly reflection-based schema API.")]
    [RequiresUnreferencedCode("Exercises the explicitly reflection-based schema API.")]
    public void Reflected_schema_is_cached_and_honors_member_attributes()
    {
        ExprEnvironmentSchema first = ExprEnvironmentSchema.Reflect<SampleEnvironment>();
        ExprEnvironmentSchema second = ExprEnvironmentSchema.Reflect<SampleEnvironment>();
        var environment = new SampleEnvironment { Name = "Ada", Hidden = 42, Score = 9 };

        Assert.Same(first, second);
        Assert.Equal("Ada", first.Read(environment, "name"));
        Assert.Equal(9, first.Read(environment, "score"));
        Assert.False(first.TryGetMember(nameof(SampleEnvironment.Hidden), out _));
        Assert.Throws<ExprRuntimeException>(() => first.Read(environment, "missing"));
    }

    [Fact]
    public void Typed_builder_is_reflection_free_and_supports_value_type_environments()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<ValueEnvironment>()
            .Member("value", static environment => environment.Value)
            .Build();

        Assert.Equal(7, schema.Read(new ValueEnvironment(7), "value"));
        Assert.Same(ExprTypes.Integer, schema.Members["value"].Type);
    }

    [Fact]
    public void Dictionary_schema_snapshots_names_and_reads_current_values()
    {
        var environment = new Dictionary<string, object?> { ["answer"] = 42 };
        ExprEnvironmentSchema schema = ExprEnvironmentSchema.FromDictionary(environment);
        environment["answer"] = 43;

        Assert.Equal(43, schema.Read(environment, "answer"));
        Assert.Same(ExprTypes.Integer, schema.Members["answer"].Type);
    }

    private sealed class SampleEnvironment
    {
        [ExprMember("score")]
        public int Score;

        [ExprMember("name")]
        public required string Name { get; init; }

        [ExprMember(Ignore = true)]
        public int Hidden { get; init; }
    }

    private readonly record struct ValueEnvironment(int Value);
}
