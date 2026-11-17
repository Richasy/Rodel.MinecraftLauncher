// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Executes CI's actual manifest generator against independently known assets and reviewed localized notes.</summary>
[TestClass]
public sealed class ReleaseManifestTests
{
    /// <summary>
    /// Review: the update reader tests cannot detect CI attaching the wrong hashes or silently dropping a language.
    /// Invoke the shipped generator, verify actual size/hash/source, and ensure invalid inputs preserve the last output.
    /// Tiny synthetic files are manifest fixtures only, not executable installers.
    /// </summary>
    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-language")]
    [DataRow("wrong-checksum")]
    public async Task Generation_BindsBothInstallersAndAllLanguagesOrPreservesOutput(string scenario)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "scripts", "generate-release-manifest.ps1")))
            repository = repository.Parent;
        Assert.IsNotNull(repository);
        var root = Path.Combine(Path.GetTempPath(), "rmcl-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = "independent installer fixture"u8.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            foreach (var architecture in new[] { "x64", "arm64" })
            {
                var name = $"RMCL-0.2611.5.0-win-{architecture}-setup.exe";
                File.WriteAllBytes(Path.Combine(root, name), bytes);
                File.WriteAllText(Path.Combine(root, name + ".sha256"), $"{hash}  {name}\n");
            }

            var notes = JsonNode.Parse(File.ReadAllText(Path.Combine(repository.FullName, ".github", "release-notes.json")))!.AsObject();
            if (scenario == "missing-language") notes.Remove("ar");
            if (scenario == "wrong-checksum") File.WriteAllText(Path.Combine(root, "RMCL-0.2611.5.0-win-arm64-setup.exe.sha256"), "not the installer hash");
            var notesPath = Path.Combine(root, "notes.json");
            File.WriteAllText(notesPath, notes.ToJsonString());
            var output = Path.Combine(root, "release-note.json");
            File.WriteAllText(output, "previous complete manifest");
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(repository.FullName, "scripts", "generate-release-manifest.ps1"),
                "-Version", "0.2611.5.0", "-AssetDirectory", root, "-NotesPath", notesPath, "-OutputPath", output,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
            await process.WaitForExitAsync(TestContext.CancellationToken);
            if (scenario == "valid")
            {
                Assert.AreEqual(0, process.ExitCode, await stdout + await stderr);
                var manifest = JsonNode.Parse(File.ReadAllText(output))!;
                Assert.AreEqual("0.2611.5.0", manifest["version"]!.GetValue<string>());
                Assert.AreEqual(40, manifest["sourceCommit"]!.GetValue<string>().Length);
                Assert.IsTrue(JsonNode.DeepEquals(notes, manifest["notes"]));
                Assert.AreEqual(2, manifest["installers"]!.AsObject().Count);
                foreach (var installer in manifest["installers"]!.AsObject())
                {
                    Assert.AreEqual(hash, installer.Value!["sha256"]!.GetValue<string>());
                    Assert.AreEqual(bytes.Length, installer.Value["size"]!.GetValue<int>());
                    Assert.AreEqual($"RMCL-0.2611.5.0-win-{installer.Key}-setup.exe", installer.Value["fileName"]!.GetValue<string>());
                }
            }
            else
            {
                Assert.AreNotEqual(0, process.ExitCode, await stdout + await stderr);
                Assert.AreEqual("previous complete manifest", File.ReadAllText(output));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Gets the test deadline for the actual PowerShell generator.</summary>
    public TestContext TestContext { get; set; }
}