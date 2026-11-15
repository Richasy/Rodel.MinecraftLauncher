using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// 提供测试专属数据根、真实CLI进程和有界清理；不封装业务步骤或生成业务预期。
/// </summary>
internal sealed class CliSandbox : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Rodel.Cli.Tests", "isolated data " + Guid.NewGuid().ToString("N"));

    internal Dictionary<string, string> SnapshotFiles() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(Root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    internal Task<CliResult> RunAsync(params string[] arguments) => RunAsync(CreateStartInfo(arguments));

    internal static async Task<CliResult> RunAsync(ProcessStartInfo start, string? standardInput = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (standardInput is not null)
        {
            start.RedirectStandardInput = true;
            start.StandardInputEncoding = new UTF8Encoding(false);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the CLI under test.");

        // 同时排空两个流，避免子进程因输出缓冲区满而阻塞。
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), deadline.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            // 仅结束本夹具启动的进程树，再读取诊断输出。
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cleanup.Token);
            var captured = await Task.WhenAll(output, error).WaitAsync(cleanup.Token);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"CLI timed out. stdout: {captured[0]} stderr: {captured[1]}");
        }

        return new(process.ExitCode, await output, await error);
    }

    internal ProcessStartInfo CreateStartInfo(params string[] arguments)
    {
        Directory.CreateDirectory(Root);
        var nativeExecutable = Environment.GetEnvironmentVariable("RODEL_TEST_CLI_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(nativeExecutable))
            nativeExecutable = null;
        var start = new ProcessStartInfo(nativeExecutable ?? Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (nativeExecutable is null)
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "cli", "rodel-mc.dll"));
        start.Environment["RODEL_CLI_LANGUAGE"] = "en";
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(Root);
        start.ArgumentList.Add("--json");
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>Deletes only this sandbox's isolated data directory after its processes have been released.</summary>
    public void Dispose()
    {
        var deadline = Stopwatch.StartNew();
        while (Directory.Exists(Root))
        {
            try
            {
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33
                && deadline.Elapsed < TimeSpan.FromSeconds(2))
            {
                // Windows can retain a working-directory handle briefly after process exit.
                // Retry only an observed sharing lock, never the test or an arbitrary cleanup failure.
                Thread.Sleep(20);
            }
        }
    }
}