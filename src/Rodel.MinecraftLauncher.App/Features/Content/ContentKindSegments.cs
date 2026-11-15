// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

[View]
internal readonly partial record struct ContentKindSegments(ContentPage Page, LauncherSegmentedMaterial Material, bool Search)
{
    public Ui Body => Column([
        LauncherControls.Segmented(new ContentKindFilters(Page, Search), Material)
            .HAlign(HorizontalAlignment.Stretch),
    ]);
}

internal sealed partial class ContentKindFilters : Control
{
    internal ContentKindFilters(ContentPage page, bool search)
    {
        Page = page;
        Search = search;
    }

    private partial ContentPage Page { get; set; }
    private partial bool Search { get; set; }

    public override void ReconcileFrom(Control source)
    {
        if (source is ContentKindFilters other)
        {
            Page = other.Page;
            Search = other.Search;
        }
    }

    private SegmentedItem[] Items => Search ? [new(Resources.ContentAllTypes)
        {
            Automation = new()
            {
                AutomationId = "content.search.filter.all"
            }
        }, ..TypedItems] : [..TypedItems, new(Resources.ContentWorlds)
        {
            Automation = new()
            {
                AutomationId = "content.kind.worlds"
            }
        },
        new(Resources.ContentScreenshots)
        {
            Automation = new() { AutomationId = "content.kind.screenshots" }
        }
    ];
    private SegmentedItem[] TypedItems => [new(Resources.ContentMods)
        {
            Automation = new()
            {
                AutomationId = Search ? "content.search.filter.mods" : "content.kind.mods"
            }
        }, new(Resources.ContentResourcePacks)
        {
            Automation = new()
            {
                AutomationId = Search ? "content.search.filter.resourcepacks" : "content.kind.resourcepacks"
            }
        }, new(Resources.ContentShaderPacks)
        {
            Automation = new()
            {
                AutomationId = Search ? "content.search.filter.shaderpacks" : "content.kind.shaderpacks"
            }
        }, ];
    private int Selected => !Search && Page.ScreenshotMode ? 4 : !Search && Page.WorldMode
        ? 3
        : Search && Page.SearchKind is null ? 0 : (Search ? 1 : 0) + ((Search ? Page.SearchKind : Page.Kind) switch
        {
            ContentKind.Mods => 0,
            ContentKind.ResourcePacks => 1,
            _ => 2
        });
    private string GroupName => string.Format(Resources.ContentDetectedType, Search
            ? Page.SearchKind?.Title() ?? Resources.ContentAllTypes
            : Page.ScreenshotMode ? Resources.ContentScreenshots : Page.WorldMode ? Resources.ContentWorlds : Page.Kind.Title());

    private void Choose(int index)
    {
        if (Search && index == 0)
        {
            Page.ChooseSearchKind(null);
            return;
        }

        if (!Search && index == 3)
        {
            Page.ChooseWorlds();
            return;
        }

        if (!Search && index == 4)
        {
            Page.ChooseScreenshots();
            return;
        }

        var kind = (Search ? index - 1 : index) switch
        {
            0 => ContentKind.Mods,
            1 => ContentKind.ResourcePacks,
            2 => ContentKind.Shaders,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
        if (Search)
            Page.ChooseSearchKind(kind);
        else
            Page.ChooseKind(kind);
    }

    private Ui Body => Segmented(Items)
        .SelectedIndex(Selected)
        .OnSelectionChanged(index => Choose(index))
        .Style(LauncherStyles.Segmented)
        .Enabled(!Page.State.Active && !Page.Screenshots.Busy)
        .MinHeight(40f)
        .HAlign(HorizontalAlignment.Stretch)
        .Automation(new()
        {
            Name = GroupName,
            AutomationId = Search ? "content.search.filter" : "content.kind.filter"
        });
}