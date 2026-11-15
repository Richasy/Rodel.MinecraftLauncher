package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeSettingsRow;
import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.screens.packs.PackSelectionModel;
import net.minecraft.client.input.MouseButtonEvent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.packs.TransferableSelectionList$PackEntry")
public abstract class PackEntrySettingsMixin implements NativeSettingsRow {
    @Shadow @Final private PackSelectionModel.Entry pack;

    @Override public int rodel$height(NativeSettingsPage page) { return page.density().row(); }

    private NativeFormContext rodel$context() {
        var screen = Minecraft.getInstance().gui.screen();
        var page = NativeSettingsPage.get(screen);
        if (page != null && page.ownsEntry(this)) return page;
        var world = WorldSubpage.get(screen);
        return world != null && world.ownsPackEntry(this) ? world : null;
    }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$packRow(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                               boolean hovered, float partialTick, CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.ownsEntry(this)) {
            page.packRow(graphics, (AbstractSelectionList.Entry<?>) (Object) this, pack, hovered, mouseX, mouseY);
            callback.cancel();
        } else if (rodel$context() instanceof WorldSubpage world) {
            world.packs().row(graphics, (AbstractSelectionList.Entry<?>) (Object) this, pack, hovered, mouseX, mouseY);
            callback.cancel();
        }
    }

    @ModifyVariable(method = "mouseClicked", at = @At("HEAD"), argsOnly = true)
    private MouseButtonEvent rodel$packIconHit(MouseButtonEvent event) {
        NativeFormContext page = rodel$context();
        if (page == null) return event;
        var entry = (AbstractSelectionList.Entry<?>) (Object) this;
        double x = event.x() - entry.getContentX() - page.density().inset();
        double y = event.y() - entry.getY() - page.density().inset();
        int icon = page.density().icon();
        if (x >= 0 && y >= 0 && x < icon && y < icon) {
            return new MouseButtonEvent(entry.getContentX() + x * 32 / icon,
                entry.getContentY() + y * 32 / icon, event.buttonInfo());
        }
        return new MouseButtonEvent(entry.getContentX() + 32, event.y(), event.buttonInfo());
    }
}
