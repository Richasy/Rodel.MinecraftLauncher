// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed class ContentPlatformUnavailableException() : InvalidOperationException("CurseForge online support is not included in this release. Use Modrinth or import local files and packages.");