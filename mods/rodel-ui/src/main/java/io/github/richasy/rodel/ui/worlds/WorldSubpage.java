package io.github.richasy.rodel.ui.worlds;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import io.github.richasy.rodel.ui.settings.NativeCycleChoices;
import io.github.richasy.rodel.ui.settings.NativeSettingsList;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import io.github.richasy.rodel.ui.settings.PackSettingsBody;
import io.github.richasy.rodel.ui.settings.PackSettingsHost;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.MultiLineTextWidget;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.ContainerEventHandler;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.packs.PackSelectionScreen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.InputWithModifiers;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.Style;
import net.minecraft.util.FormattedCharSequence;
import org.slf4j.LoggerFactory;

public final class WorldSubpage implements WorldSurface {
    private final Screen screen;
    private final MenuContext appearance;
    private final SettingsChoiceList choices = new SettingsChoiceList(this);
    private final WorldMoreNavigation navigation;
    private final Map<String, String> ruleBaseline;
    private final Map<String, Boolean> experimentBaseline;
    private WorldMoreSidebar sidebar;
    private final List<Button> topTabs = new ArrayList<>();
    private final List<Button> utilities = new ArrayList<>();
    private AbstractSelectionList<?> rules;
    private List<?> measuredRuleEntries = List.of();
    private EditBox search;
    private NativeTextBox searchBox;
    private WorldFormList experiments;
    private PackSettingsBody packs;
    private Component notice = Component.empty();
    private Button back;
    private Button done;
    private boolean initialized;
    private int actualWidth;
    private int actualHeight;
    private float canvasScale = 1;
    private int left;
    private int top;
    private int width;
    private int bodyTop;
    private int bodyBottom;
    private int frameLeft;
    private int frameWidth;
    private int sidebarWidth;
    private Component trimmedTooltip;

    public WorldSubpage(Screen screen, MenuContext appearance, WorldMoreNavigation navigation) {
        this.screen = screen;
        this.appearance = appearance;
        this.navigation = navigation;
        ruleBaseline = screen instanceof WorldRulesAccess source ? source.rodel$ruleValues() : Map.of();
        experimentBaseline = screen instanceof WorldExperimentsAccess source ? source.rodel$experimentValues() : Map.of();
        if (navigation != null) navigation.activate(screen instanceof WorldRulesAccess ? WorldMoreNavigation.Route.RULES
            : screen instanceof PackSelectionScreen ? WorldMoreNavigation.Route.PACKS : WorldMoreNavigation.Route.EXPERIMENTS);
    }
    public static WorldSubpage get(Screen screen) { return WorldSurface.get(screen) instanceof WorldSubpage page ? page : null; }
    public PackSettingsBody packs() { return packs; }
    @Override public WorldMoreNavigation moreNavigation() { return navigation; }
    public boolean dirty() {
        if (screen instanceof WorldRulesAccess source)
            return source.rodel$hasInvalidRules() || !ruleBaseline.equals(source.rodel$ruleValues());
        return screen instanceof WorldExperimentsAccess source && !experimentBaseline.equals(source.rodel$experimentValues());
    }
    public boolean canApply() { return done.active; }
    public void finish(boolean apply, InputWithModifiers input) { (apply ? done : back).onPress(input); }
    public void confirmNavigation(AbstractWidget anchor, List<NativeCycleChoices.Choice> options) {
        choices.open(anchor, screen.width, screen.height, options);
        screen.setFocused(anchor);
    }
    public boolean ownsList(Object list) { return list == rules || packs != null && packs.owns(list); }
    public boolean ownsPackEntry(Object entry) { return packs != null && packs.ownsEntry(entry); }
    public int listRowWidth(Object list) { return list == rules ? rules.getWidth() - 28 : packs.rowWidth(list); }
    public int errorInk() { return theme().light() ? 0xFFA53B2D : 0xFFFFB5A8; }

    @Override public void prepare() {
        var window = Minecraft.getInstance().getWindow();
        actualWidth = window.getGuiScaledWidth();
        actualHeight = window.getGuiScaledHeight();
        canvasScale = MenuContext.designScale() * 2;
        screen.width = Math.round(actualWidth / canvasScale);
        screen.height = Math.round(actualHeight / canvasScale);
    }
    @Override public void beginInit() { initialized = false; utilities.clear(); choices.close(); prepare(); }
    @Override public void initialized() {
        var original = List.copyOf(NativeWidgets.get(screen));
        back = null;
        done = null;
        notice = Component.empty();
        for (AbstractWidget widget : original) {
            if (widget instanceof Button button) {
                ((ThemedButton) button).rodel$setStyle(this, null, false, false, false);
                if (SettingsText.hasKey(button.getMessage(), "gui.done")) done = button;
                else if (SettingsText.hasKey(button.getMessage(), "gui.cancel")) back = button;
                else utilities.add(button);
            } else if (widget instanceof MultiLineTextWidget || widget instanceof StringWidget && !widget.getMessage().equals(screen.getTitle())) {
                notice = widget.getMessage();
            }
        }
        if (done == null) throw new IllegalStateException("Native world subpage is missing its commit action");
        if (screen instanceof WorldRulesAccess source) {
            rules = source.rodel$rules();
            search = source.rodel$ruleSearch();
        } else if (screen instanceof PackSelectionScreen) {
            packs = new PackSettingsBody(this, (PackSettingsHost) screen);
            search = original.stream().filter(EditBox.class::isInstance).map(EditBox.class::cast).findFirst().orElseThrow();
            back = done;
        } else {
            var container = original.stream().filter(ContainerEventHandler.class::isInstance)
                .map(ContainerEventHandler.class::cast).findFirst().orElseThrow();
            List<AbstractWidget> controls = container.children().stream().map(AbstractWidget.class::cast).toList();
            experiments = new WorldFormList(this, controls);
        }
        if (back == null) throw new IllegalStateException("Native world subpage is missing its cancel action");
        ((ThemedButton) back).rodel$setStyle(this, MenuIcon.BACK, false, packs == null || navigation != null, false);
        if (packs == null) {
            back.setTooltip(Tooltip.create(Component.translatable("gui.cancel")));
            ((ThemedButton) done).rodel$setStyle(this, null, true, false, false);
        }
        var widgets = NativeWidgets.get(screen);
        widgets.clear();
        widgets.add(back);
        topTabs.clear();
        if (navigation != null) {
            sidebar = new WorldMoreSidebar(this, navigation);
            widgets.addAll(sidebar.widgets());
            for (int index = 0; index < navigation.tabs().size(); index++) {
                final int tab = index;
                Button button = new Button(0, 0, 1, 1, navigation.tabs().get(index).getTabTitle(),
                    ignored -> {}, narration -> narration.get()) {
                        @Override public void onPress(InputWithModifiers input) {
                            navigation.selectTab(WorldSubpage.this, this, tab, input);
                        }
                        @Override protected void extractContents(GuiGraphicsExtractor graphics, int x, int y, float partialTick) {
                            graphics.fill(getX(), getY(), getRight(), getBottom(), tab == 2 ? theme().selection()
                                : isHovered() ? theme().hover() : theme().surface());
                            graphics.outline(getX(), getY(), getWidth(), getHeight(), theme().edge());
                            if (isFocused() && focusVisible())
                                graphics.outline(getX() + 1, getY() + 1, getWidth() - 2, getHeight() - 2, theme().focus());
                            int size = density().body();
                            var font = Minecraft.getInstance().font;
                            text(graphics, getMessage(), getX() + (getWidth() - font.width(getMessage()) * size / (float) font.lineHeight) / 2,
                                getY() + (getHeight() - size) / 2f, size, theme().ink(), false);
                        }
                    };
                topTabs.add(button);
            }
            widgets.addAll(topTabs);
        }
        if (search != null) {
            searchBox = NativeTextBox.search(this, search);
            widgets.add(searchBox);
        }
        if (rules != null) widgets.add(rules);
        if (packs != null) widgets.addAll(packs.widgets());
        if (experiments != null) widgets.add(experiments);
        widgets.addAll(utilities);
        if (done != back) widgets.add(done);
        widgets.add(choices);
        initialized = true;
        layout();
        LoggerFactory.getLogger("Rodel UI").info("Native world subpage: {}", screen.getClass().getSimpleName());
    }

    @Override public void layout() {
        if (!initialized) return;
        int margin = compact() ? 16 : 36;
        frameWidth = Math.min(screen.width - margin * 2, navigation != null ? 620 : packs == null ? 500 : 560);
        frameLeft = (screen.width - frameWidth) / 2;
        sidebarWidth = navigation == null ? 0 : compact() ? 72 : 104;
        int sidebarGap = navigation == null ? 0 : compact() ? 8 : 14;
        left = frameLeft + sidebarWidth + sidebarGap;
        width = frameWidth - sidebarWidth - sidebarGap;
        top = compact() ? 14 : 24;
        int controlHeight = density().control();
        int footer = screen.height - top - controlHeight;
        bodyTop = top + controlHeight + 12;
        bodyBottom = done == back ? screen.height - top : footer - 12;
        back.setRectangle(packs == null || navigation != null ? controlHeight : 60, controlHeight, frameLeft, top);
        if (navigation != null) {
            sidebar.layout(frameLeft, bodyTop, sidebarWidth);
            int tabWidth = screen.width < 360 ? 32 : 44;
            for (int i = 0; i < topTabs.size(); i++)
                topTabs.get(i).setRectangle(tabWidth, controlHeight, frameLeft + frameWidth - tabWidth * (topTabs.size() - i), top);
        }
        if (done != back) done.setRectangle(90, controlHeight, left + width - 90, footer);
        int utilityX = left + width;
        for (Button utility : utilities) {
            if (navigation != null) utility.setRectangle(sidebarWidth, controlHeight, frameLeft, footer);
            else {
                utilityX -= 110;
                utility.setRectangle(104, controlHeight, utilityX, top);
            }
        }
        if (packs != null) {
            packs.layoutBrowser(left, bodyTop, width, bodyBottom - bodyTop, notice);
            packs.placeSearch(searchBox);
        } else if (rules != null) {
            searchBox.setRectangle(width - 32, controlHeight, left + 16, bodyTop + density().inset());
            int ruleTop = searchBox.getBottom() + density().gap() - AbstractSelectionList.Entry.CONTENT_PADDING;
            layoutRules(left, ruleTop, width, bodyBottom - density().inset() - ruleTop);
        } else {
            int noticeSize = density().body();
            int noticeHeight = textHeight(lines(notice, width - 32, noticeSize), noticeSize, noticeSize + 2);
            // WorldFormList already supplies the first row's leading inset.
            int listTop = bodyTop + density().inset() + noticeHeight;
            experiments.resize(left, listTop, width, Math.max(20, bodyBottom - density().inset() - listTop));
        }
    }

    private void layoutRules(int x, int y, int width, int height) {
        ((NativeSettingsList) rules).rodel$topAligned();
        for (var entry : rules.children()) {
            wrapRuleInput(entry);
            entry.setHeight(ruleHeight(entry, width - 36));
        }
        double scroll = rules.scrollAmount();
        rules.updateSizeAndPosition(width, Math.max(24, height), x, y);
        rules.setScrollAmount(scroll);
        measuredRuleEntries = List.copyOf(rules.children());
    }

    public boolean invalidInput(AbstractWidget widget) {
        return rules != null && rules.children().stream().anyMatch(entry -> entry instanceof ContainerEventHandler container
            && container.children().stream().anyMatch(child -> child == widget || child instanceof NativeTextBox box && box.editor() == widget)
            && ((WorldRulesAccess) screen).rodel$invalidRule(entry));
    }

    public GuiEventListener focusTarget(GuiEventListener widget) {
        return widget == search && searchBox != null ? searchBox : widget;
    }

    @SuppressWarnings("unchecked")
    private void wrapRuleInput(AbstractSelectionList.Entry<?> entry) {
        if (!(entry instanceof NativeRuleRow) || !(entry instanceof ContainerEventHandler container)) return;
        // GameRuleEntry owns a mutable AbstractWidget list shared by input and narration.
        // Keep IntegerRuleEntry.input and its responder; only replace the event/render adapter.
        var children = (List<AbstractWidget>) (List<?>) container.children();
        for (int i = 0; i < children.size(); i++) {
            if (children.get(i) instanceof EditBox editor) {
                var wrapper = new NativeTextBox(this, editor);
                children.set(i, wrapper);
                if (container.getFocused() == editor) container.setFocused(wrapper);
            }
        }
    }

    private List<FormattedCharSequence> lines(Component text, int width, int size) {
        return text.getString().isBlank() ? List.of() : Minecraft.getInstance().font.split(MenuText.secondary(text),
            Math.max(1, Math.round(width * Minecraft.getInstance().font.lineHeight / (float) size)));
    }
    private int textHeight(List<FormattedCharSequence> lines, int size, int step) {
        return lines.isEmpty() ? 0 : size + (lines.size() - 1) * step;
    }
    private int ruleControlWidth(AbstractWidget widget) {
        return widget instanceof NativeTextBox ? (compact() ? 84 : 120) : 54;
    }
    private int ruleTextGap() { return Math.max(1, density().gap() / 2); }
    private RuleText measure(NativeRuleRow row, int width, int controlWidth) {
        int available = width < 260 ? width : Math.max(48, width - controlWidth - density().gap());
        var title = lines(row.rodel$ruleTitle(), available, density().body());
        var detail = lines(row.rodel$ruleDescription(), available, density().detail());
        var defaults = lines(row.rodel$ruleDefault(), available, density().detail());
        int height = textHeight(title, density().body(), density().body() + 2)
            + (detail.isEmpty() ? 0 : ruleTextGap() + textHeight(detail, density().detail(), density().detail() + 2))
            + (defaults.isEmpty() ? 0 : ruleTextGap() + textHeight(defaults, density().detail(), density().detail() + 2));
        return new RuleText(title, detail, defaults, height, width < 260);
    }
    private int ruleHeight(AbstractSelectionList.Entry<?> entry, int width) {
        if (!(entry instanceof NativeRuleRow row))
            return density().body() + 2 + (entry == rules.children().getFirst() ? 0 : density().gap());
        var widget = (AbstractWidget) ((ContainerEventHandler) entry).children().getFirst();
        var text = measure(row, width, ruleControlWidth(widget));
        return density().inset() * 2 + (text.stacked ? text.height + density().gap() + density().control()
            : Math.max(density().control(), text.height));
    }
    public void ruleRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, AbstractWidget widget,
                        NativeRuleRow row, int mouseX, int mouseY, float partialTick) {
        int x = entry.getContentX() + 2, y = entry.getY(), rowWidth = entry.getContentWidth() - 4;
        int controlWidth = ruleControlWidth(widget);
        var measure = measure(row, rowWidth, controlWidth);
        int controlY = measure.stacked ? y + density().inset() + measure.height + density().gap()
            : y + (entry.getHeight() - density().control()) / 2;
        widget.setRectangle(controlWidth, density().control(), x + rowWidth - controlWidth, controlY);
        if (!(widget instanceof NativeTextBox)) ((ThemedButton) widget).rodel$setStyle(this, null, false, false, false);
        float textY = measure.stacked ? y + density().inset() : y + (entry.getHeight() - measure.height) / 2f;
        drawLines(graphics, measure.title, x, textY, density().body(), density().body() + 2, theme().ink());
        textY += textHeight(measure.title, density().body(), density().body() + 2);
        if (!measure.detail.isEmpty()) {
            drawLines(graphics, measure.detail, x, textY + ruleTextGap(), density().detail(), density().detail() + 2, theme().muted());
            textY += ruleTextGap() + textHeight(measure.detail, density().detail(), density().detail() + 2);
        }
        if (!measure.defaults.isEmpty())
            drawLines(graphics, measure.defaults, x, textY + ruleTextGap(), density().detail(), density().detail() + 2, theme().muted());
        graphics.fill(x, y + entry.getHeight() - 1, x + rowWidth, y + entry.getHeight(), theme().edge());
        widget.extractRenderState(graphics, mouseX, mouseY, partialTick);
    }
    public void categoryRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, AbstractWidget widget) {
        int size = density().body() + 2;
        widget.setRectangle(entry.getContentWidth() - 4, size, entry.getContentX() + 2, entry.getY() + entry.getHeight() - size);
        text(graphics, SettingsText.label(widget.getMessage()), widget.getX(), widget.getY(), size, theme().muted(), false);
        if (widget.isFocused() && focusVisible()) graphics.outline(widget.getX(), widget.getY(), widget.getWidth(), widget.getHeight(), theme().focus());
    }
    /** Draws a native category whose game version stores a label without a focusable widget. */
    public void categoryRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, Component label) {
        int size = density().body() + 2;
        text(graphics, SettingsText.label(label), entry.getContentX() + 2,
            entry.getY() + entry.getHeight() - size, size, theme().muted(), false);
    }
    private void drawLines(GuiGraphicsExtractor graphics, List<FormattedCharSequence> lines, int x, float y, int size, int step, int ink) {
        for (int i = 0; i < lines.size(); i++) {
            graphics.pose().pushMatrix();
            graphics.pose().translate(x, y + i * step);
            graphics.pose().scale(size / (float) Minecraft.getInstance().font.lineHeight);
            graphics.text(Minecraft.getInstance().font, lines.get(i), 0, 0, ink, false);
            graphics.pose().popMatrix();
        }
    }
    private record RuleText(List<FormattedCharSequence> title, List<FormattedCharSequence> detail,
                            List<FormattedCharSequence> defaults, int height, boolean stacked) {}

    @Override public void render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        ((UiBackdrop) screen).rodel$backdrop(graphics, theme(), actualWidth, actualHeight, partialTick);
        graphics.pose().pushMatrix();
        graphics.pose().scale(canvasScale);
        UiInputGeometry.push(canvasScale, 0, 0);
        try {
            int x = Math.round(mouseX / canvasScale), y = Math.round(mouseY / canvasScale);
            trimmedTooltip = null;
            if (packs == null) {
                graphics.fill(left, bodyTop, left + width, bodyBottom, theme().surface());
                graphics.outline(left, bodyTop, width, bodyBottom - bodyTop, theme().edge());
            }
            int titleX = back.getRight() + 10;
            int titleRight = navigation != null ? topTabs.getFirst().getX() - 10
                : utilities.isEmpty() ? left + width : utilities.getFirst().getX() - 8;
            int heading = density().heading();
            Component title = fitLabel(MenuText.styled(navigation == null ? screen.getTitle() : navigation.title()),
                Math.max(20, titleRight - titleX) * Minecraft.getInstance().font.lineHeight / (float) heading,
                x >= titleX && x < titleRight && y >= top && y < top + 24);
            text(graphics, title, titleX, top + 4, heading, theme().ink(), false);
            if (packs != null) {
                packs.chrome(graphics);
            } else if (experiments != null) {
                experiments.refresh();
                int noticeSize = density().body();
                drawLines(graphics, lines(notice.copy().withColor(errorInk()), width - 32, noticeSize),
                    left + 16, bodyTop + density().inset(), noticeSize, noticeSize + 2, errorInk());
            } else if (rules != null && !measuredRuleEntries.equals(rules.children())) {
                // Native filtering replaces entries, including their input adapters and row heights.
                layoutRules(rules.getX(), rules.getY(), rules.getWidth(), rules.getHeight());
            }
            for (AbstractWidget widget : List.copyOf(NativeWidgets.get(screen))) widget.extractRenderState(graphics, x, y, partialTick);
            choices.overlay(graphics, x, y, partialTick);
            if (trimmedTooltip != null && !choices.visible) graphics.setTooltipForNextFrame(trimmedTooltip, x, y);
            UiInputGeometry.flushPreedit(graphics, x, y, partialTick);
        } finally {
            UiInputGeometry.pop();
            graphics.pose().popMatrix();
        }
    }
    @Override public void text(GuiGraphicsExtractor graphics, Component value, float x, float y, float size, int color, boolean secondary) {
        MenuText.extract(graphics, Minecraft.getInstance().font, secondary ? MenuText.secondary(value) : MenuText.styled(value), x, y, size, color);
    }
    @Override public Component fitLabel(Component full, float available, boolean tooltip) {
        var font = Minecraft.getInstance().font;
        if (font.width(full) <= available) return full;
        if (tooltip) contextualTooltip(full);
        var result = Component.empty();
        font.substrByWidth(full, Math.max(1, (int) available - font.width("…"))).visit((style, text) -> {
            result.append(Component.literal(text).setStyle(style));
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result.append("…");
    }
    @Override public Component fitValue(AbstractWidget widget, Component full, float available) {
        return fitLabel(full, available, widget.isHovered() || widget.isFocused() && focusVisible());
    }
    @Override public void contextualTooltip(Component full) { trimmedTooltip = full; }
    @Override public Component controlText(AbstractWidget widget) {
        return widget instanceof NativeOptionPresentation source ? source.rodel$valueText() : widget.getMessage();
    }
    @Override public void openChoices(AbstractWidget widget) { choices.open(widget, screen.width, screen.height); }
    @Override public SettingsChoiceList choices() { return choices; }
    @Override public int controlTextSize() { return density().body() * 2; }
    @Override public float scale() { return .5f; }
    @Override public boolean compact() { return screen.width < 480; }
    @Override public MenuTheme theme() { return appearance.theme(); }
    @Override public int p(float value) { return Math.round(value * scale()); }
    @Override public double coordinate(double value) { return value / canvasScale; }
    @Override public MouseButtonEvent mouse(MouseButtonEvent event) {
        return new MouseButtonEvent(coordinate(event.x()), coordinate(event.y()), event.buttonInfo());
    }
    @Override public void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                                    int thumbHeight, double scroll, int maxScroll) {
        if (maxScroll > 0) {
            int thumb = y + (int) (scroll * (height - thumbHeight) / maxScroll);
            graphics.fill(x, thumb, x + width, thumb + thumbHeight, theme().muted());
        }
    }
}
