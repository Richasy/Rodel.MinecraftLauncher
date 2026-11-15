"""Build synthetic save-format oracles; no Minecraft or player files are inputs.

Rebuild with Python 3 and lz4/xxhash installed. Normal .NET tests consume the
checked-in tiny fixtures and do not require Python or these modules.
Packed arrays follow the on-disk layouts; assertions use declared block
positions and materials, not this encoder's implementation details.
"""

from pathlib import Path
import gzip
import struct
import zlib
import os
import subprocess
import tempfile
import zipfile

import lz4.block
import xxhash


ROOT = Path(__file__).parent


def text(value):
    encoded = value.encode("utf-8")
    return struct.pack(">H", len(encoded)) + encoded


def named(name, tag):
    kind, payload = tag
    return bytes([kind]) + text(name) + payload


def compound(values):
    return 10, b"".join(named(name, tag) for name, tag in values.items()) + b"\0"


def number(kind, value):
    return kind, struct.pack({1: ">b", 3: ">i", 4: ">q", 5: ">f", 6: ">d"}[kind], value)


def string(value):
    return 8, text(value)


def array(kind, values):
    if kind == 7:
        data = bytes(values)
    else:
        fmt = ">i" if kind == 11 else ">Q"
        data = b"".join(struct.pack(fmt, value) for value in values)
    return kind, struct.pack(">i", len(values)) + data


def listing(kind, values):
    return 9, bytes([kind]) + struct.pack(">i", len(values)) + b"".join(value[1] for value in values)


def document(values):
    return named("", compound(values))


def packed(entries, bits, dense):
    if dense:
        # A single arbitrary-precision bit string independently encodes crossings.
        value = sum(entry << (index * bits) for index, entry in enumerate(entries))
        longs = [(value >> offset) & ((1 << 64) - 1) for offset in range(0, len(entries) * bits, 64)]
    else:
        per_long = 64 // bits
        longs = [sum(entry << (index * bits) for index, entry in enumerate(entries[start:start + per_long]))
                 for start in range(0, len(entries), per_long)]
    return array(12, longs)


def section_chunk(x, y, material, dense=False, wrapped=False, string_palette=False, structures=False):
    names = ["minecraft:air"] + [f"minecraft:synthetic_{i}" for i in range(1, 17)] + [material]
    blocks = [0] * 4096
    for local_x in range(16):
        blocks[((y % 16) * 256) + local_x] = 17
    if string_palette == "mixed":
        palette = listing(10, [compound({"id": string(name.split("[")[0]), "properties": compound({"axis": string("y")})})
                              if "[" in name else compound({"": string(name)}) for name in names])
    elif string_palette:
        palette = listing(8, [string(name) for name in names])
    else:
        palette = listing(10, [compound({"Name": string(name)}) for name in names])
    if wrapped:
        section = {"Y": number(1, y // 16), "Palette": palette, "BlockStates": packed(blocks, 5, dense)}
        values = {"xPos": number(3, x), "zPos": number(3, 0), "Sections": listing(10, [compound(section)]), "Biomes": array(11, [4] * 256)}
        return document({"DataVersion": number(3, 1900), "Level": compound(values)})
    section = {"Y": number(1, y // 16), "block_states": compound({"palette": palette, "data": packed(blocks, 5, dense)}),
               "biomes": compound({"palette": listing(8, [string("minecraft:plains")])})}
    values = {"DataVersion": number(3, 4000), "xPos": number(3, x), "zPos": number(3, 0), "sections": listing(10, [compound(section)])}
    if structures:
        section["biomes"] = compound({"palette": listing(8, [string("example:glowing_grove")])})
        values["sections"] = listing(10, [compound(section)])
        children = [compound({"BB": array(11, bounds)}) for bounds in [[10, 60, 10, 20, 80, 20], [20, 64, 20, 30, 90, 40]]]
        values["structures"] = compound({"starts": compound({"minecraft:village_plains": compound({
            "id": string("minecraft:village_plains"), "Children": listing(10, children)})})})
    return document(values)


def legacy_chunk(x=0, sections=False):
    blocks = bytearray(4096 if sections else 32768)
    for local_x in range(16):
        index = (2 * 256) + local_x if sections else (local_x * 16 * 128) + 62
        blocks[index] = 45 if sections else 5
    level = {"xPos": number(3, x), "zPos": number(3, 0)}
    if sections:
        level["Sections"] = listing(10, [compound({"Y": number(1, 4), "Blocks": array(7, blocks)})])
    else:
        level["Blocks"] = array(7, blocks)
    return document({"Level": compound(level)})


def lz4_stream(payload):
    data = lz4.block.compress(payload, store_size=False)
    # Minecraft's Java LZ4Block stream uses little endian lengths and seeded XXH32.
    return (b"LZ4Block" + bytes([0x26]) + struct.pack("<III", len(data), len(payload), xxhash.xxh32(payload, seed=0x9747B28C).intdigest())
            + data + b"LZ4Block" + bytes([0x16]) + b"\0" * 12)


def region(chunks):
    result = bytearray(8192)
    for slot, method, payload in chunks:
        offset = len(result) // 4096
        packet = struct.pack(">iB", len(payload) + 1, method) + payload
        sectors = (len(packet) + 4095) // 4096
        result[slot * 4:slot * 4 + 4] = offset.to_bytes(3, "big") + bytes([sectors])
        result.extend(packet + b"\0" * (sectors * 4096 - len(packet)))
    return result


def write_preview_stages():
    # These literal native statuses distinguish incomplete generation from completed empty terrain.
    chunks = [(0, 2, zlib.compress(section_chunk(0, 70, "minecraft:gold_block", structures=True)))]
    for x, status in [(1, "minecraft:structure_starts"), (2, "minecraft:full"), (3, "unknown_future_status")]:
        data = {"xPos": number(3, x), "zPos": number(3, 0), "Status": string(status), "sections": listing(10, [])}
        if x == 1:
            data["structures"] = compound({"starts": compound({"minecraft:village_plains": compound({
                "id": string("minecraft:village_plains"), "Children": listing(10, [compound({"BB": array(11, [20, 60, 2, 30, 90, 14])})])})})})
        chunks.append((x, 2, zlib.compress(document(data))))
    chunks.append((4, 2, zlib.compress(b"invalid-nbt")))
    (ROOT / "preview-stages.mca.fixture").write_bytes(region(chunks))


def main():
    write_preview_stages()
    def png_chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    pixel = (b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 6, 0, 0, 0))
             + png_chunk(b"IDAT", zlib.compress(bytes([0, 17, 136, 170, 255]))) + png_chunk(b"IEND", b""))
    with zipfile.ZipFile(ROOT / "textures.jar.fixture", "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("assets/minecraft/textures/block/oak_log_top.png", pixel)
        for registry, ids in [("worldgen/biome", ["plains", "forest", "nether_wastes"])]:
            for identifier in ids:
                archive.writestr(f"data/minecraft/{registry}/{identifier}.json", "{}")
        resources = {
            "dimension_type/overworld": '{"min_y":-64,"height":384}',
            "dimension_type/the_nether": '{"min_y":0,"height":256}',
            "tags/worldgen/biome/is_overworld": '{"values":["minecraft:plains","#minecraft:wooded"]}',
            "tags/worldgen/biome/wooded": '{"values":["minecraft:forest"]}',
            "tags/worldgen/biome/is_nether": '{"values":["minecraft:nether_wastes"]}',
            "tags/worldgen/biome/has_structure/village": '{"values":["minecraft:plains"]}',
            "worldgen/structure/village_plains": '{"biomes":"#minecraft:has_structure/village"}',
            "worldgen/structure/fortress": '{"biomes":["minecraft:nether_wastes"]}'
        }
        for path, data in resources.items():
            archive.writestr(f"data/minecraft/{path}.json", data)
        archive.writestr("assets/minecraft/lang/en_us.json", '{"biome.minecraft.forest":"Synthetic Forest"}')
        archive.writestr("assets/minecraft/textures/item/diamond.png", pixel)
        archive.writestr("assets/minecraft/blockstates/oak_log.json", '{"variants":{"axis=y":{"model":"minecraft:block/synthetic_cube"}}}')
        faces = ','.join('"'+face+'":{"texture":"#all","cullface":"'+face+'"}' for face in ['north','south','east','west','up','down'])
        archive.writestr("assets/minecraft/models/block/synthetic_cube.json", '{"textures":{"all":"minecraft:block/oak_log_top"},"elements":[{"from":[0,0,0],"to":[16,16,16],"faces":{'+faces+'}}]}')
    finite = bytearray(5 * 8 * 3)
    finite[((6 * 3) + 1) * 5 + 2] = 41
    (ROOT / "indev.mclevel.fixture").write_bytes(gzip.compress(document({"Map": compound({
        "Width": number(3, 5), "Height": number(3, 8), "Length": number(3, 3), "Blocks": array(7, finite),
        "Spawn": listing(3, [number(3, 2), number(3, 7), number(3, 1)])})}), mtime=0))
    java_bin = Path(os.environ["JAVA_HOME"]) / "bin"
    with tempfile.TemporaryDirectory() as classes:
        subprocess.run([str(java_bin / "javac"), "--release", "8", "-d", classes, str(ROOT / "ClassicFixture.java")], check=True)
        subprocess.run([str(java_bin / "java"), "-cp", classes, "ClassicFixture", str(ROOT / "classic.mine.fixture")], check=True)
    player = {"Pos": listing(6, [number(6, 123.25), number(6, 73.5), number(6, -88.75)]),
              "Rotation": listing(5, [number(5, 45), number(5, 0)]), "Dimension": number(3, -1)}
    data = {"LevelName": string("Synthetic map world"), "Version": compound({"Name": string("format-fixture")}),
            "WorldGenSettings": compound({"seed": number(4, -9223372036854775797)}), "Player": compound(player),
            "SpawnX": number(3, -32), "SpawnY": number(3, 66), "SpawnZ": number(3, 0)}
    (ROOT / "level.dat.fixture").write_bytes(gzip.compress(document({"Data": compound(data)}), mtime=0))
    data.pop("WorldGenSettings")
    data["RandomSeed"] = number(4, 9223372036854775797)
    (ROOT / "legacy-level.dat.fixture").write_bytes(gzip.compress(document({"Data": compound(data)}), mtime=0))
    split_data = {"LevelName": string("Split storage world"), "Version": compound({"Name": string("layout-fixture")}),
                  "singleplayer_uuid": array(11, [1, 2, 3, 4]),
                  "spawn": compound({"pos": array(11, [-32, 66, 0]), "dimension": string("minecraft:overworld")})}
    (ROOT / "split-level.dat.fixture").write_bytes(gzip.compress(document({"Data": compound(split_data)}), mtime=0))
    dimensions = {"minecraft:" + dimension: compound({"type": string("minecraft:" + dimension)})
                  for dimension in ["overworld", "the_nether", "the_end"]}
    (ROOT / "world_gen_settings.dat.fixture").write_bytes(gzip.compress(document({"data": compound({
        "seed": number(4, -9223372036854775797), "dimensions": compound(dimensions)})}), mtime=0))
    for name, uuid, x in [("split-player", [1, 2, 3, 4], 123.25), ("other-player", [9, 8, 7, 6], 999.5)]:
        record = dict(player, UUID=array(11, uuid), Dimension=string("minecraft:the_nether"),
                      Pos=listing(6, [number(6, x), number(6, 73.5), number(6, -88.75)]))
        (ROOT / (name + ".dat.fixture")).write_bytes(gzip.compress(document(record), mtime=0))
    chunks = [(0, 2, zlib.compress(section_chunk(0, 70, "minecraft:gold_block"))),
              (1, 2, zlib.compress(section_chunk(1, 80, "minecraft:diamond_block", dense=True, wrapped=True))),
              (2, 1, gzip.compress(legacy_chunk(2, sections=True), mtime=0)),
              (3, 4, lz4_stream(section_chunk(3, 44, "minecraft:bricks"))),
              (4, 0x82, b"")]
    (ROOT / "r.0.0.mca.fixture").write_bytes(region(chunks))
    # A string block-state palette must skip stored air and strip state properties.
    string_chunks = [(0, 2, zlib.compress(section_chunk(0, 70, "minecraft:oak_log[axis=y]", string_palette=True)))] + chunks[1:]
    (ROOT / "string-palette.mca.fixture").write_bytes(region(string_chunks))
    mixed_chunks = [(0, 2, zlib.compress(section_chunk(0, 70, "minecraft:oak_log[axis=y]", string_palette="mixed")))] + chunks[1:]
    (ROOT / "mixed-palette.mca.fixture").write_bytes(region(mixed_chunks))
    (ROOT / "structure.mca.fixture").write_bytes(region([(0, 2, zlib.compress(section_chunk(0, 70, "minecraft:gold_block", structures=True)))]))
    # Actual saved state overrides and inventories, including an unopened loot table and an absent neighbor chunk.
    palette = [compound({"Name": string("minecraft:air")}),
               compound({"Name": string("minecraft:oak_log"), "Properties": compound({"axis": string("x")})}),
               compound({"Name": string("minecraft:chest"), "Properties": compound({"facing": string("north")})}),
               compound({"Name": string("minecraft:diamond_block")})]
    cells = [0] * 4096
    for x, index in [(12, 1), (13, 2), (14, 2), (15, 3)]:
        cells[(6 * 256) + x] = index
    entities = [compound({"id": string("minecraft:chest"), "x": number(3, 13), "y": number(3, 70), "z": number(3, 0),
        "Items": listing(10, [compound({"id": string("minecraft:diamond"), "count": number(3, 5), "Slot": number(1, 3),
            "components": compound({"example:exact": number(4, 9223372036854775797)})})])}),
        compound({"id": string("minecraft:chest"), "x": number(3, 14), "y": number(3, 70), "z": number(3, 0),
                  "LootTable": string("minecraft:chests/simple_dungeon"), "LootTableSeed": number(4, 123456)})]
    saved = document({"xPos": number(3, 0), "zPos": number(3, 0), "sections": listing(10, [compound({"Y": number(1, 4),
        "block_states": compound({"palette": listing(10, palette), "data": packed(cells, 4, False)})})]), "block_entities": listing(10, entities)})
    (ROOT / "saved-region.mca.fixture").write_bytes(region([(0, 2, zlib.compress(saved))]))
    (ROOT / "c.4.0.mcc.fixture").write_bytes(zlib.compress(section_chunk(4, 91, "minecraft:iron_block")))
    (ROOT / "r.0.0.mcr.fixture").write_bytes(region([(0, 2, zlib.compress(legacy_chunk()))]))
    (ROOT / "c.0.0.dat.fixture").write_bytes(gzip.compress(legacy_chunk(), mtime=0))


if __name__ == "__main__":
    main()
