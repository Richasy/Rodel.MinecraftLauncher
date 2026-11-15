package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkRowLayout;
import io.github.richasy.rodel.ui.menus.SocialMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.PlayerFaceExtractor;
import net.minecraft.client.gui.screens.social.PlayerEntry;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PlayerEntry.class)
public abstract class SocialPlayerRowMixin {
    @Shadow private Button hideButton;
    @Shadow private Button showButton;
    @Shadow private Button reportButton;
    @Shadow private boolean hasDraftReport;
    @Shadow @Final private static Identifier DRAFT_REPORT_SPRITE;
    @Shadow @Final public static int SKIN_SHADE;
    @Shadow private Component getStatusComponent() { throw new AssertionError(); }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$row(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float tick, CallbackInfo callback) {
        if (!(NativeMenuPage.get(Minecraft.getInstance().gui.screen()) instanceof SocialMenuPage page) || !page.rendering()) return;
        var entry = (PlayerEntry) (Object) this;
        var density = page.density();
        int left = entry.getContentX();
        int right = entry.getContentRight();
        int top = entry.getY();
        int center = top + entry.getHeight() / 2;
        if (page.theme().highContrast()) {
            graphics.fill(left, top, right, top + entry.getHeight(), page.theme().surface());
            graphics.outline(left, top, right - left, entry.getHeight(), page.theme().edge());
        }
        int actionLeft = right;
        boolean actions = hideButton != null && showButton != null && reportButton != null;
        if (actions) {
            actionLeft -= density.control() * 2 + density.gap();
            int buttonY = center - density.control() / 2;
            hideButton.setRectangle(density.control(), density.control(), actionLeft, buttonY);
            showButton.setRectangle(density.control(), density.control(), actionLeft, buttonY);
            reportButton.setRectangle(density.control(), density.control(), right - density.control(), buttonY);
        }
        int faceY = center - density.icon() / 2;
        PlayerFaceExtractor.extractRenderState(graphics, entry.getSkinGetter().get(), left, faceY, density.icon());
        if (entry.isRemoved()) graphics.fill(left, faceY, left + density.icon(), faceY + density.icon(), SKIN_SHADE);
        int textX = left + density.icon() + density.gap();
        int textWidth = Math.max(0, (actions ? actionLeft - density.gap() : right) - textX);
        Component status = getStatusComponent();
        boolean hasStatus = !status.getString().isBlank();
        int textHeight = density.body() + (hasStatus ? density.gap() / 2 + density.detail() : 0);
        int nameY = center - textHeight / 2;
        NetworkRowLayout.text(page, graphics, Component.literal(entry.getPlayerName()), textX, nameY, textWidth,
            density.body(), entry.isRemoved() ? page.theme().muted() : page.theme().ink(), mouseX, mouseY);
        if (hasStatus) NetworkRowLayout.text(page, graphics, status, textX, nameY + density.body() + density.gap() / 2,
            textWidth, density.detail(), page.theme().muted(), mouseX, mouseY);
        if (actions) {
            hideButton.extractRenderState(graphics, mouseX, mouseY, tick);
            showButton.extractRenderState(graphics, mouseX, mouseY, tick);
            reportButton.extractRenderState(graphics, mouseX, mouseY, tick);
        }
        if (hasDraftReport && reportButton != null) {
            int side = density.control() / 2;
            graphics.blitSprite(RenderPipelines.GUI_TEXTURED, DRAFT_REPORT_SPRITE,
                reportButton.getRight() - side, reportButton.getY(), side, side);
        }
        callback.cancel();
    }
}
