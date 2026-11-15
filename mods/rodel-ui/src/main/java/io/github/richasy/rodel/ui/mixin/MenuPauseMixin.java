package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.PauseMenuAccess;
import io.github.richasy.rodel.ui.menus.PauseMenuPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.FriendsButton;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PauseScreen.class)
public abstract class MenuPauseMixin implements PauseMenuAccess {
    @Override @Accessor("disconnectButton") public abstract Button rodel$exitButton();
    @Override @Accessor("friends") public abstract FriendsButton rodel$friendsButton();

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$pauseContent(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        if (NativeMenuPage.get((Screen) (Object) this) instanceof PauseMenuPage page) {
            page.content(graphics, mouseX, mouseY, partialTick);
            callback.cancel();
        }
    }
}
