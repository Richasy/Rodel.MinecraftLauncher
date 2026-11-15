package dev.richasy.worldreroll;

import net.minecraft.client.Minecraft;

/** Preserves the native 1.21.1 pause-menu exit order before waiting for integrated-server saving. */
final class LegacyWorldExit {
    private LegacyWorldExit() { }
    static void disconnect(Minecraft client) {
        // ClientPacketListener.close alone does not disconnect the network connection in this version.
        // ClientLevel.disconnect tells the integrated server to stop before Minecraft waits for isShutdown.
        if (client.level != null) client.level.disconnect();
        client.disconnect();
    }
}
