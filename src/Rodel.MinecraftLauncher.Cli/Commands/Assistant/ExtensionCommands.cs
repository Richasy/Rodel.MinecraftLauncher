// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.CommandLine;
using static Rodel.MinecraftLauncher.Cli.Assistant.AssistantCommandFactory;
using O = Rodel.MinecraftLauncher.Cli.Assistant.AssistantOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli.Assistant;

internal static class ExtensionCommands
{
    public static void Register(Command root)
    {
        var skill = Group(root, "skill", S.Skill);
        Add(skill, "list", S.List, async (app, p, ct) => await app.Client.ListSkillsAsync(app.Host, ct).ConfigureAwait(false));
        Add(skill, "read", S.Show, async (app, p, ct) => await app.Client.ReadSkillAsync(p.GetRequiredValue(O.Target), app.Host, ct).ConfigureAwait(false), [O.Target]);
        Add(skill, "add", S.Add, async (app, p, ct) => await AiExtensionStore.SaveSkillDirectoryAsync(new AiSkillDirectoryUpdate
        {
            Id = p.GetValue(O.Id),
            ExpectedRevision = AssistantInput.Revision(p),
            Path = AssistantInput.Required(p, O.Path)
        }, ct).ConfigureAwait(false), options: [O.Id, O.Path, O.Revision]);
        Add(skill, "enable", S.Enable, async (app, p, ct) => await AiExtensionStore.SetEnabledAsync("skillDirectories", p.GetRequiredValue(O.Target), true, AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        Add(skill, "disable", S.Disable, async (app, p, ct) => await AiExtensionStore.SetEnabledAsync("skillDirectories", p.GetRequiredValue(O.Target), false, AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        Add(skill, "remove", S.Remove, async (app, p, ct) => await app.Client.Extensions.RemoveAsync("skillDirectories", p.GetRequiredValue(O.Target), AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        var mcp = Group(root, "mcp", S.Mcp);
        Add(mcp, "list", S.List, async (app, p, ct) => await app.Client.Extensions.GetAsync(ct).ConfigureAwait(false));
        Add(mcp, "save", S.Save, async (app, p, ct) => await AiExtensionStore.SaveMcpAsync(
            await AssistantInput.JsonAsync(AssistantInput.Required(p, O.File), AiExtensionJsonContext.Default.AiMcpUpdate, ct).ConfigureAwait(false), ct).ConfigureAwait(false), options: [O.File]);
        Add(mcp, "test", S.Test, async (app, p, ct) => await app.Client.InspectMcpAsync(p.GetValue(O.OptionalTarget), app.Host, ct).ConfigureAwait(false), [O.OptionalTarget]);
        Add(mcp, "enable", S.Enable, async (app, p, ct) => await AiExtensionStore.SetEnabledAsync("mcp", p.GetRequiredValue(O.Target), true, AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        Add(mcp, "disable", S.Disable, async (app, p, ct) => await AiExtensionStore.SetEnabledAsync("mcp", p.GetRequiredValue(O.Target), false, AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        Add(mcp, "remove", S.Remove, async (app, p, ct) => await app.Client.Extensions.RemoveAsync("mcp", p.GetRequiredValue(O.Target), AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        var files = Group(root, "file-root", S.FileRoots);
        Add(files, "list", S.List, async (app, p, ct) => await app.Client.Extensions.GetAsync(ct).ConfigureAwait(false));
        Add(files, "save", S.Save, async (app, p, ct) => await app.Client.Extensions.SaveRootAsync(
            await AssistantInput.JsonAsync(AssistantInput.Required(p, O.File), AiExtensionJsonContext.Default.AiFileRootUpdate, ct).ConfigureAwait(false), ct).ConfigureAwait(false), options: [O.File]);
        Add(files, "remove", S.Remove, async (app, p, ct) => await app.Client.Extensions.RemoveAsync("fileRoots", p.GetRequiredValue(O.Target), AssistantInput.Revision(p), ct).ConfigureAwait(false), [O.Target], [O.Revision]);
        var integration = Group(root, "integration", S.Integration);
        Add(integration, "plan", S.Plan, async (app, p, ct) => await app.Client.Integrations.PlanAsync(
            await AssistantInput.JsonAsync(AssistantInput.Required(p, O.File), AiClientIntegrationJsonContext.Default.AiClientIntegrationRequest, ct).ConfigureAwait(false), ct).ConfigureAwait(false), options: [O.File]);
        Add(integration, "status", S.Status, async (app, p, ct) => await app.Client.Integrations.StatusAsync(
            await AssistantInput.JsonAsync(AssistantInput.Required(p, O.File), AiClientIntegrationJsonContext.Default.AiClientIntegrationRequest, ct).ConfigureAwait(false), ct).ConfigureAwait(false), options: [O.File]);
        Add(integration, "apply", S.Apply, async (app, p, ct) =>
        {
            var request = await AssistantInput.JsonAsync(AssistantInput.Required(p, O.File), AiClientIntegrationJsonContext.Default.AiClientIntegrationRequest, ct).ConfigureAwait(false);
            return await app.Client.Integrations.ApplyAsync(request with
            {
                PlanFingerprint = AssistantInput.Required(p, O.Fingerprint)
            }, ct).ConfigureAwait(false);
        }, options: [O.File, O.Fingerprint], execution: true);
    }
}