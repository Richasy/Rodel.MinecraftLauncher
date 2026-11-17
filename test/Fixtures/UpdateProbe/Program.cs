// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;

// A controlled parent/installer process, not an actual application installer or UI.
if (args.Contains("--parent"))
{
    Console.WriteLine("ready");
    _ = Console.ReadLine();
    return 0;
}

var directory = AppContext.BaseDirectory;
var parentId = int.Parse(File.ReadAllText(Path.Combine(directory, "parent-id")), System.Globalization.CultureInfo.InvariantCulture);
var parentAlive = false;
try
{
    using var parent = Process.GetProcessById(parentId);
    parentAlive = !parent.HasExited;
}
catch (ArgumentException) { }

File.WriteAllText(Path.Combine(directory, "installer-started"), parentAlive ? "parent-still-running" : "parent-exited");
var deadline = Stopwatch.StartNew();
while (!File.Exists(Path.Combine(directory, "finish-installer")) && deadline.Elapsed < TimeSpan.FromSeconds(30))
    await Task.Delay(20);
File.WriteAllText(Path.Combine(directory, "installer-finished"), "done");
return parentAlive ? 1 : 0;