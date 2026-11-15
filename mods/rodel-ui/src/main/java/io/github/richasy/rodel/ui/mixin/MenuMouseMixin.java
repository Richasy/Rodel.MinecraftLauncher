package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.MouseHandler;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Transform once, before dispatch, including native Screens that override the base input methods. */
@Mixin(MouseHandler.class)
public abstract class MenuMouseMixin {
    @Redirect(method = "onButton", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;mouseClicked(Lnet/minecraft/client/input/MouseButtonEvent;Z)Z"))
    private boolean rodel$click(Screen screen, MouseButtonEvent event, boolean twice) {
        NativeMenuPage page = NativeMenuPage.get(screen);
        return page != null && page.choices().visible ? page.choices().mouseClicked(page.mouse(event), twice)
            : screen.mouseClicked(page == null ? event : page.mouse(event), twice);
    }

    @Redirect(method = "onButton", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;mouseReleased(Lnet/minecraft/client/input/MouseButtonEvent;)Z"))
    private boolean rodel$release(Screen screen, MouseButtonEvent event) {
        NativeMenuPage page = NativeMenuPage.get(screen);
        return page != null && page.choices().visible ? page.choices().mouseReleased(page.mouse(event))
            : screen.mouseReleased(page == null ? event : page.mouse(event));
    }

    @Redirect(method = "onScroll", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;mouseScrolled(DDDD)Z"))
    private boolean rodel$scroll(Screen screen, double x, double y, double horizontal, double vertical) {
        NativeMenuPage page = NativeMenuPage.get(screen);
        return page != null && page.choices().visible
            ? page.choices().mouseScrolled(page.coordinate(x), page.coordinate(y), horizontal, vertical)
            : screen.mouseScrolled(page == null ? x : page.coordinate(x), page == null ? y : page.coordinate(y), horizontal, vertical);
    }

    @Redirect(method = "handleAccumulatedMovement", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;mouseMoved(DD)V"))
    private void rodel$move(Screen screen, double x, double y) {
        NativeMenuPage page = NativeMenuPage.get(screen);
        if (page != null && page.choices().visible) page.choices().mouseMoved(page.coordinate(x), page.coordinate(y));
        else screen.mouseMoved(page == null ? x : page.coordinate(x), page == null ? y : page.coordinate(y));
    }

    @Redirect(method = "handleAccumulatedMovement", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;mouseDragged(Lnet/minecraft/client/input/MouseButtonEvent;DD)Z"))
    private boolean rodel$drag(Screen screen, MouseButtonEvent event, double x, double y) {
        NativeMenuPage page = NativeMenuPage.get(screen);
        if (page != null && page.choices().visible)
            return page.choices().mouseDragged(page.mouse(event), page.distance(x), page.distance(y));
        return screen.mouseDragged(page == null ? event : page.mouse(event),
            page == null ? x : page.distance(x), page == null ? y : page.distance(y));
    }
}
