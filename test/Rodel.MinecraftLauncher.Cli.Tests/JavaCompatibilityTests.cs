using Rodel.MinecraftLauncher.Core.Java;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects Java version parsing and selection order across legacy, vendor, and prerelease metadata.</summary>
[TestClass]
public sealed class JavaCompatibilityTests
{
    /// <summary>Retains numeric components and prerelease meaning when fixed patterns use source-generated regexes.</summary>
    [TestMethod]
    [DataRow("1.8.0_402-b06", 8, 0, 402, 6, false)]
    [DataRow("21.0.4+7-LTS", 21, 0, 4, 7, false)]
    [DataRow("21.0.4+7-tem", 21, 0, 4, 7, false)]
    [DataRow("25-ea", 25, 0, 0, 0, true)]
    [DataRow("17.0.12+7-RC.1", 17, 0, 12, 7, true)]
    public void Parse_ReportedVersion_PreservesCompatibilityComponents(string text, int major, int minor, int security, int build, bool prerelease)
    {
        Assert.AreEqual(new JavaVersionInfo(major, minor, security, build, prerelease), JavaVersionInfo.Parse(text));
    }

    /// <summary>Does not invent a usable runtime version when reporting is absent or nonnumeric.</summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("unknown-version")]
    public void Parse_UnrecognizedVersion_RemainsUnknown(string? text)
    {
        Assert.IsNull(JavaVersionInfo.Parse(text));
    }

    /// <summary>Preserves runtime selection order, including legacy builds and releases preferred over prereleases.</summary>
    [TestMethod]
    [DataRow("1.8.0_402-b06", "1.8.0_402-b08")]
    [DataRow("21-ea", "21")]
    [DataRow("17.0.12+7", "21.0.4+7")]
    public void Compare_CompatibleVersionCandidates_PreservesSelectionOrder(string earlier, string later)
    {
        var first = JavaVersionInfo.Parse(earlier);
        var second = JavaVersionInfo.Parse(later);
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsLessThan(0, first.CompareTo(second));
    }
}