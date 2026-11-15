package io.github.richasy.rodel.ui;

import java.util.ArrayDeque;
import java.util.Deque;
import io.github.richasy.rodel.ui.mixin.GuiPreeditAccessor;
import net.minecraft.client.gui.GuiGraphicsExtractor;

public final class UiInputGeometry {
    private static final ThreadLocal<Deque<Transform>> TRANSFORMS = new ThreadLocal<>();
    private record Transform(float scale, float x, float y) {}

    private UiInputGeometry() {}

    public static void push(float scale, float x, float y) {
        Deque<Transform> stack = TRANSFORMS.get();
        if (stack == null) {
            stack = new ArrayDeque<>();
            TRANSFORMS.set(stack);
        }
        Transform parent = stack.peek();
        stack.push(parent == null ? new Transform(scale, x, y)
            : new Transform(parent.scale * scale, parent.x + parent.scale * x, parent.y + parent.scale * y));
    }

    public static void pop() {
        Deque<Transform> stack = TRANSFORMS.get();
        if (stack == null || stack.isEmpty()) throw new IllegalStateException("No UI input transform to restore");
        stack.pop();
        if (stack.isEmpty()) TRANSFORMS.remove();
    }

    public static int x(int value) {
        Deque<Transform> stack = TRANSFORMS.get();
        return stack == null ? value : Math.round(stack.peek().x + value * stack.peek().scale);
    }

    public static int y(int value) {
        Deque<Transform> stack = TRANSFORMS.get();
        return stack == null ? value : Math.round(stack.peek().y + value * stack.peek().scale);
    }

    public static void flushPreedit(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        GuiPreeditAccessor access = (GuiPreeditAccessor) graphics;
        var overlay = access.rodel$preedit();
        if (overlay == null) return;
        access.rodel$preedit(null);
        graphics.nextStratum();
        overlay.extractRenderState(graphics, mouseX, mouseY, partialTick);
    }
}
