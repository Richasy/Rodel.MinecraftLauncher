// Copyright (c) Richasy. All rights reserved.

using Microsoft.Windows.Storage;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout.Reactive;
using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Carries one stable model binding and its optional independent local context override.</summary>
/// <param name = "Id">The persisted model binding identity.</param>
/// <param name = "Name">The editable display name.</param>
/// <param name = "Code">The online model identifier or selected local GGUF path.</param>
/// <param name = "ContextLength">The explicit local context budget; null follows the per-file automatic default.</param>
internal sealed record AiUiModel(
    string Id,
    string Name,
    string Code,
    int? ContextLength = null);

internal sealed record AiUiProvider(
    string Id,
    string Name,
    string Protocol,
    string Endpoint,
    AiUiModel[] Models,
    long Revision = 0,
    bool Enabled = true,
    bool AllowHttp = false);

internal sealed record AiUiRegistration(
    string Id,
    string Name,
    string Path,
    bool Enabled = true,
    string Mode = "read",
    string Arguments = "",
    string WorkingDirectory = "",
    string Environment = "",
    string Credential = "");