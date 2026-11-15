namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Protects the independent library's native-client boundary, not browser behavior.</summary>
[TestClass]
public sealed class SkinPackagingTests
{
    /// <summary>The shipped library must not carry an embedded HTML client after removing the web viewer.</summary>
    [TestMethod]
    public void AssemblyResources_DoNotEmbedHtmlClient()
    {
        var resources = typeof(SkinPreview).Assembly.GetManifestResourceNames();
        Assert.DoesNotContain(name => name.EndsWith(".html", StringComparison.OrdinalIgnoreCase), resources,
            string.Join(Environment.NewLine, resources));
    }
}