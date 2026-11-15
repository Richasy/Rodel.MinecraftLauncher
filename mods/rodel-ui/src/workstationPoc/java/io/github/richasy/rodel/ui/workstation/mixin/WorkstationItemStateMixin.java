package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationItemState;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.renderer.state.gui.GuiItemRenderState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GuiItemRenderState.class)
abstract class WorkstationItemStateMixin implements WorkstationItemState {
    @Unique private boolean rodel$compactIcon;

    @Inject(method = "<init>", at = @At("RETURN"))
    private void rodel$rememberIconSampling(CallbackInfo ci) {
        rodel$compactIcon = WorkstationSurface.extractingSmallItem();
    }

    /** {@inheritDoc} */
    @Override
    public boolean rodel$isCompactIcon() {
        return rodel$compactIcon;
    }
}
