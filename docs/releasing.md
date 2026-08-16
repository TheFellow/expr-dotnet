# Releasing Expr

Expr publishes from GitHub Actions through
[NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing).
GitHub exchanges an OIDC identity for a short-lived NuGet API key immediately
before publication, so the repository does not store a long-lived publishing
key.

## One-time setup

1. On NuGet.org, open the account menu, choose **Trusted Publishing**, and add a
   GitHub policy with these values:

   | Field | Value |
   | --- | --- |
   | Repository owner | `TheFellow` |
   | Repository | `expr-dotnet` |
   | Workflow file | `release.yml` |
   | Environment | `release` |

   Choose the NuGet user or organization that should own the package as the
   policy owner.

2. Configure any desired deployment protection rules on the GitHub `release`
   environment. Requiring an approval gives the final package publication a
   deliberate human gate.

If Trusted Publishing is not visible in the NuGet.org account, it has not yet
been enabled for that account. Use a narrowly scoped, expiring NuGet API key as
a temporary fallback rather than a full-account or non-expiring key.

## Publish a version

Package versions are derived from tags. For example:

```sh
git tag -a v0.2.0 -m "Expr 0.2.0"
git push origin v0.2.0
```

The release workflow validates formatting, builds and tests the solution,
checks language compatibility, verifies semport integrity, runs the bounded
fuzz gate, validates Native AOT, packs the library and symbols, publishes both
to NuGet.org, and creates the matching GitHub release.

NuGet package versions are immutable. Inspect the generated package and verify
the version before pushing a tag; never reuse a published version.

## Verify publication

NuGet.org validates and indexes a new package after upload, which can take
several minutes. Test the exact published version from a clean project:

```sh
dotnet new console --name ExprSmokeTest
cd ExprSmokeTest
dotnet add package Expr --version 0.2.0
dotnet restore
```

After the first publication, add any additional maintainers or organization as
package owners in NuGet.org rather than sharing publishing credentials.
