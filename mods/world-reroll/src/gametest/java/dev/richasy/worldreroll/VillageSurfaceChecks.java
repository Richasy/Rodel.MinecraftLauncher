package dev.richasy.worldreroll;

import java.util.LinkedHashMap;
import java.util.Map;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;

/** Exercises actual path/snow heightmaps so asynchronous preparation cannot silently restore the old rejection. */
final class VillageSurfaceChecks {
    private VillageSurfaceChecks() {}

    static void verify(ServerLevel level, ChunkPos position) {
        var chunk = level.getChunk(position.x(), position.z());
        BlockPos base = new BlockPos(position.getMinBlockX() + 4, level.getMaxY() - 4, position.getMinBlockZ() + 4);
        Map<BlockPos, BlockState> previous = new LinkedHashMap<>();
        int flags = Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE;
        try {
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) {
                BlockPos block = base.offset(x, y, 0);
                previous.put(block, level.getBlockState(block));
                level.setBlock(block, y == 0 ? Blocks.STONE.defaultBlockState() : Blocks.AIR.defaultBlockState(), flags);
            }
            level.setBlock(base, Blocks.DIRT_PATH.defaultBlockState(), flags);
            check(base.above().equals(VillageSpawn.safeFeet(level, chunk, base.getX(), base.getZ())),
                    "A path must support spawn despite its 15/16 height");

            BlockPos snow = base.east().above();
            for (int layers : new int[]{1, 7}) {
                level.setBlock(snow, Blocks.SNOW.defaultBlockState().setValue(BlockStateProperties.LAYERS, layers), flags);
                check(snow.above().equals(VillageSpawn.safeFeet(level, chunk, snow.getX(), snow.getZ())),
                        "Both heightmap-ignored thin snow and blocking thick snow must retain a safe start");
            }

            BlockPos water = base.east(2).above();
            level.setBlock(water, Blocks.WATER.defaultBlockState(), flags);
            check(VillageSpawn.safeFeet(level, chunk, water.getX(), water.getZ()) == null,
                    "Accepting paths and snow must not accept standing water");
        } finally {
            previous.forEach((block, state) -> level.setBlock(block, state, flags));
        }
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
