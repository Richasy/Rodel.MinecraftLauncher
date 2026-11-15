// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Settings;

/// <summary>Retains Sprout's real suggestion editor and applies the launcher's shared input chrome.</summary>
internal sealed class SettingsSearchEditor : Control
{
    private string _query;
    private SettingsSearchEntry[] _results;
    private Action<AutoSuggestBoxTextChangedArgs> _change;
    private Action<SettingsSearchEntry> _navigate;
    private AutoSuggestBox _box;
    private bool _mounted;

    internal SettingsSearchEditor(
        string query,
        SettingsSearchEntry[] results,
        Action<AutoSuggestBoxTextChangedArgs> change,
        Action<SettingsSearchEntry> navigate)
    {
        _query = query;
        _results = results;
        _change = change;
        _navigate = navigate;
        _box = CreateBox();
    }

    private AutoSuggestBox CreateBox()
    {
        var box = AutoSuggestBox().Text(_query).Placeholder(Resources.SettingsSearchPlaceholder).QueryIcon().ContentPadding(LauncherStyles.InputPadding)
            .SuggestionFlyoutBackground(static theme => new LauncherAppearance(theme.Colors, theme.Mode).Materials.Acrylic)
            .SuggestionPopupMode(Sprout.Popups.PopupWindowedMode.InWindow).MaxSuggestionListHeight(280f).UpdateTextOnSelect(false).OnTextChanged(args => _change(args)).OnQuerySubmitted(Submit).Automation(new()
            {
                Name = Resources.SettingsSearchPlaceholder,
                AutomationId = "settings.search"
            });
        ApplySuggestions(box, LauncherAppearance.Capture());
        return box;
    }

    private void ApplySuggestions(AutoSuggestBox box, LauncherAppearance theme)
    {
        // Retain the snapshot in row factories: projecting a row later must not re-read ambient colors.
        box.Suggestions(_results, entry => new SettingsSearchSuggestion(theme, entry.Title, entry.CategoryTitle),
            static entry => entry.Title, automationOf: static entry => new()
            {
                Name = entry.Title,
                HelpText = entry.CategoryTitle,
                AutomationId = "settings.search.result." + entry.Id
            });
        if (!string.IsNullOrWhiteSpace(_query))
            box.SuggestionEmptyView(new SettingsSearchEmpty(theme, true));
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is not SettingsSearchEditor next)
            return;
        _query = next._query;
        _results = next._results;
        _change = next._change;
        _navigate = next._navigate;
        var box = CreateBox();
        if (_mounted)
        {
            _box.ReconcileFrom(box);
            ApplyChrome();
        }
        else
        {
            _box = box;
        }
    }

    protected override void OnMount()
    {
        _mounted = true;
        _box.SetRootId(RootId.Append(0));
        _box.SetInvalidationSink(RaiseInvalidated);
        _box.Mount();
        SetProjection(_box.Render, _box.Interaction, _box.Semantics);
        Scope.Run(() => _ = new Sprout.Reactive.Effect(() =>
                {
                    using (RestoreMountEnvironment())
                    {
                        ApplyChrome();
                    }
                }));
    }

    private void ApplyChrome()
    {
        // Observe language in the mounted effect even when an empty query has no localized suggestion results.
        _ = Sprout.Localization.Localizer.CurrentLanguage;
        ApplySuggestions(_box, LauncherAppearance.Capture());
        var placeholder = Resources.SettingsSearchPlaceholder;
        _box.Automation(new()
        {
            Name = placeholder,
            AutomationId = "settings.search"
        });
        _box.Placeholder(placeholder);
        if (_box.Render is AutoSuggestBoxWidget widget)
        {
            widget.TextBox.SetControlCornerRadius(CornerRadius.Zero);
            widget.TextBox.SetContentFontSize(LauncherStyles.InputFontSize);
            widget.TextBox.SetColorPalette(LauncherStyles.InputPalette);
        }

        if (_box.Interaction is { } interaction)
            interaction.FocusVisualCornerRadius = CornerRadius.Zero;
        if (_box.Semantics is { } semantics)
        {
            semantics.Name = placeholder;
            foreach (var child in semantics.Children)
            {
                if (child.Role == Sprout.Semantics.SemanticsRole.Button)
                    child.Name = placeholder;
            }
        }
    }

    protected override void OnSyncGeometry() => _box.SyncGeometry();

    protected override void OnDispose()
    {
        _mounted = false;
        _box.Dispose();
        base.OnDispose();
    }

    private void Submit(AutoSuggestBoxQuerySubmittedArgs args)
    {
        if (args.HasChosenSuggestion && args.ChosenIndex < _results.Length)
            _navigate(_results[args.ChosenIndex]);
        else if (_results.Length == 1 && !string.IsNullOrWhiteSpace(args.QueryText))
            _navigate(_results[0]);
    }
}

[View]
internal readonly partial record struct SettingsSearchSuggestion(LauncherAppearance Theme, string Title, string Category)
{
    public Ui Body => Column([
        Text(Title)
            .FontSize(14f).Foreground(Theme.Ink)
            .Trim(TextTrimming.CharacterEllipsis),
        Text(Category)
            .FontSize(12f).Foreground(Theme.Muted)
            .Trim(TextTrimming.CharacterEllipsis),
    ])
        .Spacing(3f);
}

[View]
internal readonly partial record struct SettingsSearchEmpty(LauncherAppearance Theme, bool Show)
{
    public Ui Body => Column([
        Text(Resources.SettingsSearchEmpty)
            .FontSize(13f).Foreground(Theme.Muted)
            .Wrap()
            .Visible(Show)
    ])
        .Padding(12f);
}