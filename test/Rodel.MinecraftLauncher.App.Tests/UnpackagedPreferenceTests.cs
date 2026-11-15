// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32;
using Rodel.MinecraftLauncher.App.Settings;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Exercises real current-user persistence independently of packaged LocalSettings and in-memory preference tests.</summary>
[TestClass]
public sealed class UnpackagedPreferenceTests
{
    /// <summary>
    /// Author review: the existing enum and Core preference cases substitute the app's backing dictionary.
    /// They cannot detect loss of Boolean types, accidental environment expansion or cross-container writes in an EXE.
    /// Independently reopening two actual registry stores protects those choices; a rejected value must preserve the old one.
    /// Only this case's unique test key is removed, and no package or player configuration is accessed.
    /// </summary>
    [TestMethod]
    public void Preferences_ReopenWithOriginalTypesAndIsolatedContainers()
    {
        var root = @"Software\Richasy\RMCL.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            var preferences = new RegistrySettings(root + @"\Preferences");
            var editor = new RegistrySettings(root + @"\SkinEditor");
            preferences["Theme"] = "Dark";
            preferences["Enabled"] = true;
            preferences["Optional"] = false;
            preferences["Directory"] = @"%USERPROFILE%\games";
            editor["Theme"] = "Light";

            var reopened = new RegistrySettings(root + @"\Preferences");
            Assert.AreEqual("Dark", reopened["Theme"]);
            Assert.IsTrue((bool?)reopened["Enabled"]);
            Assert.IsFalse((bool?)reopened["Optional"]);
            Assert.AreEqual(@"%USERPROFILE%\games", reopened["Directory"]);
            Assert.AreEqual("Light", new RegistrySettings(root + @"\SkinEditor")["Theme"]);
            Assert.ThrowsExactly<ArgumentException>(() => reopened["Theme"] = 17);
            Assert.AreEqual("Dark", preferences["Theme"]);

            reopened["Enabled"] = false;
            Assert.IsFalse((bool?)preferences["Enabled"]);
            Assert.IsTrue(reopened.Remove("Optional"));
            Assert.IsFalse(preferences.ContainsKey("Optional"));
            Assert.Contains(pair => pair.Key == "Directory" && Equals(pair.Value, @"%USERPROFILE%\games"), reopened);
            Assert.AreEqual(1, editor.Count);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }
}