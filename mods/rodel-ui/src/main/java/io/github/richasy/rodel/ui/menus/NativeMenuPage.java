package io.github.richasy.rodel.ui.menus;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.WeakHashMap;
import java.util.List;
import java.util.Map;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuActionStyle;
import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import io.github.richasy.rodel.ui.settings.SettingsText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.client.gui.components.events.ContainerEventHandler;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

/**
 * A retained native viewport: the actual Screen continues rendering its lists, warnings,
 * previews and progress, and owns all initialization, tasks, callbacks and validation.
 */
public class NativeMenuPage implements NativeFormContext {
    private final Screen screen;
    private final MenuContext appearance;
    private final Map<AbstractWidget, Boolean> styled = new WeakHashMap<>();
    private final Map<AbstractWidget, Boolean> externalCaptions = new WeakHashMap<>();
    private float canvasScale = 1;
    private boolean rendering;
    private int nativeTextDepth;
    private boolean posePushed;
    private boolean inputPushed;
    private final SettingsChoiceList choices = new SettingsChoiceList(this);
    private Component trimmedTooltip;
    private static final int INSET = 12;

    public NativeMenuPage(Screen screen, MenuContext appearance) {
        this.screen = screen;
        this.appearance = appearance instanceof NativeMenuPage page ? page.appearance : appearance;
    }

    public static NativeMenuPage get(Screen screen) {
        return screen instanceof MenuPageHost host ? host.rodel$menuPage() : null;
    }

    public Screen screen() { return screen; }
    @Override public float scale() { return .5f; }
    @Override public boolean compact() { return screen.width < 500; }
    @Override public MenuTheme theme() { return appearance.theme(); }
    public boolean rendering() { return rendering; }
    public boolean stylingText() { return rendering && nativeTextDepth == 0; }
    public void beginNativeText() { nativeTextDepth++; }
    public void endNativeText() { nativeTextDepth--; }
    public float canvasScale() { return canvasScale; }
    public SettingsChoiceList choices() { return choices; }
    @Override public int p(float value) { return Math.round(value * scale()); }
    @Override public int controlTextSize() { return density().body() * 2; }
    public Component caption(AbstractWidget widget) {
        return widget instanceof NativeOptionPresentation option ? option.rodel$caption() : widget.getMessage();
    }
    public void externalCaption(AbstractWidget widget, boolean visible) {
        if (visible) externalCaptions.put(widget, true);
        else externalCaptions.remove(widget);
    }
    @Override public Component controlText(AbstractWidget widget) {
        if (NetworkMenuLayouts.worldOptionCaption(screen, widget) != null)
            return SettingsText.value(widget);
        return externalCaptions.containsKey(widget) && widget instanceof NativeOptionPresentation option
            ? option.rodel$valueText() : widget.getMessage();
    }
    @Override public Component fitValue(AbstractWidget widget, Component full, float available) {
        return fitLabel(full, available, widget.isHovered() || widget.isFocused() && focusVisible());
    }
    @Override public Component fitLabel(Component full, float available, boolean tooltip) {
        var font = Minecraft.getInstance().font;
        if (font.width(full) <= available) return full;
        if (tooltip) contextualTooltip(full);
        return MenuText.styled(Component.literal(font.substrByWidth(full,
            Math.max(1, (int) available - font.width("…"))).getString() + "…"));
    }
    @Override public void contextualTooltip(Component full) { trimmedTooltip = full; }
    @Override public void openChoices(AbstractWidget widget) { choices.open(widget, screen.width, screen.height); }
    @Override public void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                                    int thumbHeight, double scroll, int maxScroll) {
        if (maxScroll <= 0) return;
        int thumb = y + (int) (scroll * (height - thumbHeight) / maxScroll);
        graphics.fill(x, thumb, x + width, thumb + thumbHeight, theme().muted());
    }
    public void overlay(GuiGraphicsExtractor graphics, int x, int y, float tick) {
        choices.overlay(graphics, x, y, tick);
        if (!choices.visible && trimmedTooltip != null) graphics.setTooltipForNextFrame(trimmedTooltip, x, y);
    }
    public static boolean plainButton(AbstractWidget widget) {
        String name = widget.getClass().getName();
        return widget instanceof CycleButton<?> || widget.getHeight() > 12
            && (widget.getClass() == Button.class || name.equals("net.minecraft.client.gui.components.Button$Plain"));
    }
    public static boolean iconOnly(AbstractWidget widget) {
        String name = widget.getClass().getName();
        return name.equals("net.minecraft.client.gui.components.SpriteIconButton$CenteredIcon")
            || name.equals("com.mojang.realmsclient.gui.RealmsHeader$NotificationButton")
            || name.equals("net.minecraft.client.gui.components.LockIconButton")
            || widget instanceof net.minecraft.client.gui.components.ImageButton;
    }
    public double coordinate(double coordinate) { return coordinate / canvasScale - INSET; }
    public double distance(double delta) { return delta / canvasScale; }
    public MouseButtonEvent mouse(MouseButtonEvent event) {
        return new MouseButtonEvent(coordinate(event.x()), coordinate(event.y()), event.buttonInfo());
    }

    public void prepare() {
        var window = Minecraft.getInstance().getWindow();
        int width = window.getGuiScaledWidth();
        int height = window.getGuiScaledHeight();
        canvasScale = MenuContext.designScale() * 2;
        screen.width = Math.round(width / canvasScale) - INSET * 2;
        screen.height = Math.round(height / canvasScale) - INSET * 2;
    }

    public void initialized() {
        styled.clear();
        style(screen);
        if (NetworkMenuLayouts.layout(this)) return;
        if (screen.getClass().getName().endsWith(".JoinMultiplayerScreen")) multiplayerLayout();
        else {
            List<AbstractSelectionList<?>> lists = new ArrayList<>();
            collectLists(screen, lists);
            for (AbstractSelectionList<?> list : lists) {
                if (list.getWidth() <= screen.width - 40) continue;
                double scroll = list.scrollAmount();
                list.updateSizeAndPosition(screen.width - 48, list.getHeight(), 24, list.getY());
                list.setScrollAmount(scroll);
            }
        }
    }

    private void multiplayerLayout() {
        List<Button> actions = screen.children().stream().filter(Button.class::isInstance)
            .map(Button.class::cast).filter(widget -> widget.visible).toList();
        var rows = actions.stream().map(Button::getY).distinct().sorted().toList();
        List<List<Button>> groups = rows.stream().map(y -> actions.stream().filter(button -> button.getY() == y)
            .sorted(Comparator.comparingInt(Button::getX)).toList()).toList();
        int footer = rows.size() * 28 + 16;
        for (int row = 0; row < rows.size(); row++) {
            List<Button> buttons = groups.get(row);
            int available = Math.min(440, screen.width - 48);
            int width = (available - (buttons.size() - 1) * 6) / Math.max(1, buttons.size());
            int left = (screen.width - available) / 2;
            for (int i = 0; i < buttons.size(); i++)
                buttons.get(i).setRectangle(width, 24, left + i * (width + 6), screen.height - footer + row * 28);
        }
        for (GuiEventListener child : screen.children()) {
            if (child instanceof AbstractSelectionList<?> list) {
                double scroll = list.scrollAmount();
                list.updateSizeAndPosition(Math.min(560, screen.width - 48),
                    Math.max(32, screen.height - footer - 66), Math.max(24, (screen.width - 560) / 2), 52);
                list.setScrollAmount(scroll);
            }
        }
    }

    protected void style(GuiEventListener listener) {
        if (listener instanceof AbstractWidget widget && widget instanceof ThemedButton themed
            && plainButton(widget) && styled.put(widget, true) == null) {
            // Use the native label and native active state, including service permission restrictions.
            themed.rodel$setStyle(this, null, false, widget.getWidth() <= 24 && widget.getMessage().getString().isBlank(), false);
        }
        if (listener instanceof ContainerEventHandler container)
            for (GuiEventListener child : List.copyOf(container.children())) style(child);
        if (listener == screen && MenuActionStyle.requiresPrimaryAction(screen))
            MenuActionStyle.apply(this, screen, screen.children().stream().filter(Button.class::isInstance).map(Button.class::cast).toList());
    }

    public void begin(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        rendering = true;
        trimmedTooltip = null;
        style(screen);
        graphics.pose().pushMatrix();
        posePushed = true;
        graphics.pose().scale(canvasScale);
        UiInputGeometry.push(canvasScale, INSET * canvasScale, INSET * canvasScale);
        inputPushed = true;
        backdrop(graphics, partialTick);
        graphics.pose().translate(INSET, INSET);
        panels(graphics);
    }

    protected void backdrop(GuiGraphicsExtractor graphics, float partialTick) {
        var backdrop = (UiBackdrop) screen;
        if (Minecraft.getInstance().level != null) {
            backdrop.rodel$worldBackdrop(graphics, theme(), fullWidth(), fullHeight());
        } else {
            backdrop.rodel$backdrop(graphics, theme(), fullWidth(), fullHeight(), partialTick);
        }
    }

    protected int fullWidth() { return screen.width + INSET * 2; }
    protected int fullHeight() { return screen.height + INSET * 2; }

    protected void panels(GuiGraphicsExtractor graphics) {
        if (NetworkMenuLayouts.panels(this, graphics)) return;
        String type = screen.getClass().getSimpleName();
        if (type.equals("FriendsOverlayScreen") || type.equals("FriendsListConfirmScreen")) {
            int width = Math.min(344, screen.width - 32);
            theme().panel(graphics, (screen.width - width) / 2, 12, width, screen.height - 24);
            return;
        }
        List<AbstractSelectionList<?>> lists = new ArrayList<>();
        collectLists(screen, lists);
        for (AbstractSelectionList<?> list : lists) {
            if (list.visible)
                theme().panel(graphics, list.getX() - 4, list.getY() - 4, list.getWidth() + 8, list.getHeight() + 8);
        }
        if (!lists.isEmpty()) return;
        if (type.equals("PauseScreen") || type.equals("WorldOptionsScreen")
            || type.equals("ManageServerScreen") || type.equals("DirectJoinServerScreen")
            || type.equals("ConnectScreen") || type.equals("DisconnectedScreen") || type.equals("PopupScreen")) {
            int width = Math.min(type.equals("PauseScreen") || type.equals("WorldOptionsScreen") ? 340 : 360, screen.width - 32);
            int top = type.equals("PauseScreen") ? 56 : 44;
            theme().panel(graphics, (screen.width - width) / 2, top, width, Math.max(24, screen.height - top - 20));
            return;
        }
        int left = screen.width;
        int right = 0;
        int top = screen.height;
        int bottom = 0;
        for (GuiEventListener child : screen.children()) {
            if (!(child instanceof AbstractWidget widget) || !widget.visible
                || widget.getY() < 44 || widget.getBottom() > screen.height - 32) continue;
            left = Math.min(left, widget.getX());
            right = Math.max(right, widget.getRight());
            top = Math.min(top, widget.getY());
            bottom = Math.max(bottom, widget.getBottom());
        }
        if (right > left && bottom > top)
            theme().panel(graphics, left - 12, top - 12, right - left + 24, bottom - top + 24);
    }

    private void collectLists(GuiEventListener listener, List<AbstractSelectionList<?>> lists) {
        if (listener instanceof AbstractSelectionList<?> list) {
            lists.add(list);
            return;
        }
        if (listener instanceof ContainerEventHandler container)
            for (GuiEventListener child : container.children()) collectLists(child, lists);
    }

    public void end(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        try {
            if (inputPushed) UiInputGeometry.pop();
        } finally {
            inputPushed = false;
            try {
                if (posePushed) graphics.pose().popMatrix();
            } finally {
                posePushed = false;
                rendering = false;
            }
        }
    }

    public int ink(int original) {
        int rgb = original & 0xFFFFFF;
        int alpha = original & 0xFF000000;
        if (screen.getClass().getName().startsWith("net.minecraft.client.gui.screens.reporting.")
            && (rgb == 0xFFFF55 || rgb == 0xFFFF00)) return alpha | theme().focus() & 0xFFFFFF;
        if (rgb == 0xFFFFFF || rgb == 0xE0E0E0) return alpha | theme().ink() & 0xFFFFFF;
        if (secondaryInk(original))
            return alpha | theme().muted() & 0xFFFFFF;
        return original;
    }

    public static boolean secondaryInk(int color) {
        int rgb = color & 0xFFFFFF;
        return rgb == 0xA0A0A0 || rgb == 0x808080 || rgb == 0xAAAAAA || rgb == 0x707070 || rgb == 0x555555;
    }
}
