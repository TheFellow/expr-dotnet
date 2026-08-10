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
                reference.Name is "System.Runtime",
                $"Unexpected runtime dependency: {reference.FullName}"));
    }
}
