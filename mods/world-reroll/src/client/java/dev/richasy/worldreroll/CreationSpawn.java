package dev.richasy.worldreroll;

import java.util.WeakHashMap;
import java.util.concurrent.CompletableFuture;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.AlertScreen;
import net.minecraft.client.gui.screens.DisconnectedScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.gui.screens.worldselection.WorldCreationUiState;
import net.minecraft.core.BlockPos;
import net.minecraft.network.chat.Component;
import net.minecraft.world.level.storage.LevelResource;
import org.slf4j.LoggerFactory;

/** Screen-local choices are armed only when vanilla actually creates a new world. */
public final class CreationSpawn {
    private static final WeakHashMap<WorldCreationUiState, Boolean> CHOICES = new WeakHashMap<>();
    private static String target;
    private static boolean locating;
    private static CompletableFuture<BlockPos> search;
    private static BlockPos destination;
    private static VillageProgressScreen progress;
    private static net.minecraft.client.server.IntegratedServer activeServer;
    private static int readyTicks;
    private CreationSpawn() {}

    public static boolean selected(WorldCreationUiState state) {
        return state.isGenerateStructures() && !state.isDebug() && CHOICES.getOrDefault(state, false);
    }

    public static void select(WorldCreationUiState state, boolean value) {
        CHOICES.put(state, value && state.isGenerateStructures() && !state.isDebug());
        state.onChanged();
    }

    public static void arm(WorldCreationUiState state) {
        clear();
        target = selected(state) ? state.getTargetFolder() : null;
    }

    public static boolean busy() { return target != null; }

    public static void tick(Minecraft client) {
        if (target == null) return;
        var screen = client.gui.screen();
        if (screen instanceof TitleScreen || screen instanceof AlertScreen || screen instanceof DisconnectedScreen) {
            clear();
            return;
        }
        var server = client.getSingleplayerServer();
        if (server == null || client.player == null || client.level == null) { readyTicks = 0; return; }
        String folder = server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().getFileName().toString();
        if (!folder.equals(target) || server.isPublished() || (activeServer != null && activeServer != server)) {
            if (screen == progress) client.gui.setScreen(null);
            clear();
            return;
        }
        if (locating) {
            if (screen != progress || !VillageProgressScreen.areaReady(client, destination)) {
                readyTicks = 0;
                return;
            }
            if (++readyTicks < 20) return;
            LoggerFactory.getLogger("worldreroll").info("Created world {} with village spawn at {}", target, destination);
            client.gui.setScreen(null);
            clear();
            return;
        }
        if (screen != null) return;
        locating = true;
        activeServer = server;
        String expected = target;
        Runnable cancel = () -> {
            if (!expected.equals(target) || client.getSingleplayerServer() != server) return;
            clear();
            client.gui.setScreen(null);
        };
        progress = new VillageProgressScreen(Component.translatable("worldreroll.locating"), cancel);
        client.gui.setScreen(progress);
        var pending = VillageSpawn.placeAsync(server, client.player.getUUID());
        search = pending;
        pending.whenComplete((spawn, error) -> client.execute(() -> {
            if (search != pending || !expected.equals(target) || client.getSingleplayerServer() != server) return;
            if (error == null) {
                destination = spawn;
                var previous = progress;
                progress = new VillageProgressScreen(Component.translatable("worldreroll.loading_area"), cancel);
                if (client.gui.screen() == previous) client.gui.setScreen(progress);
            } else {
                clear();
                LoggerFactory.getLogger("worldreroll").error("Village placement failed for newly created world {}; no saves deleted", expected, error);
                client.gui.setScreen(new AlertScreen(() -> client.gui.setScreen(null),
                        Component.translatable("worldreroll.creation.failed_title"),
                        Component.translatable("worldreroll.creation.failed_body")));
            }
        }));
    }

    private static void clear() {
        var pending = search;
        target = null;
        locating = false;
        search = null;
        destination = null;
        progress = null;
        activeServer = null;
        readyTicks = 0;
        VillageSpawn.cancel(pending);
    }
}
