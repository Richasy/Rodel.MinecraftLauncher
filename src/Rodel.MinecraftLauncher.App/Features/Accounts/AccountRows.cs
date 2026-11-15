// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed record AccountRowData(
    int Key,
    IdentityRecord Account,
    string CurrentId,
    bool Busy,
    bool GroupEnd);

internal sealed partial class AccountRows : Control
{
    private readonly Dictionary<string, int> _keys = [with(StringComparer.Ordinal)];
    private readonly PoolingItemFactory<AccountRowData, AccountRowView> _factory;
    private readonly ItemsRepeater _repeater;

    internal AccountRows(AccountPopup owner)
    {
        _factory = new([], row => row.Key, row => new AccountRowView(
LauncherAppearance.Capture(), row.Account,
                owner._feature.Avatars,
                row.CurrentId,
                row.Busy,
                row.GroupEnd,
                id => _ = owner.SelectAsync(id),
                owner.ShowActions,
                account => _ = owner._feature.ShowAvatarAsync(account, true)), autoProject: true);
        _repeater = new(_factory, new StackLayout { EstimatedItemExtent = 64f });
        _repeater.KeyboardNavigation();
    }

    internal AccountRows WithRows(IdentityRecord[] accounts, string currentId, bool busy)
    {
        _factory.UpdateItems([..accounts.Select((account, index) => new AccountRowData(
                    Key(account.Id!),
                    account,
                    currentId,
                    busy,
                    index < accounts.Length - 1 && account.Type != accounts[index + 1].Type))]);
        return this;
    }

    private int Key(string id)
    {
        if (!_keys.TryGetValue(id, out var key))
            _keys[id] = key = _keys.Count;
        return key;
    }

    public Ui Body => _repeater;
}