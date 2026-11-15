using Rodel.MinecraftLauncher.App.Settings;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects enum preferences stored as LocalSettings-compatible strings rather than boxed enum values.</summary>
[TestClass]
public sealed class EnumPreferenceTests
{
    /// <summary>Reading defaults and unknown persisted values does not rewrite user preferences; named values retain their meaning.</summary>
    [TestMethod]
    public void Read_UsesNamedValuesAndSafeDefault_WithoutWriting()
    {
        var values = new Dictionary<string, object> { ["Theme"] = "System" };
        var settings = new Preferences(values);
        Assert.AreEqual(Choice.FollowSystem, settings.Theme);
        Assert.AreEqual("System", values["Theme"]);
        values["Theme"] = "Dark";
        Assert.AreEqual(Choice.Dark, settings.Theme);
        values["Theme"] = "future-theme";
        Assert.AreEqual(Choice.FollowSystem, settings.Theme);
        Assert.AreEqual("future-theme", values["Theme"]);
    }

    /// <summary>A committed enum choice survives reopening as a string, not an unsupported WinRT enum object, and invalid writes are atomic.</summary>
    [TestMethod]
    public void Write_PersistsStringAndNotifiesOnlyAfterCommit()
    {
        var values = new Dictionary<string, object>();
        var settings = new Preferences(values);
        var changes = 0;
        settings.PropertyChanged += (_, args) =>
        {
            Assert.AreEqual("Theme", args.PropertyName);
            Assert.AreEqual("Light", values["Theme"]);
            changes++;
        };
        settings.Theme = Choice.Light;
        settings.Theme = Choice.Light;
        Assert.AreEqual(1, changes);
        Assert.AreEqual(Choice.Light, new Preferences(values).Theme);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => settings.Theme = (Choice)99);
        Assert.AreEqual("Light", values["Theme"]);
        Assert.AreEqual(1, changes);
    }

    private enum Choice { FollowSystem, Light, Dark }

    /// <summary>A backing-store failure must not emit a successful change notification or lose the last preference.</summary>
    [TestMethod]
    public void Write_StoreFailure_RetainsPreferenceWithoutNotification()
    {
        var values = new Dictionary<string, object> { ["Theme"] = "Dark" };
        var settings = new Preferences(new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(values));
        var changes = 0;
        settings.PropertyChanged += (_, _) => changes++;
        Assert.ThrowsExactly<NotSupportedException>(() => settings.Theme = Choice.Light);
        Assert.AreEqual(Choice.Dark, settings.Theme);
        Assert.AreEqual(0, changes);
    }

    private sealed class Preferences(IDictionary<string, object> values) : SettingsBase(values)
    {
        internal Choice Theme
        {
            get => GetEnumSetting(Choice.FollowSystem, nameof(Theme));
            set => SetEnumSetting(value, Choice.FollowSystem, nameof(Theme));
        }
    }
}