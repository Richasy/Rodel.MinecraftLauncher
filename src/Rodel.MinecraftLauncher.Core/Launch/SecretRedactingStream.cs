// Copyright (c) Richasy. All rights reserved.

using System.Text;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Replaces a known ASCII session credential before bytes enter any log file, including across pipe reads.</summary>
internal sealed class SecretRedactingStream(Stream source, string secret) : Stream
{
    private readonly byte[] _secret = Encoding.UTF8.GetBytes(secret);
    private readonly byte[] _replacement = "[REDACTED]"u8.ToArray();
    private byte[] _pending = [];
    private byte[] _ready = [];
    private int _offset;
    private bool _ended;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        while (_offset == _ready.Length)
        {
            if (_ended && _pending.Length == 0) return 0;
            var input = new byte[8192];
            var read = _ended ? 0 : await source.ReadAsync(input, cancellationToken).ConfigureAwait(false);
            if (read == 0) _ended = true;
            var bytes = new byte[_pending.Length + read];
            _pending.CopyTo(bytes, 0); input.AsSpan(0, read).CopyTo(bytes.AsSpan(_pending.Length));
            using var output = new MemoryStream();
            var position = 0;
            while (position < bytes.Length)
            {
                if (_secret.Length > 0 && bytes.AsSpan(position).StartsWith(_secret)) { output.Write(_replacement); position += _secret.Length; }
                else if (!_ended && bytes.Length - position < _secret.Length && _secret.AsSpan().StartsWith(bytes.AsSpan(position)))
                {
                    break;
                }
                else
                {
                    output.WriteByte(bytes[position++]);
                }
            }

            _pending = bytes[position..]; _ready = output.ToArray(); _offset = 0;
        }

        var count = Math.Min(buffer.Length, _ready.Length - _offset);
        _ready.AsMemory(_offset, count).CopyTo(buffer); _offset += count; return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}