using Expr.Runtime;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Runtime;

public sealed class FunctionTests
{
    [Fact]
    public void Function_invokes_matching_overload_without_resource_charge()
    {
        var function = new ExprFunction(
            "add",
            [new ExprFunctionOverload([ExprTypes.Integer, ExprTypes.Integer], ExprTypes.Integer)],
            static arguments => ExprValue.ToInt64(arguments[0]) + ExprValue.ToInt64(arguments[1]));

        ExprInvocationResult result = function.Invoke([20, 22]);

        Assert.Equal(42L, result.Value);
        Assert.Equal(0UL, result.MemoryCost);
    }

    [Fact]
    public void Safe_function_reports_resource_charge()
    {
        var function = new ExprFunction(
            "repeat",
            [new ExprFunctionOverload([ExprTypes.String, ExprTypes.Integer], ExprTypes.String)],
            safeInvoker: static arguments =>
            {
                string value = arguments[0] as string ?? throw new ExprRuntimeException("repeat requires a string");
                int count = (int)ExprValue.ToInt64(arguments[1]);
                return new ExprInvocationResult(string.Concat(System.Linq.Enumerable.Repeat(value, count)), (ulong)(value.Length * count));
            });

        ExprInvocationResult result = function.Invoke(["ab", 3]);

        Assert.Equal("ababab", result.Value);
        Assert.Equal(6UL, result.MemoryCost);
    }

    [Fact]
    public void Function_rejects_invalid_arity_before_invocation()
    {
        var function = new ExprFunction(
            "identity",
            [new ExprFunctionOverload([ExprTypes.Any], ExprTypes.Any)],
            static arguments => arguments[0]);

        Assert.Throws<ExprRuntimeException>(() => function.Invoke([]));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void Variadic_overload_checks_minimum_arity(int argumentCount, bool expected)
    {
        var overload = new ExprFunctionOverload(
            [ExprTypes.String, ExprTypes.String],
            ExprTypes.String,
            isVariadic: true);

        Assert.Equal(expected, overload.AcceptsArity(argumentCount));
    }
}
