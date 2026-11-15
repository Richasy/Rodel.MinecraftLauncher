// Copyright (c) Richasy. All rights reserved.

using System.Text;
using System.Text.Json.Nodes;
using Windows.Win32;

namespace Rodel.MinecraftLauncher.Core.Launch;

public static class LogEncoding
{
    static LogEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static Encoding Resolve(string? name, out string? warning)
    {
        warning = null;
        try
        {
            return Encoding.GetEncoding(name ?? "utf-8");
        }
        catch (ArgumentException)
        {
            warning = "Unknown output encoding '" + name + "'; UTF-8 preview fallback is retained.";
            return Encoding.UTF8;
        }
    }

    public static string SystemEncoding => Encoding.GetEncoding((int)PInvoke.GetACP()).WebName;

    public static (string Text, int Consumed) Decode(byte[] bytes, int offset, int count, Encoding encoding, bool final)
    {
        var decoder = encoding.GetDecoder();
        var text = new StringBuilder();
        var chars = new char[8];
        var boundary = 0;
        for (var i = 0; i < count; i++)
        {
            var emitted = decoder.GetChars(bytes, offset + i, 1, chars, 0, flush: false);
            if (emitted == 0)
                continue;
            text.Append(chars, 0, emitted);
            boundary = i + 1;
        }

        if (final)
        {
            var emitted = decoder.GetChars([], 0, 0, chars, 0, true);
            text.Append(chars, 0, emitted);
            boundary = count;
        }

        return (text.ToString(), boundary);
    }

    public static LaunchPlan Configure(LaunchPlan plan, JsonNode settings, string? overrideEncoding)
    {
        var arguments = plan.Arguments.ToList();
        var warnings = plan.Warnings.ToList();
        var index = plan.MainClassArgumentIndex ?? arguments.IndexOf(plan.MainClass);
        string Select(string stream)
        {
            var key = (plan.JavaMajorVersion is >= 19 ? string.Empty : "sun.") + stream + ".encoding";
            var prefix = "-D" + key + "=";
            var existing = arguments.Take(index).LastOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            var requested = overrideEncoding ?? settings[stream + "Encoding"]?.ToString();
            // Defaults affect output streams only, never file.encoding or legacy mod configuration files.
            var name = requested ?? existing?[prefix.Length..] ?? "utf-8";
            var encoding = Resolve(name, out var warning);
            if (warning is not null)
                warnings.Add(warning);
            if (requested is not null || existing is null)
            {
                arguments.Insert(index++, prefix + encoding.WebName);
            }

            return encoding.WebName;
        }

        var stdout = Select("stdout");
        var stderr = Select("stderr");
        var java = plan.JavaSelection?.Candidates.FirstOrDefault(candidate => candidate.Path == plan.JavaPath);
        var fileProperty = arguments.Take(index).LastOrDefault(x => x.StartsWith("-Dfile.encoding=", StringComparison.Ordinal))?["-Dfile.encoding=".Length..];
        var fileEncoding = fileProperty == "COMPAT" ? java?.NativeEncoding : fileProperty ?? java?.FileEncoding;
        var files = Resolve(fileEncoding ?? (plan.JavaMajorVersion is >= 18 ? "utf-8" : SystemEncoding), out var fileWarning);
        if (fileWarning is not null)
            warnings.Add(fileWarning);
        return plan with
        {
            Arguments = [.. arguments],
            MainClassArgumentIndex = index,
            StdoutEncoding = stdout,
            StderrEncoding = stderr,
            FileEncoding = files.WebName,
            Warnings = [.. warnings]
        };
    }
}