// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class AppCommands
{
    internal Command Root { get; }
    internal LauncherCommand List { get; }
    private readonly Dictionary<AppControlAction, LauncherCommand> _commands = [];
    private readonly Argument<string> _app = new("app");
    private readonly Option<string?> _fingerprint = new("--expected-fingerprint") { Description = S.AppExpectedFingerprint };
    private readonly Option<string?> _file = new("--file") { Description = S.AppPreferenceFile };
    private readonly Option<string?> _changes = new("--changes") { Description = S.AppPreferenceFile };
    private readonly Option<string?> _page = new("--page") { Description = S.AppPage };
    private readonly Option<string?> _category = new("--category") { Description = S.AppCategory };
    private readonly Option<string?> _state = new("--state") { Description = S.AppWindowState };
    private readonly Option<string?> _theme = new("--theme") { Description = S.AppWallpaperTheme };
    private readonly Option<string?> _wallpaper = new("--wallpaper") { Description = S.AppWallpaperId };
    private readonly Option<string?> _source = new("--source") { Description = S.AppWallpaperSource };
    private readonly Option<string?> _name = new("--name") { Description = S.AppWallpaperName };

    internal AppCommands(Command root)
    {
        Root = Group(root, "app", S.AppCommandsDescription);
        List = Add(Root, "list", S.AppList, (app, _, _) => Task.FromResult(
            JsonSerializer.SerializeToNode(app.Client.AppControl.List(), AppControlJsonContext.Default.AppControlList)));
        List.UsesCache = false;
        Register(Root, "show", S.AppShow, AppControlAction.State, _ => new() { Action = AppControlAction.State });
        var preferences = Group(Root, "preferences", S.AppPreferences);
        Register(preferences, "update", S.AppUpdatePreferences, AppControlAction.PreferencesUpdate, parsed => new()
        {
            Action = AppControlAction.PreferencesUpdate,
            Preferences = PreferenceChanges(parsed),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_file, _changes, _fingerprint]);
        Register(Group(Root, "navigation", S.AppNavigation), "set", S.AppNavigate, AppControlAction.Navigate, parsed => new()
        {
            Action = AppControlAction.Navigate,
            Page = RequiredValue(parsed, _page),
            Category = parsed.GetValue(_category),
            InstanceId = parsed.GetValue(CliOptions.Instance),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_page, _category, CliOptions.Instance, _fingerprint]);
        Register(Group(Root, "window", S.AppWindow), "set", S.AppSetWindow, AppControlAction.WindowSet, parsed => new()
        {
            Action = AppControlAction.WindowSet,
            WindowState = RequiredValue(parsed, _state),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_state, _fingerprint]);
        Register(Root, "close", S.AppClose, AppControlAction.Close, parsed => new()
        {
            Action = AppControlAction.Close,
            Confirm = parsed.GetValue(CliOptions.Confirm),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [CliOptions.Confirm, _fingerprint]);
        var wallpapers = Group(Root, "wallpaper", S.AppWallpapers);
        var cache = Group(Root, "cache", S.AppImageCache);
        Register(cache, "show", S.AppImageCacheShow, AppControlAction.ImageCacheStatus, _ => new() { Action = AppControlAction.ImageCacheStatus });
        Register(cache, "clear", S.AppImageCacheClear, AppControlAction.ImageCacheClear, parsed => new()
        {
            Action = AppControlAction.ImageCacheClear,
            Confirm = parsed.GetValue(CliOptions.Confirm),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [CliOptions.Confirm, _fingerprint]);
        Register(wallpapers, "list", S.AppWallpaperList, AppControlAction.WallpaperList, parsed => new()
        {
            Action = AppControlAction.WallpaperList,
            Theme = RequiredValue(parsed, _theme)
        }, [_theme]);
        Register(wallpapers, "select", S.AppWallpaperSelect, AppControlAction.WallpaperSelect, parsed => new()
        {
            Action = AppControlAction.WallpaperSelect,
            Theme = RequiredValue(parsed, _theme),
            WallpaperId = RequiredValue(parsed, _wallpaper),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_theme, _wallpaper, _fingerprint]);
        Register(wallpapers, "import", S.AppWallpaperImport, AppControlAction.WallpaperImport, parsed => new()
        {
            Action = AppControlAction.WallpaperImport,
            Theme = RequiredValue(parsed, _theme),
            Source = Path.GetFullPath(RequiredValue(parsed, _source)),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_theme, _source, _fingerprint]);
        Register(wallpapers, "rename", S.AppWallpaperRename, AppControlAction.WallpaperRename, parsed => new()
        {
            Action = AppControlAction.WallpaperRename,
            Theme = RequiredValue(parsed, _theme),
            Name = RequiredValue(parsed, _name),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_theme, _name, _fingerprint]);
        Register(wallpapers, "remove", S.AppWallpaperRemove, AppControlAction.WallpaperRemove, parsed => new()
        {
            Action = AppControlAction.WallpaperRemove,
            Theme = RequiredValue(parsed, _theme),
            Confirm = parsed.GetValue(CliOptions.Confirm),
            ExpectedFingerprint = RequiredValue(parsed, _fingerprint)
        }, [_theme, CliOptions.Confirm, _fingerprint]);
    }

    internal CliInvocation Invocation(string id, AppControlRequest request)
    {
        var call = new CliInvocation(_commands[request.Action], id);
        if (request.Action is AppControlAction.State or AppControlAction.ImageCacheStatus) return call;
        if (request.Action == AppControlAction.WallpaperList) return call.Option(_theme, request.Theme);
        call.Option(_fingerprint, request.ExpectedFingerprint);
        return request.Action switch
        {
            AppControlAction.PreferencesUpdate => call.Option(_changes, JsonSerializer.Serialize(request.Preferences, AppControlJsonContext.Default.AppPreferenceUpdate)),
            AppControlAction.Navigate => call.Option(_page, request.Page).Option(_category, request.Category).Option(CliOptions.Instance, request.InstanceId),
            AppControlAction.WindowSet => call.Option(_state, request.WindowState),
            AppControlAction.Close => call.Option(CliOptions.Confirm, request.Confirm),
            AppControlAction.ImageCacheClear => call.Option(CliOptions.Confirm, request.Confirm),
            AppControlAction.WallpaperSelect => call.Option(_theme, request.Theme).Option(_wallpaper, request.WallpaperId),
            AppControlAction.WallpaperImport => call.Option(_theme, request.Theme).Option(_source, request.Source),
            AppControlAction.WallpaperRename => call.Option(_theme, request.Theme).Option(_name, request.Name),
            AppControlAction.WallpaperRemove => call.Option(_theme, request.Theme).Option(CliOptions.Confirm, request.Confirm),
            _ => throw new ArgumentException("Unknown App action.")
        };
    }

    private AppPreferenceUpdate PreferenceChanges(ParseResult parsed)
    {
        var file = parsed.GetValue(_file);
        var changes = parsed.GetValue(_changes);
        if ((file is null) == (changes is null)) throw new ArgumentException(S.AppPreferenceFileInvalid);
        if (file is not null)
        {
            using var reader = new StreamReader(file, new System.Text.UTF8Encoding(false, true));
            var buffer = new char[65537];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = reader.Read(buffer, length, buffer.Length - length);
                if (read == 0) break;
                length += read;
            }

            changes = new string(buffer, 0, length);
        }

        if (changes!.Length > 65536) throw new ArgumentException(S.AppPreferenceFileInvalid);
        return JsonNode.Parse(changes)?.Deserialize(AppControlJsonContext.Default.AppPreferenceUpdate)
            ?? throw new ArgumentException(S.AppPreferenceFileInvalid);
    }

    private void Register(Command parent, string name, string description, AppControlAction action, Func<ParseResult, AppControlRequest> request, Option[]? options = null)
    {
        var command = Add(parent, name, description, async (app, parsed, ct) =>
            await app.Client.AppControl.InvokeAsync(parsed.GetRequiredValue(_app), request(parsed), ct).ConfigureAwait(false), [_app], options);
        command.UsesCache = false;
        _commands.Add(action, command);
        command.ValidateInput = parsed =>
        {
            if (parsed.GetValue(CliOptions.Background)) throw new ArgumentException(S.AppBackgroundUnsupported);
        };
    }

    private static string RequiredValue(ParseResult parsed, Option<string?> option)
        => parsed.GetValue(option) ?? throw new ArgumentException(string.Format(S.ErrorOptionRequired, option.Name));
}