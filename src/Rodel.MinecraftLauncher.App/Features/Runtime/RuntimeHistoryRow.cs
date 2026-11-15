// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Runtime;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Widgets;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Runtime;

[View]
internal readonly partial record struct RuntimeHistoryRow(LauncherAppearance Theme, RuntimeOperationSummary Operation, RuntimePage Owner)
{
    internal Action RollbackAction
    {
        get
        {
            var owner = Owner;
            var id = Operation.Id;
            return () => owner.Rollback(id);
        }
    }

    internal string Date => DateTimeOffset.TryParse(Operation.CreatedAt, out var date) ? date.ToLocalTime().ToString("g") : Resources.ValueNotAvailable;

    public Ui Body => Row([
        Text(string.Format(Resources.RuntimeOperationSummary, Operation.Target?["versionId"]?.ToString() ?? Operation.Id, Date))
            .FontSize(13f).Foreground(Theme.Ink)
            .Wrap()
            .Weight(1f),
        LauncherControls.OutlineCommand(Theme,Operation.State == "rolled-back" ? Resources.RuntimeRolledBack : Resources.RuntimeRollback, RollbackAction)
            .MinHeight(36f)
            .Enabled(Owner.Editable && Operation.ObservedState == "target-selected")
            .Automation(new()
            {
                Name = Resources.RuntimeRollback,
                AutomationId = "runtime.rollback." + Operation.Id
            }),
    ])
        .Spacing(12f)
        .Padding(new Thickness(0f, 6f, 0f, 6f));
}