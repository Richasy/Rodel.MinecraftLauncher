// Copyright (c) Richasy. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rodel.MinecraftLauncher.SettingsGenerator;
using System.Collections.Immutable;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects handwritten value-view projections and application defaults outside Sprout's generated-view gate.</summary>
[TestClass]
public sealed class ViewThemeReadAnalyzerTests
{
    private const string Prelude = """
        using System;
        using Sprout;
        using Sprout.Theming;
        using static Sprout.Markup;
        namespace Sprout.Reconcile
        {
            public sealed class ViewAttribute : Attribute { }
            public interface IView { }
        }
        namespace Sprout
        {
            public readonly struct Ui { }
            public sealed class SproutAppBuilder
            {
                public void UseDefaultStyle<T>(T style) { }
            }
            public static class Markup
            {
                public static Ui Text(int color) => default;
                public static Ui Callback(Action callback) => default;
                public static Ui Value(Func<int> value) => default;
                public static Ui Defer(Func<Ui> content) => default;
            }
        }
        namespace Sprout.Styling
        {
            public sealed class AppStyleRegistry
            {
                public void Add<T>(T style) { }
            }
        }
        namespace Sprout.Theming
        {
            public sealed class Theme
            {
                public static Theme Resolve() => new();
                public int Colors => 1;
                public int Mode => 2;
                public int System => 3;
            }
        }
        namespace Product
        {
            public sealed class Snapshot
            {
                public int Ink { get; } = Facade.Ink;
                public int OtherInk { get => 1; set { _ = Facade.Ink; } }
            }

            public static class Facade
            {
                public static int Ink => Theme.Resolve().Colors;
                public static int Style() => Ink;
                public static readonly int Frozen = Theme.Resolve().Mode;
                public static Action Paint => () => { _ = Ink; };
                public static void PaintNow() { _ = Ink; }
                public static int CycleA => CycleB;
                public static int CycleB => DateTime.Now.Ticks == 0 ? CycleA : Ink;
            }
        }
        """;

    // Value/design review: a missed transitive read can ship unreadable retained UI after a theme switch.
    // Sprout's SPROUT074 owns generated [View] bodies. These small handwritten IView compilations
    // compilations exercise the real analyzer, distinguish executed value factories from deferred void callbacks,
    // and use the framework's documented type/member boundary rather than reproducing the analyzer's traversal.
    // The minimal Sprout declarations only make semantic binding possible; they do not simulate UI rendering.
    /// <summary>Rejects eager live-theme reads through the app patterns responsible for stale retained colors.</summary>
    [TestMethod]
    [DataRow("Text(Theme.Resolve().Colors)")]
    [DataRow("Text(Facade.Ink)")]
    [DataRow("Text(Facade.Style())")]
    [DataRow("Text(Facade.Frozen)")]
    [DataRow("Text(Facade.CycleA)")]
    [DataRow("Text(((Func<int>)(() => Facade.Ink))())")]
    [DataRow("Value(() => Facade.Ink)")]
    [DataRow("Text(new Snapshot().Ink)")]
    public async Task EagerThemeDependency_IsAnError(string body)
    {
        var diagnostics = await AnalyzeAsync(body, false, true);
        Assert.HasCount(1, diagnostics);
        Assert.AreEqual("RMLUI001", diagnostics[0].Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        Assert.Contains("ProductView", diagnostics[0].GetMessage());
    }

    /// <summary>Allows immutable input and callbacks whose theme reads occur outside pure view evaluation.</summary>
    [TestMethod]
    [DataRow("Text(Ink)")]
    [DataRow("Text(Appearance.Ink)")]
    [DataRow("Text(Appearance.OtherInk)")]
    [DataRow("Callback(() => { _ = Facade.Ink; })")]
    [DataRow("Callback(Facade.PaintNow)")]
    [DataRow("Callback(Facade.Paint)")]
    [DataRow("Text(nameof(Theme.Colors).Length)")]
    [DataRow("Defer(() => Text(Facade.Ink))")]
    public async Task DeferredOrExplicitAppearance_IsAllowed(string body)
    {
        Assert.IsEmpty(await AnalyzeAsync(body, false, true));
    }

    /// <summary>Leaves tracked control-body reads to Sprout instead of prohibiting the source of appearance snapshots.</summary>
    [TestMethod]
    public async Task TrackedOwner_CanReadTheTheme()
    {
        Assert.IsEmpty(await AnalyzeAsync("Text(Facade.Ink)", false));
    }

    /// <summary>Leaves generated bodies to Sprout so one unsafe read does not emit two competing diagnostics.</summary>
    [TestMethod]
    public async Task GeneratedView_IsOwnedBySprout()
    {
        Assert.IsEmpty(await AnalyzeAsync("Text(Facade.Ink)", true));
    }

    // Review: IDE workspaces expose source declarations from referenced compilations, while normal builds use
    // metadata. Treating that foreign tree as the current compilation crashes the analyzer instead of checking UI.
    /// <summary>Preserves the same-compilation boundary when an IDE supplies a source project reference.</summary>
    [TestMethod]
    public async Task ReferencedCompilation_DoesNotCrashTheAnalyzer()
    {
        var library = CSharpCompilation.Create("ThemeLibrary",
            [CSharpSyntaxTree.ParseText(Prelude, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: TestContext.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.IsEmpty(library.GetDiagnostics(TestContext.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var diagnostics = await AnalyzeSourceAsync("""
            public readonly record struct ReferencedView : Sprout.Reconcile.IView
            {
                public Sprout.Ui Chrome => Sprout.Markup.Text(Product.Facade.Ink);
            }
            """, library.ToMetadataReference());

        Assert.IsEmpty(diagnostics);
    }

    // Application registrations are copied once; even a legitimate appearance snapshot becomes stale there.
    // This boundary is distinct from pure-view diffing and must preserve fixed geometry/token registrations.
    /// <summary>Rejects live colors frozen into immutable application defaults, including a local alias.</summary>
    [TestMethod]
    [DataRow("styles.Add(Facade.Ink);", true)]
    [DataRow("var color = Facade.Ink; styles.Add(color);", true)]
    [DataRow("builder.UseDefaultStyle(Facade.Ink);", true)]
    [DataRow("styles.Add(1);", false)]
    [DataRow("builder.UseDefaultStyle(1);", false)]
    public async Task ApplicationDefaults_CannotCaptureLiveTheme(string statements, bool rejects)
    {
        var source = Prelude + $$"""
            namespace Product
            {
                public static class Startup
                {
                    public static void Configure(Sprout.Styling.AppStyleRegistry styles, SproutAppBuilder builder)
                    {
                        {{statements}}
                    }
                }
            }
            """;
        var diagnostics = await AnalyzeSourceAsync(source);
        if (rejects)
        {
            Assert.HasCount(1, diagnostics);
            Assert.AreEqual("RMLUI002", diagnostics[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Error, diagnostics[0].Severity);
        }
        else
        {
            Assert.IsEmpty(diagnostics);
        }
    }

    private async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body, bool pureView, bool manualView = false)
    {
        var source = Prelude + $$"""
            namespace Product
            {
                {{(pureView ? "[Sprout.Reconcile.View]" : string.Empty)}}
                public readonly record struct ProductView(int Ink, Snapshot Appearance) {{(manualView ? ": Sprout.Reconcile.IView" : string.Empty)}}
                {
                    public Ui {{(manualView ? "Chrome" : "Body")}} => {{body}};
                }
            }
            """;
        return await AnalyzeSourceAsync(source);
    }

    private async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string source, params MetadataReference[] references)
    {
        var syntax = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: TestContext.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "ThemeBoundary",
            [syntax],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), .. references],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics(TestContext.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        var options = new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false);
        return await compilation.WithAnalyzers([new ViewThemeReadAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync(TestContext.CancellationToken);
    }

    /// <summary>Provides cancellation for compilation and analyzer execution.</summary>
    public TestContext TestContext { get; set; } = null!;
}