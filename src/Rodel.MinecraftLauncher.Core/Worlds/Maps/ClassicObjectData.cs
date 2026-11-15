// Copyright (c) Richasy. All rights reserved.

using System.Buffers.Binary;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Worlds.Maps;

/// <summary>Decodes bounded Java serialization as inert data. No class loading, callbacks or object deserialization occurs.</summary>
internal sealed class ClassicObjectData(ReadOnlyMemory<byte> bytes)
{
    private readonly List<object?> _handles = [];
    private int _position, _nodes;
    private sealed record Field(byte Kind, string Name);
    private sealed record Class(string Name, byte Flags, Field[] Fields, Class? Parent);

    internal static Dictionary<string, object> Read(ReadOnlyMemory<byte> bytes)
    {
        var reader = new ClassicObjectData(bytes);
        var magic = reader.U2();
        var version = reader.U2();
        if (magic != 0xaced || version != 5) throw new InvalidDataException("Invalid Classic object stream header.");
        return reader.Value(0) as Dictionary<string, object> ?? throw new InvalidDataException("The Classic world root is not a stored object.");
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > bytes.Length - _position) throw new InvalidDataException("Truncated Classic object stream.");
        var result = bytes.Span.Slice(_position, count); _position += count; return result;
    }
    private byte U1() => Take(1)[0];
    private ushort U2() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    private int I4() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
    private string Text() => Encoding.UTF8.GetString(Take(U2()));
    private int Length(int maximum) { var length = I4(); return length is >= 0 && length <= maximum ? length : throw new InvalidDataException("Classic array exceeds its bounded size."); }

    private object? Value(int depth)
    {
        if (depth > 128 || ++_nodes > 2_000_000) throw new InvalidDataException("Classic object graph exceeds its bounded size.");
        var token = U1();
        if (token == 0x70) return null;
        if (token == 0x71)
        {
            var handle = I4() - 0x7e0000;
            return handle >= 0 && handle < _handles.Count ? _handles[handle] : throw new InvalidDataException("Invalid Classic object reference.");
        }

        if (token == 0x74) { var text = Text(); _handles.Add(text); return text; }

        if (token == 0x72)
        {
            var name = Text(); _ = Take(8); var slot = _handles.Count; _handles.Add(null);
            var flags = U1(); var count = U2();
            if (count > 1024 || (flags & ~3) != 0 || (flags & 2) == 0) throw new InvalidDataException("Unsupported Classic class data shape.");
            var fields = new Field[count];
            for (var index = 0; index < count; index++)
            {
                var kind = U1(); fields[index] = new(kind, Text());
                if (kind is (byte)'L' or (byte)'[') _ = Value(depth + 1);
            }

            Annotation(depth + 1);
            var parent = Value(depth + 1);
            if (parent is not null and not Class) throw new InvalidDataException("Invalid Classic superclass data.");
            var description = new Class(name, flags, fields, parent as Class); _handles[slot] = description; return description;
        }

        if (token == 0x73)
        {
            var type = Value(depth + 1) as Class ?? throw new InvalidDataException("Missing Classic class descriptor.");
            var data = new Dictionary<string, object>(StringComparer.Ordinal); _handles.Add(data);
            ReadFields(type, data, depth + 1); return data;
        }

        if (token == 0x75)
        {
            var type = Value(depth + 1) as Class ?? throw new InvalidDataException("Missing Classic array descriptor.");
            if (type.Name == "[B") { var data = Take(Length(WorldNbt.MaximumBytes)).ToArray(); _handles.Add(data); return data; }

            var count = Length(65536); var values = new List<object>(); _handles.Add(values);
            if (type.Name.Length < 2) throw new InvalidDataException("Invalid Classic array descriptor.");
            for (var index = 0; index < count; index++) { var value = Primitive((byte)type.Name[1], depth + 1); if (value is not null) values.Add(value); }

            return values;
        }

        throw new InvalidDataException("Unsupported Classic object stream token.");
    }

    private void ReadFields(Class type, Dictionary<string, object> data, int depth)
    {
        if (depth > 128) throw new InvalidDataException("Classic class hierarchy exceeds its bounded depth.");
        if (type.Parent is not null) ReadFields(type.Parent, data, depth + 1);
        foreach (var field in type.Fields) if (Primitive(field.Kind, depth + 1) is { } value) data[field.Name] = value;
        if ((type.Flags & 1) != 0) Annotation(depth + 1);
    }
    private object? Primitive(byte kind, int depth) => (char)kind switch
    {
        'B' => unchecked((sbyte)U1()),
        'Z' => U1() != 0,
        'C' => (char)U2(),
        'S' => unchecked((short)U2()),
        'I' => I4(),
        'J' => BinaryPrimitives.ReadInt64BigEndian(Take(8)),
        'F' => BitConverter.Int32BitsToSingle(I4()),
        'D' => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(Take(8))),
        'L' or '[' => Value(depth + 1),
        _ => throw new InvalidDataException("Unknown Classic primitive type.")
    };
    private void Annotation(int depth)
    {
        while (true)
        {
            var token = U1();
            if (token == 0x78) return;
            if (token == 0x77)
            {
                _ = Take(U1());
            }
            else if (token == 0x7a)
            {
                _ = Take(Length(WorldNbt.MaximumBytes));
            }
            else { _position--; _ = Value(depth + 1); }
        }
    }
}