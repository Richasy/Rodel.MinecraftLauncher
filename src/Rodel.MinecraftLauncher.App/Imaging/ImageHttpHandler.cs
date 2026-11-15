// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.App.Imaging;

internal sealed class ImageHttpHandler : DelegatingHandler
{
    private const int MaximumBytes = 1024 * 1024;

    internal ImageHttpHandler(HttpMessageHandler? inner = null) : base(inner ?? new HttpClientHandler { MaxConnectionsPerServer = 4 })
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var response = await base.SendAsync(request, deadline.Token).ConfigureAwait(false);
        var completed = false;
        try
        {
            if (!response.IsSuccessStatusCode)
            {
                completed = true;
                return response;
            }

            if (response.Content.Headers.ContentLength > MaximumBytes)
                throw new InvalidDataException("The content icon exceeds the image byte limit.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            while (await input.ReadAsync(buffer, deadline.Token).ConfigureAwait(false) is > 0 and var read)
            {
                if (output.Length + read > MaximumBytes)
                    throw new InvalidDataException("The content icon exceeds the image byte limit.");
                output.Write(buffer, 0, read);
            }

            var content = new ByteArrayContent(output.ToArray());
            foreach (var header in response.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Dispose();
            response.Content = content;
            completed = true;
            return response;
        }
        finally
        {
            if (!completed)
                response.Dispose();
        }
    }
}