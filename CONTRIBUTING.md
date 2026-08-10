# Contributing

Expr.NET welcomes semantic ports, tests, diagnostics improvements, benchmarks,
and documentation.

Before submitting a change:

```sh
dotnet restore expr-dotnet.slnx
dotnet format expr-dotnet.slnx --verify-no-changes --no-restore
dotnet build expr-dotnet.slnx --configuration Release --no-restore
dotnet test expr-dotnet.slnx --configuration Release --no-build
dotnet pack src/Expr/Expr.csproj --configuration Release --no-build
```

Changes copied or translated from upstream must preserve the MIT attribution.
Semantic changes should identify the corresponding upstream commit or issue.

