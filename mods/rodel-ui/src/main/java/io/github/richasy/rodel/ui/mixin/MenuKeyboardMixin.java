package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.KeyboardHandler;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(KeyboardHandler.class)
public abstract class MenuKeyboardMixin {
    @Redirect(method = "keyPress", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;keyPressed(Lnet/minecraft/client/input/KeyEvent;)Z"))
    private boolean rodel$menuChoices(Screen screen, KeyEvent event) {
        var page = NativeMenuPage.get(screen);
        return page != null && page.choices().visible ? page.choices().keyPressed(event) : screen.keyPressed(event);
    }
}
