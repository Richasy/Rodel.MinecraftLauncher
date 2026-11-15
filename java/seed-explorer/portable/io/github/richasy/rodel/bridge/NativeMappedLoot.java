package io.github.richasy.rodel.bridge;

import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

/** Evaluates an explicit saved table on a detached native container, never a player-world block. */
final class NativeMappedLoot {
    @SuppressWarnings("unchecked")
    static Map<String, Object> read(
            NativeMappedSymbols s,
            Object server,
            Object level,
            Object blocks,
            Map<String, Object> query,
            AtomicBoolean cancelled)
            throws Exception {
        NativeMappedWorld.check(cancelled);
        String id = Json.string(query, "id"),
                table = Json.string(query, "lootTable"),
                seed = Json.string(query, "lootSeed");
        int x = ((Number) query.get("x")).intValue(),
                y = ((Number) query.get("y")).intValue(),
                z = ((Number) query.get("z")).intValue();
        if (Math.abs((long) x) > 30000000
                || Math.abs((long) z) > 30000000
                || y < -32768
                || y > 32767) throw new IllegalArgumentException("Invalid saved loot origin.");
        Object block;
        try {
            block = s.call(blocks, "getValue", identifier(s, id));
        } catch (NoSuchMethodException older) {
            block = s.call(blocks, "get", identifier(s, id));
        }
        Object pos = s.make("net.minecraft.core.BlockPos", x, y, z);
        Object entity = s.call(block, "newBlockEntity", pos, s.call(block, "defaultBlockState"));
        if (entity == null || !s.type("net.minecraft.world.Container").isInstance(entity))
            throw new IllegalArgumentException("The recorded block has no original container.");
        String[] parts = table.split(":", 2);
        if (parts.length != 2
                || NativeMappedLoot.class
                                        .getClassLoader()
                                        .getResource(
                                                "data/"
                                                        + parts[0]
                                                        + "/loot_table/"
                                                        + parts[1]
                                                        + ".json")
                                == null
                        && NativeMappedLoot.class
                                        .getClassLoader()
                                        .getResource(
                                                "data/"
                                                        + parts[0]
                                                        + "/loot_tables/"
                                                        + parts[1]
                                                        + ".json")
                                == null)
            throw new IllegalArgumentException(
                    "The recorded loot table is not supplied by this original runtime.");
        s.call(entity, "setLevel", level);
        Object tableId = identifier(s, table);
        try {
            s.call(entity, "setLootTable", tableId, Long.parseLong(seed));
        } catch (NoSuchMethodException keyed) {
            Object tableKey =
                    s.call(
                            s.type("net.minecraft.resources.ResourceKey"),
                            "create",
                            s.field("net.minecraft.core.registries.Registries", "LOOT_TABLE"),
                            tableId);
            s.call(entity, "setLootTable", tableKey, Long.parseLong(seed));
        }
        s.call(entity, "unpackLootTable", new Object[] {null});
        int size = ((Number) s.call(entity, "getContainerSize")).intValue();
        if (size < 1 || size > 256)
            throw new IllegalArgumentException(
                    "The original container exceeds the prediction budget.");
        Object ops =
                s.call(
                        s.type("net.minecraft.resources.RegistryOps"),
                        "create",
                        s.field("com.mojang.serialization.JsonOps", "INSTANCE"),
                        s.call(server, "registryAccess"));
        Object codec = s.field("net.minecraft.world.item.ItemStack", "CODEC");
        List<Object> items = new ArrayList<>();
        for (int slot = 0; slot < size; slot++) {
            NativeMappedWorld.check(cancelled);
            Object stack = s.call(entity, "getItem", slot);
            if (Boolean.TRUE.equals(s.call(stack, "isEmpty"))) continue;
            Object result = s.call(codec, "encodeStart", ops, stack), encoded;
            try {
                encoded = s.call(result, "getOrThrow");
            } catch (NoSuchMethodException older) {
                encoded =
                        s.call(
                                result,
                                "getOrThrow",
                                false,
                                (Consumer<String>)
                                        message -> {
                                            throw new IllegalArgumentException(message);
                                        });
            }
            Map<String, Object> value = Json.parse(encoded.toString());
            Object components = value.get("components");
            if (!(components instanceof Map)) components = value.get("tag");
            if (!(components instanceof Map)) components = Collections.emptyMap();
            items.add(
                    Json.object(
                            "slot",
                            slot,
                            "id",
                            value.get("id"),
                            "count",
                            s.call(stack, "getCount"),
                            "components",
                            components));
        }
        return Json.object(
                "id",
                id,
                "x",
                x,
                "y",
                y,
                "z",
                z,
                "state",
                "generated",
                "slots",
                size,
                "lootTable",
                table,
                "lootSeed",
                seed,
                "items",
                items);
    }

    private static Object identifier(NativeMappedSymbols s, String value) throws Exception {
        String name =
                s.hasType("net.minecraft.resources.Identifier")
                        ? "net.minecraft.resources.Identifier"
                        : "net.minecraft.resources.ResourceLocation";
        try {
            return s.call(s.type(name), "parse", value);
        } catch (NoSuchMethodException older) {
            return s.make(name, value);
        }
    }
}
