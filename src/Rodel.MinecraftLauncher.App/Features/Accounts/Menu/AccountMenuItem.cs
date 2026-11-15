// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Semantics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Uses the native menu's navigation and activation with separate identity and selection visuals.</summary>
internal sealed class AccountMenuItem(
    IdentityRecord account,
    bool selected,
    Action select,
    AccountSkinRefresh skins,
    AccountAvatarImages avatars,
    CancellationToken lifetime) : MenuFlyoutItemBase
{
    protected override MenuItemProjection BuildItem(MenuItemBuildContext context)
    {
        Brush foreground = selected && LauncherTheme.HighContrast ? LauncherTheme.SelectionForeground : LauncherTheme.Ink;
        var label = account.Name ?? string.Empty;
        var microsoft = account.Type == "microsoft";
        var external = account.Type == "external";
        Widget glyph = microsoft ? new MicrosoftMenuGlyph() : new LineIconWidget(PathData.Parse(external ? "M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 M3 12 H21 M12 3 C7 8 7 16 12 21 M12 3 C17 8 17 16 12 21" : "M3 4 H21 V17 H3 Z M12 17 V21 M7 21 H17"), 16f, false);
        var type = AccountsFeature.TypeLabel(account);
        var typeHit = new InteractionNode(context.ItemId.Append(20), default)
        {
            ToolTip = ToolTipContent.OfText(type)
        };
        var images = Sprout.Theming.Environment.Resolve(ImageLoadServices.Key);
        var canLoad = (microsoft || external) && AccountAvatar.SkinUri(account) is not null && images?.Loader is not null;
        var cached = avatars.Get(account);
        var image = new AccountMenuAvatar(cached ?? AccountAvatarPreferences.Default, canLoad && cached is null);
        if (cached is not null) image.Complete(cached);
        var expired = (microsoft || external) && account.Status is "login-required" or "profile-removed";
        var status = expired ? new TextWidget(default, Resources.AccountSignInAgain, context.TextFormat with { FontSize = 12f }, LauncherTheme.Error) : null;
        var row = new AccountMenuRow(
            image,
            glyph,
            new TextWidget(default, label, context.TextFormat, foreground),
            status,
            selected,
            typeHit);
        // The account menu's cancellation lifetime ends on close; update retained ink outside the render walk.
        var appearance = new Sprout.Reactive.Effect(() => row.ApplyAppearance(LauncherAppearance.Capture()));
        _ = lifetime.Register(appearance.Dispose);
        if (images is not null)
        {
            var generation = 0;
            void Load(IdentityRecord value)
            {
                var current = ++generation;
                _ = LoadAsync(image, value, images, avatars.Begin(value), () => current == generation, lifetime);
            }

            void Updated(IdentityRecord value)
            {
                if (!lifetime.IsCancellationRequested && AccountSkinRefresh.SameRole(account, value)) Load(value);
            }

            skins.Updated += Updated;
            _ = lifetime.Register(() => skins.Updated -= Updated);
            Load(account);
        }

        void Highlight(Brush? hover) => row.SetHighlight(hover);
        void Activate()
        {
            context.Close();
            select();
        }

        Highlight(null);
        var interaction = new MenuItemInteraction(context.ItemId, default, Highlight, context.Palette.HighlightBackground)
        {
            Invoked = Activate,
        };
        interaction.FocusRequested = () => context.Focus(interaction);
        interaction.InsertChild(0, typeHit);
        var semantics = new SemanticsNode(context.ItemId, SemanticsRole.MenuItem, label, default)
        {
            Invoked = Activate,
            Select = () =>
            {
                if (!selected)
                    Activate();
            },
            IsSelected = selected,
        };
        return new(row, interaction, semantics, TypeAheadText: label);
    }

    private static async Task LoadAsync(
        AccountMenuAvatar image,
        IdentityRecord account,
        ImageLoadServices images,
        Action<ImageSource> remember,
        Func<bool> isCurrent,
        CancellationToken ct)
    {
        ImageSource? source = null;
        try
        {
            source = await Task.Run(async () =>
                {
                    if (AccountAvatar.SkinUri(account) is not { } uri || images.Loader is null)
                        return AccountAvatarPreferences.Read(account);
                    var skin = await images.Loader.LoadAsync(ImageSource.FromUri(uri, cacheBucket: "account-skins"), new(64, 64), images.Decoder, ct).ConfigureAwait(false);
                    return ImageSource.FromPixels(AccountAvatar.ExtractHead(skin));
                }, ct).ConfigureAwait(false);
        }
        catch (Exception)
        { /* The optional image never prevents account switching. */
        }

        images.Post(() =>
            {
                if (!ct.IsCancellationRequested && source is not null)
                    remember(source);
                if (!ct.IsCancellationRequested && (isCurrent() || !image.HasImage))
                    image.Complete(source);
            });
    }
}