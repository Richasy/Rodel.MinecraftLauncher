// Copyright (c) Richasy. All rights reserved.

namespace Rodel.Minecraft.Skins.Tests;

/// <summary>Owns only a test-specific temporary directory; serialization and business assertions stay in the cases.</summary>
internal sealed class SkinSandbox : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Rodel.Skins.Tests", Guid.NewGuid().ToString("N"));

    internal SkinSandbox() => Directory.CreateDirectory(Root);

    /// <summary>Deletes only this fixture's resolved temporary directory after the test releases its file handles.</summary>
    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}