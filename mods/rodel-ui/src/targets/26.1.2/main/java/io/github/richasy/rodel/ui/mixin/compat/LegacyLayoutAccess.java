package io.github.richasy.rodel.ui.mixin.compat;

import java.util.List;
import net.minecraft.client.gui.layouts.GridLayout;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.layouts.FrameLayout;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.tabs.TabNavigationBar;
import net.minecraft.client.gui.components.tabs.GridLayoutTab;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Exact 26.1 layout fields, before Minecraft exposed clearing and tab geometry publicly. */
public final class LegacyLayoutAccess {
    @Mixin(GridLayout.class) public interface Grid {
        @Accessor("children") List<?> rodel$children();
    }
    @Mixin(LinearLayout.class) public interface Linear {
        @Accessor("wrapped") GridLayout rodel$wrapped();
        @Accessor("nextChildIndex") void rodel$next(int value);
    }
    @Mixin(FrameLayout.class) public interface Frame {
        @Accessor("children") List<?> rodel$children();
    }
    @Mixin(TabNavigationBar.class) public interface Bar {
        @Accessor("layout") LinearLayout rodel$layout();
    }
    @Mixin(GridLayoutTab.class) public interface Tab {
        @Accessor("layout") GridLayout rodel$layout();
    }
    @Mixin(ScrollableLayout.class) public interface Scroll {
        @Mutable @Accessor("scrollbarSpacing") void rodel$spacing(int value);
    }
    @Mixin(targets = "net.minecraft.client.gui.components.ScrollableLayout$Container") public interface Container {
        @Accessor("children") List<AbstractWidget> rodel$children();
    }
}
