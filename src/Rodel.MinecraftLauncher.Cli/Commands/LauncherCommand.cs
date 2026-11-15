// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class LauncherCommand(string name, string description, Func<LauncherApplication, ParseResult, CancellationToken, Task<JsonNode?>> handler) : Command(name, description)
{
    internal Func<LauncherApplication, ParseResult, CancellationToken, Task<JsonNode?>> Handler { get; } = handler;
    internal Func<ParseResult, LauncherTaskRequest>? TaskFactory { get; set; }
    internal Func<LauncherClient, ParseResult, CancellationToken, Task<LauncherTaskRequest>>? AsyncTaskFactory { get; set; }
    internal Action<ParseResult>? ValidateInput { get; set; }
    internal bool UsesCache { get; set; } = true;
    internal Func<JsonNode?, LauncherResultStatus>? ResultStatus { get; set; }
}