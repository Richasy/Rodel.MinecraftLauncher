# Skin Library Tests

Independent MSTest/.NET 11 Microsoft.Testing.Platform tests for `Rodel.Minecraft.Skins`. The project references only the skin library: running it does not build the launcher, CLI, MCP host or Sprout App.

Run from the repository root:

```powershell
dotnet test --project test\Rodel.Minecraft.Skins.Tests\Rodel.Minecraft.Skins.Tests.csproj -c Release --report-trx
```

Use the usual test-platform filters for a focused behavior:

```powershell
dotnet test --project test\Rodel.Minecraft.Skins.Tests\Rodel.Minecraft.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinClipboardTests" --report-trx
```

The SDK and runner conventions match the [CLI test project](../Rodel.MinecraftLauncher.Cli.Tests/Rodel.MinecraftLauncher.Cli.Tests.csproj), but its process harness and business cases are not copied. In-memory cases use small synthetic textures. Project serialization uses source-generated metadata and a separate temporary directory per test, removed after file handles close. Cancellation is triggered at a controlled input-enumeration boundary rather than with sleeps or timing assumptions.

The tests do not use `.local` artifacts, player saves, accounts or public services. They do not prove browser interaction, Minecraft loading or NativeAOT execution of the test host. Actual CLI/MCP storage and transport contracts remain in `Rodel.MinecraftLauncher.Cli.Tests`; its `RODEL_TEST_CLI_EXECUTABLE` switch selects a published NativeAOT launcher, not this library test runner.

Check all enabled suggestion-level and higher formatting/analyzer rules:

```powershell
dotnet format test\Rodel.Minecraft.Skins.Tests\Rodel.Minecraft.Skins.Tests.csproj --no-restore --severity info --verify-no-changes
```

TRX, generated files and temporary directories are not source. Follow the shared [test design guidelines](../GUIDELINES.md) for value review, independent expectations and isolation; do not add tests solely to increase counts.

The independent geometry witness covers Classic/Slim at both 64 and 128 texels. Editor workflow cases additionally protect independent HD micro-pixels, continuous eraser coverage, raster/cursor agreement, preview without commit, complete cross-model/size history and packaged original person templates. Core project/candidate/grant/receipt persistence lives in the CLI test project; skin-bound assistant admission, policy and journal behavior lives in the Assistant test project. Those managed Core cases are not NativeAOT child execution.
