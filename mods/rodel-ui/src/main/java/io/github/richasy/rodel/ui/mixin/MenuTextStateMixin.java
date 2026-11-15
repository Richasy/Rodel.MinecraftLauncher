package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.renderer.state.gui.GuiTextRenderState;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.resources.Identifier;
import net.minecraft.util.FormattedCharSequence;
import org.joml.Matrix3x2f;
import org.joml.Matrix3x2fc;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Includes ActiveTextCollector, which native list children use instead of GuiGraphicsExtractor.text. */
@Mixin(GuiTextRenderState.class)
public abstract class MenuTextStateMixin {
    @Unique private static final FontDescription rodel$regular = new FontDescription.Resource(
        Identifier.fromNamespaceAndPath("rodel_game_ui", "regular"));
    @Shadow @Final private Font font;
    @Shadow @Final @Mutable private FormattedCharSequence text;
    @Shadow @Final @Mutable public Matrix3x2fc pose;
    @Shadow @Final private int x;
    @Shadow @Final private int y;
    @Shadow @Final @Mutable private int color;
    @Shadow @Final @Mutable private boolean dropShadow;

    @Inject(method = "<init>", at = @At("RETURN"))
    private void rodel$nativeText(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) return;
        dropShadow = false;
        if (!page.stylingText()) {
            FormattedCharSequence original = text;
            text = sink -> original.accept((index, style, codepoint) ->
                sink.accept(index, style.withBold(false), codepoint));
            return;
        }
        int rgb = color & 0xFFFFFF;
        boolean secondary = NativeMenuPage.secondaryInk(rgb);
        FormattedCharSequence original = text;
        StringBuilder plain = new StringBuilder();
        boolean[] allSecondary = {true};
        original.accept((index, style, codepoint) -> {
            plain.appendCodePoint(codepoint);
            int source = style.getColor() == null ? rgb : style.getColor().getValue();
            allSecondary[0] &= NativeMenuPage.secondaryInk(source);
            return true;
        });
        boolean secondaryText = secondary || !plain.isEmpty() && allSecondary[0];
        color = page.ink(color);
        dropShadow = false;
        text = sink -> original.accept((index, style, codepoint) -> {
            var presentation = style.withBold(false);
            if (style.getColor() != null) {
                int source = style.getColor().getValue();
                int mapped = page.ink(0xFF000000 | source) & 0xFFFFFF;
                if (source != mapped) presentation = presentation.withColor(mapped);
            }
            int source = style.getColor() == null ? rgb : style.getColor().getValue();
            if (secondaryText || NativeMenuPage.secondaryInk(source))
                presentation = presentation.withFont(rodel$regular).withBold(false);
            return sink.accept(index, presentation, codepoint);
        });
        float size = secondaryText ? .9f : 1;
        if (size != 1) {
            pose = new Matrix3x2f(pose).translate(x, y).scale(size).translate(-x, -y);
        }
    }
}
