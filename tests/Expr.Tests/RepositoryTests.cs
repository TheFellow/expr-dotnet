using System;
using System.Reflection;
using Xunit;

namespace Expr.Tests;

public sealed class RepositoryTests
{
    [Fact]
    public void RuntimeHasNoPackageDependencies()
    {
        Assembly runtime = typeof(ExprVersion).Assembly;

        Assert.All(runtime.GetReferencedAssemblies(), static reference =>
            Assert.True(
                reference.Name is "System.Runtime" or "netstandard"
                    || reference.Name?.StartsWith("System.", StringComparison.Ordinal) is true,
                $"Unexpected runtime dependency: {reference.FullName}"));
    }
}
