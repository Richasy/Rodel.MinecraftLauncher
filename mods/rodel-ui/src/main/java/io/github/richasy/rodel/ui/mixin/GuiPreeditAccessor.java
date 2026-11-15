package io.github.richasy.rodel.ui.mixin;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Renderable;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(GuiGraphicsExtractor.class)
public interface GuiPreeditAccessor {
    @Accessor("preeditOverlay") Renderable rodel$preedit();
    @Accessor("preeditOverlay") void rodel$preedit(Renderable overlay);
}
