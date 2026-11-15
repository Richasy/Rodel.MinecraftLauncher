package io.github.richasy.rodel.ui.settings;

import java.util.ArrayList;
import java.util.List;
import java.util.IdentityHashMap;
import java.util.Map;
import java.util.Collections;
import java.util.Set;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.ThemedSlider;
import io.github.richasy.rodel.ui.mixin.OptionCaptionAccessor;
import io.github.richasy.rodel.ui.mixin.ButtonActionAccessor;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractButton;
import net.minecraft.client.gui.components.AbstractSliderButton;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.OptionsList;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.options.VideoSettingsScreen;
import net.minecraft.client.gui.screens.packs.PackSelectionModel;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.resources.Identifier;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.world.entity.HumanoidArm;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import org.slf4j.LoggerFactory;

public final class NativeSettingsPage implements NativeFormContext {
    final SettingsSession session;
    final Button category;
    private final Screen screen;
    private final OptionsList options;
    private final AbstractSelectionList<?> specialized;
    private final PackSettingsBody packs;
    private final SettingsWidgetList sidebar;
    private final SettingsWidgetList rootBody;
    private final SettingsChoiceList choices;
    private AbstractWidget mainHand;
    private Button done;
    private final List<AbstractWidget> footer = new ArrayList<>();
    private final List<AbstractWidget> headerActions = new ArrayList<>();
    private final List<AbstractWidget> auxiliary = new ArrayList<>();
    private final List<AbstractWidget> utilities = new ArrayList<>();
    private final List<AbstractWidget> notices = new ArrayList<>();
    private final Set<AbstractWidget> literalControls = Collections.newSetFromMap(new IdentityHashMap<>());
    private final Map<EditBox, NativeTextBox> searchBoxes = new IdentityHashMap<>();
    private final Button back;
    private final Button headerBack;
    private final Map<AbstractWidget, Component> captions = new IdentityHashMap<>();
    private final Map<AbstractWidget, RowInfo> measurements = new IdentityHashMap<>();
    private final Map<AbstractWidget, Integer> rowWidths = new IdentityHashMap<>();
    private final Set<AbstractWidget> gridControls = Collections.newSetFromMap(new IdentityHashMap<>());
    private int bodyX;
    private int bodyY;
    private int bodyWidth;
    private int bodyHeight;
    private int headerBottom;
    private int panelY;
    private int panelHeight;
    private float layoutScale;
    private Component trimmedTooltip;
    private final List<AbstractWidget> formWidgets = new ArrayList<>();

    private NativeSettingsPage(Screen screen, Screen parent, SettingsSession session, OptionsList options,
                               List<AbstractWidget> original, boolean root) {
        this.screen = screen;
        this.session = session;
        this.options = options;
        this.specialized = screen instanceof SpecialSettingsHost host ? host.rodel$nativeList() : null;
        this.packs = screen instanceof PackSettingsHost host ? new PackSettingsBody(this, host) : null;
        this.category = root ? null : session.category(screen, parent);
        session.selected = category;
        this.sidebar = new SettingsWidgetList(this, true);
        this.rootBody = root ? new SettingsWidgetList(this, false) : null;
        this.choices = new SettingsChoiceList(this);
        if (screen.getClass() == VideoSettingsScreen.class && options != null) {
            VideoSettingsLayout.arrange(options, screen, Minecraft.getInstance().options);
        }
        if (options != null) {
            List<OptionsList.AbstractEntry> rows = new ArrayList<>();
            List<OptionsList.OptionInstanceWidget> simpleControls = new ArrayList<>();
            for (OptionsList.AbstractEntry entry : options.children()) {
                if (entry instanceof NativeOptionRows nativeRows) {
                    for (OptionsList.OptionInstanceWidget item : nativeRows.rodel$options()) {
                        if (item.optionInstance() != null) {
                            captions.put(item.widget(), ((OptionCaptionAccessor<?>) (Object) item.optionInstance()).rodel$caption());
                        }
                        if (item.optionInstance() == Minecraft.getInstance().options.mainHand()
                            || item.widget() instanceof CycleButton<?> cycle && cycle.getValue() instanceof HumanoidArm) {
                            mainHand = item.widget();
                        }
                        if (item.widget() != mainHand && item.widget() instanceof CycleButton<?>
                            && SettingsText.description(item.widget()).getString().isBlank()) {
                            simpleControls.add(item);
                        } else {
                            flushCompactControls(rows, simpleControls);
                            rows.add(new OptionsList.Entry(List.of(item), screen));
                        }
                    }
                } else {
                    flushCompactControls(rows, simpleControls);
                    rows.add(entry);
                }
            }
            flushCompactControls(rows, simpleControls);
            options.replaceEntries(rows);
        }
        List<AbstractWidget> body = new ArrayList<>();
        List<Button> routes = new ArrayList<>();
        for (AbstractWidget widget : original) {
            if (widget == options || widget == specialized || packs != null && packs.owns(widget)
                || widget instanceof StringWidget && widget.getMessage().equals(screen.getTitle())) {
                continue;
            }
            if (widget instanceof Button button) {
                if (key(button).equals("gui.done")) {
                    done = button;
                    continue;
                }
                if (root) {
                    routes.add(button);
                } else if (key(button).equals("options.accessibility.link") || key(button).equals("options.font")
                    || specialized != null || packs != null) {
                    headerActions.add(widget);
                } else {
                    footer.add(widget);
                }
            } else if (root) {
                body.add(widget);
            } else if (widget instanceof StringWidget) {
                notices.add(widget);
            } else {
                if (widget instanceof EditBox editor) {
                    NativeTextBox search = packs != null || specialized != null
                        ? NativeTextBox.search(this, editor) : new NativeTextBox(this, editor);
                    searchBoxes.put(editor, search);
                    auxiliary.add(search);
                } else {
                    auxiliary.add(widget);
                }
            }
        }
        if (root) {
            session.routes = List.copyOf(routes);
            session.selected = null;
            body.forEach(this::style);
            rootBody.setWidgets(body);
            formWidgets.addAll(body);
        } else if (options != null) {
            for (OptionsList.AbstractEntry entry : options.children()) {
                if (entry instanceof OptionsList.Entry) {
                    for (var child : entry.children()) {
                        if (child instanceof AbstractWidget widget) formWidgets.add(widget);
                    }
                }
            }
        }
        List<AbstractWidget> navigation = new ArrayList<>();
        navigation.add(new SettingsWidgetList.RouteButton(this, null));
        for (Button route : session.routes) {
            if (key(route).equals("options.credits_and_attribution") || key(route).equals("rodel.settings.mods")) {
                utilities.add(new SettingsWidgetList.RouteButton(this, route, true));
            } else {
                navigation.add(new SettingsWidgetList.RouteButton(this, route));
            }
        }
        sidebar.setWidgets(navigation);
        back = Button.builder(Component.translatable(packs == null ? "rodel.settings.exit" : "gui.back"),
            button -> { if (packs == null) session.close(); else closeToParent(); })
            .tooltip(Tooltip.create(Component.translatable("rodel.settings.exit"))).build();
        ((ThemedButton) back).rodel$setStyle(this, MenuIcon.BACK, false, packs == null, false);
        headerBack = packs == null && parent != null && parent != session.root
            ? Button.builder(Component.translatable("gui.back"), button -> closeToParent())
                .createNarration(ignored -> Component.translatable("rodel.settings.back_to", parent.getTitle())).build()
            : null;
        if (headerBack != null) ((ThemedButton) headerBack).rodel$setStyle(this, MenuIcon.BACK, false, false, false);
        footer.forEach(this::style);
        headerActions.forEach(this::style);
        List<AbstractWidget> widgets = NativeWidgets.get(screen);
        widgets.clear();
        if (packs == null) widgets.add(sidebar);
        if (packs != null) widgets.addAll(packs.widgets());
        else widgets.add(options != null ? options : specialized != null ? specialized : rootBody);
        widgets.addAll(auxiliary);
        widgets.addAll(footer);
        widgets.addAll(headerActions);
        if (packs != null && done != null) {
            ((ThemedButton) done).rodel$setStyle(this, null, true, false, false);
            widgets.add(done);
        }
        if (packs == null) widgets.addAll(utilities);
        widgets.add(back);
        if (headerBack != null) widgets.add(headerBack);
        widgets.add(choices);
        ((SettingsHost) screen).rodel$settingsPage(this);
        if (specialized instanceof NativeSettingsList listStyle) listStyle.rodel$topAligned();
        layout();
        LoggerFactory.getLogger("Rodel UI").info("Native settings layout: {} routes={}, nativeEntries={}",
            screen.getClass().getSimpleName(), session.routes.size(), options == null ? body.size() : options.children().size());
    }

    public static void root(Screen screen, MenuContext source) {
        NativeSettingsPage existing = get(screen);
        if (existing != null) existing.rememberSidebar();
        SettingsSession session = existing == null ? new SettingsSession(screen, source) : existing.session;
        new NativeSettingsPage(screen, null, session, null, List.copyOf(NativeWidgets.get(screen)), true);
    }

    public static void subpage(Screen screen, Screen parent, OptionsList options) {
        NativeSettingsPage ancestor = get(parent);
        if (ancestor == null || options == null && !(screen instanceof SpecialSettingsHost)
            || !screen.getClass().getName().startsWith("net.minecraft.client.gui.screens.options.")) {
            return;
        }

        NativeSettingsPage existing = get(screen);
        if (existing != null && existing.session == ancestor.session) existing.rememberSidebar();
        new NativeSettingsPage(screen, parent, ancestor.session, options, List.copyOf(NativeWidgets.get(screen)), false);
    }

    public static void nativePage(Screen screen, Screen parent) {
        NativeSettingsPage ancestor = get(parent);
        if (ancestor == null) return;
        NativeSettingsPage existing = get(screen);
        if (existing != null) existing.rememberSidebar();
        new NativeSettingsPage(screen, parent, ancestor.session, null, List.copyOf(NativeWidgets.get(screen)), false);
    }

    public static NativeSettingsPage get(Screen screen) {
        return screen instanceof SettingsHost host ? host.rodel$settingsPage() : null;
    }

    private static String key(AbstractWidget widget) {
        return widget.getMessage().getContents() instanceof TranslatableContents text ? text.getKey() : "";
    }

    private void flushCompactControls(List<OptionsList.AbstractEntry> rows, List<OptionsList.OptionInstanceWidget> pending) {
        int columns = pending.size() >= 4 ? 2 : 1;
        if (columns == 2) pending.forEach(item -> gridControls.add(item.widget()));
        for (int i = 0; i < pending.size(); i += columns) {
            rows.add(new OptionsList.Entry(List.copyOf(pending.subList(i, Math.min(i + columns, pending.size()))), screen));
        }
        pending.clear();
    }

    private boolean grid(OptionsList.AbstractEntry entry) {
        if (compact() || entry.children().isEmpty() || entry.children().size() > 2) return false;
        for (var child : entry.children()) {
            if (!(child instanceof AbstractWidget widget) || !gridControls.contains(widget)
                || !SettingsText.description(widget).getString().isBlank()) return false;
        }
        return true;
    }

    private int entryHeight(OptionsList.AbstractEntry entry) {
        int height = 0;
        for (var child : entry.children()) {
            if (child instanceof AbstractWidget widget) {
                height = grid(entry) ? Math.max(height, rowHeight(widget)) : height + rowHeight(widget);
            }
        }
        return height;
    }

    private void style(AbstractWidget widget) {
        if (!widget.getClass().getName().startsWith("net.minecraft.")) {
            return;
        }
        if (widget instanceof AbstractButton button) {
            ((ThemedButton) button).rodel$setStyle(this, null, false, false, false);
        } else if (widget instanceof AbstractSliderButton slider) {
            ((ThemedSlider) slider).rodel$setStyle(this);
        }
    }

    public void activated() {
        session.selected = category;
        layout();
        if (packs == null) sidebar.restoreNavigation(session.sidebarScroll);
    }

    public void rememberSidebar() {
        if (packs != null) return;
        session.sidebarScroll = sidebar.scrollAmount() / scale();
    }

    public boolean navigationFocused() { return screen.getFocused() == sidebar; }

    public void closeToParent() {
        rememberSidebar();
        if (done != null) ((ButtonActionAccessor) done).rodel$action().onPress(done);
        else screen.onClose();
    }

    public void openCategory(String key) {
        if (screen != session.root) throw new IllegalStateException("Category shortcuts require the native settings root");
        for (Button route : session.routes) {
            String candidate = key(route);
            if (candidate.equals(key) || candidate.equals(key + ".title")) {
                session.selected = route;
                ((ButtonActionAccessor) route).rodel$action().onPress(route);
                NativeSettingsPage destination = get(Minecraft.getInstance().gui.screen());
                if (destination != null) destination.sidebar.revealSelection();
                return;
            }
        }
        throw new IllegalArgumentException("Native settings category is unavailable: " + key);
    }

    public void layout() {
        double navigationScroll = layoutScale == 0 ? session.sidebarScroll : sidebar.scrollAmount() / layoutScale;
        layoutScale = scale();
        choices.close();
        measurements.clear();
        if (packs != null) {
            layoutPackBrowser();
            return;
        }
        int outerX = outerX();
        int outerY = packs != null ? 40 : density().tight() ? 20 : compact() ? 28 : 48;
        int navWidth = density().tight() ? 120 : compact() ? 144 : 208;
        int gap = compact() ? 16 : 28;
        panelY = packs != null ? p(outerY) : p(outerY + (density().tight() ? 44 : compact() ? 60 : 88));
        panelHeight = screen.height - p(outerY) - panelY;
        int panelX = packs != null ? p(compact() ? 32 : 72) : p(outerX + navWidth + gap);
        int panelWidth = packs != null ? screen.width - panelX * 2
            : Math.min(screen.width - p(outerX) - panelX, compact() ? screen.width : p(800));
        bodyX = panelX;
        headerBottom = panelY + p(density().tight() ? 40 : compact() ? 60 : 76);
        bodyWidth = panelWidth;
        bodyY = headerBottom;
        for (AbstractWidget widget : auxiliary) {
            if (widget instanceof NativeTextBox) bodyY += p(packs != null ? 76 : density().tight() ? 44 : 60);
        }
        if (packs == null || compact()) for (AbstractWidget widget : notices) bodyY += noteHeight(widget);
        bodyHeight = Math.max(p(40), panelY + panelHeight - p(footer.isEmpty() ? density().tight() ? 16 : 24 : density().tight() ? 72 : 100) - bodyY);
        configureForm();
        sidebar.resize(p(outerX), panelY, p(navWidth), panelHeight);
        sidebar.setScrollAmount(navigationScroll * scale());
        if (packs == null) back.setRectangle(p(density().tight() ? 32 : 44), p(density().tight() ? 32 : 44), p(outerX), p(outerY));
        else back.setRectangle(p(104), p(44), panelX + p(24), panelY + (headerBottom - panelY - p(44)) / 2);
        if (headerBack != null) headerBack.setRectangle(p(density().tight() ? 80 : 104), density().control(), bodyX + p(24),
            panelY + (headerBottom - panelY - density().control()) / 2);
        int utilityRight = bodyX + bodyWidth;
        for (int i = utilities.size() - 1; i >= 0; i--) {
            AbstractWidget widget = utilities.get(i);
            int size = density().tight() ? controlTextSize() : compact() ? 12 : 14;
            int width = p(Math.min(260, Math.round(Minecraft.getInstance().font.width(
                MenuText.styled(SettingsText.label(widget.getMessage()))) * (float) size / Minecraft.getInstance().font.lineHeight) + 32));
            utilityRight -= width;
            widget.setRectangle(width, p(density().tight() ? 32 : 44), utilityRight, p(outerY));
            utilityRight -= p(12);
        }
        int actionRight = bodyX + bodyWidth - p(24);
        for (int i = headerActions.size() - 1; i >= 0; i--) {
            AbstractWidget widget = headerActions.get(i);
            int width = Math.min(p(240), Math.max(p(120), p(32) + Math.round(Minecraft.getInstance().font.width(
                MenuText.styled(SettingsText.label(widget.getMessage()))) * (float) p(controlTextSize()) / Minecraft.getInstance().font.lineHeight)));
            width = Math.min(width, (bodyWidth - p(headerBack == null && packs == null ? 72 : 192)) / 2);
            actionRight -= width;
            int actionHeight = p(density().tight() ? 32 : 44);
            widget.setRectangle(width, actionHeight, actionRight, panelY + (headerBottom - panelY - actionHeight) / 2);
            actionRight -= p(12);
        }
        if (options != null) {
            boolean first = true;
            for (OptionsList.AbstractEntry entry : options.children()) {
                int count = entry instanceof OptionsList.HeaderEntry ? 0 : entry.children().size();
                entry.setHeight(count == 0 ? p(density().tight() ? first ? 24 : 36 : first ? 36 : 56) : entryHeight(entry));
                entry.children().forEach(child -> {
                    if (child instanceof AbstractWidget widget) style(widget);
                });
                first = false;
            }
            options.updateSizeAndPosition(bodyWidth, bodyHeight, bodyX, bodyY);
        } else if (packs != null) {
            packs.layout(bodyX, bodyY, bodyWidth, bodyHeight);
        } else if (specialized != null) {
            layoutSpecialized();
            specialized.updateSizeAndPosition(bodyWidth, bodyHeight, bodyX, bodyY);
        } else {
            rootBody.resize(bodyX, bodyY, bodyWidth, bodyHeight);
        }
        int footerWidth = Math.min(p(180), (panelWidth - p(48) - Math.max(0, footer.size() - 1) * p(12)) / Math.max(1, footer.size()));
        int x = panelX + panelWidth - p(24) - footer.size() * footerWidth - Math.max(0, footer.size() - 1) * p(12);
        for (AbstractWidget widget : footer) {
            widget.setRectangle(footerWidth, p(density().tight() ? 32 : 52), x, panelY + panelHeight - p(density().tight() ? 48 : 76));
            x += footerWidth + p(12);
        }
        int auxiliaryY = headerBottom + p(packs != null ? 16 : 8);
        for (AbstractWidget widget : auxiliary) {
            if (widget instanceof NativeTextBox search) {
                int searchWidth = packs == null ? rowWidth()
                    : compact() ? bodyWidth - p(48) : (bodyWidth - p(64)) / 2;
                search.setRectangle(searchWidth, p(density().tight() ? 32 : 44), bodyX + p(packs != null ? 24 : 28), auxiliaryY);
                auxiliaryY += p(packs != null ? 76 : 60);
            } else {
                widget.setPosition(bodyX + p(28), auxiliaryY);
                auxiliaryY += widget.getHeight() + p(8);
            }
        }
    }

    public int rowWidth() {
        return bodyWidth - p(56);
    }

    public PackSettingsBody packBrowser() { return packs; }

    private void layoutPackBrowser() {
        bodyX = p(compact() ? 32 : 56);
        bodyWidth = screen.width - bodyX * 2;
        int controlHeight = density().control();
        int outerY = density().tight() ? 12 : p(28);
        panelY = outerY + controlHeight + density().gap() * 2;
        bodyY = panelY;
        panelHeight = Math.max(1, screen.height - panelY - (density().tight() ? 12 : p(56)));
        bodyHeight = panelHeight;
        back.setRectangle(controlHeight, controlHeight, bodyX, outerY);
        back.setTooltip(Tooltip.create(Component.translatable("gui.back")));
        ((ThemedButton) back).rodel$setStyle(this, MenuIcon.BACK, false, true, false);
        int right = bodyX + bodyWidth;
        for (int i = headerActions.size() - 1; i >= 0; i--) {
            var action = headerActions.get(i);
            int width = Math.min(p(284), Math.max(p(140), p(32) + Math.round(Minecraft.getInstance().font.width(
                MenuText.styled(action.getMessage())) * density().body() / (float) Minecraft.getInstance().font.lineHeight)));
            right -= width;
            action.setRectangle(width, controlHeight, right, outerY);
            right -= p(12);
        }
        Component notice = Component.empty();
        for (var widget : notices) notice = notice.copy().append(widget.getMessage()).append(" ");
        packs.layoutBrowser(bodyX, bodyY, bodyWidth, bodyHeight, notice, done);
        bodyHeight = panelHeight = packs.browserHeight();
        for (var search : searchBoxes.values()) packs.placeSearch(search);
    }

    public Component caption(AbstractWidget widget) {
        Component caption = captions.get(widget);
        return SettingsText.caption(caption != null ? caption : widget instanceof NativeOptionPresentation nativeOption
            ? nativeOption.rodel$caption() : widget.getMessage());
    }

    public Component controlText(AbstractWidget widget) {
        if (literalControls.contains(widget)) return SettingsText.bindingValue(widget.getMessage(), theme());
        if (widget == done || footer.contains(widget) || headerActions.contains(widget)) return SettingsText.label(widget.getMessage());
        return SettingsText.value(widget);
    }

    public int controlTextSize() {
        return density().body() * 2;
    }

    private int outerX() {
        int canvasWidth = Math.round(screen.width / scale());
        return compact() ? 32 : Math.max(72, (canvasWidth - 208 - 28 - 800) / 2);
    }

    private boolean toggle(AbstractWidget widget) {
        return widget instanceof CycleButton<?> cycle && cycle.getValue() instanceof Boolean;
    }

    private int fieldWidth(AbstractWidget widget) {
        if (widget instanceof NativeOptionPresentation option
            && SettingsText.hasKey(option.rodel$caption(), "options.fullscreen.exclusive.mode")) return 280;
        if (density().tight()) return toggle(widget) ? 80 : widget instanceof AbstractSliderButton ? 200 : 140;
        return toggle(widget) ? 104 : widget instanceof AbstractSliderButton ? 280 : 200;
    }

    private int fieldSlot(int available) {
        return density().tight() ? available < p(500) ? 140 : 200 : available < p(500) ? 160 : 280;
    }

    private void configureForm() {
        rowWidths.clear();
        for (AbstractWidget widget : formWidgets) {
            rowWidths.put(widget, rowWidth());
        }
        if (options != null) {
            for (OptionsList.AbstractEntry entry : options.children()) {
                int width = grid(entry) ? (rowWidth() - p(32)) / 2 : rowWidth();
                for (var child : entry.children()) {
                    if (child instanceof AbstractWidget widget) rowWidths.put(widget, width);
                }
            }
        }
    }

    public Component fitValue(AbstractWidget widget, Component full, float availableInFontUnits) {
        return fitText(full, availableInFontUnits, widget.isHovered() || widget.isFocused() && focusVisible());
    }

    private Component fitText(Component full, float availableInFontUnits, boolean tooltip) {
        var font = Minecraft.getInstance().font;
        if (font.width(full) <= availableInFontUnits) return full;
        if (tooltip) trimmedTooltip = full;
        String prefix = font.substrByWidth(full,
            Math.max(1, (int) availableInFontUnits - font.width("…"))).getString();
        return MenuText.styled(Component.literal(prefix + "…"));
    }

    public Component fitLabel(Component full, float available, boolean tooltip) { return fitText(full, available, tooltip); }

    private RowInfo measure(AbstractWidget widget) {
        Component caption = SettingsText.label(caption(widget));
        Component description = SettingsText.description(widget);
        boolean brief = description.getString().isBlank();
        boolean inline = density().tight() || !compact() || brief && toggle(widget);
        int available = rowWidths.getOrDefault(widget, rowWidth());
        int width = inline ? available - p(fieldSlot(available) + 24) : available;
        RowInfo old = measurements.get(widget);
        String captionText = caption.getString();
        String descriptionText = description.getString();
        if (old != null && old.width == width && old.scale == scale()
            && old.captionText.equals(captionText) && old.descriptionText.equals(descriptionText)
            && old.description.equals(description)) {
            return old;
        }
        var font = Minecraft.getInstance().font;
        int labelSize = p(controlTextSize());
        int descriptionSize = density().detail();
        List<FormattedCharSequence> labels = font.split(MenuText.styled(caption),
            Math.max(1, Math.round(width * (float) font.lineHeight / labelSize)));
        List<FormattedCharSequence> details = descriptionText.isBlank() ? List.of() :
            font.split(MenuText.secondary(description), Math.max(1, Math.round(width * (float) font.lineHeight / descriptionSize)));
        int textHeight = labels.size() * p(controlTextSize() * 1.5f);
        if (!details.isEmpty()) textHeight += p(density().tight() ? 6 : 8) + details.size() * Math.round(descriptionSize * 1.6f);
        int padding = density().tight() ? brief ? 4 : 8 : brief ? 6 : compact() ? 18 : 20;
        int controlHeight = density().tight() ? density().control() : p(toggle(widget) ? 44 : compact() ? 48 : 52);
        int height = inline ? p(padding * 2) + Math.max(controlHeight, textHeight)
            : p(padding * 2 + 14) + controlHeight + textHeight;
        RowInfo row = new RowInfo(captionText, descriptionText, description, width, scale(),
            labels, details, labelSize, descriptionSize, textHeight, height, inline, padding);
        measurements.put(widget, row);
        return row;
    }

    public int rowHeight(AbstractWidget widget) {
        if (isMainHand(widget)) return p(208);
        return measure(widget).height;
    }

    public void optionRow(GuiGraphicsExtractor graphics, OptionsList.Entry entry, AbstractWidget widget,
                          int mouseX, int mouseY) {
        int index = 0;
        int offsetY = 0;
        for (var child : entry.children()) {
            if (child == widget) break;
            if (child instanceof AbstractWidget previous) offsetY += rowHeight(previous);
            index++;
        }
        int width = grid(entry) ? (rowWidth() - p(32)) / 2 : rowWidth();
        int x = bodyX + p(28) + (grid(entry) ? index * (width + p(32)) : 0);
        int y = entry.getContentY() + (grid(entry) ? 0 : offsetY);
        row(graphics, widget, x, y, width, mouseX, mouseY);
    }

    public void row(GuiGraphicsExtractor graphics, AbstractWidget widget, int x, int y, int width,
                    int mouseX, int mouseY) {
        if (isMainHand(widget)) {
            Component title = MenuText.styled(caption(widget));
            float titleWidth = Minecraft.getInstance().font.width(title) * (float) p(controlTextSize()) / Minecraft.getInstance().font.lineHeight;
            MenuText.extract(graphics, Minecraft.getInstance().font, title,
                x + (width - titleWidth) / 2, y + p(8), p(controlTextSize()), theme().ink());
            int card = Math.min(p(160), (width - p(16)) / 2);
            widget.setRectangle(card * 2 + p(16), card, x + (width - card * 2 - p(16)) / 2, y + p(40));
            return;
        }
        RowInfo row = measure(widget);
        int padding = p(row.padding);
        int contentHeight = row.height - padding * 2;
        int textY = y + padding + (row.inline ? (contentHeight - row.textHeight) / 2 : 0);
        drawLines(graphics, row.labels, x, textY, row.labelSize, p(controlTextSize() * 1.5f),
            widget.active ? theme().ink() : theme().muted(), row.labels.size());
        int detailsY = textY + row.labels.size() * p(controlTextSize() * 1.5f) + p(density().tight() ? 6 : 8);
        drawLines(graphics, row.details, x, detailsY, row.descriptionSize, Math.round(row.descriptionSize * 1.6f), theme().muted(), row.details.size());
        int controlWidth = row.inline ? Math.min(p(fieldWidth(widget)), p(fieldSlot(width))) : width;
        int controlX = row.inline ? x + width - controlWidth : x;
        int controlHeight = density().tight() ? density().control() : p(toggle(widget) ? 44 : compact() ? 48 : 52);
        int controlY = row.inline ? y + padding + (contentHeight - controlHeight) / 2 : y + padding + row.textHeight + p(14);
        if (widget instanceof AbstractSliderButton) {
            Component value = MenuText.styled(controlText(widget));
            float textScale = (float) p(controlTextSize()) / Minecraft.getInstance().font.lineHeight;
            int valueWidth = Math.max(density().tight() ? density().body() : p(24), Math.min(controlWidth - p(64),
                (int) Math.ceil(Minecraft.getInstance().font.width(value) * textScale)));
            int trackWidth = controlWidth - valueWidth - p(density().tight() ? 4 : 12);
            widget.setRectangle(trackWidth, controlHeight, controlX, controlY);
            value = fitValue(widget, value, valueWidth / textScale);
            MenuText.extract(graphics, Minecraft.getInstance().font, value,
                controlX + controlWidth - Minecraft.getInstance().font.width(value) * textScale,
                controlY + (controlHeight - p(controlTextSize())) / 2f, p(controlTextSize()),
                widget.active ? theme().ink() : theme().muted());
        } else {
            widget.setRectangle(controlWidth, controlHeight, controlX, controlY);
        }
        divider(graphics, x, y + row.height - scale(), width);
    }

    private void drawLines(GuiGraphicsExtractor graphics, List<FormattedCharSequence> lines,
                           int x, int y, int size, int lineHeight, int color, int limit) {
        for (int i = 0; i < Math.min(limit, lines.size()); i++) {
            graphics.pose().pushMatrix();
            graphics.pose().translate(x, y + i * lineHeight + (lineHeight - size) / 2f);
            graphics.pose().scale((float) size / Minecraft.getInstance().font.lineHeight);
            graphics.text(Minecraft.getInstance().font, lines.get(i), 0, 0, color, false);
            graphics.pose().popMatrix();
        }
    }

    private record RowInfo(String captionText, String descriptionText, Component description, int width, float scale,
                           List<FormattedCharSequence> labels, List<FormattedCharSequence> details,
                           int labelSize, int descriptionSize, int textHeight, int height, boolean inline, int padding) {}

    public void group(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, AbstractWidget widget) {
        widget.setRectangle(rowWidth(), p(24), bodyX + p(28), entry.getContentY() + entry.getHeight() - p(28));
        MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(SettingsText.label(widget.getMessage())),
            widget.getX(), widget.getY(), density().tight() ? density().body() : p(16), theme().muted());
        if (widget.isFocused() && focusVisible()) {
            graphics.outline(widget.getX(), widget.getY(), rowWidth(), entry.getHeight(), theme().focus());
        }
    }

    public void chrome(GuiGraphicsExtractor graphics) {
        trimmedTooltip = null;
        if (packs != null) {
            packs.chrome(graphics);
            int x = back.getRight() + p(16);
            int right = headerActions.isEmpty() ? bodyX + bodyWidth : headerActions.getFirst().getX() - p(16);
            float textScale = density().heading() / (float) Minecraft.getInstance().font.lineHeight;
            Component title = fitLabel(MenuText.styled(screen.getTitle()), Math.max(1, right - x) / textScale, false);
            MenuText.extract(graphics, Minecraft.getInstance().font, title, x,
                back.getY() + (back.getHeight() - density().heading()) / 2f, density().heading(), theme().ink());
            return;
        }
        configureForm();
        if (specialized != null && layoutSpecialized()) {
            double scroll = specialized.scrollAmount();
            specialized.updateSizeAndPosition(bodyWidth, bodyHeight, bodyX, bodyY);
            specialized.setScrollAmount(scroll);
        }
        if (options != null) {
            boolean changed = false;
            for (OptionsList.AbstractEntry entry : options.children()) {
                if (entry instanceof OptionsList.Entry) {
                    int height = entryHeight(entry);

                    if (entry.getHeight() != height) {
                        entry.setHeight(height);
                        changed = true;
                    }
                }
            }
            if (changed) {
                double scroll = options.scrollAmount();
                options.updateSizeAndPosition(bodyWidth, bodyHeight, bodyX, bodyY);
                options.setScrollAmount(scroll);
            }
        }
        MenuTheme theme = theme();
        int outerX = outerX();
        int outerY = density().tight() ? 20 : compact() ? 28 : 48;
        if (packs == null) MenuText.extract(graphics, Minecraft.getInstance().font,
            MenuText.styled(Component.translatable("rodel.settings.title")), p(outerX + (density().tight() ? 44 : 64)), p(outerY + 5),
            density().tight() ? density().heading() : p(30), theme.ink());
        if (packs == null) theme.panel(graphics, bodyX, panelY, bodyWidth, panelHeight);
        else {
            graphics.fill(bodyX, panelY, bodyX + bodyWidth, panelY + panelHeight, theme.surface());
            graphics.outline(bodyX, panelY, bodyWidth, panelHeight, theme.edge());
        }
        var font = Minecraft.getInstance().font;
        int titleX = headerBack == null ? bodyX + p(24) : headerBack.getRight() + p(16);
        int titleWidth = headerActions.isEmpty() ? bodyX + bodyWidth - p(24) - titleX
            : headerActions.getFirst().getX() - p(24) - titleX;
        int headingSize = density().tight() ? 8 : p(22);
        int headingStep = density().tight() ? 11 : p(28);
        var titleLines = font.split(MenuText.styled(screen.getTitle()),
            Math.max(1, Math.round(titleWidth * (float) font.lineHeight / headingSize)));
        drawLines(graphics, titleLines, titleX,
            panelY + (headerBottom - panelY - titleLines.size() * headingStep) / 2,
            headingSize, headingStep, theme.ink(), titleLines.size());
        graphics.horizontalLine(bodyX, bodyX + bodyWidth - 1, headerBottom - 1, theme.edge());
        if (packs != null) packs.chrome(graphics);
        int noteY = packs != null && !compact() ? headerBottom + p(16)
            : headerBottom + searchBoxes.size() * p(packs != null ? 76 : 60);
        for (AbstractWidget widget : notices) {
            int available = packs == null ? rowWidth()
                : compact() ? bodyWidth - p(48) : (bodyWidth - p(64)) / 2;
            var lines = font.split(MenuText.secondary(widget.getMessage(), theme.muted()), Math.max(1,
                Math.round(available * (float) font.lineHeight / p(14))));
            int noteX = packs != null && !compact() ? bodyX + bodyWidth - p(24) - available
                : bodyX + p(packs != null ? 24 : 28);
            int textHeight = p(14) + Math.max(0, lines.size() - 1) * p(22);
            int y = packs != null && !compact() ? noteY + (p(44) - textHeight) / 2 : noteY;
            drawLines(graphics, lines, noteX, y, p(14), p(22), theme.muted(), lines.size());
            noteY += noteHeight(widget);
        }
    }

    public boolean ownsList(Object list) { return list == options || list == specialized || packs != null && packs.owns(list); }
    public int listRowWidth(Object list) { return packs != null && packs.owns(list) ? packs.rowWidth(list) : rowWidth(); }

    public GuiEventListener focusTarget(GuiEventListener widget) {
        return widget instanceof EditBox editor && searchBoxes.containsKey(editor) ? searchBoxes.get(editor) : widget;
    }

    public boolean ownsEntry(Object entry) {
        return specialized != null && specialized.children().contains(entry) || packs != null && packs.ownsEntry(entry);
    }

    private int noteHeight(AbstractWidget widget) {
        var font = Minecraft.getInstance().font;
        return font.split(MenuText.secondary(widget.getMessage()),
            Math.max(1, Math.round((packs == null ? rowWidth() : bodyWidth - p(48))
                * (float) font.lineHeight / p(14)))).size() * p(22) + p(8);
    }

    private boolean layoutSpecialized() {
        boolean changed = false;
        for (var entry : specialized.children()) {
            if (entry instanceof NativeSettingsRow row) {
                int height = row.rodel$height(this);
                if (entry.getHeight() != height) {
                    entry.setHeight(height);
                    changed = true;
                }
            }
        }
        return changed;
    }

    public void languageRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry,
                            Component language, String code, boolean hovered) {
        int x = bodyX + p(28);
        int y = entry.getContentY();
        int height = entry.getHeight() - p(4);
        boolean selected = specialized.getSelected() instanceof NativeLanguageChoice choice
            ? choice.rodel$languageCode().equals(code) : Minecraft.getInstance().options.languageCode.equals(code);
        if (selected || hovered) {
            graphics.fill(x, y, x + rowWidth(), y + height, selected ? theme().selection() : theme().hover());
        }
        if (selected || entry.isFocused() && focusVisible()) graphics.outline(x, y, rowWidth(), height, selected ? theme().focus() : theme().edge());
        Component display = fitText(MenuText.styled(language),
            (rowWidth() - p(32)) * (float) Minecraft.getInstance().font.lineHeight / p(controlTextSize()),
            hovered || entry.isFocused() && focusVisible());
        MenuText.extract(graphics, Minecraft.getInstance().font, display,
            x + p(16), y + (height - p(controlTextSize())) / 2f, p(controlTextSize()), theme().ink());
    }

    public int bindingHeight(Component name, AbstractWidget change, boolean collision) {
        captions.put(change, name);
        return Math.max(density().control() + density().gap() * 2, measure(change).height);
    }

    public void bindingRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, Component name,
                           Button change, Button reset, boolean collision, int mouseX, int mouseY, float partialTick) {
        literalControls.add(change);
        literalControls.add(reset);
        style(change);
        style(reset);
        RowInfo measurement = measure(change);
        int x = bodyX + p(28);
        int y = entry.getContentY();
        int slot = Math.min(p(fieldSlot(rowWidth())), rowWidth() / 2);
        int fieldX = x + rowWidth() - slot;
        int controlHeight = density().control();
        int controlY = y + (entry.getHeight() - controlHeight) / 2;
        int resetWidth = p(density().tight() ? 64 : 88);
        change.setRectangle(slot - resetWidth - p(12), controlHeight, fieldX, controlY);
        reset.setRectangle(resetWidth, controlHeight, fieldX + slot - resetWidth, controlY);
        drawLines(graphics, measurement.labels, x, y + (entry.getHeight() - measurement.textHeight) / 2,
            measurement.labelSize, p(controlTextSize() * 1.5f), collision ? theme().focus() : theme().ink(), measurement.labels.size());
        if (!measurement.details.isEmpty()) {
            drawLines(graphics, measurement.details, x,
                y + (entry.getHeight() - measurement.textHeight) / 2
                    + measurement.labels.size() * p(controlTextSize() * 1.5f) + p(density().tight() ? 6 : 8),
                measurement.descriptionSize, Math.round(measurement.descriptionSize * 1.6f), theme().muted(), measurement.details.size());
        }
        change.extractRenderState(graphics, mouseX, mouseY, partialTick);
        reset.extractRenderState(graphics, mouseX, mouseY, partialTick);
        divider(graphics, x, y + entry.getHeight() - scale(), rowWidth());
    }

    private void divider(GuiGraphicsExtractor graphics, int x, float y, int width) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(scale());
        graphics.fill(0, 0, Math.round(width / scale()), 1,
            theme().highContrast() ? theme().edge() : (theme().edge() & 0x00FFFFFF) | 0x50000000);
        graphics.pose().popMatrix();
    }

    public void openChoices(AbstractWidget widget) {
        choices.open(widget, screen.width, screen.height);
    }

    public boolean choicesOpen() { return choices.visible; }

    public boolean choiceClick(MouseButtonEvent event, boolean doubleClick) {
        return choices.mouseClicked(event, doubleClick);
    }

    public boolean choiceScroll(double x, double y, double horizontal, double vertical) {
        return choices.mouseScrolled(x, y, horizontal, vertical);
    }

    public boolean choiceKey(KeyEvent event) { return choices.keyPressed(event); }

    public boolean choiceDrag(MouseButtonEvent event, double x, double y) {
        return choices.mouseDragged(event, x, y);
    }

    public boolean choiceRelease(MouseButtonEvent event) { return choices.mouseReleased(event); }

    public void overlay(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        choices.overlay(graphics, mouseX, mouseY, partialTick);
        if (trimmedTooltip != null && !choices.visible) graphics.setTooltipForNextFrame(trimmedTooltip, mouseX, mouseY);
    }

    public void contextualTooltip(Component full) { trimmedTooltip = full; }

    public void packRow(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, PackSelectionModel.Entry model,
                        boolean hovered, int mouseX, int mouseY) {
        packs.row(graphics, entry, model, hovered, mouseX, mouseY);
    }

    public boolean isMainHand(Object widget) { return mainHand != null && widget == mainHand; }

    public void selectHand(HumanoidArm arm) {
        for (var choice : ((NativeCycleChoices) mainHand).rodel$choices()) {
            if (choice.value() == arm) {
                choice.select().run();
                return;
            }
        }
        throw new IllegalStateException("Native main-hand option does not offer " + arm);
    }

    public void handClick(double mouseX) {
        int card = mainHand.getHeight();
        double x = mouseX - mainHand.getX();
        if (x >= 0 && x < card) selectHand(HumanoidArm.LEFT);
        else if (x >= card + p(16) && x < mainHand.getWidth()) selectHand(HumanoidArm.RIGHT);
    }

    public void hands(GuiGraphicsExtractor graphics, AbstractWidget widget, int mouseX, int mouseY) {
        int side = Math.round(widget.getHeight() / scale());
        graphics.pose().pushMatrix();
        graphics.pose().translate(widget.getX(), widget.getY());
        graphics.pose().scale(scale());
        for (int i = 0; i < 2; i++) {
            HumanoidArm arm = i == 0 ? HumanoidArm.LEFT : HumanoidArm.RIGHT;
            int x = i * (side + 16);
            boolean selected = ((CycleButton<?>) widget).getValue() == arm;
            boolean hovered = mouseX >= widget.getX() + p(x) && mouseX < widget.getX() + p(x + side)
                && mouseY >= widget.getY() && mouseY < widget.getBottom();
            graphics.fill(x, 0, x + side, side, theme().surface());
            Identifier texture = Identifier.fromNamespaceAndPath("rodel_game_ui",
                "textures/gui/main_hand_" + (i == 0 ? "left" : "right") + ".png");
            int imageWidth = side - 16;
            int imageHeight = side - 48;
            int sourceHeight = Math.round(512f * imageHeight / imageWidth);
            graphics.blit(RenderPipelines.GUI_TEXTURED, texture, x + 8, 8, 0f, (512 - sourceHeight) / 2f,
                imageWidth, imageHeight, 512, sourceHeight, 512, 512);
            graphics.outline(x, 0, side, side, selected ? theme().focus() : hovered ? theme().ink() : theme().edge());
            if (selected) graphics.outline(x + 1, 1, side - 2, side - 2, theme().focus());
            if (widget.isFocused() && selected && focusVisible()) graphics.outline(x - 3, -3, side + 6, side + 6, theme().focus());
            Component text = MenuText.styled(arm.caption());
            float textScale = 18f / Minecraft.getInstance().font.lineHeight;
            MenuText.extract(graphics, Minecraft.getInstance().font, text,
                x + (side - Minecraft.getInstance().font.width(text) * textScale) / 2, side - 26, 18, theme().ink());
        }
        graphics.pose().popMatrix();
    }

    public void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                          int thumbHeight, double scroll, int maxScroll) {
        if (maxScroll <= 0) return;
        int top = y + (int) (scroll * (height - thumbHeight) / maxScroll);
        graphics.fill(x, y, x + width, y + height, (theme().edge() & 0x00FFFFFF) | 0x30000000);
        graphics.fill(x, top, x + width, top + thumbHeight,
            theme().highContrast() ? theme().muted() : (theme().muted() & 0x00FFFFFF) | 0xA0000000);
    }

    public void shade(GuiGraphicsExtractor graphics) {
        graphics.fill(0, 0, screen.width, screen.height,
            theme().highContrast() ? theme().surface() : theme().light() ? 0xA8FFF8E4 : 0xB809161B);
    }

    public int p(float value) {
        return Math.round(value * scale());
    }

    @Override
    public float scale() {
        return MenuContext.designScale();
    }

    @Override
    public boolean compact() {
        return MenuContext.compact(screen.width, screen.height);
    }

    @Override
    public MenuTheme theme() {
        return session.appearance.theme();
    }
}
