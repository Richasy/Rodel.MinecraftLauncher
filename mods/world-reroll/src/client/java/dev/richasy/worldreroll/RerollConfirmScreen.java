package dev.richasy.worldreroll;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.minecraft.client.input.KeyEvent;
import com.mojang.blaze3d.platform.InputConstants;

/** Cancel/Escape retain the actual preferences and active world; only the affirmative callback starts a reroll. */
class RerollConfirmScreen extends Screen {
    private final Screen parent;
    private final RerollLook look;
    private final Component message;
    private final Runnable accept;
    private final String acceptKey, cancelKey;
    private int left, top, panelWidth, panelHeight, bodyY, bodyBottom, messageHeight, scroll;

    RerollConfirmScreen(Screen parent, RerollLook look, Component title, Component message, Runnable accept) {
        this(parent, look, title, message, "worldreroll.button", "gui.cancel", accept);
    }
    RerollConfirmScreen(Screen parent, RerollLook look, Component title, Component message,
            String acceptKey, String cancelKey, Runnable accept) {
        super(title); this.parent = parent; this.look = look; this.message = message; this.accept = accept;
        this.acceptKey = acceptKey; this.cancelKey = cancelKey;
    }
    @Override protected void init() {
        look.refresh(); panelWidth = Math.min(360, width - 24);
        int contentWidth = panelWidth - look.inset * 2;
        messageHeight = look.height(font, message, contentWidth, look.body);
        panelHeight = Math.min(height - 8, look.inset * 2 + look.heading + messageHeight + look.gap * 3 + look.control);
        left = (width - panelWidth) / 2; top = Math.max(4, (height - panelHeight) / 2);
        bodyY = top + look.inset + look.heading + look.gap;
        int footerY = top + panelHeight - look.inset - look.control;
        bodyBottom = footerY - look.gap;
        scroll = Math.min(scroll, Math.max(0, messageHeight - (bodyBottom - bodyY)));
        int actionWidth = (contentWidth - look.inset) / 2;
        addRenderableWidget(look.button(left + look.inset, footerY, actionWidth, acceptKey, true, accept));
        addRenderableWidget(look.button(left + look.inset + actionWidth + look.inset, footerY,
            contentWidth - actionWidth - look.inset, cancelKey, false, this::onClose));
    }
    @Override public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
        look.refresh(); look.panel(graphics, left, top, panelWidth, panelHeight);
        look.emphasizedText(graphics, font, title, left + look.inset, top + look.inset, panelWidth - look.inset * 2, look.heading, look.ink);
        graphics.enableScissor(left + look.inset, bodyY, left + panelWidth - look.inset, Math.max(bodyY, bodyBottom));
        look.text(graphics, font, message, left + look.inset, bodyY - scroll, panelWidth - look.inset * 2, look.body, look.ink);
        graphics.disableScissor();
        if (messageHeight > bodyBottom - bodyY) look.scrollbar(graphics, left + panelWidth - 6, bodyY, bodyBottom - bodyY, messageHeight, scroll);
        super.extractRenderState(graphics, mouseX, mouseY, delta);
    }
    @Override public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        scroll = Math.clamp(scroll - (int) (vertical * 20), 0, Math.max(0, messageHeight - (bodyBottom - bodyY))); return true;
    }
    @Override public boolean keyPressed(KeyEvent event) {
        if (event.key() == InputConstants.KEY_PAGEDOWN || event.key() == InputConstants.KEY_PAGEUP) {
            int distance = bodyBottom - bodyY;
            scroll = Math.clamp(scroll + (event.key() == InputConstants.KEY_PAGEDOWN ? distance : -distance), 0, Math.max(0, messageHeight - distance));
            return true;
        }
        return super.keyPressed(event);
    }
    @Override public Component getNarrationMessage() { return Component.empty().append(title).append(". ").append(message); }
    @Override public void onClose() { minecraft.gui.setScreen(parent); }
    @Override public boolean isPauseScreen() { return true; }
}
