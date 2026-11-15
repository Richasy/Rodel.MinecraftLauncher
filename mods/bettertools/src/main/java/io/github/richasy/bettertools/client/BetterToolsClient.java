package io.github.richasy.bettertools.client;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.EditingPayload;
import io.github.richasy.bettertools.CubeSelection;
import java.util.function.Consumer;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;

/** Shares native inventory suspension and loader-specific bundle selection transport. */
public final class BetterToolsClient {
    private static Boolean editing;
    private static Consumer<CustomPacketPayload> sender;
    private BetterToolsClient() { }

    /** Binds the loader's native payload channel; no screen or menu is registered. */
    public static void initialize(Consumer<CustomPacketPayload> send) { sender = send; }

    /** Sends one editing or selection update through the current loader's registered channel. */
    public static void send(CustomPacketPayload payload) { if (sender != null) sender.accept(payload); }

    /** Reports screen transitions through the loader's transport, with no independent inventory model. */
    public static void tick(net.minecraft.client.Minecraft client) {
        if (client.player == null) { editing = null; return; }
        boolean screenOpen = client.gui.screen() != null;
        CubeSelection.update(client.player, screenOpen);
        if (editing == null || editing != screenOpen) {
            send(new EditingPayload(screenOpen));
            editing = screenOpen;
        }
    }
}
