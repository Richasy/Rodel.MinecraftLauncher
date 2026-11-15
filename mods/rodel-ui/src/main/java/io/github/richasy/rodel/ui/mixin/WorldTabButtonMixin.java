package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldCreateAccess;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.TabButton;
import net.minecraft.client.gui.components.tabs.Tab;
import net.minecraft.client.gui.components.tabs.TabManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.components.tabs.MenuTabBar$MenuTabButton")
public abstract class WorldTabButtonMixin extends TabButton {
    protected WorldTabButtonMixin(TabManager manager, Tab tab, int width, int height) { super(manager, tab, width, height); }

    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$tabFace(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var screen = Minecraft.getInstance().gui.screen();
        var page = WorldPage.get(screen);
        if (page == null || !(screen instanceof WorldCreateAccess access) || !access.rodel$tabs().children().contains(this)) return;
        boolean highlighted = isHovered() || isFocused() && page.focusVisible();
        graphics.fill(getX(), getY(), getRight(), getBottom(), isSelected() ? page.theme().selection()
            : highlighted ? page.theme().hover() : page.theme().surface());
        graphics.outline(getX(), getY(), getWidth(), getHeight(), page.theme().edge());
        if (isFocused() && page.focusVisible()) graphics.outline(getX() + 1, getY() + 1, getWidth() - 2, getHeight() - 2, page.theme().focus());
        var font = Minecraft.getInstance().font;
        float size = page.density().body();
        page.text(graphics, getMessage(), getX() + (getWidth() - font.width(getMessage()) * size / font.lineHeight) / 2f,
            getY() + (getHeight() - size) / 2f, size, isSelected() || highlighted ? page.theme().ink() : page.theme().muted(), false);
        handleCursor(graphics);
        callback.cancel();
    }
}
