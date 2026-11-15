// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Enforces the MCP JSON-RPC line budget without owning the child process's stdout stream.</summary>
internal sealed class BoundedMcpStream(Stream inner) : Stream
{
    private int _lineBytes;
    /// <inheritdoc />
    public override bool CanRead => true;
    /// <inheritdoc />
    public override bool CanSeek => false;
    /// <inheritdoc />
    public override bool CanWrite => false;
    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var count = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        Check(buffer.Span[..count]);
        return count;
    }
    private void Check(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            _lineBytes = value == (byte)'\n' ? 0 : _lineBytes + 1;
            if (_lineBytes > 2 * 1024 * 1024) throw new InvalidDataException("An MCP JSON-RPC line exceeded 2 MiB.");
        }
    }
    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Check(buffer.AsSpan(offset, read));
        return read;
    }
    /// <inheritdoc />
    public override void Flush() => throw new NotSupportedException();
    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}