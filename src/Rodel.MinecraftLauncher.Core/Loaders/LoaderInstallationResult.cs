// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Loaders;

/// <summary>Reports a committed loader profile and the files and processors used to prepare it.</summary>
/// <param name="VersionId">Installed launch profile identifier.</param>
/// <param name="MinecraftVersion">Base Minecraft version.</param>
/// <param name="Loader">Selected loader family.</param>
/// <param name="LoaderVersion">Selected loader release.</param>
/// <param name="ProfileSource">Actual provider or preserved profile source.</param>
/// <param name="ProcessorsRun">Installer processors completed during this attempt.</param>
/// <param name="Files">Committed file paths.</param>
/// <param name="Warnings">Recoverable preparation warnings.</param>
/// <param name="JournalPath">Retained installation journal.</param>
public sealed record LoaderInstallationResult(string VersionId, string MinecraftVersion, string Loader,
    string? LoaderVersion, string ProfileSource, int ProcessorsRun, IReadOnlyList<string> Files,
    IReadOnlyList<string> Warnings, string JournalPath);