using Expr.Checking;
using Expr.Configuration;
using Expr.Runtime;
using Expr.Syntax;
using Expr.Types;
using Xunit;

namespace Expr.Tests.Optimization;

// Provenance: inspiration/expr/optimizer/in_array.go, in_range.go,
// filter_map_test.go, and optimizer_test.go TestOptimize_filter_*.
public sealed class MembershipAndFilterTests : OptimizerTestBase
{
    [Fact]
    public void Integer_array_membership_uses_a_constant_map_for_typed_integer()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<IntegerEnvironment>()
            .Member("v", static environment => environment.Value)
            .Build();

        var result = Optimize("v in [1,2,3]", ExprConfiguration.Default.WithEnvironment(schema));

        var binary = Assert.IsType<BinaryNode>(result.SyntaxTree.Root);
        var constant = Assert.IsType<ConstantNode>(binary.Right);
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyDictionary<long, object?>>(constant.Value);
    }

    [Fact]
    public void String_array_membership_uses_ordinal_constant_map()
    {
        var result = Optimize("name in ['a','b','a']");

        var constant = Assert.IsType<ConstantNode>(Assert.IsType<BinaryNode>(result.SyntaxTree.Root).Right);
        var values = Assert.IsType<System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>>(constant.Value);
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void Integer_membership_in_constant_range_becomes_bounds_checks()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<IntegerEnvironment>()
            .Member("v", static environment => environment.Value)
            .Build();

        var result = Optimize("v in 18..31", ExprConfiguration.Default.WithEnvironment(schema));

        var conjunction = Assert.IsType<BinaryNode>(result.SyntaxTree.Root);
        Assert.Equal("and", conjunction.Operator);
        Assert.Equal(">=", Assert.IsType<BinaryNode>(conjunction.Left).Operator);
        Assert.Equal("<=", Assert.IsType<BinaryNode>(conjunction.Right).Operator);
        Assert.Same(
            Assert.IsType<BinaryNode>(conjunction.Left).Left,
            Assert.IsType<BinaryNode>(conjunction.Right).Left);
    }

    [Fact]
    public void Float_membership_in_integer_range_is_not_rewritten()
    {
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<FloatEnvironment>()
            .Member("v", static environment => environment.Value)
            .Build();

        var result = Optimize("v in 1..3", ExprConfiguration.Default.WithEnvironment(schema));

        Assert.Equal("in", Assert.IsType<BinaryNode>(result.SyntaxTree.Root).Operator);
    }

    [Fact]
    public void Filter_map_fuses_projection_but_index_projection_does_not()
    {
        var fused = Assert.IsType<BuiltinNode>(Optimize("map(filter(users, .Name == 'Bob'), .Age)").SyntaxTree.Root);
        var indexed = Assert.IsType<BuiltinNode>(Optimize("map(filter(users, true), #index)").SyntaxTree.Root);

        Assert.Equal("filter", fused.Name);
        Assert.IsType<MemberNode>(fused.Map);
        Assert.Equal("map", indexed.Name);
        Assert.Null(indexed.Map);
    }

    [Fact]
    public void Fused_projection_preserves_projected_semantic_type()
    {
        var userType = new MapTypeDescriptor(
            [new System.Collections.Generic.KeyValuePair<string, ExprTypeDescriptor>("Age", ExprTypes.Integer)]);
        var usersType = ExprTypes.ArrayOf(userType);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<UsersEnvironment>()
            .Member("users", static environment => environment.Users, usersType)
            .Build();

        var result = Optimize(
            "map(filter(users, true), .Age)",
            ExprConfiguration.Default
                .WithEnvironment(schema)
                .WithExpectedType(ExprTypes.ArrayOf(ExprTypes.Integer)));

        var array = Assert.IsType<ArrayTypeDescriptor>(result.ResultType);
        Assert.Same(ExprTypes.Integer, array.ElementType);
    }

    [Fact]
    public void Public_expected_type_validation_remains_strict_while_valid_fusion_can_recheck()
    {
        var userType = new MapTypeDescriptor(
            [new System.Collections.Generic.KeyValuePair<string, ExprTypeDescriptor>("Age", ExprTypes.Integer)]);
        ExprEnvironmentSchema schema = new ExprEnvironmentSchemaBuilder<UsersEnvironment>()
            .Member("users", static environment => environment.Users, ExprTypes.ArrayOf(userType))
            .Build();
        ExprConfiguration configuration = ExprConfiguration.Default
            .WithEnvironment(schema)
            .WithExpectedType(ExprTypes.ArrayOf(ExprTypes.Integer));

        Assert.Throws<ExprCheckException>(() =>
            new ExprChecker().Check(new SyntaxParser().Parse("users"), configuration));

        var result = Optimize("map(filter(users, true), .Age)", configuration);

        Assert.Same(
            ExprTypes.Integer,
            Assert.IsType<ArrayTypeDescriptor>(result.ResultType).ElementType);
    }

    [Theory]
    [InlineData("len(filter(users, true))", "count", false)]
    [InlineData("filter(users, true)[0]", "find", true)]
    [InlineData("first(filter(users, true))", "find", false)]
    [InlineData("filter(users, true)[-1]", "findLast", true)]
    [InlineData("last(filter(users, true))", "findLast", false)]
    public void Filter_consumers_use_single_pass_builtins(string expression, string expectedName, bool throws)
    {
        var result = Assert.IsType<BuiltinNode>(Optimize(expression).SyntaxTree.Root);

        Assert.Equal(expectedName, result.Name);
        Assert.Equal(throws, result.Throws);
    }

    private readonly record struct IntegerEnvironment(long Value);

    private readonly record struct FloatEnvironment(double Value);

    private readonly record struct UsersEnvironment(object? Users);
}
