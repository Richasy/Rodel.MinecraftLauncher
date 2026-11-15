// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

if (args is ["--version"])
{
    Console.WriteLine("Claude MCP test fixture");
    return 0;
}

var home = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? throw new InvalidOperationException("An isolated config is required.");
var file = Path.Combine(home, ".claude.json");
var config = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))!.AsObject() : new JsonObject();
var servers = config["mcpServers"] as JsonObject ?? new JsonObject();
if (config["mcpServers"] is null) config["mcpServers"] = servers;

if (args is ["mcp", "remove", "--scope", "user", "rodel-minecraft"])
{
    if (!servers.Remove("rodel-minecraft")) return 1;
}
else if (args is ["mcp", "add", "--scope", "user", "--transport", "stdio", "rodel-minecraft", "--",
    var command, "--data-dir", var root, "mcp", "serve", "--stdio", "--tool-profile", "discovery"])
{
    if (File.Exists(Path.Combine(root, "reject-claude-add"))) return 55;
    if (servers.ContainsKey("rodel-minecraft")) return 1;
    servers["rodel-minecraft"] = new JsonObject
    {
        ["type"] = "stdio",
        ["command"] = command,
        ["args"] = new JsonArray([.. args.Skip(9).Select(argument => (JsonNode?)JsonValue.Create(argument))]),
        ["env"] = new JsonObject()
    };
}
else
{
    return 2;
}

File.WriteAllText(file, config.ToJsonString());
return 0;
