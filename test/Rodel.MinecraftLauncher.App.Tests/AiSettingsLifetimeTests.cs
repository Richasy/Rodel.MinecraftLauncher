// Copyright (c) Richasy. All rights reserved.

extern alias NativeApp;

using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Semantics;
using Sprout.Widgets;
using NativeAi = NativeApp::Rodel.MinecraftLauncher.App.Features.Ai;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Exercises the production AI settings projection across virtualized provider lifetimes without a window or network.</summary>
[TestClass]
public sealed class AiSettingsLifetimeTests
{
    private static readonly string[] ModelNames = ["Original model", "Updated model", "Reopened model"];

    /// <summary>Scrolling a provider out and back must create live model controls, not reattach disposed children from a page-wide cache.</summary>
    [TestMethod]
    public void RecycledProvider_RecreatesModelRowsAndShowsCurrentConfiguration() => StateTestScope.Run(() =>
    {
        // Review: state-only configuration cases never mount controls. The real generated App view and Sprout
        // factory cross the dispose/re-realize boundary that crashed; only the unused window and artwork service are absent.
        using var scope = new StateTestScope();
        using var configuration = new NativeAi.AiConfigurationStore(scope.Client,
            readIdentity: static (_, _, _) => Task.FromResult(new AiModelIdentity(null, null, null)));
        using var section = new NativeAi.AiSettingsSection(configuration, null!);
        var provider = new NativeAi.AiUiProvider("service", "Service", AiProtocols.OpenAiChat, "http://127.0.0.1:9/",
            [new("model", "Original model", "model-code")]);
        var factory = new PoolingItemFactory<NativeAi.AiUiProvider, NativeAi.AiProviderPresentation>(
            [provider], static _ => 1, item => new(section, item, null), autoProject: true);

        foreach (var name in ModelNames)
        {
            provider = provider with { Models = [provider.Models[0] with { Name = name }] };
            factory.UpdateItems([provider]);
            var item = factory.Realize(0, ElementId.Of(1));
            try
            {
                Assert.IsNotNull(Descendants(item.Semantics!).SingleOrDefault(node => node.AutomationId == "ai.model.name.model"));
                Assert.Contains(name, TextValues(item.Render), "Re-entry must render the current configuration, not a dead or stale model row.");
                Assert.Contains(node => node.AutomationId == "ai.model.edit.model", Descendants(item.Semantics!),
                    "The model's real edit action must survive recycling with its accessible projection.");
            }
            finally
            {
                factory.Recycle(1, item);
            }
        }

        return Task.CompletedTask;
    });

    private static IEnumerable<string> TextValues(Widget root)
    {
        if (root is TextWidget text)
            yield return text.Text;
        var children = new List<Widget>();
        root.VisitChildren(children.Add);
        foreach (var child in children)
        {
            foreach (var value in TextValues(child))
                yield return value;
        }
    }

    private static IEnumerable<SemanticsNode> Descendants(SemanticsNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}