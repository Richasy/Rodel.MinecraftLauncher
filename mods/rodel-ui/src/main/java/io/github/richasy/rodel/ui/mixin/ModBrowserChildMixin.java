package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import com.terraformersmc.modmenu.gui.widget.entries.ModListEntry;
import com.terraformersmc.modmenu.util.mod.Mod;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import io.github.richasy.rodel.ui.compat.ModBrowserRow;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.widget.entries.ChildEntry", remap = false)
public abstract class ModBrowserChildMixin extends ModListEntry implements ModBrowserRow.Child {
    @Shadow(remap = false) @Final private boolean bottomChild;

    protected ModBrowserChildMixin(Mod mod, ModListWidget list) {
        super(mod, list);
    }

    @Override
    public boolean rodel$isBottomChild() { return bottomChild; }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$drawChild(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered,
                                float partialTick, CallbackInfo callback) {
        var browser = ((ModBrowserList) list).rodel$browser();
        if (browser == null) return;
        ModBrowserRow.measure(this, browser).draw(graphics, this, browser, mouseX, mouseY, hovered);
        callback.cancel();
    }
}
