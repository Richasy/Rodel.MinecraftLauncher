package io.github.richasy.rodel.ui.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Replace only vanilla control faces; native content sprites, icons, skins and slot previews remain. */
@Mixin(GuiGraphicsExtractor.class)
public abstract class MenuGraphicsMixin {
    @Inject(method = "blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIIIF)V",
        at = @At("HEAD"), cancellable = true)
    private void rodel$buttonFace(RenderPipeline pipeline, Identifier sprite, int x, int y, int width, int height,
                                   float alpha, CallbackInfo callback) {
        rodel$face(sprite, x, y, width, height, callback);
    }

    @Inject(method = "blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIII)V",
        at = @At("HEAD"), cancellable = true)
    private void rodel$buttonFaceOpaque(RenderPipeline pipeline, Identifier sprite, int x, int y, int width, int height,
                                         CallbackInfo callback) {
        rodel$face(sprite, x, y, width, height, callback);
    }

    @Inject(method = "blitSprite(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIIII)V",
        at = @At("HEAD"), cancellable = true)
    private void rodel$buttonFaceTinted(RenderPipeline pipeline, Identifier sprite, int x, int y, int width, int height,
                                       int color, CallbackInfo callback) {
        rodel$face(sprite, x, y, width, height, callback);
    }

    private void rodel$face(Identifier sprite, int x, int y, int width, int height, CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering() || !sprite.getNamespace().equals("minecraft")) return;
        String path = sprite.getPath();
        GuiGraphicsExtractor graphics = (GuiGraphicsExtractor) (Object) this;
        if (path.equals("icon/news") || path.equals("icon/invite")) {
            var icon = io.github.richasy.rodel.ui.MenuIcon.nativeSprite(sprite);
            int side = Math.min(width, height);
            icon.extract(graphics, x + (width - side) / 2f, y + (height - side) / 2f, side, page.theme().ink());
            callback.cancel();
            return;
        }
        if (path.equals("friends/list_separator_top")) {
            graphics.fill(x, y, x + width, y + height, page.theme().edge());
            callback.cancel();
            return;
        }
        if (path.equals("widget/slot_frame")
            && page.screen() instanceof io.github.richasy.rodel.ui.menus.NetworkMenuLayouts.RealmManager) {
            callback.cancel();
            return;
        }
        if (path.startsWith("realm_status/")) {
            int color = switch (path) {
                case "realm_status/open" -> page.theme().primary();
                case "realm_status/expires_soon" -> page.theme().light() ? 0xFFA16716 : 0xFFE5BD6F;
                default -> page.theme().light() ? 0xFFAF3931 : 0xFFF09A8E;
            };
            int side = Math.min(7, Math.min(width, height));
            int left = x + (width - side) / 2;
            int top = y + (height - side) / 2;
            graphics.fill(left, top, left + side, top + side, color);
            graphics.outline(left, top, side, side, page.theme().edge());
            callback.cancel();
            return;
        }
        if (!path.equals("widget/button") && !path.equals("widget/button_highlighted")
            && !path.equals("widget/button_disabled") && !path.equals("friends/button")
            && !path.equals("friends/button_highlighted") && !path.equals("friends/button_disabled")) return;
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(.5f);
        page.theme().button(graphics, width * 2, height * 2, false, path.endsWith("_highlighted"), false, !path.endsWith("_disabled"));
        graphics.pose().popMatrix();
        callback.cancel();
    }
}
