// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

internal sealed partial class SettingsSearchBox(Action<SettingsSearchEntry> navigate) : Control
{
    private partial string Query { get; set; }

    private Ui Body => new SettingsSearchEditor(Query ?? string.Empty, SettingsSearchIndex.Search(Query ?? string.Empty), Change, navigate).MinHeight(40f);

    private void Change(AutoSuggestBoxTextChangedArgs args)
    {
        if (args.Reason == AutoSuggestBoxTextChangeReason.UserInput)
            Query = args.Text;
    }
}