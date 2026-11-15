package io.github.richasy.rodel.ui.platform;

import java.util.*;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.screens.Screen;
import io.github.richasy.rodel.ui.mixin.ScreenRenderablesAccessor;

/** A live view of the native screen's widgets, keeping input, rendering and narration membership together. */
public final class NativeWidgets {
    private NativeWidgets() { }

    public static List<AbstractWidget> get(Screen screen) {
        ScreenRenderablesAccessor access = (ScreenRenderablesAccessor) screen;
        return new AbstractList<>() {
            private List<AbstractWidget> widgets() {
                return access.rodel$children().stream().filter(AbstractWidget.class::isInstance).map(AbstractWidget.class::cast).toList();
            }
            @Override public AbstractWidget get(int index) { return widgets().get(index); }
            @Override public int size() { return widgets().size(); }
            @Override public void add(int index, AbstractWidget widget) {
                Objects.checkIndex(index, size() + 1);
                AbstractWidget following = index == size() ? null : get(index);
                int child = following == null ? access.rodel$children().size() : access.rodel$children().indexOf(following);
                int render = following == null ? -1 : access.rodel$renderables().indexOf(following);
                int narration = following == null ? -1 : access.rodel$narratables().indexOf(following);
                access.rodel$children().add(child, widget);
                access.rodel$renderables().add(render < 0 ? access.rodel$renderables().size() : render, widget);
                access.rodel$narratables().add(narration < 0 ? access.rodel$narratables().size() : narration, widget);
                modCount++;
            }
            @Override public AbstractWidget remove(int index) {
                AbstractWidget previous = get(index); access.rodel$removeWidget(previous); modCount++; return previous;
            }
            @Override public AbstractWidget set(int index, AbstractWidget widget) {
                AbstractWidget previous = remove(index); add(index, widget); return previous;
            }
        };
    }
}
