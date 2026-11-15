using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Follows actual text continuations and checks cursor progress without deciding the expected business contents.
/// </summary>
internal static class ResultReader
{
    internal static async Task<ArrayReadResult> ReadArrayAsync(McpSession session, JsonObject continuation, CancellationToken cancellationToken)
    {
        var items = new JsonArray();
        var pages = 0;
        var references = 0;
        var next = continuation;
        while (next is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var arguments = next["arguments"]!.AsObject();
            var requestedLimit = arguments["limit"]?.GetValue<int>() ?? 20;
            var page = await session.CallAsync(next["tool"]!.GetValue<string>(), arguments);
            Assert.AreEqual("succeeded", page["status"]?.GetValue<string>(), page.ToJsonString());
            var data = page["data"]!.AsObject();
            pages++;
            Assert.AreEqual(items.Count, data["offset"]?.GetValue<int>(), "Continuation skipped or repeated array items.");
            Assert.AreEqual(requestedLimit, data["limit"]?.GetValue<int>(), "The requested page limit was not preserved.");
            var returned = data["items"]!.AsArray();
            Assert.IsNotEmpty(returned, "A continuation must make progress.");
            Assert.IsLessThanOrEqualTo(requestedLimit, returned.Count, "A page exceeded its requested item limit.");
            foreach (var item in returned)
            {
                if (item is JsonObject reference && reference["truncated"]?.GetValue<bool>() == true)
                {
                    references++;
                    var text = await ReadTextAsync(session, reference["continuation"]!.AsObject(), cancellationToken);
                    items.Add(JsonNode.Parse(text));
                }
                else
                {
                    items.Add(item?.DeepClone());
                }
            }

            next = data["continuation"] as JsonObject;
            Assert.AreEqual(next is not null, data["hasMore"]?.GetValue<bool>());
            if (next is not null)
                Assert.AreEqual(items.Count, data["nextOffset"]?.GetValue<int>());
            else
                Assert.AreEqual(items.Count, data["total"]?.GetValue<int>());
        }

        return new(items, pages, references);
    }

    internal static async Task<string> ReadTextAsync(McpSession session, JsonObject continuation, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var next = continuation;
        while (next is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await session.CallAsync(next["tool"]!.GetValue<string>(), next["arguments"]!.AsObject());
            Assert.AreEqual("succeeded", page["status"]?.GetValue<string>(), page.ToJsonString());
            var data = page["data"]!.AsObject();
            Assert.AreEqual(text.Length, data["offset"]?.GetValue<int>(), "Continuation skipped or repeated text.");
            var chunk = data["text"]!.GetValue<string>();
            Assert.IsNotEmpty(chunk, "A continuation must make progress.");
            text.Append(chunk);
            next = data["continuation"] as JsonObject;
            Assert.AreEqual(next is not null, data["hasMore"]?.GetValue<bool>());
            if (next is not null)
                Assert.AreEqual(text.Length, data["nextOffset"]?.GetValue<int>());
            else
                Assert.AreEqual(text.Length, data["totalCharacters"]?.GetValue<int>());
        }

        return text.ToString();
    }
}