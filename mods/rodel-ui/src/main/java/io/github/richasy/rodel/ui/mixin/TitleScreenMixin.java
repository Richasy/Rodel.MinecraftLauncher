package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.StyledMenuScreen;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import java.util.List;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.PlainTextButton;
import net.minecraft.client.gui.components.Renderable;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(TitleScreen.class)
public abstract class TitleScreenMixin extends Screen implements StyledMenuScreen {
    @Shadow @Final private static Component COPYRIGHT_TEXT;
    @Unique
    private TitleMenu rodel$menu;
    @Unique
    private boolean rodel$pendingLayout;
    @Unique private List<? extends GuiEventListener> rodel$expectedChildren;
    @Unique private List<Renderable> rodel$expectedRenderables;
    @Unique private List<Button> rodel$copyright = List.of();

    protected TitleScreenMixin(Component title) {
        super(title);
    }

    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        rodel$menu = null;
        rodel$copyright = List.of();
        // Every Fabric AFTER_INIT extension gets to finish before the first layout.
        rodel$pendingLayout = getClass().equals(TitleScreen.class) && UiConfig.enabled(Effect.TITLE);
    }

    @Inject(method = "tick", at = @At("TAIL"))
    private void rodel$afterInit(CallbackInfo callback) {
        rodel$updateLayout();
    }

    @Unique
    private void rodel$updateLayout() {
        if (rodel$menu != null && (!children().equals(rodel$expectedChildren)
            || !((ScreenRenderablesAccessor) this).rodel$renderables().equals(rodel$expectedRenderables))) {
            TitleMenu previous = rodel$menu;
            rodel$menu = null;
            boolean retainedControls = children().stream().anyMatch(child -> child != previous.themeButton()
                && rodel$expectedChildren.contains(child));
            previous.restore();
            removeWidget(previous.themeButton());
            for (Button copyright : retainedControls ? rodel$copyright : List.<Button>of()) {
                if (!children().contains(copyright)) {
                    if (((ScreenRenderablesAccessor) this).rodel$renderables().contains(copyright)) addWidget(copyright);
                    else addRenderableWidget(copyright);
                }
            }
            org.slf4j.LoggerFactory.getLogger("Rodel UI").info("Keeping native title page after its controls changed");
        }
        if (!rodel$pendingLayout) return;
        rodel$pendingLayout = false;
        List<? extends GuiEventListener> original = List.copyOf(children());
        if (((ScreenRenderablesAccessor) this).rodel$renderables().stream().anyMatch(item -> !original.contains(item))) {
            org.slf4j.LoggerFactory.getLogger("Rodel UI").info("Keeping native title page with additional renderables");
            return;
        }
        TitleMenu candidate = new TitleMenu(minecraft, width, height);
        if (candidate.layout(original, COPYRIGHT_TEXT) == null) {
            org.slf4j.LoggerFactory.getLogger("Rodel UI").info("Keeping native title page with unsupported controls or insufficient layout space");
            return;
        }
        rodel$copyright = original.stream().filter(PlainTextButton.class::isInstance).map(Button.class::cast).toList();
        for (Button copyright : rodel$copyright) removeWidget(copyright);
        addRenderableWidget(candidate.themeButton());
        rodel$menu = candidate;
        rodel$expectedChildren = List.copyOf(children());
        rodel$expectedRenderables = List.copyOf(((ScreenRenderablesAccessor) this).rodel$renderables());
    }

    @Override
    public MenuContext rodel$context() {
        return rodel$menu;
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                              float partialTick, CallbackInfo callback) {
        rodel$updateLayout();
        if (rodel$menu == null) {
            return;
        }
        if (!rodel$menu.highContrast()) {
            extractPanorama(graphics, partialTick);
        }
        rodel$menu.extract(graphics, mouseX, mouseY, partialTick);
        callback.cancel();
    }

    @Inject(method = "mouseClicked", at = @At("HEAD"), cancellable = true)
    private void rodel$click(MouseButtonEvent event, boolean doubleClick,
                             CallbackInfoReturnable<Boolean> callback) {
        // Hidden vanilla Realms overlays must not retain invisible click targets.
        rodel$updateLayout();
        if (rodel$menu != null) callback.setReturnValue(super.mouseClicked(event, doubleClick));
    }
}
