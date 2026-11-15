package io.github.richasy.rodel.ui.worlds;

import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.tabs.Tab;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import net.minecraft.client.gui.screens.worldselection.WorldSelectionList;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import org.slf4j.LoggerFactory;

public final class WorldPage implements WorldSurface {
    private final Screen screen;
    private final MenuContext appearance;
    private final SettingsChoiceList choices = new SettingsChoiceList(this);
    private final Map<Tab, WorldFormList> forms = new IdentityHashMap<>();
    private final WorldMoreNavigation moreNavigation;
    private WorldMoreSidebar sidebar;
    private List<AbstractWidget> actions = List.of();
    private WorldSelectionList worlds;
    private EditBox search;
    private NativeTextBox searchBox;
    private Button back;
    private Button primary;
    private Button newWorld;
    private WorldFormList form;
    private boolean initialized;
    private int selectedTab;
    private int actualWidth;
    private int actualHeight;
    private float canvasScale = 1;
    private int left;
    private int top;
    private int width;
    private int bottom;
    private Component trimmedTooltip;

    public WorldPage(Screen screen, MenuContext appearance) {
        this.screen = screen;
        this.appearance = appearance;
        moreNavigation = screen instanceof CreateWorldScreen create ? new WorldMoreNavigation(create, this) : null;
    }

    public static WorldPage get(Screen screen) {
        return WorldSurface.get(screen) instanceof WorldPage page ? page : null;
    }

    public boolean creating() { return screen instanceof CreateWorldScreen; }
    @Override public WorldMoreNavigation moreNavigation() { return moreNavigation; }
    public boolean moreActive() { return initialized && creating() && selectedTab == 2; }
    public void returnToTab(int tab) {
        selectedTab = tab;
        if (initialized && creating()) {
            ((WorldCreateAccess) screen).rodel$tabs().selectTab(tab, false);
            refreshTab();
        }
    }
    public boolean owns(Object list) { return list == worlds; }
    public SettingsChoiceList choices() { return choices; }

    public void prepare() {
        var window = Minecraft.getInstance().getWindow();
        actualWidth = window.getGuiScaledWidth();
        actualHeight = window.getGuiScaledHeight();
        canvasScale = MenuContext.designScale() * 2;
        screen.width = Math.round(actualWidth / canvasScale);
        screen.height = Math.round(actualHeight / canvasScale);
    }

    public void beginInit() {
        initialized = false;
        choices.close();
        forms.clear();
        prepare();
    }

    public void initialized() {
        List<AbstractWidget> original = List.copyOf(NativeWidgets.get(screen));
        actions = original.stream().filter(widget -> widget instanceof Button).toList();
        back = null;
        primary = null;
        newWorld = null;
        for (AbstractWidget widget : actions) {
            Button button = (Button) widget;
            ((ThemedButton) button).rodel$setStyle(this, null, false, false, false);
            if (SettingsText.hasKey(button.getMessage(), "gui.back") || SettingsText.hasKey(button.getMessage(), "gui.cancel")) {
                back = button;
                button.setMessage(Component.translatable("gui.back"));
                ((ThemedButton) button).rodel$setStyle(this, MenuIcon.BACK, false, true, false);
                button.setTooltip(Tooltip.create(Component.translatable(creating() ? "gui.back" : "rodel.worlds.back_menu")));
            } else if (SettingsText.hasKey(button.getMessage(), creating() ? "selectWorld.create" : "selectWorld.select")) {
                primary = button;
                ((ThemedButton) button).rodel$setStyle(this, creating() ? null : MenuIcon.PLAY, true, false, false);
            } else if (SettingsText.hasKey(button.getMessage(), "selectWorld.create")) {
                newWorld = button;
            }
        }
        if (back == null || primary == null) throw new IllegalStateException("Native world screen is missing its original actions");
        if (creating()) {
            for (Tab tab : ((WorldCreateAccess) screen).rodel$tabs().getTabs()) forms.put(tab, new WorldFormList(this, tab));
            moreNavigation.capture(((WorldCreateAccess) screen).rodel$tabs().getTabs().get(2));
            sidebar = new WorldMoreSidebar(this, moreNavigation);
        } else {
            worlds = original.stream().filter(WorldSelectionList.class::isInstance).map(WorldSelectionList.class::cast).findFirst().orElseThrow();
            search = original.stream().filter(EditBox.class::isInstance).map(EditBox.class::cast).findFirst().orElseThrow();
            searchBox = NativeTextBox.search(this, search);
        }
        initialized = true;
        if (creating()) {
            ((WorldCreateAccess) screen).rodel$tabs().selectTab(selectedTab, false);
            refreshTab();
        } else install();
        LoggerFactory.getLogger("Rodel UI").info("Native world page: {}", screen.getClass().getSimpleName());
    }

    public void refreshTab() {
        if (!initialized || !creating()) return;
        var access = (WorldCreateAccess) screen;
        Tab tab = access.rodel$tabManager().getCurrentTab();
        form = forms.get(tab);
        if (form == null) throw new IllegalStateException("Native world tab was not captured");
        selectedTab = access.rodel$tabs().getTabs().indexOf(tab);
        choices.close();
        install();
    }

    private void install() {
        var widgets = NativeWidgets.get(screen);
        widgets.clear();
        widgets.add(back);
        if (creating()) {
            widgets.add(((WorldCreateAccess) screen).rodel$tabs());
            if (moreActive()) widgets.addAll(sidebar.widgets());
            else {
                widgets.add(form);
                widgets.add(primary);
            }
        } else {
            widgets.add(searchBox);
            widgets.add(worlds);
            actions.stream().filter(widget -> widget != back).forEach(widgets::add);
        }
        widgets.add(choices);
        layout();
        if (creating()) {
            if (screen.getFocused() instanceof EditBox editor && form.focusEditor(screen, editor)) return;
            if (screen.getFocused() != null && !widgets.contains(screen.getFocused()))
                screen.setFocused(((WorldCreateAccess) screen).rodel$tabs());
        }
    }

    public void layout() {
        if (!initialized) return;
        var density = density();
        int margin = density.tight() ? 24 : compact() ? 16 : 36;
        width = Math.min(screen.width - margin * 2, moreActive() ? 620 : creating() ? 500 : 560);
        left = (screen.width - width) / 2;
        top = density.tight() ? 12 : compact() ? 14 : 24;
        int controlHeight = density.control();
        bottom = screen.height - top - controlHeight;
        back.setRectangle(controlHeight, controlHeight, left, top);
        int primaryWidth = density.tight() ? 68 : compact() ? 82 : 100;
        primary.setRectangle(primaryWidth, controlHeight, left + width - primaryWidth, bottom);
        int panelTop = top + controlHeight + 10;
        int panelHeight = bottom - density.gap() - panelTop;
        if (creating()) {
            var bar = ((WorldCreateAccess) screen).rodel$tabs();
            int tabWidth = screen.width < 360 ? 32 : 44;
            bar.setRectangle(tabWidth * 3, controlHeight, left + width - tabWidth * 3, top);
            int index = 0;
            for (var child : bar.children()) {
                ((AbstractWidget) child).setRectangle(tabWidth, controlHeight, bar.getX() + index++ * tabWidth, top);
            }
            if (moreActive()) sidebar.layout(left, panelTop, compact() ? 72 : 104);
            else form.resize(left, panelTop, width, panelHeight);
        } else {
            searchBox.setRectangle(width - 2 * density.inset(), controlHeight, left + density.inset(), panelTop + density.inset());
            double scroll = worlds.scrollAmount();
            // The first native row already owns its top padding and content inset.
            int listOffset = controlHeight + density.inset() + density.detail() + density.gap();
            worlds.updateSizeAndPosition(width - density.inset(), Math.max(20, panelHeight - listOffset - density.inset()),
                left + density.inset(), panelTop + listOffset);
            worlds.setScrollAmount(scroll);
            int createWidth = density.tight() ? 72 : 92;
            if (newWorld != null) newWorld.setRectangle(createWidth, controlHeight, left + width - createWidth, top);
            int x = left;
            List<AbstractWidget> ordered = actions.stream().sorted(java.util.Comparator.comparingInt(action ->
                SettingsText.hasKey(action.getMessage(), "selectWorld.delete") ? 2
                    : SettingsText.hasKey(action.getMessage(), "selectWorld.recreate") ? 1 : 0)).toList();
            for (AbstractWidget action : ordered) {
                if (action == back || action == primary || action == newWorld) continue;
                int actionWidth = density.tight() ? 40 : SettingsText.hasKey(action.getMessage(), "selectWorld.recreate") ? 68 : 46;
                action.setRectangle(actionWidth, controlHeight, x, bottom);
                x += actionWidth + 6;
            }
        }
    }

    public void render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        ((UiBackdrop) screen).rodel$backdrop(graphics, theme(), actualWidth, actualHeight, partialTick);
        graphics.pose().pushMatrix();
        graphics.pose().scale(canvasScale);
        UiInputGeometry.push(canvasScale, 0, 0);
        try {
            int x = Math.round(mouseX / canvasScale);
            int y = Math.round(mouseY / canvasScale);
            trimmedTooltip = null;
            if (form != null && creating() && !moreActive()) form.refresh();
            int panelLeft = moreActive() ? left + (compact() ? 80 : 118) : left;
            int panelTop = top + density().control() + 10;
            graphics.fill(panelLeft, panelTop, left + width, bottom - density().gap(), theme().surface());
            graphics.outline(panelLeft, panelTop, left + width - panelLeft, bottom - density().gap() - panelTop, theme().edge());
            int titleRight = creating() ? ((WorldCreateAccess) screen).rodel$tabs().getX()
                : newWorld == null ? left + width : newWorld.getX();
            int titleX = left + density().control() + 10;
            int headingSize = density().heading();
            Component title = fit(MenuText.styled(screen.getTitle()),
                Math.max(1, titleRight - titleX - 10) * Minecraft.getInstance().font.lineHeight / (float) headingSize,
                x >= titleX && x < titleRight && y >= top && y < top + density().control());
            text(graphics, title, titleX, top + (density().control() - headingSize) / 2f, headingSize, theme().ink(), false);
            if (!creating()) {
                long count = worlds.children().stream().filter(WorldSelectionList.WorldListEntry.class::isInstance).count();
                text(graphics, Component.translatable("rodel.worlds.count", count), searchBox.getX(),
                    searchBox.getBottom() + density().gap(), density().detail(), theme().muted(), true);
                if (worlds.children().isEmpty()) {
                    var font = Minecraft.getInstance().font;
                    int iconSize = density().tight() ? 18 : p(48);
                    int gap = p(16);
                    int textSize = density().body();
                    int lineHeight = textSize + 4;
                    float textScale = textSize / (float) font.lineHeight;
                    var lines = font.split(MenuText.secondary(Component.translatable("rodel.worlds.no_results")),
                        Math.max(1, Math.round((searchBox.getWidth() - p(32)) / textScale)));
                    int textHeight = textSize + Math.max(0, lines.size() - 1) * lineHeight;
                    float centerX = searchBox.getX() + searchBox.getWidth() / 2f;
                    float groupTop = worlds.getY() + (worlds.getHeight() - iconSize - gap - textHeight) / 2f;
                    MenuIcon.WORLD.extract(graphics, centerX - iconSize / 2f, groupTop, iconSize, theme().muted());
                    for (int i = 0; i < lines.size(); i++) {
                        graphics.pose().pushMatrix();
                        graphics.pose().translate(centerX - font.width(lines.get(i)) * textScale / 2,
                            groupTop + iconSize + gap + i * lineHeight);
                        graphics.pose().scale(textScale);
                        graphics.text(font, lines.get(i), 0, 0, theme().muted(), false);
                        graphics.pose().popMatrix();
                    }
                }
            }
            for (AbstractWidget widget : List.copyOf(NativeWidgets.get(screen))) widget.extractRenderState(graphics, x, y, partialTick);
            if (creating()) {
                var state = ((CreateWorldScreen) screen).getUiState();
                Component summary = Component.translatable("rodel.worlds.summary", state.getGameMode().displayName,
                    state.getDifficulty().getDisplayName());
                text(graphics, summary, left, bottom + 7, 7, theme().muted(), true);
            }
            choices.overlay(graphics, x, y, partialTick);
            if (trimmedTooltip != null && !choices.visible) graphics.setTooltipForNextFrame(trimmedTooltip, x, y);
            UiInputGeometry.flushPreedit(graphics, x, y, partialTick);
        } finally {
            UiInputGeometry.pop();
            graphics.pose().popMatrix();
        }
    }

    public void worldRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry,
                         Component name, Component info, Component lastPlayed, boolean hovered) {
        int x = entry.getContentX(), y = entry.getY(), rowWidth = entry.getContentWidth();
        if (worlds.getSelected() == entry) {
            graphics.fill(x, y, x + rowWidth, y + entry.getHeight(), theme().selection());
            graphics.outline(x, y, rowWidth, entry.getHeight(), theme().edge());
        }
        if (entry.isFocused() && focusVisible()) graphics.outline(x + 1, y + 1, rowWidth - 2, entry.getHeight() - 2, theme().focus());
        int textX = x + worldIconInset() + worldIconSize() + (density().tight() ? 8 : 12);
        int available = rowWidth - (textX - x) - density().inset();
        rowText(graphics, name, textX, y + (density().tight() ? 6 : 9), available, density().body(), theme().ink(), false, hovered);
        rowText(graphics, info.copy().withColor(theme().muted()), textX, y + (density().tight() ? 15 : 22), available,
            density().detail(), theme().muted(), true, hovered);
        rowText(graphics, lastPlayed.copy().withColor(theme().muted()), textX, y + (density().tight() ? 23 : 33), available,
            density().detail(), theme().muted(), true, hovered);
    }

    public int worldRowHeight() { return density().row(); }
    public int worldIconSize() { return density().icon(); }
    public int worldIconInset() { return density().inset(); }

    private void rowText(GuiGraphicsExtractor graphics, Component source, int x, int y, int available,
                         int size, int color, boolean secondary, boolean hovered) {
        var font = Minecraft.getInstance().font;
        Component styled = secondary ? MenuText.secondary(source) : MenuText.styled(source);
        float textScale = size / (float) font.lineHeight;
        Component fitted = fit(styled, available / textScale, hovered);
        text(graphics, fitted, x, y, size, color, secondary);
    }

    public void text(GuiGraphicsExtractor graphics, Component value, float x, float y, float size, int color, boolean secondary) {
        MenuText.extract(graphics, Minecraft.getInstance().font, secondary ? MenuText.secondary(value) : MenuText.styled(value), x, y, size, color);
    }

    private Component fit(Component full, float available, boolean showTooltip) {
        var font = Minecraft.getInstance().font;
        if (font.width(full) <= available) return full;
        if (showTooltip) trimmedTooltip = full;
        var result = Component.empty();
        font.substrByWidth(full, Math.max(1, (int) available - font.width("…"))).visit((style, text) -> {
            result.append(Component.literal(text).setStyle(style));
            return java.util.Optional.empty();
        }, net.minecraft.network.chat.Style.EMPTY);
        return result.append("…");
    }

    @Override public Component fitLabel(Component full, float available, boolean tooltip) { return fit(full, available, tooltip); }
    @Override public void contextualTooltip(Component full) { trimmedTooltip = full; }

    public MouseButtonEvent mouse(MouseButtonEvent event) {
        return new MouseButtonEvent(coordinate(event.x()), coordinate(event.y()), event.buttonInfo());
    }
    public double coordinate(double value) { return value / canvasScale; }
    @Override public float scale() { return .5f; }
    @Override public boolean compact() { return screen.width < 480; }
    @Override public MenuTheme theme() { return appearance.theme(); }
    @Override public int p(float value) { return Math.round(value * scale()); }
    @Override public int controlTextSize() { return density().body() * 2; }
    @Override public Component controlText(AbstractWidget widget) {
        if (widget == primary && SettingsText.hasKey(widget.getMessage(), "selectWorld.select"))
            return Component.translatable("rodel.worlds.play");
        return widget instanceof NativeOptionPresentation presentation ? presentation.rodel$valueText() : widget.getMessage();
    }
    @Override public Component fitValue(AbstractWidget widget, Component full, float available) {
        return fit(full, available, widget.isHovered() || widget.isFocused() && focusVisible());
    }
    @Override public void openChoices(AbstractWidget widget) { choices.open(widget, screen.width, screen.height); }
    @Override public void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                                    int thumbHeight, double scroll, int maxScroll) {
        if (maxScroll > 0) {
            int thumb = y + (int) (scroll * (height - thumbHeight) / maxScroll);
            graphics.fill(x, thumb, x + width, thumb + thumbHeight, theme().muted());
        }
    }
}
