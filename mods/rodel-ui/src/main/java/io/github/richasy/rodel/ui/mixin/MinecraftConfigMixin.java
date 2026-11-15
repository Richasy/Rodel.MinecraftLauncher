package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.StyledMenuScreen;
import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.compat.ModMenuParent;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.options.OptionsScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.ModMenu", remap = false)
public abstract class MinecraftConfigMixin {
    @Inject(method = "getConfigScreen", at = @At("RETURN"))
    private static void rodel$minecraftSettings(String modId, Screen parent, CallbackInfoReturnable<Screen> callback) {
        Screen result = callback.getReturnValue();
        if (!UiConfig.enabled(Effect.SETTINGS) || !modId.equals("minecraft")
            || result == null || result.getClass() != OptionsScreen.class) return;
        MenuContext appearance = null;
        Screen ancestor = parent;
        while (ancestor != null) {
            MenuContext context = UiContexts.owned(ancestor);
            if (context != null) {
                appearance = context;
                break;
            }
            if (ancestor instanceof StyledMenuScreen styled) {
                appearance = styled.rodel$context();
                break;
            }
            if (!(ancestor instanceof ModMenuParent mods)) break;
            Screen previous = mods.rodel$previousScreen();
            if (previous == ancestor) break;
            ancestor = previous;
        }
        if (appearance == null) appearance = new TitleMenu(Minecraft.getInstance(), parent.width, parent.height);
        ((SettingsHost) result).rodel$rootContext(appearance);
    }
}
