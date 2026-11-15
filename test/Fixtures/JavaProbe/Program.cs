// This worker substitutes Java's reporting protocol, not a JVM or Minecraft.
var home = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName;
var calls = Path.Combine(home, "calls");
Directory.CreateDirectory(calls);
File.WriteAllText(Path.Combine(calls, Environment.ProcessId + "-" + Guid.NewGuid().ToString("N") + ".txt"), string.Join('\n', args));
if (args.Contains("Synthetic.NetworkOwner"))
    return await NetworkOwnerProbe.OwnerAsync(home);
if (args.Contains("--hmcl"))
    return await NetworkOwnerProbe.BootstrapAsync(home);
if (args.Contains("--network-worker"))
    return await NetworkOwnerProbe.WorkerAsync(home, leaf: false);
if (args.Contains("--network-leaf"))
    return await NetworkOwnerProbe.WorkerAsync(home, leaf: true);
if (args.Contains("Synthetic.Window"))
    return WindowProbe.Run(home);
if (args.Contains("Synthetic.Console"))
    return await ConsoleProbe.RunAsync(home);
if (args.Contains("Synthetic.Startup"))
    return await StartupProbe.RunAsync(home);
if (File.Exists(Path.Combine(home, "wait")))
{
    var marker = Path.Combine(home, "started");
    File.WriteAllText(marker + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(marker + ".tmp", marker, overwrite: true);
    while (File.Exists(Path.Combine(home, "wait")))
        await Task.Delay(20);
}

var reportedVersion = Path.Combine(home, "reported-version");
var version = File.ReadAllText(File.Exists(reportedVersion) ? reportedVersion : Path.Combine(home, "release")).Trim();
var properties = args.Contains("-XshowSettings:properties");
if (File.Exists(Path.Combine(home, "fail")))
{
    Console.Error.WriteLine("Runtime could not initialize.");
    return 2;
}

if (properties && File.Exists(Path.Combine(home, "legacy")))
{
    Console.Error.WriteLine("Unsupported properties option.");
    return 1;
}

Console.Error.WriteLine($"openjdk version \"{version}\"");
if (properties)
{
    Console.Error.WriteLine($"    java.version = {version}");
    var reportedHome = Path.Combine(home, "reported-home");
    Console.Error.WriteLine($"    java.home = {(File.Exists(reportedHome) ? File.ReadAllText(reportedHome).Trim() : home)}");
    Console.Error.WriteLine("    os.arch = amd64");
    Console.Error.WriteLine("    sun.arch.data.model = 64");
    Console.Error.WriteLine("    file.encoding = UTF-8");
    Console.Error.WriteLine("    native.encoding = UTF-8");
}

return 0;