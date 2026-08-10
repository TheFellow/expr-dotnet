using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Parity;

public sealed class DocumentationPlatformParityTests
{
    [Fact]
    [RequiresDynamicCode("Validates the reflection-backed counterpart to upstream doc generation.")]
    [RequiresUnreferencedCode("Validates the reflection-backed counterpart to upstream doc generation.")]
    public void TestCreateDoc()
    {
        _ = new DocumentationEnvironment(
            [new DocumentationTweet(1, "hello")],
            new DocumentationConfig(),
            new Dictionary<string, object?>(),
            DayOfWeek.Monday,
            new DocumentationWeekday());
        ExprEnvironmentSchema schema = ExprEnvironmentSchema.Reflect<DocumentationEnvironment>();

        Assert.IsType<ArrayTypeDescriptor>(schema.Members["Tweets"].Type);
        Assert.IsType<ObjectTypeDescriptor>(schema.Members["Config"].Type);
        MapTypeDescriptor map = Assert.IsType<MapTypeDescriptor>(schema.Members["Env"].Type);
        Assert.Same(ExprTypes.String, map.KeyType);
        Assert.Same(ExprTypes.Any, map.AdditionalValueType);
        Assert.IsType<ObjectTypeDescriptor>(schema.Members["TimeWeekday"].Type);
        Assert.IsType<ObjectTypeDescriptor>(schema.Members["Weekday"].Type);

        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        _ = ExprEngine.Compile(
            "Tweets[0].Message == '' && Config.MaxSize >= 0 && Duration('1s').String() == ''",
            configuration);
    }

    [Fact]
    public void TestCreateDoc_Ambiguous()
    {
        var a = new AmbiguousA(1, 2);
        var b = new AmbiguousB("other");
        _ = new AmbiguousEnvironment(a, b, new AmbiguousC(a, b));
        var selected = new ExprEnvironmentSchemaBuilder<AmbiguousEnvironment>()
            .Member("A", static environment => environment.A, new ObjectTypeDescriptor(typeof(AmbiguousA)))
            .Member("B", static environment => environment.B, new ObjectTypeDescriptor(typeof(AmbiguousB)))
            .Member("C", static environment => environment.C, new ObjectTypeDescriptor(typeof(AmbiguousC)))
            .Member("AmbiguousField", static environment => environment.A.AmbiguousField, ExprTypes.Integer)
            .Member("OkField", static environment => environment.A.OkField, ExprTypes.Integer)
            .Build();

        Assert.Equal(
            ["A", "AmbiguousField", "B", "C", "OkField"],
            selected.Members.Keys.Order(StringComparer.Ordinal));
        _ = ExprEngine.Compile(
            "AmbiguousField + OkField + C.AmbiguousField",
            ExprConfiguration.Default.WithEnvironment(selected));

        var ambiguous = new ExprEnvironmentSchemaBuilder<AmbiguousEnvironment>()
            .Member("AmbiguousField", static environment => environment.A.AmbiguousField, ExprTypes.Integer)
            .Member("AmbiguousField", static environment => environment.B.AmbiguousField, ExprTypes.String);
        Assert.Throws<ArgumentException>(ambiguous.Build);
    }

    [Fact]
    [RequiresUnreferencedCode("Infers the map-provided values exactly as the reflection-backed host API does.")]
    public void TestCreateDoc_FromMap()
    {
        var environment = new Dictionary<string, object?>
        {
            ["Tweets"] = new[] { new DocumentationTweet(1, "hello") },
            ["Config"] = new DocumentationConfig(),
            ["Max"] = new Func<double, double, double>(Math.Max),
        };
        ExprEnvironmentSchema schema = ExprEnvironmentSchema.FromDictionary(environment);

        ArrayTypeDescriptor tweets = Assert.IsType<ArrayTypeDescriptor>(schema.Members["Tweets"].Type);
        Assert.Equal(typeof(DocumentationTweet), Assert.IsType<ObjectTypeDescriptor>(tweets.ElementType).ClrType);
        Assert.Equal(typeof(DocumentationConfig), Assert.IsType<ObjectTypeDescriptor>(schema.Members["Config"].Type).ClrType);
        FunctionTypeDescriptor max = Assert.IsType<FunctionTypeDescriptor>(schema.Members["Max"].Type);
        Assert.Equal([ExprTypes.Float, ExprTypes.Float], max.Parameters);
        Assert.Same(ExprTypes.Float, max.ReturnType);

        object? result = ExprEngine.Evaluate(
            "len(Tweets) == 1 && Config.MaxSize == 0 && Max(1.0, 2.0) == 2.0",
            environment,
            ExprConfiguration.Default.WithEnvironment(schema),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(true, result);
    }

    [Fact]
    public void TestContext_Markdown()
    {
        string documentationPath = Path.ChangeExtension(typeof(ExprEngine).Assembly.Location, ".xml");

        Assert.True(File.Exists(documentationPath), $"Missing XML documentation: {documentationPath}");
        string documentation = File.ReadAllText(documentationPath);
        Assert.Contains("T:Expr.ExprEngine", documentation, StringComparison.Ordinal);
        Assert.Contains("T:Expr.Runtime.ExprEnvironmentSchema", documentation, StringComparison.Ordinal);
        Assert.Contains("T:Expr.Types.ExprTypeDescriptor", documentation, StringComparison.Ordinal);
    }

    private sealed record DocumentationTweet(int Size, string Message);

    private sealed record DocumentationConfig(int MaxSize = 0);

    private sealed record DocumentationEnvironment(
        IReadOnlyList<DocumentationTweet> Tweets,
        DocumentationConfig Config,
        IReadOnlyDictionary<string, object?> Env,
        DayOfWeek TimeWeekday,
        DocumentationWeekday Weekday)
    {
        public DocumentationDuration Duration(string value)
        {
            _ = value;
            return new DocumentationDuration();
        }
    }

    private readonly record struct DocumentationWeekday
    {
        public string String() => string.Empty;
    }

    private readonly record struct DocumentationDuration
    {
        public string String() => string.Empty;
    }

    private sealed record AmbiguousA(long AmbiguousField, long OkField);

    private sealed record AmbiguousB(string AmbiguousField);

    private sealed record AmbiguousC(AmbiguousA A, AmbiguousB B)
    {
        public long AmbiguousField => A.AmbiguousField;

        public long OkField => A.OkField;
    }

    private sealed record AmbiguousEnvironment(AmbiguousA A, AmbiguousB B, AmbiguousC C);
}
