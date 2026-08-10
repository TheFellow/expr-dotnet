using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Expr.Tests.Conformance;

public sealed class ConformanceCorpusTests
{
    private const string CaseSchema = "expr.conformance.case/v1";
    private const string UpstreamRevision = "4b31df3a2e0eefec04c017a82a00e0f08541d3e4";

    [Fact]
    public void Checked_in_corpus_has_unique_ids_pinned_provenance_and_expected_outcomes()
    {
        string repositoryRoot = FindRepositoryRoot();
        string corpusPath = Path.Combine(repositoryRoot, "conformance", "corpus", "upstream.jsonl");
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;

        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            Assert.Equal(CaseSchema, root.GetProperty("schema").GetString());

            string identifier = Assert.IsType<string>(root.GetProperty("id").GetString());
            Assert.True(identifiers.Add(identifier), $"Duplicate conformance case id: {identifier}");
            Assert.Equal(JsonValueKind.String, root.GetProperty("expression").ValueKind);

            JsonElement provenance = root.GetProperty("provenance");
            Assert.Equal(UpstreamRevision, provenance.GetProperty("revision").GetString());
            Assert.False(string.IsNullOrEmpty(provenance.GetProperty("path").GetString()));
            Assert.True(provenance.GetProperty("line").GetInt32() > 0);

            JsonElement expected = root.GetProperty("expected");
            string? status = expected.GetProperty("status").GetString();
            Assert.True(status is "success" or "error", $"Invalid outcome status for {identifier}: {status}");
            Assert.False(string.IsNullOrEmpty(expected.GetProperty("phase").GetString()));
            count++;
        }

        Assert.True(count >= 100, $"The initial differential corpus unexpectedly shrank to {count} cases.");
    }

    [Fact]
    public void Dotnet_matches_every_checked_in_upstream_outcome()
    {
        IReadOnlyList<JsonElement> cases = ReadCases();
        var differences = new List<string>();
        foreach (JsonElement testCase in cases)
        {
            string identifier = testCase.GetProperty("id").GetString() ?? string.Empty;
            JsonNode expected = JsonNode.Parse(testCase.GetProperty("expected").GetRawText()) ??
                throw new InvalidDataException($"Expected outcome for {identifier} is null.");
            JsonObject actual = DotNetConformanceRunner.Execute(testCase);
            if (!JsonNode.DeepEquals(expected, actual))
            {
                differences.Add(FormatDifference(identifier, expected, actual));
            }
        }

        Assert.True(
            differences.Count is 0,
            $"{differences.Count} of {cases.Count} .NET outcomes differ from the pinned Go oracle:{Environment.NewLine}" +
            string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void Optimization_preserves_every_applicable_corpus_outcome()
    {
        IReadOnlyList<JsonElement> cases = ReadCases();
        var differences = new List<string>();
        foreach (JsonElement testCase in cases)
        {
            if (testCase.TryGetProperty("options", out JsonElement options) &&
                options.TryGetProperty("optimize", out _))
            {
                continue;
            }

            string identifier = testCase.GetProperty("id").GetString() ?? string.Empty;
            JsonObject optimized = DotNetConformanceRunner.Execute(testCase, optimizeOverride: true);
            JsonObject unoptimized = DotNetConformanceRunner.Execute(testCase, optimizeOverride: false);
            if (!JsonNode.DeepEquals(optimized, unoptimized))
            {
                differences.Add(FormatDifference(identifier, unoptimized, optimized));
            }
        }

        Assert.True(
            differences.Count is 0,
            $"{differences.Count} optimized outcomes differ from unoptimized execution:{Environment.NewLine}" +
            string.Join(Environment.NewLine, differences));
    }

    private static IReadOnlyList<JsonElement> ReadCases()
    {
        string repositoryRoot = FindRepositoryRoot();
        string corpusPath = Path.Combine(repositoryRoot, "conformance", "corpus", "upstream.jsonl");
        var cases = new List<JsonElement>();
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            cases.Add(document.RootElement.Clone());
        }

        return cases;
    }

    private static string FormatDifference(string identifier, JsonNode expected, JsonNode actual) =>
        $"{identifier}:{Environment.NewLine}" +
        $"  expected: {expected.ToJsonString()}{Environment.NewLine}" +
        $"  actual:   {actual.ToJsonString()}";

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "expr-dotnet.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Expr.NET repository root.");
    }
}
