// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class SkinReviewCommands
{
    internal Option<string?> Label { get; } = new("--label") { Description = S.SkinLabelDescription };
    internal Option<string> Operation { get; } = new("--operation") { Description = S.SkinOperationDescription, Required = true };
    internal Command ScopeCreate { get; }
    internal Command ScopeShow { get; }
    internal Command ScopeRevoke { get; }
    internal Command Propose { get; }
    internal Command Show { get; }
    internal Command Accept { get; }
    internal Command Reject { get; }
    internal Command Preview { get; }
    internal Command Receipt { get; }

    internal SkinReviewCommands(Command skin, SkinCommands parent)
    {
        var scope = Group(skin, "scope", S.SkinScopeDescription);
        ScopeCreate = Add(scope, "create", S.SkinScopeCreateDescription, async (app, result, ct) =>
        {
            var limits = SkinCommands.ParseRequest(result.GetRequiredValue(parent.Request), SkinsJsonContext.Default.SkinScopeRequest);
            var grant = await new SkinService(app.Paths).Proposals.CreateScopeAsync(result.GetRequiredValue(CliArguments.Target), limits, ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(grant, SkinsJsonContext.Default.SkinScope);
        }, [CliArguments.Target], [parent.Request]);
        ScopeShow = Add(scope, "show", S.SkinScopeShowDescription, (app, result, _) =>
            Task.FromResult(JsonSerializer.SerializeToNode(new SkinService(app.Paths).Proposals.ReadScope(result.GetRequiredValue(CliArguments.Target)), SkinsJsonContext.Default.SkinScope)), [CliArguments.Target]);
        ScopeRevoke = Add(scope, "revoke", S.SkinScopeRevokeDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths).Proposals;
            var id = result.GetRequiredValue(CliArguments.Target);
            await service.RevokeScopeAsync(id, ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.ReadScope(id), SkinsJsonContext.Default.SkinScope);
        }, [CliArguments.Target]);
        var proposal = Group(skin, "proposal", S.SkinProposalDescription);
        Propose = Add(proposal, "create", S.SkinProposeDescription, async (app, result, ct) =>
        {
            var edits = SkinCommands.ParseRequest(result.GetValue(parent.Edits) ?? throw new ArgumentException(S.SkinEditsRequiredError), SkinJsonContext.Default.SkinEditArray);
            var info = await new SkinService(app.Paths).Proposals.ProposeAsync(result.GetRequiredValue(CliArguments.Target), result.GetValue(parent.Revision),
                edits, result.GetValue(Label), ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(info, SkinsJsonContext.Default.SkinProposalInfo);
        }, [CliArguments.Target], [parent.Revision, parent.Edits, Label]);
        Show = Add(proposal, "show", S.SkinProposalShowDescription, (app, result, _) =>
            Task.FromResult(JsonSerializer.SerializeToNode(new SkinService(app.Paths).Proposals.Describe(result.GetRequiredValue(CliArguments.Target)), SkinsJsonContext.Default.SkinProposalInfo)), [CliArguments.Target]);
        Accept = Add(proposal, "accept", S.SkinAcceptDescription, async (app, result, ct) =>
            JsonSerializer.SerializeToNode(await new SkinService(app.Paths).Proposals.AcceptAsync(result.GetRequiredValue(CliArguments.Target),
                result.GetValue(parent.Revision), ct).ConfigureAwait(false), SkinsJsonContext.Default.SkinCommitReceipt), [CliArguments.Target], [parent.Revision]);
        Reject = Add(proposal, "reject", S.SkinRejectDescription, async (app, result, ct) =>
        {
            var service = new SkinService(app.Paths).Proposals;
            var id = result.GetRequiredValue(CliArguments.Target);
            await service.RejectAsync(id, ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(service.Describe(id), SkinsJsonContext.Default.SkinProposalInfo);
        }, [CliArguments.Target]);
        Preview = Add(proposal, "preview", S.SkinProposalPreviewDescription, async (app, result, ct) =>
        {
            var view = result.GetValue(parent.View) is { } json ? SkinCommands.ParseRequest(json, SkinJsonContext.Default.SkinViewOptions) : null;
            var output = await new SkinService(app.Paths).Proposals.PreviewAsync(result.GetRequiredValue(CliArguments.Target), view, ct).ConfigureAwait(false);
            return new System.Text.Json.Nodes.JsonObject { ["path"] = output, ["kind"] = "png", ["saved"] = false };
        }, [CliArguments.Target], [parent.View]);
        Receipt = Add(skin, "receipt", S.SkinReceiptDescription, (app, result, _) =>
            Task.FromResult(JsonSerializer.SerializeToNode(new SkinService(app.Paths).Proposals.Receipt(result.GetRequiredValue(CliArguments.Target),
                result.GetRequiredValue(Operation)), SkinsJsonContext.Default.SkinCommitReceipt)), [CliArguments.Target], [Operation]);
    }
}