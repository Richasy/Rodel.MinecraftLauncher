// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Stores full project history with shared, lossless texture blobs instead of repeated JSON pixel arrays.</summary>
internal static class SkinProjectFile
{
    private const string Format = "rodel-skin-project/1";
    internal static SkinProject Read(string path) => JsonFiles.ReadSnapshot(path, stream =>
    {
        Span<byte> signature = stackalloc byte[4];
        var read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        stream.Position = 0;
        if (read != 4 || !signature.SequenceEqual("PK\u0003\u0004"u8))
        {
            return JsonSerializer.Deserialize(stream, SkinStoreJsonContext.Default.SkinProject)
                ?? throw new InvalidDataException("The skin project is empty.");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var format = Entry(archive, "format");
        if (format.Length != Format.Length) throw new InvalidDataException("Unsupported skin project container.");
        using (var reader = new StreamReader(format.Open()))
            if (reader.ReadToEnd() != Format) throw new InvalidDataException("Unsupported skin project container.");
        var options = new JsonSerializerOptions(SkinStoreJsonContext.Default.Options);
        options.Converters.Add(new TextureConverter(archive));
        using var manifest = Entry(archive, "project.json").Open();
        return JsonSerializer.Deserialize(manifest, new SkinStoreJsonContext(options).SkinProject)
            ?? throw new InvalidDataException("The skin project is empty.");
    });

    internal static byte[] Encode(SkinProject project, CancellationToken ct)
    {
        var textures = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var options = new JsonSerializerOptions(SkinStoreJsonContext.Default.Options);
        options.Converters.Add(new TextureConverter(textures, ct));
        var manifest = JsonSerializer.SerializeToUtf8Bytes(project, new SkinStoreJsonContext(options).SkinProject);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("format", CompressionLevel.Fastest).Open()))
                writer.Write(Format);
            using (var stream = archive.CreateEntry("project.json", CompressionLevel.Fastest).Open())
                stream.Write(manifest);
            foreach (var (id, bytes) in textures)
            {
                ct.ThrowIfCancellationRequested();
                using var stream = archive.CreateEntry("textures/" + id + ".u32le", CompressionLevel.Fastest).Open();
                stream.Write(bytes);
            }
        }

        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private static ZipArchiveEntry Entry(ZipArchive archive, string name)
    {
        var entries = archive.Entries.Where(entry => entry.FullName == name).Take(2).ToArray();
        return entries.Length == 1 ? entries[0] : throw new InvalidDataException("Missing or duplicate skin project entry: " + name);
    }

    private sealed class TextureConverter : JsonConverter<uint[]>
    {
        private readonly ZipArchive? _archive;
        private readonly Dictionary<string, byte[]>? _encoded;
        private readonly Dictionary<string, uint[]> _decoded = new(StringComparer.Ordinal);
        private readonly CancellationToken _ct;

        internal TextureConverter(ZipArchive archive) => _archive = archive;
        internal TextureConverter(Dictionary<string, byte[]> encoded, CancellationToken ct) { _encoded = encoded; _ct = ct; }

        /// <inheritdoc/>
        public override uint[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var id = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (id is null || id.Length != 64 || !id.All(Uri.IsHexDigit))
                throw new JsonException("A skin texture must reference its SHA-256 blob.");
            if (!_decoded.TryGetValue(id, out var pixels))
            {
                var entry = Entry(_archive!, "textures/" + id + ".u32le");
                if (!SkinLayout.SupportedSizes.Any(size => entry.Length == size * size * sizeof(uint) || entry.Length == size * (size / 2) * sizeof(uint)))
                    throw new InvalidDataException("Invalid skin texture byte length.");
                var bytes = new byte[checked((int)entry.Length)];
                using (var stream = entry.Open()) stream.ReadExactly(bytes);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The skin texture does not match its content digest.");
                pixels = new uint[bytes.Length / sizeof(uint)];
                for (var index = 0; index < pixels.Length; index++)
                    pixels[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint), sizeof(uint)));
                _decoded.Add(id, pixels);
            }

            // Public document arrays remain independently mutable; blob sharing must not alias live pixels with history.
            return (uint[])pixels.Clone();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, uint[] value, JsonSerializerOptions options)
        {
            _ct.ThrowIfCancellationRequested();
            var bytes = new byte[checked(value.Length * sizeof(uint))];
            for (var index = 0; index < value.Length; index++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint), sizeof(uint)), value[index]);
            var id = Convert.ToHexString(SHA256.HashData(bytes));
            _encoded!.TryAdd(id, bytes);
            writer.WriteStringValue(id);
        }
    }
}