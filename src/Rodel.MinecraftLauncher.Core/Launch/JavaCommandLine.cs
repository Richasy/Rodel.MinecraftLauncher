// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;
using System.Text;
using Windows.Win32;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Only changes transport when Windows' process command line would be too long.</summary>
public static class JavaCommandLine
{
    private const int SafeLength = 32767;
    public static string Strategy(string executable, IReadOnlyList<string> arguments, int? javaMajor) => EstimatedLength(executable, arguments) < SafeLength ? "direct" : javaMajor >= 9 ? "argument-file" : "classpath-jar";
    /// <summary>Prepares direct arguments, an argument file, or classpath transport for the Java version without launching the game.</summary>
    public static async Task<(string[] Arguments, string Strategy)> PrepareAsync(string executable, string[] arguments, int? javaMajor, string workingDirectory, string sessionDirectory, int? mainClassArgumentIndex = null, int? unpersistedArgumentIndex = null, CancellationToken ct = default)
    {
        var strategy = Strategy(executable, arguments, javaMajor);
        if (strategy == "direct")
            return (arguments, strategy);
        Directory.CreateDirectory(sessionDirectory);
        if (strategy == "argument-file")
        {
            var file = Path.Combine(sessionDirectory, "java.args");
            // The native Windows Java launcher reads its platform encoding (including on
            // modern JDKs); failover below keeps characters the code page cannot represent.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding((int)PInvoke.GetACP(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var prefixLength = unpersistedArgumentIndex ?? arguments.Length;
            var text = string.Join('\n', arguments.Take(prefixLength).Select(QuoteArgumentFile)) + "\n";
            try
            {
                var bytes = encoding.GetBytes(text);
                await File.WriteAllBytesAsync(file, bytes, ct).ConfigureAwait(false);
                return (["@" + file, .. arguments.Skip(prefixLength)], strategy);
            }
            catch (EncoderFallbackException)
            {
                strategy = "classpath-jar";
            }
        }

        var shortened = arguments.ToArray();
        var targetIndex = mainClassArgumentIndex ?? FindLaunchTarget(arguments);
        for (var i = 0; i < targetIndex; i++)
        {
            var inline = shortened[i].StartsWith("--class-path=", StringComparison.Ordinal);
            if (!inline && shortened[i] is not ("-cp" or "-classpath" or "--class-path"))
                continue;
            var valueIndex = inline ? i : i + 1;
            if (valueIndex >= targetIndex)
                break;
            var value = inline ? shortened[i][13..] : shortened[valueIndex];
            var urls = new List<string>();
            foreach (var entry in value.Split(Path.PathSeparator))
            {
                var path = Path.GetFullPath(entry.Length == 0 ? "." : entry, workingDirectory);
                if (Path.GetFileName(path) == "*" && Directory.Exists(Path.GetDirectoryName(path)))
                    urls.AddRange(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.jar").Select(ToUrl));
                else
                    urls.Add(ToUrl(path));
            }

            // Keep each manifest value below the portable 65535-byte boundary.
            var group = new List<string>();
            var jars = new List<string>();
            var size = 0;
            foreach (var url in urls)
            {
                if (size + url.Length > 50000 && group.Count != 0)
                {
                    WriteGroup();
                }

                group.Add(url);
                size += url.Length + 1;
            }

            WriteGroup();
            shortened[valueIndex] = (inline ? "--class-path=" : string.Empty) + string.Join(Path.PathSeparator, jars);
            if (!inline)
                i++;
            void WriteGroup()
            {
                ct.ThrowIfCancellationRequested();
                var jar = Path.Combine(sessionDirectory, $"classpath-{valueIndex}-{jars.Count}.jar");
                using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
                using (var output = archive.CreateEntry("META-INF/MANIFEST.MF").Open())
                {
                    var line = "Class-Path: " + string.Join(' ', group);
                    var manifest = new StringBuilder("Manifest-Version: 1.0\r\n");
                    while (line.Length > 70)
                    {
                        manifest.Append(line.AsSpan(0, 70)).Append("\r\n");
                        line = " " + line[70..];
                    }

                    manifest.Append(line).Append("\r\n\r\n");
                    output.Write(Encoding.ASCII.GetBytes(manifest.ToString()));
                }

                jars.Add(jar);
                group.Clear();
                size = 0;
            }
        }

        return (shortened, strategy);
    }

    private static string ToUrl(string path) => new Uri(Directory.Exists(path) ? Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar : path).AbsoluteUri;
    private static int FindLaunchTarget(string[] arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is "-jar" or "-m" or "--module" || !arguments[i].StartsWith('-'))
                return i;
            if (arguments[i] is "-cp" or "-classpath" or "--class-path" or "-p" or "--module-path" or "--upgrade-module-path" or "--add-modules" or "--limit-modules" or "--add-reads" or "--add-exports" or "--add-opens" or "--patch-module")
                i++;
        }

        return arguments.Length;
    }

    private static long EstimatedLength(string executable, IReadOnlyList<string> arguments) => executable.Length + 3L + arguments.Sum(x => 1 + QuotedLength(x));
    private static long QuotedLength(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return value.Length;
        long count = 2;
        var slashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                slashes++;
                continue;
            }

            count += ch == '"' ? (2L * slashes) + 2 : slashes + 1;
            slashes = 0;
        }

        return count + (2L * slashes);
    }

    private static string QuoteArgumentFile(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal) + "\"";
}