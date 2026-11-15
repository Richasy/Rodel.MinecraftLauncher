package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.StyledMenuScreen;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.compat.ModMenuIntegration;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.gui.screens.options.OptionsScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(OptionsScreen.class)
public abstract class OptionsScreenMixin extends Screen {
    @Shadow @Final
    private Screen lastScreen;
    @Unique
    private Button rodel$mods;

    protected OptionsScreenMixin(Component title) {
        super(title);
    }

    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$copyright(CallbackInfo callback) {
        rodel$mods = null;
        if (!getClass().equals(OptionsScreen.class) || !UiConfig.enabled(Effect.SETTINGS)) return;
        MenuContext appearance = lastScreen instanceof StyledMenuScreen styled ? styled.rodel$context()
            : ((SettingsHost) this).rodel$rootContext();
        if (appearance == null && lastScreen != null
            && (lastScreen.getClass() == TitleScreen.class && !UiConfig.enabled(Effect.TITLE)
                || lastScreen.getClass() == PauseScreen.class && !UiConfig.enabled(Effect.MENUS))) {
            appearance = new TitleMenu(minecraft, width, height);
        }
        if (appearance == null) return;
        if (ModMenuIntegration.installed()) {
            rodel$mods = Button.builder(Component.translatable("rodel.settings.mods"),
                button -> ModMenuIntegration.open(this))
                .bounds(12, 7, 92, 20)
                .tooltip(Tooltip.create(Component.translatable("rodel.settings.mods.tooltip")))
                .build();
            addRenderableWidget(rodel$mods);
            rodel$positionMods();
        }
        NativeSettingsPage.root(this, appearance);
    }

    @Inject(method = "repositionElements", at = @At("TAIL"))
    private void rodel$resizeMods(CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(this);
        if (page != null) page.layout();
        else rodel$positionMods();
    }

    @Unique
    private void rodel$positionMods() {
        if (rodel$mods != null && lastScreen instanceof StyledMenuScreen styled) {
            MenuContext appearance = styled.rodel$context();
            if (appearance == null) return;
            rodel$mods.setRectangle(92, 20, 12, 7);
            ((ThemedButton) rodel$mods).rodel$setStyle(
                new MenuContext.Viewport(width, height, appearance),
                MenuIcon.SETTINGS, false, false, false);
        }
    }
}
