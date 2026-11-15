// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Identifies one credited project and its independently applicable license.</summary>
/// <param name="Id">Stable identity used by the list and accessibility tree.</param>
/// <param name="Name">The project's untranslated public name.</param>
/// <param name="Project">The project or package information address chosen for the application.</param>
/// <param name="License">The license identifier, or Microsoft for the bundled distribution terms.</param>
/// <param name="Terms">The exact license address, including packaged files through ms-appx.</param>
internal sealed record AboutCredit(string Id, string Name, string Project, string License, string Terms);

/// <summary>Preserves catalog order while two equal-width columns share each available row.</summary>
/// <param name="First">The leading entry, also the stable row identity.</param>
/// <param name="Second">The optional trailing entry, stacked below the first in compact layouts.</param>
internal sealed record AboutCreditPair(AboutCredit First, AboutCredit? Second);

/// <summary>Lists principal dependencies and their independently applicable licenses.</summary>
internal static class AboutCredits
{
    private const string Mit = "https://licenses.nuget.org/MIT";
    private const string Apache = "https://licenses.nuget.org/Apache-2.0";

    internal static readonly AboutCredit[] Items =
    [
        new("sprout", "Sprout", "https://github.com/Richasy/Sprout", "MIT", Mit),
        new("dotnet", ".NET", "https://github.com/dotnet/runtime", "MIT", Mit),
        new("windows-app-sdk", "Windows App SDK", "https://github.com/microsoft/WindowsAppSDK", "Microsoft", "ms-appx:///Assets/Licenses/WindowsAppSDK.txt"),
        new("msal", "MSAL.NET", "https://github.com/AzureAD/microsoft-authentication-library-for-dotnet", "MIT", Mit),
        new("msal-native", "MSAL Native Interop", "https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop", "Microsoft", "ms-appx:///Assets/Licenses/MsalNativeInterop.txt"),
        new("terrafx", "TerraFX.Interop.Windows", "https://github.com/terrafx/terrafx.interop.windows", "MIT", Mit),
        new("extensions-ai", "Microsoft.Extensions.AI", "https://github.com/dotnet/extensions", "MIT", Mit),
        new("mcp", "MCP C# SDK", "https://github.com/modelcontextprotocol/csharp-sdk", "Apache-2.0", Apache),
        new("tokenizers", "Microsoft.ML.Tokenizers", "https://github.com/dotnet/machinelearning", "MIT", Mit),
        new("protobuf", "Google.Protobuf", "https://github.com/protocolbuffers/protobuf", "BSD-3-Clause", "https://licenses.nuget.org/BSD-3-Clause"),
        new("models-dev", "models.dev", "https://models.dev/", "MIT", "https://github.com/anomalyco/models.dev/blob/dev/LICENSE"),
        new("stb-image", "StbImageSharp", "https://github.com/StbSharp/StbImageSharp", "Public Domain / MIT", "https://github.com/StbSharp/StbImageSharp#license"),
        new("stb-write", "StbImageWriteSharp", "https://github.com/StbSharp/StbImageWriteSharp", "Public Domain", "https://github.com/StbSharp/StbImageWriteSharp#license"),
        new("humanizer", "Humanizer", "https://github.com/Humanizr/Humanizer", "MIT", Mit),
        new("yaml", "YamlDotNet", "https://github.com/aaubry/YamlDotNet", "MIT", Mit),
        new("terracotta", "Terracotta", "https://github.com/burningtnt/Terracotta/tree/v0.4.2", "AGPL-3.0-or-later", "https://github.com/burningtnt/Terracotta/blob/v0.4.2/LICENSE"),
        new("filesystem", "rust-mcp-filesystem", "https://github.com/rust-mcp-stack/rust-mcp-filesystem", "MIT", "https://github.com/rust-mcp-stack/rust-mcp-filesystem/blob/ef4797360ea03eec5375e03a6f10d092dfc6a5e0/LICENSE"),
        new("retrieval", "mcp-retrieval", "https://github.com/Role1776/mcp-retrieval", "MIT", "https://github.com/Role1776/mcp-retrieval/blob/98eadccee4f374d7c178e9bcab82b3a906b84efe/LICENSE"),
        new("search", "doh-mcp", "https://github.com/99apps-id/doh-mcp", "Apache-2.0", "https://github.com/99apps-id/doh-mcp/blob/cb8e483dc06435bf7f5cddb2caac68f4cbae3d80/LICENSE"),
        new("fabric-api", "Fabric API", "https://github.com/FabricMC/fabric-api", "Apache-2.0", "https://github.com/FabricMC/fabric-api/blob/26.3/LICENSE"),
        new("cswin32", "CsWin32", "https://github.com/microsoft/CsWin32", "MIT", Mit),
        new("cswinrt", "C#/WinRT", "https://github.com/microsoft/CsWinRT", "MIT", Mit),
        new("roslyn", "Roslyn", "https://github.com/dotnet/roslyn", "MIT", Mit),
        new("roslynator", "Roslynator", "https://github.com/dotnet/roslynator", "Apache-2.0", Apache),
    ];

    internal static readonly AboutCreditPair[] Rows =
        [.. Items.Chunk(2).Select(pair => new AboutCreditPair(pair[0], pair.Length == 2 ? pair[1] : null))];
}

/// <summary>Observes theme and language inside each retained row rather than freezing them in the list factory.</summary>
internal sealed partial class AboutCreditRow : Control
{
    private readonly Action<string> _open;
    private readonly AboutSettingsSection _owner;

    internal AboutCreditRow(AboutCreditPair entry, AboutSettingsSection owner, Action<string> open)
    {
        Entry = entry;
        _owner = owner;
        _open = open;
    }

    internal partial AboutCreditPair Entry { get; set; }

    private Ui Body => new AboutCreditPairView(
        LauncherAppearance.Capture(),
        Entry,
        _owner.Compact,
        Resources.AboutMicrosoftTerms,
        Resources.AboutLicenseLabel,
        _open);
}

[View]
internal readonly partial record struct AboutCreditPairView(
    LauncherAppearance Theme,
    AboutCreditPair Entry,
    bool Compact,
    string MicrosoftTerms,
    string LicenseLabel,
    Action<string> Open)
{
    internal GridLength[] Columns => Compact ? [GridLength.Star()] : [GridLength.Star(), GridLength.Star()];
    internal GridLength[] Rows => Compact ? [GridLength.Auto, GridLength.Auto] : [GridLength.Auto];

    /// <summary>Fills the current viewport without centering a width-capped list or adding extra columns.</summary>
    public Ui Body => Grid(
        Columns,
        Rows,
        [
            new AboutCreditView(Theme, Entry.First, MicrosoftTerms, LicenseLabel, Open).Cell(0, 0),
            When(Entry.Second is not null, new AboutCreditView(Theme, Entry.Second!, MicrosoftTerms, LicenseLabel, Open))
                .Cell(Compact ? 0 : 1, Compact ? 1 : 0),
        ])
        .ColumnSpacing(36f);
}

[View]
internal readonly partial record struct AboutCreditView(
    LauncherAppearance Theme,
    AboutCredit Entry,
    string MicrosoftTerms,
    string LicenseLabel,
    Action<string> Open)
{
    internal string License => Entry.License == "Microsoft" ? MicrosoftTerms : Entry.License;

    /// <summary>Shows direct project and license links in one row, preserving technical-name reading order.</summary>
    public Ui Body => Grid(
        [GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            Flow(FlowDirection.LeftToRight,
                HyperlinkButton(Entry.Name, Entry.Project)
                    .OnNavigate(Open)
                    .Style(Theme.Buttons.Hyperlink())
                    .Automation(new()
                    {
                        Name = Entry.Name,
                        AutomationId = "settings.about.credit." + Entry.Id + ".project"
                    }))
                .Cell(0, 0),
            HyperlinkButton(License, Entry.Terms)
                .OnNavigate(Open)
                .Style(Theme.Buttons.Hyperlink(true))
                .Automation(new()
                {
                    Name = string.Format(LicenseLabel, Entry.Name, License),
                    AutomationId = "settings.about.credit." + Entry.Id + ".license"
                })
                .VAlign(VerticalAlignment.Center)
                .Cell(1, 0),
        ])
        .ColumnSpacing(12f)
        .Padding(0f, 4f);
}