using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects staged publication, borrowed stream lifetime and real Windows replacement failures, independently of loader/Java business recovery.</summary>
[TestClass]
public sealed class AtomicFileTests
{
    /// <summary>Review: creating and replacing the actual file must publish only the source's remaining bytes, without taking ownership of either input.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Copy_RepeatedPublication_UsesCurrentPositionAndLeavesInputsOpen(bool durableAsync)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var destination = Path.Combine(paths.Root, "nested", "payload.bin");
            using var input = new MemoryStream([1, 2, 3, 4, 5]);
            input.Position = 2;
            await CopyAsync(input, destination, durableAsync, TestContext.CancellationToken);
            CollectionAssert.AreEqual(new byte[] { 3, 4, 5 }, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
            input.Position = 0;
            Assert.AreEqual(1, input.ReadByte(), "The caller retains the source stream after publication.");

            using var replacement = new MemoryStream([9, 8, 7, 6]);
            await CopyAsync(replacement, destination, durableAsync, TestContext.CancellationToken);
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7, 6 }, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
            replacement.Position = 0;
            Assert.AreEqual(9, replacement.ReadByte());
            Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp"));
        });
    }

    /// <summary>
    /// Review: the producer stops on its second read, after the real output has staged bytes. An I/O failure or
    /// cancellation at that boundary must preserve the original target and an unrelated temporary file. Ordinary
    /// successful Java imports cannot distinguish staging from writing directly into the destination.
    /// </summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Copy_InterruptedStaging_PreservesTargetAndCleansOnlyItsOwnTemporary(bool durableAsync, bool cancel)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var destination = Path.Combine(paths.Root, "payload.bin");
            byte[] original = [4, 3, 2, 1];
            await File.WriteAllBytesAsync(destination, original, TestContext.CancellationToken);
            var unrelated = Path.Combine(paths.Root, "unrelated.tmp");
            await File.WriteAllBytesAsync(unrelated, [7, 7], TestContext.CancellationToken);
            using var input = new InterruptedReadStream();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            var copy = CopyAsync(input, destination, durableAsync, cancellation.Token);
            try
            {
                await input.Staged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
                var temporary = Directory.GetFiles(paths.Root, "*.tmp").Single(path => path != unrelated);
                Assert.IsGreaterThan(0L, new FileInfo(temporary).Length, "The controlled boundary must follow an actual staged write.");
                CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
                if (cancel) await cancellation.CancelAsync();
                input.Release.TrySetResult();
                if (cancel)
                    await Assert.ThrowsAsync<OperationCanceledException>(() => copy);
                else
                    Assert.AreSame(input.Failure, await Assert.ThrowsAsync<IOException>(() => copy));
                CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
                CollectionAssert.AreEqual(new byte[] { 7, 7 }, await File.ReadAllBytesAsync(unrelated, TestContext.CancellationToken));
                CollectionAssert.AreEquivalent(new[] { unrelated }, Directory.GetFiles(paths.Root, "*.tmp"));
            }
            finally
            {
                input.Release.TrySetResult();
                try { await copy; }
                catch (IOException error) when (ReferenceEquals(error, input.Failure)) { }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
        });
    }

    /// <summary>Review: a real reader denying deletion must retain the old target; after releasing it, the same borrowed input can be rewound and published normally.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Copy_LockedDestination_PreservesOriginalAndAllowsRetry(bool durableAsync)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var destination = Path.Combine(paths.Root, "payload.bin");
            await File.WriteAllBytesAsync(destination, [1, 2, 3], TestContext.CancellationToken);
            using var input = new MemoryStream([6, 7, 8, 9]);
            using (var held = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = await Assert.ThrowsAsync<Exception>(() => CopyAsync(input, destination, durableAsync, TestContext.CancellationToken));
                Assert.IsTrue(error is IOException or UnauthorizedAccessException,
                    "Windows replacement denial must remain a filesystem error, with its original destination retained.");
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
                Assert.IsEmpty(Directory.GetFiles(paths.Root, "*.tmp"));
            }

            input.Position = 0;
            await CopyAsync(input, destination, durableAsync, TestContext.CancellationToken);
            CollectionAssert.AreEqual(new byte[] { 6, 7, 8, 9 }, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
            Assert.IsEmpty(Directory.GetFiles(paths.Root, "*.tmp"));
        });
    }

    private static Task CopyAsync(Stream input, string destination, bool durableAsync, CancellationToken ct) =>
        AtomicFile.CopyAsync(input, destination, durableAsync ? 65536 : 4096,
            durableAsync ? FileOptions.Asynchronous | FileOptions.WriteThrough : FileOptions.None, ct);

    /// <summary>
    /// Review: screenshot exports must not overwrite a file created after their initial existence check.
    /// The producer is held after a real staged write; publishing without overwrite must retain the intervening file and remove only its own temporary.
    /// </summary>
    [TestMethod]
    public async Task Copy_NoOverwriteAndDestinationAppearsDuringStaging_PreservesInterveningFile()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var target = Path.Combine(paths.Root, "export.png");
            using var input = new PublicationBoundaryStream();
            var copy = AtomicFile.CopyAsync(input, target, 4096, FileOptions.Asynchronous, TestContext.CancellationToken, overwrite: false);
            try
            {
                await input.Staged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
                Assert.IsGreaterThan(0L, new FileInfo(Directory.GetFiles(paths.Root, "*.tmp").Single()).Length);
                await File.WriteAllBytesAsync(target, [8, 9], TestContext.CancellationToken);
                input.Release.TrySetResult();
                await Assert.ThrowsAsync<IOException>(() => copy);
                CollectionAssert.AreEqual(new byte[] { 8, 9 }, await File.ReadAllBytesAsync(target, TestContext.CancellationToken));
                Assert.IsEmpty(Directory.GetFiles(paths.Root, "*.tmp"));
            }
            finally
            {
                input.Release.TrySetResult();
                try { await copy; }
                catch (IOException) { }
            }
        });
    }

    private sealed class PublicationBoundaryStream : MemoryStream
    {
        private bool _written;
        internal TaskCompletionSource Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc/>
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (_written) throw new InvalidOperationException("The publication source can only be read once.");
            _written = true;
            await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            Staged.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class InterruptedReadStream : Stream
    {
        private bool _first = true;
        internal TaskCompletionSource Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IOException Failure { get; } = new("The fixture source failed after its staged prefix.");

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
        public override void Flush() => throw new NotSupportedException();
        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                buffer.Span.Fill(42);
                return ValueTask.FromResult(buffer.Length);
            }

            Staged.TrySetResult();
            return FailAsync(cancellationToken);
        }

        private async ValueTask<int> FailAsync(CancellationToken ct)
        {
            await Release.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            throw Failure;
        }
    }

    /// <summary>Gets the runner cancellation signal for owned I/O and controlled producer waits.</summary>
    public TestContext TestContext { get; set; } = null!;
}