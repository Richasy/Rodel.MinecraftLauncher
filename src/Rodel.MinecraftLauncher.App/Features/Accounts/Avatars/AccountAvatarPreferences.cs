// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Images;
using Rodel.MinecraftLauncher.App.Settings;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>App-owned avatar preferences; Microsoft accounts never resolve this local override.</summary>
internal static class AccountAvatarPreferences
{
    internal static readonly AccountAvatarStore Store = new(AppStorage.OpenSettings("AccountAvatars"), Path.Combine(AppStorage.LocalPath, "AccountAvatars"), [.. ImagePresetCatalog.Avatars.Select(p => p.Id)]);
    internal static event Action<string>? Changed;
    internal static ImageSource Default => ImagePresetCatalog.Avatars[0].Source;

    internal static ImageSource Source(AccountAvatarPreference preference) => preference.File is not null ? ImageSource.FromFile(Store.FilePath(preference)) : ImagePresetCatalog.Avatars.First(p => p.Id == preference.Preset).Source;

    internal static ImageSource Read(IdentityRecord? account)
    {
        if (account is null || account.Type != "offline" || account.Id is null)
            return Default;
        try
        {
            return Source(Store.Read(account.Id));
        }
        catch (Exception)
        {
            return Default;
        }
    }

    internal static void Notify(string id) => Changed?.Invoke(id);
}