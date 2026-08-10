using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Expr.Checking;
using Expr.Compilation;
using Expr.Configuration;
using Expr.Optimization;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Parity;

// Semantic port of test/crowdsec/crowdsec_test.go:TestCrowdsec at upstream
// revision 4b31df3a2e0eefec04c017a82a00e0f08541d3e4.
public sealed class CrowdsecCorpusTests
{
    private const int ExpectedExpressionCount = 673;
    private const int ExpectedFunctionCount = 57;
    private const string ExpectedSha256 = "c22ea3a08b02614f4c87b6e304a433fccda3fe74da3c8117a8dd5651dbc12367";

    [Fact]
    public void TestCrowdsec()
    {
        string corpusPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Upstream",
            "crowdsec.json");
        Assert.Equal(ExpectedSha256, ComputeSha256(corpusPath));

        string[] expressions = JsonSerializer.Deserialize<string[]>(File.ReadAllText(corpusPath)) ??
            throw new InvalidDataException("The CrowdSec corpus did not contain a JSON string array.");
        Assert.Equal(ExpectedExpressionCount, expressions.Length);

        IReadOnlyList<ExprFunction> functions = CreateFunctions();
        Assert.Equal(ExpectedFunctionCount, functions.Count);
        var sampleEnvironment = new CrowdsecEnvironment(new object());
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<CrowdsecEnvironment>()
            .Member("evt", static environment => environment.Event, ExprTypes.Any)
            .Build();
        Assert.Same(sampleEnvironment.Event, schema.Read(sampleEnvironment, "evt"));
        ExprConfiguration configuration = ExprConfiguration.Default.WithEnvironment(schema);
        foreach (ExprFunction function in functions)
        {
            configuration = configuration.WithFunction(function);
        }

        var failures = new List<string>();
        for (var index = 0; index < expressions.Length; index++)
        {
            try
            {
                _ = ExprEngine.Compile(expressions[index], configuration);
            }
            catch (SyntaxException exception)
            {
                failures.Add(FormatFailure(index, expressions[index], exception));
            }
            catch (ExprCheckException exception)
            {
                failures.Add(FormatFailure(index, expressions[index], exception));
            }
            catch (ExprOptimizationException exception)
            {
                failures.Add(FormatFailure(index, expressions[index], exception));
            }
            catch (ExprCompilationException exception)
            {
                failures.Add(FormatFailure(index, expressions[index], exception));
            }
        }

        Assert.True(
            failures.Count is 0,
            $"{failures.Count} CrowdSec expressions failed to compile. First 25:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Take(25)));
    }

    private static IReadOnlyList<ExprFunction> CreateFunctions()
    {
        ExprTypeDescriptor any = ExprTypes.Any;
        ExprTypeDescriptor boolean = ExprTypes.Boolean;
        ExprTypeDescriptor duration = ExprTypes.Duration;
        ExprTypeDescriptor floating = ExprTypes.Float;
        ExprTypeDescriptor integer = ExprTypes.Integer;
        ExprTypeDescriptor nil = ExprTypes.Nil;
        ExprTypeDescriptor text = ExprTypes.String;
        ExprTypeDescriptor textArray = ExprTypes.ArrayOf(text);
        ExprTypeDescriptor anyArray = ExprTypes.ArrayOf(any);
        ExprTypeDescriptor anyMap = new MapTypeDescriptor([], any, text);
        ExprTypeDescriptor stringArrayMap = new MapTypeDescriptor([], textArray, text);

        return
        [
            Function("Distance", floating, text, text, text, text),
            Function("GetFromStash", text, text, text),
            Function("Atof", floating, text),
            Function("JsonExtract", text, text, text),
            Variadic("JsonExtractUnescape", text, text, text),
            Variadic("JsonExtractLib", text, text, text),
            Function("JsonExtractSlice", anyArray, text, text),
            Function("JsonExtractObject", anyMap, text, text),
            Function("ToJsonString", text, any),
            Function("File", textArray, text),
            Function("RegexpInFile", boolean, text, text),
            Function("Upper", text, text),
            Function("Lower", text, text),
            Function("IpInRange", boolean, text, text),
            Function("TimeNow", text),
            Function("ParseUri", stringArrayMap, text),
            Function("PathUnescape", text, text),
            Function("QueryUnescape", text, text),
            Function("PathEscape", text, text),
            Function("QueryEscape", text, text),
            Function("XMLGetAttributeValue", text, text, text, text),
            Function("XMLGetNodeValue", text, text, text),
            Function("IpToRange", text, text, text),
            Function("IsIPV6", boolean, text),
            Function("IsIPV4", boolean, text),
            Function("IsIP", boolean, text),
            Function("LookupHost", textArray, text),
            Function("GetDecisionsCount", integer, text),
            Function("GetDecisionsSinceCount", integer, text, text),
            Variadic("Sprintf", text, text, any),
            Function("ParseUnix", text, text),
            Function("SetInStash", nil, text, text, text, ExprTypes.Nullable(duration)),
            Function("Fields", textArray, text),
            Function("Index", integer, text, text),
            Function("IndexAny", integer, text, text),
            Function("Join", text, textArray, text),
            Function("Split", textArray, text, text),
            Function("SplitAfter", textArray, text, text),
            Function("SplitAfterN", textArray, text, text, integer),
            Function("SplitN", textArray, text, text, integer),
            Function("Replace", text, text, text, text, integer),
            Function("ReplaceAll", text, text, text, text),
            Function("Trim", text, text, text),
            Function("TrimLeft", text, text, text),
            Function("TrimRight", text, text, text),
            Function("TrimSpace", text, text),
            Function("TrimPrefix", text, text, text),
            Function("TrimSuffix", text, text, text),
            Function("Get", text, textArray, integer),
            Function("ToString", text, any),
            Function("Match", boolean, text, text),
            Function("KeyExists", boolean, text, anyMap),
            Variadic("LogInfo", boolean, text, any),
            Function("B64Decode", text, text),
            Function("UnmarshalJSON", nil, text, anyMap, text),
            Function("ParseKV", nil, text, anyMap, text),
            Function("Hostname", text),
        ];
    }

    private static ExprFunction Function(
        string name,
        ExprTypeDescriptor result,
        params ExprTypeDescriptor[] parameters) =>
        new(name, [new ExprFunctionOverload(parameters, result)], static _ => null);

    private static ExprFunction Variadic(
        string name,
        ExprTypeDescriptor result,
        params ExprTypeDescriptor[] parameters) =>
        new(name, [new ExprFunctionOverload(parameters, result, isVariadic: true)], static _ => null);

    private static string FormatFailure(int index, string expression, Exception exception) =>
        $"case {index + 1}: {expression}{Environment.NewLine}  {exception.Message}";

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed class CrowdsecEnvironment
    {
        internal CrowdsecEnvironment(object @event)
        {
            Event = @event;
        }

        internal object Event { get; }
    }
}
