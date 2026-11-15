package io.github.richasy.bettertools;

import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.component.DataComponentType;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.codec.ByteBufCodecs;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.world.item.Item;

/** Shared registrations and per-player policy. Loaders call these factories at their own registry lifecycle. */
public final class BetterTools {
    public static final String ID = "bettertools";
    public static DataComponentType<CubeState> STATE;
    public static Item CUBE;
    private static final Set<UUID> EDITING = new HashSet<>();

    private BetterTools() { }

    /** Creates identifiers within this independent mod's namespace. */
    public static Identifier id(String path) { return Identifier.fromNamespaceAndPath(ID, path); }

    /** Constructs the persistent component within a loader's component-registry event. */
    public static DataComponentType<CubeState> createState() {
        return STATE = DataComponentType.<CubeState>builder().persistent(CubeState.CODEC)
                .networkSynchronized(ByteBufCodecs.fromCodecWithRegistries(CubeState.CODEC)).build();
    }

    /** Constructs the shell only while the native item registry accepts intrusive holders. */
    public static Item createCube() {
        return CUBE = new CubeItem(new Item.Properties().setId(ResourceKey.create(Registries.ITEM, id("tool_cube"))).stacksTo(1));
    }

    /** Changes only the sending player's automatic morph suspension, never any item contents or permissions. */
    public static void editing(net.minecraft.server.level.ServerPlayer player, boolean editing) {
        if (editing) EDITING.add(player.getUUID());
        else EDITING.remove(player.getUUID());
    }

    /** Runs shared target selection after the server has updated player movement and native equipment. */
    public static void tick(net.minecraft.server.MinecraftServer server) {
        server.getPlayerList().getPlayers().forEach(player -> CubeSelection.tick(player, EDITING.contains(player.getUUID())));
    }
}
