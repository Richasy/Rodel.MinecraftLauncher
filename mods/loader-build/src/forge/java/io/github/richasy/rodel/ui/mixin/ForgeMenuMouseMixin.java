package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.MouseHandler;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraftforge.client.ForgeHooksClient;
import net.minecraftforge.client.event.ForgeEventFactoryClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Transforms native menu coordinates at Forge's dispatch boundary, retaining its event and cancellation chain. */
@Mixin(MouseHandler.class)
public abstract class ForgeMenuMouseMixin {
    @Redirect(method = "onButton", at = @At(value = "INVOKE", target = "Lnet/minecraftforge/client/event/ForgeEventFactoryClient;onScreenMouseClicked(Lnet/minecraft/client/gui/screens/Screen;DDLnet/minecraft/client/input/MouseButtonEvent;Z)Z"))
    private boolean rodel$click(Screen screen, double x, double y, MouseButtonEvent event, boolean twice) {
        var page = NativeMenuPage.get(screen);
        if (page != null && page.choices().visible) return page.choices().mouseClicked(page.mouse(event), twice);
        return ForgeEventFactoryClient.onScreenMouseClicked(screen, page == null ? x : page.coordinate(x),
            page == null ? y : page.coordinate(y), page == null ? event : page.mouse(event), twice);
    }

    @Redirect(method = "onButton", at = @At(value = "INVOKE", target = "Lnet/minecraftforge/client/event/ForgeEventFactoryClient;onScreenMouseReleased(Lnet/minecraft/client/gui/screens/Screen;DDLnet/minecraft/client/input/MouseButtonEvent;)Z"))
    private boolean rodel$release(Screen screen, double x, double y, MouseButtonEvent event) {
        var page = NativeMenuPage.get(screen);
        if (page != null && page.choices().visible) return page.choices().mouseReleased(page.mouse(event));
        return ForgeEventFactoryClient.onScreenMouseReleased(screen, page == null ? x : page.coordinate(x),
            page == null ? y : page.coordinate(y), page == null ? event : page.mouse(event));
    }

    @Redirect(method = "onScroll", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/screens/Screen;mouseScrolled(DDDD)Z"))
    private boolean rodel$scroll(Screen screen, double x, double y, double horizontal, double vertical) {
        var page = NativeMenuPage.get(screen);
        return page != null && page.choices().visible
            ? page.choices().mouseScrolled(page.coordinate(x), page.coordinate(y), horizontal, vertical)
            : screen.mouseScrolled(page == null ? x : page.coordinate(x), page == null ? y : page.coordinate(y), horizontal, vertical);
    }

    @Redirect(method = "handleAccumulatedMovement", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/screens/Screen;mouseMoved(DD)V"))
    private void rodel$move(Screen screen, double x, double y) {
        var page = NativeMenuPage.get(screen);
        if (page != null && page.choices().visible) page.choices().mouseMoved(page.coordinate(x), page.coordinate(y));
        else screen.mouseMoved(page == null ? x : page.coordinate(x), page == null ? y : page.coordinate(y));
    }

    @Redirect(method = "handleAccumulatedMovement", at = @At(value = "INVOKE", target = "Lnet/minecraftforge/client/ForgeHooksClient;onScreenMouseDrag(Lnet/minecraft/client/gui/screens/Screen;Lnet/minecraft/client/input/MouseButtonEvent;DD)Z"))
    private boolean rodel$drag(Screen screen, MouseButtonEvent event, double x, double y) {
        var page = NativeMenuPage.get(screen);
        if (page != null && page.choices().visible) return page.choices().mouseDragged(page.mouse(event), page.distance(x), page.distance(y));
        return ForgeHooksClient.onScreenMouseDrag(screen, page == null ? event : page.mouse(event),
            page == null ? x : page.distance(x), page == null ? y : page.distance(y));
    }
}
