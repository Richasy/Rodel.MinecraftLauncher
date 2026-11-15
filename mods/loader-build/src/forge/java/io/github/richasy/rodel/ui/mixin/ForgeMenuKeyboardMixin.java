package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.KeyboardHandler;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraftforge.client.ForgeHooksClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Retains Forge's key events while routing an open first-party choice popup before its parent. */
@Mixin(KeyboardHandler.class)
public abstract class ForgeMenuKeyboardMixin {
    @Redirect(method = "keyPress", at = @At(value = "INVOKE", target = "Lnet/minecraftforge/client/ForgeHooksClient;onScreenKeyPressed(Lnet/minecraft/client/gui/screens/Screen;Lnet/minecraft/client/input/KeyEvent;)Z"))
    private boolean rodel$menuChoices(Screen screen, KeyEvent event) {
        var page = NativeMenuPage.get(screen);
        return page != null && page.choices().visible ? page.choices().keyPressed(event) : ForgeHooksClient.onScreenKeyPressed(screen, event);
    }
}
