package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.UiContexts;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Checkbox;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Checkbox.class)
public abstract class CheckboxModernMixin {
    @Inject(method = "extractContents", at = @At("HEAD"), cancellable = true)
    private void rodel$checkbox(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var minecraft = Minecraft.getInstance();
        var context = UiContexts.modern(minecraft.gui.screen());
        if (context == null) return;
        var widget = (AbstractWidget) (Object) this;
        var checkbox = (Checkbox) (Object) this;
        int box = Math.max(1, Math.round((context.density().tight() ? 20 : 24) * context.scale()));
        int gap = Math.max(1, Math.round(10 * context.scale()));
        int size = context.density().body();
        int x = widget.getX();
        int y = widget.getY() + (widget.getHeight() - box) / 2;
        int ink = widget.active ? context.theme().ink() : context.theme().muted();
        graphics.fill(x, y, x + box, y + box, checkbox.selected() ? context.theme().primary() : context.theme().surface());
        graphics.outline(x, y, box, box, widget.isFocused() && context.focusVisible() ? context.theme().focus() : context.theme().edge());
        if (checkbox.selected()) {
            MenuText.extract(graphics, minecraft.font, Component.literal("✓"), x + box * 0.1f,
                y - box * 0.1f, box, context.theme().primaryInk());
        }
        MenuText.extract(graphics, minecraft.font, MenuText.styled(widget.getMessage()), x + box + gap,
            widget.getY() + (widget.getHeight() - size) / 2f, size, ink);
        callback.cancel();
    }
}
