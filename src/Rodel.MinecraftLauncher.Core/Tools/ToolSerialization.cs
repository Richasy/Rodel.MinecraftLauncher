// Copyright (c) Richasy. All rights reserved.
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Rodel.MinecraftLauncher.Core.Tools;

internal static class ToolSerialization
{
    private static readonly System.Text.Json.Serialization.JsonSerializerContext[] Contexts = [
        global::Rodel.MinecraftLauncher.Core.GameBridge.GameBridgeJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Skins.SkinsJsonContext.Default,
        global::Rodel.Minecraft.Skins.SkinJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Instances.InstanceJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Configuration.ConfigurationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Runtime.RuntimeJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Launch.LaunchJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Content.ContentJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.ApplicationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AppControlJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiRepairJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiApprovalJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiChatJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiClientIntegrationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiContextJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiDnsJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiEffectJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiExtensionJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiFileRootJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiHostJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiHostRequestJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiImageJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiManagedJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiTitleJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiToolObservationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AI.AiUserPromptJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.AppControlWireJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.DiagnosticJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.ExternalAuthenticationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.LauncherOperationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Application.LauncherTaskJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Backups.BackupJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.GameBridge.BridgeWireJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.GameBridge.GameAiJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.GameBridge.GameAiSlotJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Installation.InstallationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Java.JavaJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Java.JavaOperationJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Launch.GameLogJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Loaders.LoaderJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Packs.PackJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Servers.ServerConnectionJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Servers.ServerJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Skins.SkinStoreJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Storage.StorageJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Tools.GameBridgeToolJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Tools.SkinToolJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Tools.ToolInputJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Tools.ToolJsonContext.Default,
        global::Rodel.MinecraftLauncher.Core.Worlds.WorldJsonContext.Default];
    internal static JsonNode? Node<T>(T value)
    {
        var typeInfo = Contexts.Select(context => context.GetTypeInfo(typeof(T))).FirstOrDefault(info => info is not null)
            ?? throw new InvalidOperationException("Missing source-generated tool output metadata for " + typeof(T).Name);
        return JsonSerializer.SerializeToNode(value, typeInfo);
    }
}