package io.github.richasy.rodel.ui.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.worlds.WorldSurface;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.IMEPreeditOverlay;
import net.minecraft.resources.Identifier;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.Slice;

@Mixin(EditBox.class)
public abstract class EditBoxSettingsMixin {
    @Shadow private boolean textShadow;
    @Shadow private int textColor;
    @Shadow private int textColorUneditable;
    @Shadow private String suggestion;
    @Shadow private IMEPreeditOverlay preeditOverlay;

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "FIELD",
        target = "Lnet/minecraft/client/gui/components/EditBox;textColor:I"))
    private int rodel$inputInk(EditBox editor) {
        var context = UiContexts.modern(Minecraft.getInstance().gui.screen());
        return context != null && textColor == EditBox.DEFAULT_TEXT_COLOR ? context.theme().ink() : textColor;
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "FIELD",
        target = "Lnet/minecraft/client/gui/components/EditBox;textColorUneditable:I"))
    private int rodel$uneditableInk(EditBox editor) {
        var context = UiContexts.modern(Minecraft.getInstance().gui.screen());
        return context != null && textColorUneditable == 0xFF707070 ? context.theme().muted() : textColorUneditable;
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "FIELD",
        target = "Lnet/minecraft/client/gui/components/EditBox;textShadow:Z"))
    private boolean rodel$inputTextShadow(EditBox editor) {
        return UiContexts.modern(Minecraft.getInstance().gui.screen()) == null && textShadow;
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/components/EditBox;isFocused()Z"),
        slice = @Slice(from = @At(value = "FIELD", ordinal = 0,
            target = "Lnet/minecraft/client/gui/components/EditBox;hint:Lnet/minecraft/network/chat/Component;")))
    private boolean rodel$hideFocusedHint(EditBox editor) {
        // Only the native hint branch is sliced here; focus, cursor and IME handling stay native.
        return editor.isFocused() && (UiContexts.modern(Minecraft.getInstance().gui.screen()) == null
            || !editor.getValue().isEmpty() || preeditOverlay != null || suggestion != null && !suggestion.isEmpty());
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$hintText(GuiGraphicsExtractor graphics, Font font, Component hint, int x, int y, int color) {
        var context = UiContexts.modern(Minecraft.getInstance().gui.screen());
        if (context == null) graphics.text(font, hint, x, y, color);
        else graphics.text(font, hint.copy().withColor(context.theme().muted()), x, y, context.theme().muted(), false);
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Ljava/lang/String;IIIZ)V"))
    private void rodel$suggestionInk(GuiGraphicsExtractor graphics, Font font, String text, int x, int y, int color, boolean shadow) {
        var context = UiContexts.modern(Minecraft.getInstance().gui.screen());
        graphics.text(font, text, x, y, context != null && color == 0xFF808080 ? context.theme().muted() : color, shadow);
    }

    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V"))
    private void rodel$inputFace(GuiGraphicsExtractor graphics, RenderPipeline pipeline, Identifier sprite,
                                 int x, int y, int width, int height) {
        var screen = Minecraft.getInstance().gui.screen();
        MenuContext page = NativeSettingsPage.get(screen);
        if (page == null) page = WorldSurface.get(screen);
        if (page == null) page = io.github.richasy.rodel.ui.menus.NativeMenuPage.get(screen);
        if (page == null) page = UiContexts.modern(screen);
        if (page == null) {
            graphics.blitSprite(pipeline, sprite, x, y, width, height);
        } else {
            graphics.fill(x, y, x + width, y + height, page.theme().surface());
            graphics.outline(x, y, width, height, page.theme().edge());
            if (((AbstractWidget) (Object) this).isFocused() && page.focusVisible()) {
                graphics.outline(x, y, width, height, page.theme().focus());
            }
            var subpage = WorldSubpage.get(screen);
            if (subpage != null && subpage.invalidInput((AbstractWidget) (Object) this))
                graphics.outline(x, y, width, height, subpage.errorInk());
        }
    }
}
