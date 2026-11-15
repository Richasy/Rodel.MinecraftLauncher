package dev.richasy.worldreroll;

import java.io.IOException;
import java.nio.file.Path;
import java.util.Optional;
import java.util.OptionalLong;
import java.util.concurrent.CompletableFuture;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.AlertScreen;
import net.minecraft.client.gui.screens.DisconnectedScreen;
import net.minecraft.client.gui.screens.GenericMessageScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.LayeredRegistryAccess;
import net.minecraft.network.chat.Component;
import net.minecraft.server.RegistryLayer;
import net.minecraft.util.Util;
import net.minecraft.world.level.levelgen.WorldGenSettings;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.storage.LevelDataAndDimensions;
import net.minecraft.world.level.storage.LevelResource;
import net.minecraft.world.level.storage.LevelStorageSource.LevelStorageAccess;
import net.minecraft.world.level.storage.PrimaryLevelData;
import net.minecraft.world.level.storage.LevelStorageSource;
import net.minecraft.world.level.gamerules.GameRuleMap;
import net.minecraft.world.level.gamerules.GameRules;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class RerollController {
    private static final Logger LOGGER = LoggerFactory.getLogger("worldreroll");
    private static Attempt attempt;

    private RerollController() {}

    public static boolean available(Minecraft client) {
        var server = client.getSingleplayerServer();
        return attempt == null && !CreationSpawn.busy() && client.player != null && server != null && !server.isPublished();
    }

    public static void start(Minecraft client) {
        start(client, false);
    }

    public static void start(Minecraft client, boolean village) {
        if (!available(client)) return;
        var server = client.getSingleplayerServer();
        if (village && !server.getWorldGenSettings().options().generateStructures()) return;
        try {
            Path sourcePath = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
            String sourceId = sourcePath.getFileName().toString();
            WorldIdentity source = WorldIdentity.capture(client.getLevelSource().getBaseDir(), sourceId);
            if (!source.directory().equals(sourcePath.toRealPath())) throw new IOException("Source world is outside saves");
            Attempt pending = new Attempt(source, server, village);
            attempt = pending;

            // Vanilla waits for the integrated server to save, shut down and release its world lock.
            client.disconnectFromWorld(net.minecraft.client.multiplayer.ClientLevel.DEFAULT_QUIT_MESSAGE);
            client.setScreenAndShow(new GenericMessageScreen(Component.translatable("worldreroll.preparing")));
        } catch (Exception error) {
            failed(client, error);
        }
    }

    private static void prepare(Minecraft client, Attempt pending) {
        LevelStorageAccess targetAccess = null;
        try {
            client.setScreenAndShow(new GenericMessageScreen(Component.translatable("worldreroll.preparing")));
            pending.source.verifyIdentity();
            try (var sourceAccess = client.getLevelSource().validateAndCreateAccess(pending.source.id())) {
                var recreated = client.createWorldOpenFlows().recreateWorldData(sourceAccess);
                var context = recreated.getSecond();
                context.validate();
                long previousSeed = context.options().seed();
                long seed;
                do { seed = WorldOptions.randomSeed(); } while (seed == previousSeed || pending.seed != null && seed == pending.seed);
                pending.seed = seed;
                var options = context.options().withSeed(OptionalLong.of(seed));
                var dimensions = context.selectedDimensions().bake(context.datapackDimensions());
                LayeredRegistryAccess<RegistryLayer> registries = context.worldgenRegistries()
                        .replaceFrom(RegistryLayer.DIMENSIONS, dimensions.dimensionsRegistryAccess());
                var storedRules = LevelStorageSource.readExistingSavedData(sourceAccess,
                        registries.compositeAccess(), GameRuleMap.TYPE).getOrThrow();
                var gameRules = new GameRules(context.dataConfiguration().enabledFeatures(), storedRules);
                var data = new PrimaryLevelData(recreated.getFirst(), dimensions.specialWorldProperty(),
                        registries.compositeAccess().allRegistriesLifecycle());
                var settings = new LevelDataAndDimensions.WorldDataAndGenSettings(data,
                        new WorldGenSettings(options, context.selectedDimensions()));
                String targetId = WorldFolder.reserve(client.getLevelSource().getBaseDir(), recreated.getFirst().levelName());
                targetAccess = client.getLevelSource().validateAndCreateAccess(targetId);
                pending.target = WorldIdentity.capture(client.getLevelSource().getBaseDir(), targetId);
                WorldDataPacks.copy(sourceAccess.getLevelPath(LevelResource.DATAPACK_DIR),
                        targetAccess.getLevelPath(LevelResource.DATAPACK_DIR));
                pending.session.loading(targetId);
                LOGGER.info("Starting reroll: source={}, replacement={}, seed={}", pending.source.id(), targetId, seed);
                client.createWorldOpenFlows().createLevelFromExistingSettings(targetAccess,
                        context.dataPackResources(), registries, settings, Optional.of(gameRules));
                targetAccess = null; // Ownership transferred to the new integrated server.
            }
        } catch (Exception error) {
            if (targetAccess != null) {
                try { targetAccess.close(); }
                catch (IOException closeError) { error.addSuppressed(closeError); }
            }
            failed(client, error);
        }
    }

    private static void failed(Minecraft client, Exception error) {
        LOGGER.error("Could not create replacement world; source world retained", error);
        Attempt pending = attempt;
        if (pending != null) {
            pending.session.cancel();
            cancelVillage(pending);
            // The failed replacement is retained, but must never become the source of a retry.
            if (pending.target != null) {
                releaseMarker(pending.target);
                pending.target = null;
            }
            client.gui.setScreen(new RerollFailureScreen(client.gui.screen(),
                    () -> retry(client, pending), () -> {
                        if (attempt != pending) return;
                        abandon(pending);
                        returnToTitle(client);
                    }, () -> abandon(pending)));
            return;
        }
        client.gui.setScreen(new AlertScreen(() -> returnToTitle(client),
                Component.translatable("worldreroll.failed.title"), Component.translatable("worldreroll.failed.body")));
    }

    private static void retry(Minecraft client, Attempt failed) {
        if (attempt != failed) return;
        try {
            failed.source.verifyIdentity();
            var server = client.getSingleplayerServer();
            var next = new Attempt(failed.source, server == null ? failed.previousServer : server, failed.village);
            next.seed = failed.seed;
            // Transfer the pinned source before removing the failure screen; stale callbacks then do nothing.
            attempt = next;
            cancelVillage(failed);
            if (server != null || client.level != null) {
                client.disconnectFromWorld(net.minecraft.client.multiplayer.ClientLevel.DEFAULT_QUIT_MESSAGE);
            }
            client.setScreenAndShow(new GenericMessageScreen(Component.translatable("worldreroll.preparing")));
        } catch (Exception error) {
            if (attempt == failed) abandon(failed);
            failed(client, error);
        }
    }

    private static void abandon(Attempt pending) {
        if (attempt != pending) return;
        attempt = null;
        pending.session.cancel();
        cancelVillage(pending);
        releaseMarkers(pending);
    }

    private static void returnToTitle(Minecraft client) {
        if (client.level != null) client.disconnectFromWorld(net.minecraft.client.multiplayer.ClientLevel.DEFAULT_QUIT_MESSAGE);
        client.gui.setScreen(new TitleScreen());
    }

    public static void tick(Minecraft client) {
        Attempt pending = attempt;
        if (pending == null) return;
        if (pending.session.phase() == RerollSession.Phase.PREPARING) {
            if (pending.previousServer.isShutdown() && client.getSingleplayerServer() == null && client.level == null) {
                prepare(client, pending);
            }
            return;
        }
        if (pending.session.phase() != RerollSession.Phase.LOADING) return;
        var screen = client.gui.screen();
        if (screen instanceof TitleScreen || screen instanceof DisconnectedScreen || screen instanceof AlertScreen) {
            abandon(pending);
            LOGGER.warn("Replacement load cancelled; source {} retained", pending.source.id());
            return;
        }
        var server = client.getSingleplayerServer();
        boolean playable = (screen == null || screen == pending.progress)
                && client.level != null && client.player != null && server != null;
        if (!playable) { pending.readyTicks = 0; return; }
        if (pending.replacementServer != null && pending.replacementServer != server) {
            abandon(pending);
            if (screen == pending.progress) client.gui.setScreen(null);
            return;
        }
        String activeId = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().getFileName().toString();
        if (!activeId.equals(pending.target.id())) { pending.readyTicks = 0; return; }
        pending.replacementServer = server;
        if (server.isPublished()) {
            failed(client, new IOException("Replacement was published before its new start was ready; source retained"));
            return;
        }
        if (pending.village && !pending.locating) {
            pending.locating = true;
            Runnable cancel = () -> {
                if (attempt == pending && pending.session.phase() == RerollSession.Phase.LOADING) {
                    failed(client, new java.util.concurrent.CancellationException("Village preparation cancelled"));
                }
            };
            pending.progress = new VillageProgressScreen(Component.translatable("worldreroll.locating"), cancel);
            client.gui.setScreen(pending.progress);
            var search = VillageSpawn.placeAsync(server, client.player.getUUID());
            pending.search = search;
            search.whenComplete((spawn, error) -> client.execute(() -> {
                if (attempt != pending || pending.search != search || client.getSingleplayerServer() != server
                        || pending.session.phase() != RerollSession.Phase.LOADING) return;
                if (error != null) {
                    failed(client, new IOException("Could not establish village spawn; source retained", error));
                } else {
                    pending.spawn = spawn;
                    var previous = pending.progress;
                    pending.progress = new VillageProgressScreen(Component.translatable("worldreroll.loading_area"), cancel);
                    if (client.gui.screen() == previous) client.gui.setScreen(pending.progress);
                    LOGGER.info("Village spawn established for {} at {}", pending.target.id(), spawn);
                }
            }));
            return;
        }
        if (pending.village && !VillageProgressScreen.areaReady(client, pending.spawn)) {
            pending.readyTicks = 0;
            return;
        }
        if (++pending.readyTicks < 20 || !pending.session.ready(activeId, true)) return;
        if (client.gui.screen() == pending.progress) client.gui.setScreen(null);
        CompletableFuture.runAsync(() -> {
            try {
                pending.target.verify(pending.source.directory());
                pending.source.verify(pending.target.directory());
                try (var access = client.getLevelSource().validateAndCreateAccess(pending.source.id())) {
                    // Recheck after taking the old world's lock. Use vanilla deletion, never an arbitrary recursive delete.
                    pending.source.verify(pending.target.directory());
                    access.deleteLevel();
                }
                pending.target.release();
                LOGGER.info("Replacement {} playable; deleted exact source {}", pending.target.id(), pending.source.id());
            } catch (IOException | net.minecraft.world.level.validation.ContentValidationException error) {
                throw new java.util.concurrent.CompletionException(error);
            }
        }, Util.ioPool()).whenComplete((ignored, error) -> client.execute(() -> {
            pending.session.complete();
            if (attempt == pending) attempt = null;
            if (error != null) LOGGER.error("Replacement {} loaded but source {} could not be deleted",
                    pending.target.id(), pending.source.id(), error);
            if (client.player != null) client.player.sendSystemMessage(Component.translatable(
                    error == null ? "worldreroll.complete" : "worldreroll.delete_failed"));
        }));
    }

    private static void releaseMarkers(Attempt pending) {
        for (WorldIdentity identity : new WorldIdentity[]{pending.source, pending.target}) {
            if (identity == null) continue;
            releaseMarker(identity);
        }
    }

    private static void releaseMarker(WorldIdentity identity) {
        try { identity.release(); }
        catch (IOException error) { LOGGER.warn("Could not remove attempt marker from {}", identity.id(), error); }
    }

    private static void cancelVillage(Attempt pending) {
        var search = pending.search;
        pending.search = null;
        pending.spawn = null;
        pending.readyTicks = 0;
        VillageSpawn.cancel(search);
    }

    private static final class Attempt {
        final WorldIdentity source;
        final net.minecraft.client.server.IntegratedServer previousServer;
        net.minecraft.client.server.IntegratedServer replacementServer;
        final RerollSession session = new RerollSession();
        final boolean village;
        boolean locating;
        net.minecraft.core.BlockPos spawn;
        CompletableFuture<net.minecraft.core.BlockPos> search;
        VillageProgressScreen progress;
        WorldIdentity target;
        Long seed;
        int readyTicks;
        Attempt(WorldIdentity source, net.minecraft.client.server.IntegratedServer previousServer, boolean village) {
            this.source = source;
            this.previousServer = previousServer;
            this.village = village;
        }
    }
}
