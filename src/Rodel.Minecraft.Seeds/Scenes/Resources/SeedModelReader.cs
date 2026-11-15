// Copyright (c) Richasy. All rights reserved.

using StbImageSharp;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;

namespace Rodel.Minecraft.Seeds;

/// <summary>Decodes original block models and textures within one caller-owned archive.</summary>
internal sealed class SeedModelReader(ZipArchive archive, CancellationToken ct)
{
    private readonly Dictionary<string, SeedResourceModel> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SeedModelTexture> _textures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);
    private static readonly string[] Directions = ["down", "up", "north", "south", "west", "east"];

    internal uint? SurfaceColor(SeedBlockState state, int[] tints)
    {
        double r = 0, g = 0, b = 0, weight = 0;
        foreach (var choice in StateModels(state))
        {
            var faces = choice.Model.Elements.SelectMany(element => ElementFaces(element, choice.Model.Textures, choice.Rotation, choice.LockUv)).ToArray();
            static Vector3 Area(SeedModelFace face) => Vector3.Cross(face.Positions[1] - face.Positions[0], face.Positions[2] - face.Positions[0]);
            var hasTop = faces.Any(face => Area(face).Y < -.0001f);
            foreach (var face in faces)
            {
                var cross = Area(face);
                // Crossed plants have no horizontal face: retain their authored cutout material instead of painting them as missing models.
                var area = hasTop ? Math.Max(0, -cross.Y) : cross.Length();
                if (area < .0001f) continue;
                var texture = _textures[face.Texture];
                var tint = face.TintIndex >= 0 && face.TintIndex < tints.Length && tints[face.TintIndex] >= 0 ? (uint)tints[face.TintIndex] : 0xffffff;
                var left = Math.Clamp((int)MathF.Floor(face.TextureCoordinates.Min(uv => uv.X) * texture.Size), 0, texture.Size - 1);
                var right = Math.Clamp((int)MathF.Ceiling(face.TextureCoordinates.Max(uv => uv.X) * texture.Size), left + 1, texture.Size);
                var top = Math.Clamp((int)MathF.Floor(face.TextureCoordinates.Min(uv => uv.Y) * texture.Size), 0, texture.Size - 1);
                var bottom = Math.Clamp((int)MathF.Ceiling(face.TextureCoordinates.Max(uv => uv.Y) * texture.Size), top + 1, texture.Size);
                for (var y = top; y < bottom; y++)
                {
                    for (var x = left; x < right; x++)
                    {
                        var index = ((y * texture.Size) + x) * 4;
                        var alpha = texture.Pixels[index + 3] / 255d * area / ((right - left) * (bottom - top));
                        r += texture.Pixels[index] * ((tint >> 16) & 255) / 255d * alpha;
                        g += texture.Pixels[index + 1] * ((tint >> 8) & 255) / 255d * alpha;
                        b += texture.Pixels[index + 2] * (tint & 255) / 255d * alpha;
                        weight += alpha;
                    }
                }
            }
        }

        return weight == 0 ? null : ((uint)(r / weight) << 16) | ((uint)(g / weight) << 8) | (uint)(b / weight);
    }

    internal SeedBlockScene Read(SeedRegionResult region)
    {
        var faces = new SeedModelFace[region.Palette.Length][];
        var cubes = new bool[faces.Length];
        for (var i = 0; i < faces.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var state = region.Palette[i];
            if (region.GenerationPolicy == "saved_snapshot_v1" && state.Id == "rodel:unknown") { faces[i] = []; continue; }

            if (state.Id is "minecraft:air" or "minecraft:cave_air" or "minecraft:void_air" or "minecraft:water" or "minecraft:lava")
            { faces[i] = []; continue; }

            try
            {
                var choices = StateModels(state).ToArray();
                if (choices.Length == 0) throw new InvalidDataException("No drawable original model.");
                var list = new List<SeedModelFace>();
                var fullCube = choices.Length == 1;
                foreach (var choice in choices)
                {
                    var model = choice.Model;
                    fullCube &= model.Elements.Length == 1 && !model.Elements[0].TryGetProperty("rotation", out _)
                        && Vector(model.Elements[0].GetProperty("from")) == Vector3.Zero && Vector(model.Elements[0].GetProperty("to")) == new Vector3(16);
                    foreach (var element in model.Elements) list.AddRange(ElementFaces(element, model.Textures, choice.Rotation, choice.LockUv));
                }

                if (list.Count == 0) throw new InvalidDataException("Original model has no visible elements.");
                faces[i] = [.. list];
                cubes[i] = fullCube && list.Count == 6 && list.All(face => _textures[face.Texture].Opaque);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            {
                faces[i] = [];
                _unavailable.Add(state.Id);
            }
        }

        var tile = 16;
        while (_textures.Values.Any(texture => texture.Size > tile)) tile *= 2;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_textures.Count)));
        var size = 16;
        while (size < columns * (tile + 4)) size *= 2;
        if (size > 4096) throw new InvalidDataException("Original preview textures exceed the atlas budget.");
        var pixels = new byte[size * size * 4];
        var placements = new Dictionary<string, Vector4>(StringComparer.Ordinal);
        var index = 0;
        foreach (var (id, texture) in _textures)
        {
            ct.ThrowIfCancellationRequested();
            var x = (index % columns * (tile + 4)) + 2;
            var y = (index / columns * (tile + 4)) + 2;
            index++;
            for (var row = 0; row < texture.Size; row++)
                texture.Pixels.AsSpan(row * texture.Size * 4, texture.Size * 4).CopyTo(pixels.AsSpan((((y + row) * size) + x) * 4));
            placements[id] = new(x, y, texture.Size, size);
        }

        foreach (var list in faces)
        {
            foreach (var face in list)
            {
                var placement = placements[face.Texture];
                for (var i = 0; i < 4; i++) face.TextureCoordinates[i] = (new Vector2(placement.X, placement.Y) + (face.TextureCoordinates[i] * placement.Z)) / placement.W;
            }
        }

        return new(region, faces, cubes, pixels, size, [.. _unavailable.Order(StringComparer.Ordinal)]);
    }

    private IEnumerable<SeedModelChoice> StateModels(SeedBlockState state)
    {
        if (state.Id is "minecraft:chest" or "minecraft:trapped_chest" or "minecraft:ender_chest")
        {
            yield return Chest(state);
            yield break;
        }

        var json = Json("blockstates", state.Id);
        if (json.TryGetProperty("variants", out var variants))
        {
            foreach (var variant in variants.EnumerateObject())
            {
                if (!variant.Name.Split(',', StringSplitOptions.RemoveEmptyEntries).All(pair =>
                {
                    var parts = pair.Split('=', 2);
                    return parts.Length == 2 && state.Properties.TryGetValue(parts[0], out var actual) && parts[1].Split('|').Contains(actual, StringComparer.Ordinal);
                }))
                {
                    continue;
                }

                yield return ChoiceFor(variant.Value);
                yield break;
            }
        }

        if (json.TryGetProperty("multipart", out var multipart))
        {
            foreach (var part in multipart.EnumerateArray())
                if (!part.TryGetProperty("when", out var when) || Matches(when, state.Properties)) yield return ChoiceFor(part.GetProperty("apply"));
        }
    }

    private static bool Matches(JsonElement condition, Dictionary<string, string> state) => condition.EnumerateObject().All(pair => pair.Name switch
    {
        "OR" => pair.Value.EnumerateArray().Any(value => Matches(value, state)),
        "AND" => pair.Value.EnumerateArray().All(value => Matches(value, state)),
        _ => state.TryGetValue(pair.Name, out var value) && pair.Value.GetString()!.Split('|').Contains(value, StringComparer.Ordinal),
    });

    private SeedModelChoice ChoiceFor(JsonElement json)
    {
        // Weighted variants alter cosmetic model/texture choice, not the original generated block state.
        if (json.ValueKind == JsonValueKind.Array) json = json[0];
        var x = json.TryGetProperty("x", out var rx) ? rx.GetSingle() : 0;
        var y = json.TryGetProperty("y", out var ry) ? ry.GetSingle() : 0;
        return new(GetModel(json.GetProperty("model").GetString()!, []), Matrix4x4.CreateRotationX(-x * MathF.PI / 180) * Matrix4x4.CreateRotationY(-y * MathF.PI / 180),
            json.TryGetProperty("uvlock", out var locked) && locked.GetBoolean());
    }

    private SeedResourceModel GetModel(string id, HashSet<string> ancestors)
    {
        id = Namespaced(id);
        if (_models.TryGetValue(id, out var found)) return found;
        if (ancestors.Count >= 32 || !ancestors.Add(id)) throw new InvalidDataException("Original model parent cycle.");
        var json = Json("models", id);
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        JsonElement[] elements = [];
        if (json.TryGetProperty("parent", out var parent))
        {
            var name = parent.GetString()!;
            if (!name.Contains("builtin/", StringComparison.Ordinal))
            {
                var inherited = GetModel(name, ancestors);
                textures = new(inherited.Textures, StringComparer.Ordinal);
                elements = inherited.Elements;
            }
        }

        if (json.TryGetProperty("textures", out var values))
        {
            foreach (var value in values.EnumerateObject())
                textures[value.Name] = (value.Value.ValueKind == JsonValueKind.String ? value.Value : value.Value.GetProperty("sprite")).GetString()!;
        }

        if (json.TryGetProperty("elements", out var geometry)) elements = [.. geometry.EnumerateArray()];
        ancestors.Remove(id);
        return _models[id] = new(textures, elements);
    }

    private IEnumerable<SeedModelFace> ElementFaces(JsonElement element, Dictionary<string, string> textures, Matrix4x4 variant, bool uvlock)
    {
        var from = Vector(element.GetProperty("from")) / 16;
        var to = Vector(element.GetProperty("to")) / 16;
        foreach (var face in element.GetProperty("faces").EnumerateObject())
        {
            ct.ThrowIfCancellationRequested();
            var texture = face.Value.GetProperty("texture").GetString()!;
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            while (texture.StartsWith('#'))
            {
                if (!aliases.Add(texture)) throw new InvalidDataException("Original model texture alias cycle.");
                texture = textures[texture[1..]];
            }

            texture = Namespaced(texture);
            TextureFor(texture);
            var positions = Corners(face.Name, from, to);
            var coordinates = DefaultUv(face.Name, from, to);
            if (face.Value.TryGetProperty("uv", out var values))
            {
                var uv = values.EnumerateArray().Select(item => item.GetSingle() / 16).ToArray();
                coordinates = [new(uv[0], uv[1]), new(uv[2], uv[1]), new(uv[2], uv[3]), new(uv[0], uv[3])];
            }

            if (face.Value.TryGetProperty("rotation", out var rotation))
            {
                var steps = rotation.GetInt32() / 90 % 4;
                coordinates = [.. coordinates.Skip(steps), .. coordinates.Take(steps)];
            }

            var normal = Vector3.TransformNormal(Normal(face.Name), variant);
            if (uvlock)
            {
                var destination = Direction(normal);
                var (u, v) = Basis(face.Name);
                var (du, dv) = Basis(destination);
                u = Vector3.TransformNormal(u, variant); v = Vector3.TransformNormal(v, variant);
                for (var i = 0; i < 4; i++)
                {
                    var centered = coordinates[i] - new Vector2(.5f);
                    coordinates[i] = new Vector2((Vector3.Dot(u, du) * centered.X) + (Vector3.Dot(v, du) * centered.Y),
                        (Vector3.Dot(u, dv) * centered.X) + (Vector3.Dot(v, dv) * centered.Y)) + new Vector2(.5f);
                }
            }

            for (var i = 0; i < 4; i++)
            {
                var p = positions[i];
                if (element.TryGetProperty("rotation", out var part))
                {
                    var origin = Vector(part.GetProperty("origin")) / 16;
                    var axis = part.GetProperty("axis").GetString();
                    var angle = part.GetProperty("angle").GetSingle() * MathF.PI / 180;
                    p -= origin;
                    if (part.TryGetProperty("rescale", out var rescale) && rescale.GetBoolean())
                    {
                        var factor = 1 / MathF.Cos(angle);
                        p *= axis switch { "x" => new(1, factor, factor), "y" => new(factor, 1, factor), _ => new Vector3(factor, factor, 1) };
                    }

                    p = Vector3.Transform(p, axis switch { "x" => Matrix4x4.CreateRotationX(angle), "y" => Matrix4x4.CreateRotationY(angle), _ => Matrix4x4.CreateRotationZ(angle) }) + origin;
                }

                positions[i] = Vector3.Transform(p - new Vector3(.5f), variant) + new Vector3(.5f);
            }

            var cull = face.Value.TryGetProperty("cullface", out var culling) ? Normal(Direction(Vector3.TransformNormal(Normal(culling.GetString()!), variant))) : Vector3.Zero;
            var shadeDirection = element.TryGetProperty("shade_direction_override", out var directionOverride) ? directionOverride.GetString()! : Direction(normal);
            var shade = element.TryGetProperty("shade", out var shading) && !shading.GetBoolean() ? 1f
                : shadeDirection switch { "up" => 1f, "down" => .65f, "east" => .89f, "west" => .8f, "north" => .84f, _ => .94f };
            var tint = face.Value.TryGetProperty("tintindex", out var tintIndex) ? tintIndex.GetInt32() : -1;
            yield return new(positions, coordinates, cull, shade, texture, tint);
        }
    }

    private static SeedModelChoice Chest(SeedBlockState state)
    {
        var type = state.Properties.GetValueOrDefault("type", "single");
        var texture = "minecraft:entity/chest/" + (state.Id == "minecraft:ender_chest" ? "ender" : state.Id == "minecraft:trapped_chest" ? "trapped" : "normal")
            + (type == "single" ? string.Empty : "_" + type);
        var elements = new List<JsonElement>();
        var left = type == "left" ? 0 : 1;
        var right = type == "right" ? 16 : 15;
        foreach (var (a, b, u, v) in new (Vector3, Vector3, int, int)[]
        {
            (new(left, 0, 1), new(right, 10, 15), 0, 19), (new(left, 9, 1), new(right, 14, 15), 0, 0),
            (new(type == "left" ? 14 : 7, 7, 15), new(type == "left" ? 16 : type == "right" ? 2 : 9, 11, 16), 0, 0),
        })
        {
            var start = a;
            var end = b;
            if (type == "right" && u == 0 && v == 0 && a.Z == 15) { start = new(0, 7, 15); end = new(2, 11, 16); }

            var dx = end.X - start.X; var dy = end.Y - start.Y; var dz = end.Z - start.Z;
            // ModelPart Y points down, so the world-facing top uses the source down-face UV.
            var uv = new Dictionary<string, float[]>
            {
                ["west"] = [u, v + dz, u + dz, v + dz + dy],
                ["north"] = [u + dz, v + dz, u + dz + dx, v + dz + dy],
                ["east"] = [u + dz + dx, v + dz, u + (2 * dz) + dx, v + dz + dy],
                ["south"] = [u + (2 * dz) + dx, v + dz, u + (2 * dz) + (2 * dx), v + dz + dy],
                ["down"] = [u + dz, v, u + dz + dx, v + dz],
                ["up"] = [u + dz + dx, v, u + dz + (2 * dx), v + dz],
            };
            // This tiny JSON is only an internal geometry representation; public transport remains typed.
            using var json = JsonDocument.Parse("{\"from\":[" + Number(start.X) + "," + Number(start.Y) + "," + Number(start.Z)
                + "],\"to\":[" + Number(end.X) + "," + Number(end.Y) + "," + Number(end.Z) + "],\"faces\":{" + string.Join(',', uv.Select(pair =>
                    "\"" + pair.Key + "\":{\"texture\":\"" + texture + "\",\"uv\":[" + string.Join(',', pair.Value.Select(value => Number(value / 4))) + "]}")) + "}}");
            elements.Add(json.RootElement.Clone());
        }

        var angle = state.Properties.GetValueOrDefault("facing", "north") switch { "south" => 0, "west" => 90, "east" => 270, _ => 180 };
        return new(new([], [.. elements]), Matrix4x4.CreateRotationY(-angle * MathF.PI / 180), false);
    }

    private static string Number(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void TextureFor(string id)
    {
        if (_textures.ContainsKey(id)) return;
        ct.ThrowIfCancellationRequested();
        var entry = archive.GetEntry(PathFor("textures", id, ".png")) ?? throw new InvalidDataException("Missing original texture: " + id);
        if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("Original texture exceeds the decode budget.");
        using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
        var bytes = memory.ToArray();
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Invalid original PNG.");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)); var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        if (width is < 1 or > 4096 || height < width || height > 8192 || (long)width * height > 16_777_216) throw new InvalidDataException("Original PNG dimensions exceed the preview budget.");
        var decoded = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        var pixels = decoded.Data.AsSpan(0, width * width * 4).ToArray();
        var opaque = true;
        for (var index = 3; index < pixels.Length; index += 4) opaque &= pixels[index] == 255;
        _textures[id] = new(width, pixels, opaque);
    }

    private JsonElement Json(string directory, string id)
    {
        ct.ThrowIfCancellationRequested();
        var entry = archive.GetEntry(PathFor(directory, id, ".json")) ?? throw new InvalidDataException("Missing original JSON: " + id);
        if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("Original model JSON exceeds the preview budget.");
        using var stream = entry.Open(); using var json = JsonDocument.Parse(stream); return json.RootElement.Clone();
    }

    private static string Namespaced(string id) => id.Contains(':', StringComparison.Ordinal) ? id : "minecraft:" + id;
    private static string PathFor(string directory, string id, string extension)
    {
        var parts = Namespaced(id).Split(':', 2);
        if (parts.Any(string.IsNullOrWhiteSpace) || parts.Any(value => value.Split('/').Contains("..", StringComparer.Ordinal))) throw new InvalidDataException("Invalid original resource ID.");
        return $"assets/{parts[0]}/{directory}/{parts[1]}{extension}";
    }
    private static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    private static Vector3 Normal(string face) => face switch { "down" => -Vector3.UnitY, "up" => Vector3.UnitY, "north" => -Vector3.UnitZ, "south" => Vector3.UnitZ, "west" => -Vector3.UnitX, "east" => Vector3.UnitX, _ => throw new InvalidDataException("Unknown original model face.") };
    private static string Direction(Vector3 normal) => Directions.MaxBy(direction => Vector3.Dot(Normal(direction), normal))!;
    private static (Vector3 U, Vector3 V) Basis(string face) => face switch
    {
        "up" => (Vector3.UnitX, Vector3.UnitZ),
        "down" => (Vector3.UnitX, -Vector3.UnitZ),
        "north" => (-Vector3.UnitX, -Vector3.UnitY),
        "south" => (Vector3.UnitX, -Vector3.UnitY),
        "west" => (Vector3.UnitZ, -Vector3.UnitY),
        _ => (-Vector3.UnitZ, -Vector3.UnitY),
    };
    private static Vector3[] Corners(string face, Vector3 a, Vector3 b) => face switch
    {
        "up" => [new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z)],
        "down" => [new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, a.Y, a.Z), new(a.X, a.Y, a.Z)],
        "north" => [new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z), new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z)],
        "south" => [new(a.X, b.Y, b.Z), new(b.X, b.Y, b.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z)],
        "west" => [new(a.X, b.Y, a.Z), new(a.X, b.Y, b.Z), new(a.X, a.Y, b.Z), new(a.X, a.Y, a.Z)],
        _ => [new(b.X, b.Y, b.Z), new(b.X, b.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z)],
    };
    private static Vector2[] DefaultUv(string face, Vector3 a, Vector3 b)
    {
        var (u, v) = Basis(face);
        var positions = Corners(face, a, b);
        return [.. positions.Select(p => new Vector2(Vector3.Dot(u, p) + (u.X < 0 || u.Y < 0 || u.Z < 0 ? 1 : 0),
            Vector3.Dot(v, p) + (v.X < 0 || v.Y < 0 || v.Z < 0 ? 1 : 0)))];
    }

}