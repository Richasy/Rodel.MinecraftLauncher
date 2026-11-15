// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Keeps the last decoded online avatar across this window's header, menus and account rows; accessed only on the UI context.</summary>
internal sealed class AccountAvatarImages
{
    private readonly Dictionary<string, Entry> _images = [];
    internal event Action<IdentityRecord>? RefreshRequested;
    internal event Action<IdentityRecord, ImageSource>? Available;

    internal void Refresh(IdentityRecord account) => RefreshRequested?.Invoke(account);

    internal ImageSource? Get(IdentityRecord? account) => account?.Id is { } id && _images.TryGetValue(id, out var entry)
        && AccountSkinRefresh.SameRole(account, entry.Account) ? entry.Source : null;

    internal Action<ImageSource> Begin(IdentityRecord account)
    {
        if (account.Id is null || account.Type is not ("microsoft" or "external")) return _ => { };
        if (!_images.TryGetValue(account.Id, out var entry) || !AccountSkinRefresh.SameRole(account, entry.Account))
            _images[account.Id] = entry = new(account);
        if (entry.Account.SkinUrl != account.SkinUrl)
        {
            entry.Account = account;
            entry.Generation++;
        }

        var generation = entry.Generation;
        return source =>
        {
            if (!_images.TryGetValue(account.Id, out var current) || !ReferenceEquals(current, entry)) return;
            if (entry.Generation == generation || entry.Source is null)
            {
                entry.Source = source;
                Available?.Invoke(account, source);
            }
        };
    }

    internal void Retain(IEnumerable<IdentityRecord> accounts)
    {
        var ids = accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _images.Keys.Where(id => !ids.Contains(id)).ToArray())
            _images.Remove(id);
    }

    private sealed class Entry(IdentityRecord account)
    {
        internal IdentityRecord Account = account;
        internal ImageSource? Source;
        internal int Generation;
    }
}