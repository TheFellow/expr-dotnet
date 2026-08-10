using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Security.Tests;

public sealed class EnvironmentSecurityTests
{
    [Fact]
    public void Typed_builder_exposes_only_explicitly_allowlisted_members()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<HostEnvironment>()
            .Member("name", static environment => environment.Name, ExprTypes.String)
            .Build();
        var environment = new HostEnvironment("Ada", "private-value");

        Assert.Equal("Ada", schema.Read(environment, "name"));
        Assert.False(schema.TryGetMember(nameof(HostEnvironment.Secret), out _));
        Assert.Throws<ExprRuntimeException>(() => schema.Read(environment, nameof(HostEnvironment.Secret)));
    }

    [Fact]
    [RequiresDynamicCode("Exercises the explicitly reflection-based schema API.")]
    [RequiresUnreferencedCode("Exercises the explicitly reflection-based schema API.")]
    public void Reflected_schema_excludes_nonpublic_static_indexer_and_ignored_members()
    {
        _ = new ReflectedEnvironment();
        ExprEnvironmentSchema schema = ExprEnvironmentSchema.Reflect<ReflectedEnvironment>();

        Assert.True(schema.TryGetMember(nameof(ReflectedEnvironment.Allowed), out _));
        Assert.False(schema.TryGetMember(nameof(ReflectedEnvironment.Ignored), out _));
        Assert.False(schema.TryGetMember("Hidden", out _));
        Assert.False(schema.TryGetMember(nameof(ReflectedEnvironment.StaticValue), out _));
        Assert.False(schema.TryGetMember("Item", out _));
    }

    [Fact]
    public void Immutable_schema_can_be_reused_concurrently_without_cross_invocation_state()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<HostEnvironment>()
            .Member("name", static environment => environment.Name, ExprTypes.String)
            .Build();
        var failures = new ConcurrentQueue<string>();

        Parallel.For(
            0,
            1_000,
            iteration =>
            {
                string expected = iteration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var environment = new HostEnvironment(expected, "not-exposed");
                if (!string.Equals(schema.Read(environment, "name") as string, expected, StringComparison.Ordinal))
                {
                    failures.Enqueue(expected);
                }
            });

        Assert.Empty(failures);
    }

    [Fact]
    public void Unknown_member_diagnostic_does_not_disclose_environment_values()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<HostEnvironment>()
            .Member("name", static environment => environment.Name, ExprTypes.String)
            .Build();
        var environment = new HostEnvironment("Ada", "DO-NOT-DISCLOSE");

        ExprRuntimeException exception = Assert.Throws<ExprRuntimeException>(() => schema.Read(environment, "missing"));

        Assert.Equal("unknown name missing", exception.Message);
        Assert.DoesNotContain(environment.Secret, exception.Message, StringComparison.Ordinal);
    }

    private readonly record struct HostEnvironment(string Name, string Secret);

    private sealed class ReflectedEnvironment
    {
        public static string StaticValue => "static";

        public string Allowed => "allowed";

        [ExprMember(Ignore = true)]
        public string Ignored => "ignored";

        public string this[int index] => index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private string Hidden => "hidden";
    }
}
