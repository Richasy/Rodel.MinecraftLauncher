// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Installs a managed Java runtime and publishes the actual current file download progress.</summary>
    public Task<JavaRegistrationResult> InstallJavaAsync(string component, string? architecture = null, string? expectedVersion = null, CancellationToken cancellationToken = default) =>
        JavaRuntimes.InstallAsync(component: component, architecture: architecture, progress: DownloadEvents, ct: cancellationToken, expectedVersion: expectedVersion);

    private async Task PrepareInstalledJavaAsync(InstanceRecord instance, string version, string? javaPath, InstallationResult result, CancellationToken cancellationToken)
    {
        void ReportJava(LauncherProgress value, bool force = false)
        {
            Report(value with { CompletedFiles = result.CompletedFiles, TotalFiles = result.TotalFiles, SharedReusedFiles = result.SharedReusedFiles, IncludesFileTotal = true }, force);
        }

        ReportJava(new LauncherProgress { Stage = "java-discovery" }, force: true);
        var java = SelectJavaPath(Settings.GetEffective(instance), javaPath, instance.JavaPath);
        var metadata = await new VersionResolver().ResolveAsync(instance.MinecraftDirectory, version, cancellationToken).ConfigureAwait(false);
        var preferred = JavaSelection.PreferredMajor(metadata);
        var candidates = java is null
            ? await Java.DiscoverAsync(minecraftDirectory: instance.MinecraftDirectory, ct: cancellationToken).ConfigureAwait(false)
            : [await Java.ProbeAsync(JavaDiscovery.ResolveExecutable(java, javaPath is null ? instance.GameDirectory : Environment.CurrentDirectory), cancellationToken).ConfigureAwait(false)];
        var chosen = java is null ? JavaSelection.Choose(candidates, preferred, metadata) : candidates[0];
        await Activity.WriteAsync("java.selected", instance.Id, code: java is not null ? "EXPLICIT" : "DISCOVERED", count: chosen?.MajorVersion, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        if (java is null && (chosen is null || (preferred is not null && chosen.MajorVersion != preferred)))
        {
            var component = metadata["javaVersion"]?["component"]?.GetValue<string>() ?? JavaSelection.Component(preferred);
            if (component is not null)
            {
                try
                {
                    ReportJava(new LauncherProgress { Stage = "java-install" }, force: true);
                    var runtime = await JavaRuntimes.InstallAsync(component: component, progress: new InlineProgress<Downloads.DownloadProgress>(item => ReportJava(new LauncherProgress { Stage = "java-install", Bytes = item.Bytes, TotalBytes = item.TotalBytes, Path = item.Path, IncludesByteTotal = true })), ct: cancellationToken).ConfigureAwait(false);
                    chosen = await Java.ProbeAsync(runtime.JavaPath, cancellationToken).ConfigureAwait(false);
                    await Settings.SetAsync("javaRuntimeId", JsonValue.Create(runtime.Id), instance, cancellationToken).ConfigureAwait(false);
                    await Activity.WriteAsync("java.prepared", instance.Id, count: chosen.MajorVersion, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or System.Text.Json.JsonException)
                {
                    if (chosen is null || !chosen.IsUsable) throw;
                    result.JavaWarning = exception.Message;
                    await Activity.WriteAsync("java.preparation-fallback", instance.Id, code: LauncherOperationException.ToResult(exception)["error"]?["code"]?.ToString(), cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        result.Java = JavaSelection.Describe(candidates, chosen, metadata, java is not null);
        if (chosen is not null && preferred is not null && chosen.MajorVersion != preferred)
            await Activity.WriteAsync("java.compatibility-advisory", instance.Id, code: "MAJOR_MISMATCH", count: preferred, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        if (chosen is null || !chosen.IsUsable)
        {
            throw new LauncherOperationException("JAVA_UNAVAILABLE", "Game files are installed. Select a usable Java executable and retry.",
                JsonSerializer.SerializeToNode(result, InstallationJsonContext.Default.InstallationResult));
        }
    }
}