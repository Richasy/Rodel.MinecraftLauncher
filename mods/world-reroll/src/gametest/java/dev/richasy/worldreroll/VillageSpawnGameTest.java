package dev.richasy.worldreroll;

import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.levelgen.presets.WorldPresets;

/** Real snowy terrain regression; the other village cases use a temperate seed and cannot detect this failure. */
public final class VillageSpawnGameTest implements FabricClientGameTest {
    /** Reproduces the reported seed without player saves; generation, collision and bonus loot remain native. */
    @Override public void runTest(ClientGameTestContext context) {
        var world = context.worldBuilder().adjustSettings(state -> {
            state.setSeed("-2693081779595188761");
            state.setWorldType(state.getNormalPresetList().stream().filter(entry -> entry.preset() != null
                    && entry.preset().is(WorldPresets.NORMAL)).findFirst().orElseThrow());
            state.setGenerateStructures(true);
            state.setBonusChest(true);
        }).create();
        try {
            // The previous air/sturdy-face filter rejected every candidate in this actual snowy village.
            // Direct placement isolates terrain handling from the separately tested reroll/deletion lifecycle.
            world.getServer().runOnServer(server -> {
                var level = server.overworld();
                var player = server.getPlayerList().getPlayers().getFirst();
                // Native spawn jitter changes which village wins locate's search. Use the reported initial position,
                // not GameTest's fixed (0, 0) origin, and verify the actual candidate before testing placement.
                player.teleportTo(level, -8.5, 69, 3.5, java.util.Set.of(), 0, 0, true);
                var village = level.findNearestMapStructure(net.minecraft.tags.StructureTags.VILLAGE,
                        player.blockPosition(), 100, false);
                if (village == null || village.getX() != -976 || village.getZ() != 592) {
                    throw new AssertionError("Reported snowy village was not reached: " + village);
                }
                // Keep the snow regression at this known village even if distance-ordered lookup picks a nearer one.
                var chunk = level.getChunk(village.getX() >> 4, village.getZ() >> 4);
                int height = chunk.getHeight(net.minecraft.world.level.levelgen.Heightmap.Types.WORLD_SURFACE,
                        village.getX() & 15, village.getZ() & 15) + 1;
                player.teleportTo(level, village.getX() + 0.5, height, village.getZ() + 0.5,
                        java.util.Set.of(), 0, 0, true);
            });
            var preparation = context.computeOnClient(client ->
                    VillageSpawn.placeAsync(client.getSingleplayerServer(), client.player.getUUID()));
            context.waitFor(client -> preparation.isDone(), 6000);
            var spawn = preparation.join();
            world.getServer().runOnServer(server -> {
                var level = server.overworld();
                var player = server.getPlayerList().getPlayers().getFirst();
                CreationGameTest.villageNear(level, spawn);
                if (Math.abs(spawn.getX() + 976) > 64 || Math.abs(spawn.getZ() - 592) > 64) {
                    throw new AssertionError("Snow regression escaped the reported village");
                }
                if (!level.noCollision(player) || !level.getFluidState(spawn).isEmpty()
                        || level.getBlockState(spawn.below()).is(Blocks.POWDER_SNOW)) {
                    throw new AssertionError("Snowy village placement is obstructed or hazardous");
                }
                if (!level.getRespawnData().globalPos().pos().equals(spawn)) {
                    throw new AssertionError("Snowy village world spawn was not updated");
                }
                CreationGameTest.assertBonusChest(level, spawn, true);
                VillageSurfaceChecks.verify(level, net.minecraft.world.level.ChunkPos.containing(spawn));
            });
            org.slf4j.LoggerFactory.getLogger("worldreroll").info("WORLDREROLL_SNOW_PASS: reported seed, native village placement, collision and bonus loot");
        } finally { world.close(); }
    }
}
