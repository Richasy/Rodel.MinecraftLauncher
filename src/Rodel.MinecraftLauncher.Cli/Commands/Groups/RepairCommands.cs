// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class RepairCommands
{
    internal readonly Argument<string> Incident = new("repair-id");
    internal readonly Argument<string> Trial = new("plan-id");
    internal readonly Option<string?> Session = new("--session") { Description = S.RepairSession };
    internal readonly Option<string?> Job = new("--job") { Description = S.RepairJob };
    internal readonly Option<string?> Kind = new("--kind") { Description = S.RepairKind };
    internal readonly Option<string?> Target = new("--target") { Description = S.RepairTarget };
    internal readonly Option<int?> Memory = new("--memory-mb") { Description = S.RepairMemory };
    internal readonly Option<string?> Reason = new("--reason") { Description = S.RepairReason };
    internal LauncherCommand Create { get; }
    internal LauncherCommand Show { get; }
    internal LauncherCommand Plan { get; }
    internal LauncherCommand Apply { get; }
    internal LauncherCommand Verify { get; }
    internal LauncherCommand Restore { get; }

    internal RepairCommands(Command root)
    {
        var group = Group(root, "repair", S.RepairGroup);
        Create = Add(group, "create", S.RepairCreate, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Repairs.CreateAsync(new()
        {
            InstanceId = args.GetRequiredValue(CliArguments.Target),
            SessionId = args.GetValue(Session),
            JobId = args.GetValue(Job)
        }, ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairReport), [CliArguments.Target], [Session, Job]);
        Add(group, "start", S.RepairCreate, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Assistant.StartRepairAsync(new()
        {
            InstanceId = args.GetRequiredValue(CliArguments.Target),
            SessionId = args.GetValue(Session),
            JobId = args.GetValue(Job)
        }, host: app.AssistantHost, ct: ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairReport), [CliArguments.Target], [Session, Job]);
        Show = Add(group, "show", S.RepairShow, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Repairs.InspectAsync(args.GetRequiredValue(Incident), ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairReport), [Incident]);
        Plan = Add(group, "plan", S.RepairPlan, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Repairs.PlanAsync(new()
        {
            RepairId = args.GetRequiredValue(Incident),
            Kind = args.GetValue(Kind) ?? throw new ArgumentException(S.RepairKind),
            Target = args.GetValue(Target),
            MemoryMb = args.GetValue(Memory),
            Reason = args.GetValue(Reason) ?? throw new ArgumentException(S.RepairReason)
        }, ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairPlanReport), [Incident], [Kind, Target, Memory, Reason]);
        Apply = Add(group, "apply", S.RepairApply, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Repairs.ApplyAsync(Action(args), ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairPlanReport), [Incident, Trial]);
        Verify = Add(group, "verify", S.RepairVerify, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Repairs.VerifyAsync(Action(args), ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairPlanReport), [Incident, Trial]);
        Restore = Add(group, "restore", S.RepairRestore, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Repairs.RestoreAsync(Action(args), ct).ConfigureAwait(false), AiRepairJsonContext.Default.AiRepairPlanReport), [Incident, Trial]);
    }

    private AiRepairAction Action(ParseResult args) => new() { RepairId = args.GetRequiredValue(Incident), PlanId = args.GetRequiredValue(Trial) };
    internal CliInvocation ReadInvocation(AiRepairRead input) => new(Show, input.RepairId);
    internal CliInvocation PlanInvocation(AiRepairPlan input) => new CliInvocation(Plan, input.RepairId).Option(Kind, input.Kind).Option(Target, input.Target).Option(Memory, input.MemoryMb).Option(Reason, input.Reason);
    internal static CliInvocation ActionInvocation(LauncherCommand command, AiRepairAction input) => new(command, input.RepairId, input.PlanId);
}