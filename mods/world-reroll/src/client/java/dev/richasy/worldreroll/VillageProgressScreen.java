package dev.richasy.worldreroll;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.core.BlockPos;
import net.minecraft.network.chat.Component;

/** A cancellable preparation dialog using the module's own theme without pausing chunk delivery. */
final class VillageProgressScreen extends Screen {
    private final RerollLook look = new RerollLook();
    private final Runnable cancel;
    private int left, top, panelWidth, panelHeight;

    VillageProgressScreen(Component message, Runnable cancel) {
        super(message);
        this.cancel = cancel;
    }

    @Override protected void init() {
        look.refresh();
        panelWidth = Math.min(360, width - 24);
        int contentWidth = panelWidth - look.inset * 2;
        int messageHeight = look.height(font, title, contentWidth, look.body);
        panelHeight = look.inset * 2 + messageHeight + look.gap + look.control;
        left = (width - panelWidth) / 2;
        top = Math.max(4, (height - panelHeight) / 2);
        addRenderableWidget(look.button(left + look.inset, top + panelHeight - look.inset - look.control,
                contentWidth, "gui.cancel", false, cancel));
    }

    /** The integrated server must continue generating and sending chunks while this dialog is visible. */
    @Override public boolean isPauseScreen() { return false; }

    /** Escape cancels the same owned preparation as the visible cancel action. */
    @Override public void onClose() { cancel.run(); }

    /** Renders the existing module palette and native text without taking over the world background. */
    @Override public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
        look.refresh();
        look.panel(graphics, left, top, panelWidth, panelHeight);
        look.text(graphics, font, title, left + look.inset, top + look.inset,
                panelWidth - look.inset * 2, look.body, look.ink);
        super.extractRenderState(graphics, mouseX, mouseY, delta);
    }

    static boolean areaReady(Minecraft client, BlockPos spawn) {
        if (spawn == null || client.level == null || client.player == null
                || client.level.dimension() != net.minecraft.world.level.Level.OVERWORLD
                || client.player.blockPosition().distSqr(spawn) > 64) return false;
        for (int x = -1; x <= 1; x++) {
            for (int z = -1; z <= 1; z++) {
                if (!client.level.hasChunkAt(spawn.offset(x * 16, 0, z * 16))) return false;
            }
        }
        return true;
    }
}
