package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import net.minecraft.client.gui.components.Renderable;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.narration.NarratableEntry;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.gen.Invoker;

@Mixin(Screen.class)
public interface ScreenRenderablesAccessor {
    @Accessor("renderables")
    List<Renderable> rodel$renderables();

    @Accessor("children")
    List<GuiEventListener> rodel$children();

    @Accessor("narratables")
    List<NarratableEntry> rodel$narratables();

    @Invoker("removeWidget")
    void rodel$removeWidget(GuiEventListener widget);
}
