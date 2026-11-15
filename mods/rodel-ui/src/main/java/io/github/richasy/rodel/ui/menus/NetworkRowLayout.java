package io.github.richasy.rodel.ui.menus;

import java.util.List;
import java.util.ArrayList;
import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractContainerWidget;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.FocusableTextWidget;
import net.minecraft.client.gui.components.PlayerFaceWidget;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.layouts.LayoutElement;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.network.chat.Component;

/** Row geometry shared by the native friends and Realms presentations. */
public final class NetworkRowLayout {
    private NetworkRowLayout() {}

    public static int textWidth(Component text, int size) {
        var font = Minecraft.getInstance().font;
        return (int) Math.ceil(font.width(MenuText.secondary(text)) * size / (float) font.lineHeight);
    }

    public static void text(NativeMenuPage page, GuiGraphicsExtractor graphics, Component text,
                            int x, int y, int width, int size, int color, int mouseX, int mouseY) {
        if (width <= 0 || size <= 0) return;
        var font = Minecraft.getInstance().font;
        Component styled = MenuText.themedSecondary(text, color);
        Component fitted = MenuText.themedSecondary(page.fitLabel(styled, Math.max(0, width) * font.lineHeight / (float) size, false), color);
        page.beginNativeText();
        try {
            MenuText.extract(graphics, font, fitted, x, y, size, color);
        } finally {
            page.endNativeText();
        }
        if (!fitted.getString().equals(styled.getString()) && mouseX >= x && mouseX < x + width
            && mouseY >= y && mouseY < y + size) page.contextualTooltip(text);
    }

    /** Keeps the native pending-request headings and rows in one compact, left-aligned column. */
    public static boolean arrangeFriendSections(NativeMenuPage page, LinearLayout content, int width) {
        var elements = new ArrayList<LayoutElement>();
        content.visitChildren(elements::add);
        if (elements.stream().noneMatch(FocusableTextWidget.class::isInstance)) return false;
        content.removeChildren();
        content.spacing(0);
        content.defaultCellSetting().padding(0).alignHorizontallyLeft();
        boolean afterHeading = false;
        for (int i = 0; i < elements.size(); i++) {
            LayoutElement element = elements.get(i);
            boolean heading = element instanceof FocusableTextWidget;
            if (heading) ((FocusableTextWidget) element).setSize(width, page.density().body());
            // The row's own vertical inset supplies the gap directly below its heading.
            int top = i == 0 || afterHeading ? 0 : page.density().gap();
            content.addChild(element, settings -> settings.paddingTop(top));
            afterHeading = heading;
        }
        content.arrangeElements();
        return true;
    }

    public static boolean drawFriendSection(NativeMenuPage page, FocusableTextWidget widget,
                                             GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        if (!(page.screen() instanceof NetworkMenuLayouts.Friends friends)
            || !(friends.rodel$tabs().getCurrentTab() instanceof NetworkMenuLayouts.FriendTab tab)) return false;
        var headings = new ArrayList<LayoutElement>();
        tab.rodel$entries().visitChildren(element -> {
            if (element instanceof FocusableTextWidget) headings.add(element);
        });
        if (!headings.contains(widget)) return false;
        Component label = widget.getMessage().copy().withStyle(style -> style.withUnderlined(false));
        text(page, graphics, label, widget.getX(), widget.getY(), widget.getWidth(), page.density().body(),
            page.theme().muted(), mouseX, mouseY);
        if (widget.isFocused() && page.focusVisible())
            graphics.outline(widget.getX(), widget.getY(), widget.getWidth(), widget.getHeight(), page.theme().focus());
        return true;
    }

    public static void arrangeFriend(NativeMenuPage page, AbstractWidget row) {
        if (!(row instanceof AbstractContainerWidget container)) return;
        var density = page.density();
        row.setHeight(density.row());
        List<AbstractWidget> children = container.children().stream().filter(AbstractWidget.class::isInstance)
            .map(AbstractWidget.class::cast).toList();
        List<Button> buttons = children.stream().filter(Button.class::isInstance).map(Button.class::cast).toList();
        int actionLeft = row.getRight() - buttons.size() * density.control() - Math.max(0, buttons.size() - 1) * density.gap();
        for (int i = 0; i < buttons.size(); i++) buttons.get(i).setRectangle(density.control(), density.control(),
            actionLeft + i * (density.control() + density.gap()), row.getY() + (row.getHeight() - density.control()) / 2);
        for (AbstractWidget child : children) if (child instanceof PlayerFaceWidget)
            child.setRectangle(density.icon(), density.icon(), row.getX(), row.getY() + (row.getHeight() - density.icon()) / 2);
        List<StringWidget> labels = children.stream().filter(StringWidget.class::isInstance).map(StringWidget.class::cast).toList();
        int textHeight = density.body() + (labels.size() > 1 ? density.gap() / 2 + density.detail() : 0);
        int y = row.getY() + (row.getHeight() - textHeight) / 2;
        int x = row.getX() + density.icon() + density.gap();
        int width = Math.max(0, (buttons.isEmpty() ? row.getRight() : actionLeft - density.gap()) - x);
        for (int i = 0; i < labels.size(); i++) {
            int size = i == 0 ? density.body() : density.detail();
            // StringWidget otherwise returns its native-font cached maxWidth instead of this GUI rectangle.
            labels.get(i).setMaxWidth(0);
            labels.get(i).setRectangle(width, size, x, y);
            y += size + density.gap() / 2;
        }
    }

    public static void drawFriend(NativeMenuPage page, AbstractContainerWidget row, GuiGraphicsExtractor graphics,
                                   int mouseX, int mouseY, float tick) {
        arrangeFriend(page, row);
        if (page.theme().highContrast()) {
            graphics.fill(row.getX(), row.getY(), row.getRight(), row.getBottom(), page.theme().surface());
            graphics.outline(row.getX(), row.getY(), row.getWidth(), row.getHeight(), page.theme().edge());
        }
        int labelIndex = 0;
        for (var child : row.children()) if (child instanceof AbstractWidget widget) {
            if (!widget.visible) continue;
            if (widget instanceof StringWidget) {
                int color = labelIndex++ == 0 ? page.theme().ink() : page.theme().muted();
                text(page, graphics, widget.getMessage(), widget.getX(), widget.getY(), widget.getWidth(),
                    widget.getHeight(), color, mouseX, mouseY);
            } else widget.extractRenderState(graphics, mouseX, mouseY, tick);
        }
    }
}
