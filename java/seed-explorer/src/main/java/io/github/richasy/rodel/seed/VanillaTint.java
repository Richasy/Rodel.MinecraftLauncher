package io.github.richasy.rodel.seed;

import static io.github.richasy.rodel.seed.Protocol.*;

import net.minecraft.client.color.block.BlockColors;
import net.minecraft.client.renderer.block.BlockAndTintGetter;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.ColorResolver;
import net.minecraft.world.level.DryFoliageColor;
import net.minecraft.world.level.FoliageColor;
import net.minecraft.world.level.GrassColor;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Proxy;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Function;

import javax.imageio.ImageIO;

/**
 * Resolves original block tint providers against actual frozen states and biomes, without a client
 * or player world.
 */
final class VanillaTint {
    private static boolean initialized;
    private final Registry<Biome> biomes;
    private final BlockColors colors;

    VanillaTint(RegistryAccess registries) throws IOException {
        initializeColorMaps();
        biomes = registries.lookupOrThrow(Registries.BIOME);
        colors = BlockColors.createDefault();
    }

    private static synchronized void initializeColorMaps() throws IOException {
        if (initialized) return;
        GrassColor.init(colorMap("grass"));
        FoliageColor.init(colorMap("foliage"));
        DryFoliageColor.init(colorMap("dry_foliage"));
        initialized = true;
    }

    private static int[] colorMap(String name) throws IOException {
        try (var stream =
                VanillaTint.class.getResourceAsStream(
                        "/assets/minecraft/textures/colormap/" + name + ".png")) {
            if (stream == null) throw new IOException("Missing original biome color map: " + name);
            var image = ImageIO.read(stream);
            if (image == null || image.getWidth() != 256 || image.getHeight() != 256)
                throw new IOException("Invalid original biome color map: " + name);
            // NativeImage.makePixelArray used by the original reload listener is row-major ARGB
            // too.
            return image.getRGB(0, 0, 256, 256, null, 0, 256);
        }
    }

    static RegionTints generated(ServerLevel level, Bounds bounds, AtomicBoolean cancelled) {
        try {
            return new VanillaTint(level.registryAccess())
                    .read(
                            bounds,
                            level::getBlockState,
                            pos -> level.getBiome(pos).value(),
                            cancelled);
        } catch (IOException failure) {
            throw new UncheckedIOException("Original biome color maps are unavailable.", failure);
        }
    }

    RegionTints frozen(FrozenTintQuery query, AtomicBoolean cancelled) {
        if (query == null
                || query.bounds() == null
                || query.palette() == null
                || query.biomePalette() == null)
            throw new IllegalArgumentException("A frozen tint volume is required.");
        var b = query.bounds();
        if (b.volume() < 1
                || b.volume() > 32_768
                || b.maxX() < b.minX()
                || b.maxY() < b.minY()
                || b.maxZ() < b.minZ()
                || query.palette().isEmpty()
                || query.palette().size() > 65_536
                || query.biomePalette().isEmpty()
                || query.biomePalette().size() > 65_536)
            throw new IllegalArgumentException("Frozen tint volume exceeds its transport budget.");
        var blocks = decode(query.blocks(), b.volume());
        var biomeIndices = decode(query.biomes(), b.volume());
        var states = new BlockState[query.palette().size()];
        for (int i = 0; i < states.length; i++) states[i] = state(query.palette().get(i));
        var registered = new Biome[query.biomePalette().size()];
        for (int i = 0; i < registered.length; i++) {
            var id = Identifier.tryParse(query.biomePalette().get(i));
            registered[i] = id != null && biomes.containsKey(id) ? biomes.getValue(id) : null;
        }
        for (int i = 0; i < b.volume(); i++) {
            if (index(blocks, i) >= states.length || index(biomeIndices, i) >= registered.length)
                throw new IllegalArgumentException("Frozen tint palette index is invalid.");
        }
        int width = b.maxX() - b.minX() + 1, depth = b.maxZ() - b.minZ() + 1;
        Function<BlockPos, Integer> offset =
                pos ->
                        b.contains(pos.getX(), pos.getY(), pos.getZ())
                                ? ((pos.getY() - b.minY()) * depth + pos.getZ() - b.minZ()) * width
                                        + pos.getX()
                                        - b.minX()
                                : -1;
        return read(
                b,
                pos -> {
                    int i = offset.apply(pos);
                    return i < 0 ? null : states[index(blocks, i)];
                },
                pos -> {
                    int i = offset.apply(pos);
                    return i < 0 ? null : registered[index(biomeIndices, i)];
                },
                cancelled);
    }

    private static byte[] decode(String value, long volume) {
        byte[] bytes = Base64.getDecoder().decode(value);
        if (bytes.length != volume * 2)
            throw new IllegalArgumentException("Frozen tint volume is incomplete.");
        return bytes;
    }

    private static int index(byte[] bytes, int position) {
        return (bytes[position * 2] & 255) | ((bytes[position * 2 + 1] & 255) << 8);
    }

    List<Integer> surface(BlockState state, BlockPos pos, ServerLevel level) {
        var getter = getter(level::getBlockState, at -> level.getBiome(at).value());
        var result = new ArrayList<Integer>();
        for (var source : colors.getTintSources(state)) {
            int color = source.colorInWorld(state, getter, pos);
            result.add(color == -1 ? -1 : color & 0xffffff);
        }
        return result;
    }

    private static BlockState state(BlockStateEntry value) {
        var id = Identifier.tryParse(value.id());
        if (id == null || !BuiltInRegistries.BLOCK.containsKey(id)) return null;
        var block = BuiltInRegistries.BLOCK.getValue(id);
        var state = block.defaultBlockState();
        for (var entry : value.properties().entrySet()) {
            var property = block.getStateDefinition().getProperty(entry.getKey());
            if (property == null) return null;
            state = property(state, property, entry.getValue());
            if (state == null) return null;
        }
        return state;
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    private static BlockState property(BlockState state, Property property, String value) {
        Optional parsed = property.getValue(value);
        return parsed.isPresent() ? state.setValue(property, (Comparable) parsed.get()) : null;
    }

    private RegionTints read(
            Bounds b,
            Function<BlockPos, BlockState> states,
            Function<BlockPos, Biome> actualBiomes,
            AtomicBoolean cancelled) {
        var world = getter(states, actualBiomes);
        return read(b, states, actualBiomes, cancelled, world);
    }

    private BlockAndTintGetter getter(Function<BlockPos, BlockState> states, Function<BlockPos, Biome> actualBiomes) {
        return (BlockAndTintGetter)
                        Proxy.newProxyInstance(
                                VanillaTint.class.getClassLoader(),
                                new Class<?>[] {BlockAndTintGetter.class},
                                (proxy, method, args) -> {
                                    if (method.getName().equals("getBlockTint")) {
                                        var pos = (BlockPos) args[0];
                                        var biome = actualBiomes.apply(pos);
                                        if (biome == null) throw new MissingSample();
                                        return ((ColorResolver) args[1])
                                                .getColor(biome, pos.getX(), pos.getZ());
                                    }
                                    if (method.getName().equals("getBlockState")
                                            || method.getName().equals("getFluidState")) {
                                        var state = states.apply((BlockPos) args[0]);
                                        if (state == null) throw new MissingSample();
                                        return method.getName().equals("getBlockState")
                                                ? state
                                                : state.getFluidState();
                                    }
                                    if (method.isDefault())
                                        return InvocationHandler.invokeDefault(proxy, method, args);
                                    throw new UnsupportedOperationException(
                                            "Unsupported original tint dependency: " + method);
                                });
    }

    private RegionTints read(Bounds b, Function<BlockPos, BlockState> states, Function<BlockPos, Biome> actualBiomes,
            AtomicBoolean cancelled, BlockAndTintGetter world) {
        var palette = new ArrayList<RegionTint>();
        var identities = new HashMap<RegionTint, Integer>();
        var binary =
                ByteBuffer.allocate(Math.toIntExact(b.volume() * 2)).order(ByteOrder.LITTLE_ENDIAN);
        for (int y = b.minY(); y <= b.maxY(); y++)
            for (int z = b.minZ(); z <= b.maxZ(); z++) {
                if (cancelled.get() || Thread.currentThread().isInterrupted())
                    throw new CancellationException();
                for (int x = b.minX(); x <= b.maxX(); x++) {
                    var pos = new BlockPos(x, y, z);
                    var biome = actualBiomes.apply(pos);
                    String biomeId =
                            biome == null ? "rodel:unknown" : biomes.getKey(biome).toString();
                    var state = states.apply(pos);
                    var result = new ArrayList<Integer>();
                    if (state != null)
                        for (var source : colors.getTintSources(state)) {
                            int rgb;
                            try {
                                rgb = source.colorInWorld(state, world, pos);
                            } catch (MissingSample absent) {
                                rgb = -1;
                            }
                            result.add(rgb == -1 ? -1 : rgb & 0xFFFFFF);
                        }
                    var tint = new RegionTint(biomeId, List.copyOf(result));
                    Integer index = identities.get(tint);
                    if (index == null) {
                        if (palette.size() >= 65_536)
                            throw new IllegalArgumentException(
                                    "Original tint palette exceeds its volume budget.");
                        index = palette.size();
                        palette.add(tint);
                        identities.put(tint, index);
                    }
                    binary.putShort(index.shortValue());
                }
            }
        return new RegionTints(palette, Base64.getEncoder().encodeToString(binary.array()));
    }

    private static final class MissingSample extends RuntimeException {}
}
