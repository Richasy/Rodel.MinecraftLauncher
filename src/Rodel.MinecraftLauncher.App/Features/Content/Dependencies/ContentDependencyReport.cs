// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using Sprout;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Presents the existing local JAR analysis as a cancellable read-only report, not a launch requirement.</summary>
internal sealed partial class ContentDependencyReport : Control
{
    private readonly LauncherClient _client;
    private readonly string _instanceId;
    private readonly string _name;
    private readonly DetailReportPopup _report;
    private readonly CancellationTokenSource _lifetime = new();

    internal ContentDependencyReport(
        LauncherClient client,
        Window window,
        string instanceId,
        string name,
        Action close)
    {
        _client = client;
        _instanceId = instanceId;
        _name = name;
        _report = new(
            window,
            Resources.DependencyAnalysisTitle,
            Resources.DependencyAnalysisReading,
            close,
            Math.Min(560f, window.ClientSize.Width - 72f),
            "content.analysis",
            canClose: () => CanClose)
        {
            ContentReady = false
        };
    }

    internal bool CanClose => !_report.Exporting;

    private Ui Body => _report;

    private async Task ReadAsync()
    {
        try
        {
            var token = _lifetime.Token;
            var result = await Task.Run(() => _client.AnalyzeContentAsync(_instanceId, token), token);
            if (_lifetime.IsCancellationRequested)
                return;
            var findings = result.Findings;
            var lines = findings.Select(Finding);
            var readable = string.Format(
                Resources.DependencyAnalysisReport,
                _name,
                result.Mods.Count,
                findings.Count == 0 ? Resources.DependencyAnalysisNoFindings : string.Join(Environment.NewLine, lines),
                Resources.DependencyAnalysisLimits);
            _report.SetReport(readable, JsonSerializer.SerializeToNode(result, ContentJsonContext.Analysis)!.ToJsonString(new() { WriteIndented = true }));
            _report.ContentReady = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _report.SetReport(string.Format(Resources.DependencyAnalysisFailed, exception.Message), LauncherOperationException.ToResult(exception).ToJsonString(new() { WriteIndented = true }));
                _report.ContentReady = true;
            }
        }
    }

    private static string Finding(ContentAnalysisFinding finding) => finding.Kind switch
    {
        "missing-declared-dependency" => string.Format(Resources.DependencyAnalysisMissing, finding.Requester, finding.Dependency?.Id, finding.Dependency?.Range?.ToJsonString()),
        "duplicate-id" => string.Format(Resources.DependencyAnalysisDuplicate, finding.Id, string.Join(", ", finding.Files ?? [])),
        "declared-breaks" or "declared-conflicts" => string.Format(Resources.DependencyAnalysisConflict, finding.Requester, finding.Target, finding.Range?.ToJsonString()),
        _ => JsonSerializer.Serialize(finding, ContentJsonContext.AnalysisFinding)
    };

    protected override IDisposable? OnMounted()
    {
        _ = ReadAsync();
        return null;
    }

    protected override void OnDispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnDispose();
    }
}