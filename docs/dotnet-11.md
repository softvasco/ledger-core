# Building on .NET 11

Main targets .NET 10, the LTS release. .NET 11 went GA on 10 November 2026 as an STS release, and both are supported until November 2028, so there's no rush to move. CI builds and tests on both, so whatever .NET 11 breaks shows up in a pull request instead of on the day we switch.

```
dotnet build -p:DotNetVersion=11
dotnet test -p:DotNetVersion=11
```

Locally you also need the 11.0 SDK and `global.json` pointing at it, which is what the `dotnet-11` CI job does.

`DotNetVersion` sets the target framework and the version of the ASP.NET Core, EF Core and Microsoft.Extensions packages, which have to move together. With only the target framework changed, restore fails with NU1109: `Microsoft.AspNetCore.Mvc.Testing` 10.0 pulls in `Microsoft.Extensions.Logging` 11.0 transitively, below the central 10.0 pin.

## What broke

Three things on the move from 10.0.15 to 11.0.0. All 418 tests pass on both after the fixes.

### double to decimal keeps every digit

On .NET 10, `(decimal)0.05d` is `0.05`. On .NET 11 it is `0.0500000000000000027755575616`, the exact value of the double. `Convert.ToDecimal(double)` changed the same way.

Two rounding tests failed because of it. xUnit's `[InlineData]` can't hold a decimal, so `[InlineData(0.05, 0.5, 0.02)]` passed doubles that were converted on the way in. Half of 0.05 is 0.025, which rounds half to even to 0.02. Half of the exact double is a little over 0.025, so it rounded up to 0.03. The tests now use `TheoryData<decimal, ...>` with decimal literals.

The ledger itself never converts from double: amounts arrive as JSON numbers that System.Text.Json reads straight into decimal. But any code that does `(decimal)someDouble` with money gets different numbers on .NET 11, and a test with `InlineData` doubles can pass or fail for the wrong reason.

### OpenAPI 3.2 by default

`Microsoft.AspNetCore.OpenApi` 11 writes OpenAPI 3.2 unless told otherwise, and `The_document_is_served_as_openapi_3_1` caught it. The document now sets `OpenApiVersion = OpenApiSpecVersion.OpenApi3_1`, because client generators read 3.1 and the API has nothing that needs 3.2.

### Extensions abstractions ship with the runtime

`Microsoft.NETCore.App` 11.0 includes `Microsoft.Extensions.DependencyInjection.Abstractions`, `Logging.Abstractions`, `Diagnostics.Abstractions`, `Options`, `Primitives` and a few more; 10.0 has none of them. NuGet prunes those package references and, with warnings as errors, fails on NU1510. `LedgerCore.Application` references the three it uses only when building for .NET 10.

## What didn't

Npgsql 10, SqlClient 7.1, Aspire 13.6, OpenTelemetry 1.21, Testcontainers and xUnit v3 all built and passed on 11.0 with no changes.
