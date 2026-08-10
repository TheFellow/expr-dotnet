using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Expr.Configuration;
using Expr.Runtime;
using Xunit;

namespace Expr.Tests.Parity;

// Semantic port of test/gen/gen_test.go:TestGenerated at upstream revision
// 4b31df3a2e0eefec04c017a82a00e0f08541d3e4. The checked-in generated.txt corpus
// contains expressions that upstream Expr successfully compiles and executes.
public sealed class GeneratedCorpusTests
{
    private const int ExpectedExpressionCount = 43_689;
    private const string ExpectedSha256 = "825f78816881c92a77893bd422db168f8bf8174eb08e701ea8575c65b449b216";
    private static readonly long[] ArrayValues = [1L, 2L, 3L, 4L, 5L];
    private static readonly Foo[] ListValues = [new("bar"), new("baz")];

    [Fact]
    public void TestGenerated()
    {
        string corpusPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Upstream",
            "generated.txt");
        Assert.Equal(ExpectedSha256, ComputeSha256(corpusPath));

        string[] expressions = File.ReadLines(corpusPath)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        Assert.Equal(ExpectedExpressionCount, expressions.Length);

        Dictionary<string, object?> environment = CreateEnvironment();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(
            ExprEnvironmentSchema.FromDictionary(environment));
        var differences = new List<string>();

        for (var index = 0; index < expressions.Length; index++)
        {
            string expression = expressions[index];
            try
            {
                CompiledExpression compiled = ExprEngine.Compile(expression, configuration);
                _ = compiled.Run(environment, cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                differences.Add($"{index + 1}:{exception.GetType().Name}:{exception.Message}");
            }
        }

        string summary = string.Join(
            Environment.NewLine,
            differences
                .Select(static difference => difference.Split('\n', 2)[0])
                .Select(static difference => difference[(difference.IndexOf(':', StringComparison.Ordinal) + 1)..])
                .GroupBy(static difference => difference, StringComparer.Ordinal)
                .OrderByDescending(static group => group.Count())
                .Take(25)
                .Select(static group => $"{group.Count(),5} {group.Key}"));
        Assert.True(
            differences.Count is 0,
            $"Expected every upstream expression to execute, but found {differences.Count} failures. " +
            $"Top families:{Environment.NewLine}{summary}" +
            $"{Environment.NewLine}First 25:{Environment.NewLine}" +
            string.Join(Environment.NewLine, differences.Take(25)));
    }

    [Fact]
    public void Optional_computed_environment_index_remains_dynamic()
    {
        Dictionary<string, object?> environment = CreateEnvironment();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(
            ExprEnvironmentSchema.FromDictionary(environment));

        _ = ExprEngine.Compile("$env?.[missing]", configuration);
    }

    [Theory]
    [InlineData("target?.missing(tick(1))", 1)]
    [InlineData("target?.missing(tick(1), tick(2))", 2)]
    [InlineData("target?.missing(tick(1), tick(2), tick(3))", 3)]
    public void Optional_nil_callee_preserves_argument_side_effect_order(string source, int argumentCount)
    {
        var observed = new List<long>();
        Dictionary<string, object?> environment = CreateEnvironment();
        environment["target"] = null;
        environment["tick"] = (Func<long, long>)(value =>
        {
            observed.Add(value);
            return value;
        });
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<Dictionary<string, object?>>()
            .Member("target", static instance => instance["target"], Expr.Types.ExprTypes.Any)
            .Member(
                "tick",
                static instance => instance["tick"],
                new Expr.Types.FunctionTypeDescriptor([Expr.Types.ExprTypes.Integer], Expr.Types.ExprTypes.Integer))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        object? result = ExprEngine.Evaluate(
            source,
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(Enumerable.Range(1, argumentCount).Select(static value => (long)value), observed);
    }

    [Fact]
    public void Unchecked_fast_builtin_evaluates_all_arguments_and_consumes_the_last()
    {
        var observed = new List<long>();
        Dictionary<string, object?> environment = CreateEnvironment();
        environment["target"] = (Func<object?, object?>)(value => value);
        environment["tick"] = (Func<long, long>)(value =>
        {
            observed.Add(value);
            return value;
        });
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<Dictionary<string, object?>>()
            .Member("target", static instance => instance["target"], Expr.Types.ExprTypes.Any)
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);

        object? result = ExprEngine.Evaluate(
            "target(abs(tick(1), tick(-2)))",
            environment,
            configuration,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2L, result);
        Assert.Equal([1L, -2L], observed);
    }

    private static Dictionary<string, object?> CreateEnvironment() =>
        new(StringComparer.Ordinal)
        {
            ["ok"] = true,
            ["i"] = 1L,
            ["str"] = "str",
            ["f64"] = 0.5,
            ["array"] = ArrayValues,
            ["foo"] = new Foo("foo"),
            ["list"] = ListValues,
            ["add"] = (Func<long, long, long>)((left, right) => left + right),
            ["greet"] = (Func<string, string>)(name => $"Hello, {name}"),
        };

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed record Foo(string Bar) : IComparable, IExprMap
    {
        public Type KeyType => typeof(string);

        public Type ValueType => typeof(string);

        public int Count => 1;

        public string String() => "foo";

        public bool TryGetValue(object? key, out object? value)
        {
            if (string.Equals(key as string, nameof(String), StringComparison.Ordinal))
            {
                value = (Func<string>)String;
                return true;
            }

            if (string.Equals(key as string, nameof(Bar), StringComparison.Ordinal))
            {
                value = Bar;
                return true;
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator()
        {
            yield return new KeyValuePair<object?, object?>(nameof(Bar), Bar);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int CompareTo(object? obj) => obj is Foo other
            ? string.CompareOrdinal(Bar, other.Bar)
            : 1;

        public override string ToString() => "foo";
    }

}
