// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Graphics;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed partial class AccountAvatar : Control
{
    private bool _disposed;
    private bool _mounted;
    private IdentityRecord? _account;
    private bool _pendingAccount;
    private ImageLoadServices? _images;
    private CancellationTokenSource? _load;
    private int _generation;
    private readonly AccountAvatarImages _avatars;
    private partial float AvatarSize { get; set; }
    /// <summary>Gets the current account image; online skins remain independent of local overrides.</summary>
    public partial ImageSource? Source { get; private set; }
    /// <summary>Gets whether the initial identity or its remote skin is still being loaded.</summary>
    public partial bool Loading { get; private set; }

    internal AccountAvatar(IdentityRecord? account, float size, AccountAvatarImages avatars, bool pendingAccount = false)
    {
        _avatars = avatars;
        _account = account;
        AvatarSize = size;
        _pendingAccount = pendingAccount;
        Source = avatars.Get(account);
        Loading = pendingAccount || (Source is null && SkinUri(account) is not null);
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is not AccountAvatar next)
            return;
        AvatarSize = next.AvatarSize;
        var sameRole = (_account is null && next._account is null) || AccountSkinRefresh.SameRole(_account, next._account);
        var unchanged = sameRole && SkinUri(_account) == SkinUri(next._account) && _pendingAccount == next._pendingAccount;
        _account = next._account;
        _pendingAccount = next._pendingAccount;
        if (unchanged)
            return;
        if (_mounted)
        {
            Reload(preserveSource: sameRole);
        }
        else
        {
            Source = AccountAvatarPreferences.Default;
            Loading = _pendingAccount || SkinUri(_account) is not null;
        }
    }

    /// <summary>Shows a fixed-size shimmer until the account's image resolves, without flashing the default face.</summary>
    public Ui Body => Overlay([
        Shimmer(new Size(AvatarSize, AvatarSize))
            .Style(default(ShimmerStyle).CornerRadius(0f)
                .BaseColor(LauncherTheme.Muted.MultiplyAlpha(.14f))
                .HighlightColor(LauncherTheme.Ink.MultiplyAlpha(.18f)))
            .Active(Loading)
            .Visible(Loading),
        Image(Source ?? AccountAvatarPreferences.Default)
            .Stretch(ImageStretch.Uniform)
            .Visible(!Loading),
    ])
        .Width(AvatarSize).Height(AvatarSize);

    protected override IDisposable? OnMounted()
    {
        _mounted = true;
        _images = Sprout.Theming.Environment.Resolve(ImageLoadServices.Key);
        AccountAvatarPreferences.Changed += OnAvatarChanged;
        _avatars.RefreshRequested += OnSkinRefreshed;
        _avatars.Available += OnImageAvailable;
        Reload();
        return null;
    }

    private void Reload(bool preserveSource = false)
    {
        if (!preserveSource)
        {
            _load?.Cancel();
            _load?.Dispose();
            _load = null;
        }

        var generation = ++_generation;
        if (!preserveSource)
            Source = _avatars.Get(_account);
        if (_pendingAccount)
        {
            Loading = true;
            return;
        }

        _load ??= new();
        Loading = Source is null && SkinUri(_account) is not null && _images?.Loader is not null;
        if (SkinUri(_account) is { } uri && _images is { Loader: not null } services)
        {
            _ = LoadAsync(services, uri, generation, _avatars.Begin(_account!), _load.Token);
        }
        else
        {
            _ = LoadLocalAsync(_account, generation, _load.Token);
        }
    }

    private async Task LoadLocalAsync(IdentityRecord? account, int generation, CancellationToken cancellation)
    {
        try
        {
            var source = await Task.Run(() => AccountAvatarPreferences.Read(account), cancellation);
            if (!_disposed && generation == _generation && !cancellation.IsCancellationRequested)
            {
                Source = source;
                if (account is not null)
                    _avatars.Begin(account)(source);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    internal static Uri? SkinUri(IdentityRecord? account)
    {
        if (!Uri.TryCreate(account?.SkinUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            return null;
        if (account?.Type == "external")
            return uri;
        return account?.Type == "microsoft" && uri.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase) ? new UriBuilder(uri)
        {
            Scheme = "https",
            Port = -1
        }.Uri : null;
    }

    private void OnAvatarChanged(string id)
    {
        if (!_disposed && _account?.Type == "offline" && _account.Id == id)
            Reload();
    }

    private void OnSkinRefreshed(IdentityRecord account)
    {
        if (_disposed || !AccountSkinRefresh.SameRole(_account, account)) return;
        _account = account;
        Reload(preserveSource: true);
    }

    private void OnImageAvailable(IdentityRecord account, ImageSource source)
    {
        if (_disposed || !AccountSkinRefresh.SameRole(_account, account) || (Source is not null && SkinUri(_account) != SkinUri(account))) return;
        Source = source;
        Loading = false;
    }

    private async Task LoadAsync(
        ImageLoadServices services,
        Uri uri,
        int generation,
        Action<ImageSource> remember,
        CancellationToken ct)
    {
        ImageSource? source = null;
        try
        {
            source = await Task.Run(async () =>
                {
                    var skin = await services.Loader!.LoadAsync(ImageSource.FromUri(uri, cacheBucket: "account-skins"), new(64, 64), services.Decoder, ct).ConfigureAwait(false);
                    return ImageSource.FromPixels(ExtractHead(skin));
                }, ct).ConfigureAwait(false);
        }
        catch (Exception)
        { /* A failed optional avatar never blocks account operations. */
        }

        services.Post(() =>
            {
                if (_disposed || ct.IsCancellationRequested)
                    return;
                if (source is not null)
                    remember(source);
                // A same-role refresh may finish before the cached old image. Let that image fill an
                // empty slot, but never replace a newer successfully decoded image or a different role.
                if (generation != _generation && (Source is not null || source is null))
                    return;
                if (source is not null)
                    Source = source;
                Loading = false;
            });
    }

    internal static DecodedImage ExtractHead(DecodedImage skin)
    {
        if (skin.Width < 64 || skin.Height < 32)
            throw new InvalidDataException("Invalid skin dimensions.");
        var pixels = skin.Pixels;
        var head = new byte[64 * 64 * 4];
        // Nearest-neighbour expansion preserves Minecraft's pixels; compose the hat over the face.
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var face = (((8 + (y / 8)) * skin.Width) + 8 + (x / 8)) * 4;
                var hat = (((8 + (y / 8)) * skin.Width) + 40 + (x / 8)) * 4;
                var output = ((y * 64) + x) * 4;
                var inverseAlpha = 255 - pixels[hat + 3];
                for (var channel = 0; channel < 4; channel++)
                    head[output + channel] = (byte)Math.Min(255, pixels[hat + channel] + (((pixels[face + channel] * inverseAlpha) + 127) / 255));
            }
        }

        return new(64, 64, head);
    }

    protected override void OnDispose()
    {
        _disposed = true;
        AccountAvatarPreferences.Changed -= OnAvatarChanged;
        _avatars.RefreshRequested -= OnSkinRefreshed;
        _avatars.Available -= OnImageAvailable;
        _load?.Cancel();
        _load?.Dispose();
        base.OnDispose();
    }
}