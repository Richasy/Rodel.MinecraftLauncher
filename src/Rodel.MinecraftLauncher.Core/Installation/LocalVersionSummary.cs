// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Installation;

/// <summary>Identifies version metadata found in a previously used Minecraft directory.</summary>
/// <param name="Id">The installed version identifier.</param>
/// <param name="MinecraftDirectory">The directory containing its versions folder.</param>
/// <param name="Type">The local-source marker used to distinguish installed metadata from online releases.</param>
public sealed record LocalVersionSummary(string Id, string MinecraftDirectory, string Type = "local");