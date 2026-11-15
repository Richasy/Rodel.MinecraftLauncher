package io.github.richasy.rodel.ui.mixin;

import java.util.Map;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.mojang.blaze3d.platform.Window;
import io.github.richasy.rodel.ui.menus.AdvancementsMenuAccess;
import io.github.richasy.rodel.ui.menus.AdvancementsMenuPage;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.client.MouseHandler;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.advancements.AdvancementsScreen;
import net.minecraft.client.gui.screens.advancements.AdvancementTab;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.multiplayer.ClientAdvancements;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AdvancementsScreen.class)
public abstract class MenuAdvancementsMixin extends Screen implements AdvancementsMenuAccess {
    @Shadow @Final private Map<AdvancementHolder, AdvancementTab> tabs;
    @Shadow @Final private ClientAdvancements advancements;
    @Shadow private AdvancementTab selectedTab;
    @Shadow private int leftPos;
    @Shadow private int topPos;
    @Shadow private boolean isScrolling;
    @Shadow @Final private static Component NO_ADVANCEMENTS_LABEL;

    protected MenuAdvancementsMixin(Component title) { super(title); }
    @Override public Map<AdvancementHolder, AdvancementTab> rodel$tabs() { return tabs; }
    @Override public AdvancementTab rodel$selectedTab() { return selectedTab; }
    @Override public ClientAdvancements rodel$advancements() { return advancements; }
    @Override public Component rodel$emptyLabel() { return NO_ADVANCEMENTS_LABEL; }
    @Override public void rodel$origin(int left, int top) { leftPos = left; topPos = top; }

    @Inject(method = {"repositionElements", "onAdvancementsUpdated"}, at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        if (NativeMenuPage.get(this) instanceof AdvancementsMenuPage page) page.initialized();
    }

    @Inject(method = "extractInside", at = @At("HEAD"), cancellable = true)
    private void rodel$empty(GuiGraphicsExtractor graphics, CallbackInfo callback) {
        if (selectedTab == null && NativeMenuPage.get(this) instanceof AdvancementsMenuPage page) {
            page.empty(graphics);
            callback.cancel();
        }
    }

    @Inject(method = "extractWindow", at = @At("HEAD"), cancellable = true)
    private void rodel$noVanillaFrame(GuiGraphicsExtractor graphics, int x, int y, CallbackInfo callback) {
        if (NativeMenuPage.get(this) instanceof AdvancementsMenuPage) callback.cancel();
    }

    @Inject(method = "extractTooltips", at = @At("HEAD"), cancellable = true)
    private void rodel$tooltips(GuiGraphicsExtractor graphics, int x, int y, CallbackInfo callback) {
        if (NativeMenuPage.get(this) instanceof AdvancementsMenuPage page) {
            page.tooltips(graphics);
            callback.cancel();
        }
    }

    @Inject(method = "mouseClicked", at = @At("HEAD"), cancellable = true)
    private void rodel$navigation(MouseButtonEvent event, boolean doubleClick, CallbackInfoReturnable<Boolean> callback) {
        if (NativeMenuPage.get(this) instanceof AdvancementsMenuPage)
            callback.setReturnValue(super.mouseClicked(event, doubleClick));
    }

    @Inject(method = "mouseDragged", at = @At("HEAD"), cancellable = true)
    private void rodel$drag(MouseButtonEvent event, double x, double y, CallbackInfoReturnable<Boolean> callback) {
        if (NativeMenuPage.get(this) instanceof AdvancementsMenuPage page
            && !isScrolling && !page.containsTree(event.x(), event.y())) callback.setReturnValue(false);
    }

    @Inject(method = "mouseScrolled", at = @At("HEAD"), cancellable = true)
    private void rodel$scroll(double x, double y, double horizontal, double vertical, CallbackInfoReturnable<Boolean> callback) {
        if (NativeMenuPage.get(this) instanceof AdvancementsMenuPage page && !page.containsTree(x, y))
            callback.setReturnValue(super.mouseScrolled(x, y, horizontal, vertical));
    }

    @WrapOperation(method = "tick", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/MouseHandler;getScaledXPos(Lcom/mojang/blaze3d/platform/Window;)D"))
    private double rodel$mouseX(MouseHandler mouse, Window window, Operation<Double> original) {
        double value = original.call(mouse, window);
        var page = NativeMenuPage.get(this);
        return page == null ? value : page.coordinate(value);
    }

    @WrapOperation(method = "tick", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/MouseHandler;getScaledYPos(Lcom/mojang/blaze3d/platform/Window;)D"))
    private double rodel$mouseY(MouseHandler mouse, Window window, Operation<Double> original) {
        double value = original.call(mouse, window);
        var page = NativeMenuPage.get(this);
        return page == null ? value : page.coordinate(value);
    }
}
