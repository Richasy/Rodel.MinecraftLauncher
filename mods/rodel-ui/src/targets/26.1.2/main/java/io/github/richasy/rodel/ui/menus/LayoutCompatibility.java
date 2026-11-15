package io.github.richasy.rodel.ui.menus;

import io.github.richasy.rodel.ui.mixin.compat.LegacyLayoutAccess;
import net.minecraft.client.gui.layouts.Layout;
import net.minecraft.client.gui.layouts.GridLayout;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.layouts.FrameLayout;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.components.tabs.TabNavigationBar;

/** Reorganizes the existing 26.1 native controls without replacing their callbacks or data. */
public final class LayoutCompatibility {
    /** 26.1 snapshots scroll children at construction; refresh only the layouts composed by Rodel. */
    public static void arrange(ScrollableLayout scroll) {
        scroll.arrangeElements();
        scroll.visitWidgets(widget -> {
            var children = ((LegacyLayoutAccess.Container) widget).rodel$children();
            children.clear();
            ((NetworkMenuLayouts.ScrollContent) scroll).rodel$content().visitWidgets(children::add);
        });
    }
    public static void clear(Layout layout) {
        if (layout instanceof LinearLayout) {
            var access = (LegacyLayoutAccess.Linear) layout;
            clear(access.rodel$wrapped());
            access.rodel$next(0);
        } else if (layout instanceof GridLayout) ((LegacyLayoutAccess.Grid) layout).rodel$children().clear();
        else if (layout instanceof FrameLayout) ((LegacyLayoutAccess.Frame) layout).rodel$children().clear();
        else throw new IllegalStateException("Unexpected native layout: " + layout.getClass().getName());
    }

    public static void tabs(TabNavigationBar bar, int width, int height, int x, int y) {
        bar.updateWidth(width);
        bar.children().forEach(child -> ((AbstractWidget) child).setHeight(height));
        bar.arrangeElements();
        ((LegacyLayoutAccess.Bar) bar).rodel$layout().setPosition(x, y);
    }
}
