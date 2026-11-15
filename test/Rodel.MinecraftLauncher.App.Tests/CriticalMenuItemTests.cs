// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Menus;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Theming;
using Sprout.Widgets;
using System.Reflection;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects critical command activation and live contrast without replacing the native menu input machinery.</summary>
[TestClass]
public sealed class CriticalMenuItemTests
{
    private static readonly string[] expected = ["closed", "action"];

    /// <summary>All input sources close the menu before one action, while an open highlighted item follows contrast changes.</summary>
    [TestMethod]
    [DataRow("pointer")]
    [DataRow("keyboard")]
    [DataRow("uia")]
    public void CriticalCommand_UsesNativeActivationAndLiveContrast(string input)
    {
        var theme = new Theme(SystemThemeSnapshot.Default);
        using var ambient = Sprout.Theming.Environment.Push(Theme.Key, theme);
        var events = new List<string>();
        using var item = new CriticalMenuItem("Remove", () => events.Add("action"));
        var projection = Project(item, () => events.Add("closed"));
        var row = (MenuItemRowWidget)projection.Row;
        var label = row.Children.OfType<TextWidget>().Single();
        var node = projection.Interaction!;
        LayoutPass.Run(row, BoxConstraints.Tight(new Size(240f, 40f)), LayoutContext.Measureless);
        node.HitRegion = row.Bounds;
        node.OnFocusChanged(true);
        Assert.AreEqual(ThemeColorToken.SystemFillColorCritical, label.Foreground.AsThemeToken);

        var contrast = SystemThemeSnapshot.Default with
        {
            Mode = ThemeMode.HighContrast,
            HighContrast = SystemThemeSnapshot.Default.HighContrast with
            {
                SystemColorWindowTextColor = Colors.White,
                SystemColorHighlightTextColor = Colors.Yellow,
            }
        };
        theme.SetSystem(contrast);
        Assert.AreEqual(Colors.Yellow, label.Foreground.AsSolid, "Highlighted critical ink must not override the system contrast pair.");
        node.OnFocusChanged(false);
        Assert.AreEqual(Colors.White, label.Foreground.AsSolid);
        theme.SetSystem(SystemThemeSnapshot.Default);
        Assert.AreEqual(ThemeColorToken.SystemFillColorCritical, label.Foreground.AsThemeToken);

        if (input == "uia")
        {
            projection.Semantics!.Invoked!();
        }
        else if (input == "keyboard")
        {
            node.OnKeyEvent(new(KeyEventKind.Down, Key.Space, Repeat: false));
            node.OnKeyEvent(new(KeyEventKind.Up, Key.Space, Repeat: false));
        }
        else
        {
            node.OnPointerEvent(new(PointerEventKind.Down, new Point(10f, 10f), PointerButton.Primary, PointerDeviceType.Mouse));
            node.OnPointerEvent(new(PointerEventKind.Up, new Point(10f, 10f), PointerButton.Primary, PointerDeviceType.Mouse));
        }

        CollectionAssert.AreEqual(expected, events);
        item.Dispose();
        theme.SetSystem(contrast);
        Assert.AreEqual(ThemeColorToken.SystemFillColorCritical, label.Foreground.AsThemeToken, "Closing must release the old menu's theme observer.");
    }

    /// <summary>A disabled critical item remains announced but cannot be focused or invoked, and keeps disabled ink.</summary>
    [TestMethod]
    public void DisabledCriticalCommand_IsNotInvokable()
    {
        var calls = 0;
        using var item = new CriticalMenuItem("Remove", () => calls++) { IsEnabled = false };
        var projection = Project(item, () => calls++);
        Assert.IsNull(projection.Interaction);
        Assert.IsFalse(projection.Semantics!.IsEnabled);
        Assert.IsNull(projection.Semantics.Invoked);
        var label = ((MenuItemRowWidget)projection.Row).Children.OfType<TextWidget>().Single();
        Assert.AreEqual(MenuPalette.FromTheme(ThemeColors.ForDark(SystemColors.Default)).ForegroundDisabled, label.Foreground);
        Assert.AreEqual(0, calls);
    }

    private static MenuItemProjection Project(CriticalMenuItem item, Action close)
    {
        // Sprout exposes the custom-item seam but constructs its context internally. Reflection is confined
        // to that pinned test boundary; production receives the real context from the menu host.
        var context = (MenuItemBuildContext)Activator.CreateInstance(typeof(MenuItemBuildContext),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [ElementId.Of(19), MenuPalette.FromTheme(ThemeColors.ForDark(SystemColors.Default)),
                new TextFormat("Segoe UI", 13f, TextFormat.NormalWeight), close, (Action<InteractionNode>)(_ => { })], null)!;
        return (MenuItemProjection)typeof(CriticalMenuItem).GetMethod("BuildItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, [context])!;
    }
}