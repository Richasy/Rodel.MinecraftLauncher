package io.github.richasy.rodel.ui.menus;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.WeakHashMap;
import java.lang.ref.WeakReference;
import com.mojang.realmsclient.dto.RealmsServer;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractSliderButton;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.ImageWidget;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.TabButton;
import net.minecraft.client.gui.components.tabs.MenuTabBar;
import net.minecraft.client.gui.components.tabs.Tab;
import net.minecraft.client.gui.components.tabs.TabManager;
import net.minecraft.client.gui.components.tabs.TabNavigationBar;
import net.minecraft.client.gui.layouts.FrameLayout;
import net.minecraft.client.gui.layouts.HeaderAndFooterLayout;
import net.minecraft.client.gui.layouts.Layout;
import net.minecraft.client.gui.layouts.GridLayout;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.layouts.LayoutElement;
import net.minecraft.client.gui.layouts.LayoutSettings;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.gui.narration.NarratedElementType;
import net.minecraft.client.gui.navigation.ScreenRectangle;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Concrete online page compositions. Every field, list, tab and action remains the native object. */
public final class NetworkMenuLayouts {
    public interface ServerBrowser {
        Button rodel$join();
        Button rodel$edit();
        Button rodel$delete();
    }

    public interface RealmBrowser {
        Button rodel$play();
        Button rodel$back();
        Button rodel$renew();
        Button rodel$configure();
        Button rodel$leave();
        Button rodel$add();
    }

    public interface RealmManager {
        TabManager rodel$tabs();
        MenuTabBar rodel$bar();
        HeaderAndFooterLayout rodel$layout();
        Button rodel$play();
        RealmsServer rodel$server();
    }

    public interface Friends {
        TabManager rodel$tabs();
        TabNavigationBar rodel$bar();
        LinearLayout rodel$layout();
    }

    public interface FriendTab {
        LinearLayout rodel$layout();
        LinearLayout rodel$entries();
        ScrollableLayout rodel$scroll();
        FrameLayout rodel$emptyFrame();
        AbstractWidget rodel$addFriend();
    }

    public interface FriendComposer {
        void rodel$arrange(NativeMenuPage page, int width);
    }

    public interface InviteForm {
        Component rodel$nameLabel();
        Component rodel$status();
    }

    public interface WorldOptions {
        ScrollableLayout rodel$scroll();
        Button rodel$apply();
        default Button rodel$back() { return null; }
    }

    public interface RealmSettings {
        ScrollableLayout rodel$scroll();
    }

    public interface ScrollContent {
        Layout rodel$content();
    }

    private static final String GAME = "net.minecraft.client.gui.screens.";
    private static final String REALMS = "com.mojang.realmsclient.gui.screens.configuration.";
    private static final Map<Screen, Composition> COMPOSITIONS = new WeakHashMap<>();

    private record Label(Component text, int x, int y) {}
    private record Bounds(int x, int y, int width, int height) {}

    private static final class Composition {
        int left;
        int top;
        int width;
        int height;
        int headerY;
        PageHeader header;
        Bounds body;
        Bounds secondary;
        Component subtitle;
        Component status;
        int statusX;
        int statusY;
        int statusWidth;
        final List<Label> labels = new ArrayList<>();
        final Set<Component> replacedLabels = new HashSet<>();
        final List<WeakReference<AbstractWidget>> bodyContainers = new ArrayList<>();
        WeakReference<Tab> scrollingTab = new WeakReference<>(null);
        WeakReference<ScrollableLayout> bodyScroll = new WeakReference<>(null);
        int scrollWidth;
        final Map<Layout, List<WeakReference<LayoutElement>>> worldGroups = new WeakHashMap<>();
        final Map<AbstractWidget, Component> worldCaptions = new WeakHashMap<>();
        final Map<AbstractWidget, WeakReference<SliderValue>> sliderValues = new WeakHashMap<>();
        WeakReference<Button> navigation = new WeakReference<>(null);
        boolean arranging;
    }

    private NetworkMenuLayouts() {}

    public static boolean supports(Screen screen) {
        String name = screen.getClass().getName();
        return name.equals(GAME + "multiplayer.JoinMultiplayerScreen")
            || name.equals(GAME + "friends.FriendsOverlayScreen")
            || name.equals("com.mojang.realmsclient.RealmsMainScreen")
            || name.equals(REALMS + "RealmsConfigureWorldScreen")
            || name.equals(GAME + "WorldOptionsScreen")
            || name.equals(GAME + "options.InWorldGameRulesScreen")
            || form(screen);
    }

    /** Invoke after native init/reposition, instead of the generic viewport's layout branch. */
    public static boolean layout(NativeMenuPage page) {
        Screen screen = page.screen();
        if (!supports(screen)) return false;
        Composition state = COMPOSITIONS.computeIfAbsent(screen, ignored -> new Composition());
        if (state.arranging) return true;
        state.arranging = true;
        try {
            state.labels.clear();
            state.replacedLabels.clear();
            state.subtitle = null;
            state.status = null;
            state.secondary = null;
            state.header = null;
            state.left = screen.width < 450 ? 8 : 24;
            state.top = 12;
            state.width = screen.width - state.left * 2;
            state.height = screen.height - 24;
            state.headerY = state.top + 4;
            if (screen instanceof ServerBrowser access) serverBrowser(page, state, access);
            else if (screen instanceof Friends access) friends(page, state, access);
            else if (screen instanceof RealmBrowser access) realmBrowser(page, state, access);
            else if (screen instanceof RealmManager access) realmManager(page, state, access);
            else if (screen instanceof WorldOptions access) worldOptions(page, state, access);
            else if (screen.getClass().getName().equals(GAME + "options.InWorldGameRulesScreen")) inWorldRules(page, state);
            else onlineForm(page, state);
            hideReplacedLabels(screen, state);
            return true;
        } finally {
            state.arranging = false;
        }
    }

    private static void serverBrowser(NativeMenuPage page, Composition state, ServerBrowser access) {
        Screen screen = page.screen();
        int control = page.density().control();
        int gap = page.density().gap();
        int inset = page.density().inset();
        List<Button> buttons = buttons(screen);
        Button back = key(buttons, "gui.cancel", "gui.back");
        if (back != null) back(page, back, state.left, state.top);
        List<Button> tools = buttons.stream().filter(button -> button != back && button != access.rodel$join()
            && button != access.rodel$edit() && button != access.rodel$delete() && button.visible).toList();
        int bodyTop = state.top + control + gap;
        int toolsTop = bodyTop + inset;
        row(page, tools, state.left + inset, toolsTop, state.width - inset * 2, control, false);
        int listTop = toolsTop + (tools.isEmpty() ? 0 : control + gap);
        int footer = screen.height - state.top - control;
        state.body = new Bounds(state.left, bodyTop, state.width, Math.max(0, footer - gap - bodyTop));
        for (AbstractSelectionList<?> list : lists(screen))
            resize(list, state.left + inset, listTop, state.width - inset * 2,
                Math.max(0, footer - gap - inset - listTop));
        row(page, List.of(access.rodel$edit(), access.rodel$delete()), state.left, footer, Math.min(220, state.width / 2), control, false);
        place(page, access.rodel$join(), state.left + state.width - Math.min(150, state.width / 3),
            footer, Math.min(150, state.width / 3), control, true);
    }

    private static void realmBrowser(NativeMenuPage page, Composition state, RealmBrowser access) {
        Screen screen = page.screen();
        int control = page.density().control();
        int gap = page.density().gap();
        int inset = page.density().inset();
        List<Button> buttons = buttons(screen);
        if (access.rodel$back() != null) back(page, access.rodel$back(), state.left, state.top);
        Set<Button> actions = new HashSet<>();
        for (Button button : new Button[] {access.rodel$play(), access.rodel$back(), access.rodel$renew(),
            access.rodel$configure(), access.rodel$leave(), access.rodel$add()}) if (button != null) actions.add(button);
        List<Button> extras = buttons.stream().filter(button -> !actions.contains(button) && button.visible).toList();
        List<Button> header = extras.stream().filter(button -> button.getClass().getName().equals(
            "com.mojang.realmsclient.gui.RealmsHeader$NotificationButton")).toList();
        int headerHeight = Math.max(control, widgets(screen).stream()
            .filter(widget -> widget instanceof ImageWidget && widget.getHeight() <= 40)
            .mapToInt(AbstractWidget::getHeight).max().orElse(control));
        int headerWidth = header.size() * (control + gap) - gap;
        row(page, header, state.left + state.width - headerWidth, state.top + (headerHeight - control) / 2,
            headerWidth, control, false);
        List<Button> toolbar = new ArrayList<>();
        if (present(screen, access.rodel$add())) toolbar.add(access.rodel$add());
        extras.stream().filter(button -> !header.contains(button)).forEach(toolbar::add);
        List<Button> management = new ArrayList<>();
        if (present(screen, access.rodel$configure())) management.add(access.rodel$configure());
        if (present(screen, access.rodel$renew())) management.add(access.rodel$renew());
        if (present(screen, access.rodel$leave())) management.add(access.rodel$leave());
        int playWidth = Math.max(control, Math.min(page.density().tight() ? 100 : 150, state.width / 3));
        boolean play = present(screen, access.rodel$play());
        int managementWidth = management.stream().mapToInt(button -> actionWidth(page, button)).sum()
            + Math.max(0, management.size() - 1) * gap;
        boolean narrow = play && managementWidth + playWidth + gap > state.width;
        int playTop = screen.height - state.top - control;
        int footer = narrow ? playTop - control - gap : playTop;
        int bodyTop = state.top + headerHeight + gap;
        int toolsTop = bodyTop + inset;
        row(page, toolbar, state.left + inset, toolsTop,
            Math.min(state.width - inset * 2, toolbar.size() * 144), control, false);
        int listTop = toolsTop + (toolbar.isEmpty() ? 0 : control + gap);
        state.body = new Bounds(state.left, bodyTop, state.width, Math.max(0, footer - gap - bodyTop));
        List<AbstractSelectionList<?>> lists = lists(screen);
        for (AbstractSelectionList<?> list : lists)
            resize(list, state.left + inset, listTop, state.width - inset * 2,
                Math.max(0, footer - gap - inset - listTop));
        int actionWidth = narrow || !play ? state.width : state.width - playWidth - gap;
        row(page, management, state.left, footer, actionWidth, control, false);
        if (present(screen, access.rodel$play()))
            place(page, access.rodel$play(), state.left + state.width - playWidth, playTop, playWidth, control, true);
        // The native loading/empty illustrations and text are retained and centered inside the browser body.
        if (lists.isEmpty()) {
            List<AbstractWidget> content = widgets(screen).stream().filter(widget -> !(widget instanceof Button)
                && !(widget instanceof ImageWidget && widget.getY() < 44)).toList();
            shiftGroup(content, state.left + state.width / 2, listTop);
        }
        for (AbstractWidget widget : widgets(screen)) {
            if (widget instanceof ImageWidget && widget.getHeight() <= 40)
                widget.setPosition(state.left + 34, state.top + (headerHeight - widget.getHeight()) / 2);
        }
    }

    private static void realmManager(NativeMenuPage page, Composition state, RealmManager access) {
        Screen screen = page.screen();
        int gap = page.density().gap();
        int inset = page.density().inset();
        int controlHeight = page.density().control();
        state.top = inset;
        if (access.rodel$server() != null && access.rodel$server().name != null)
            state.subtitle = Component.literal(access.rodel$server().name);
        state.header = PageHeader.measure(page.density(), state.left, state.top, state.width, state.subtitle != null);
        int headerHeight = state.header.height();
        int footer = screen.height - state.top - controlHeight;
        MenuTabBar bar = access.rodel$bar();
        if (bar == null) {
            int bodyTop = state.top + headerHeight + gap;
            state.body = new Bounds(state.left, bodyTop, state.width, Math.max(0, footer - gap - bodyTop));
            access.rodel$layout().setHeaderHeight(bodyTop + inset);
            access.rodel$layout().setFooterHeight(screen.height - footer);
            managerActions(page, state, access, Set.of(), List.of(), footer);
            return;
        }
        boolean sidebar = screen.width >= 520;
        int navWidth = sidebar ? 112 : state.width;
        List<TabButton> tabs = bar.children().stream().filter(TabButton.class::isInstance).map(TabButton.class::cast).toList();
        int tabHeight = controlHeight;
        int navStride = tabHeight + gap;
        var font = Minecraft.getInstance().font;
        int minimumTabWidth = tabs.stream().mapToInt(tab -> (int) Math.ceil(
            font.width(MenuText.styled(tab.getMessage())) * page.density().body() / (float) font.lineHeight)
            + inset * 2).max().orElse(controlHeight);
        int columns = sidebar ? 1 : Math.max(1, Math.min(tabs.size(),
            (navWidth + gap) / (minimumTabWidth + gap)));
        int rows = (tabs.size() + columns - 1) / columns;
        int navHeight = rows * tabHeight + Math.max(0, rows - 1) * gap;
        int navTop = state.top + headerHeight + gap;
        bar.setRectangle(navWidth, navHeight, state.left, navTop);
        for (int i = 0; i < tabs.size(); i++) {
            int width = (navWidth - (columns - 1) * gap) / columns;
            tabs.get(i).setRectangle(width, tabHeight,
                state.left + i % columns * (width + gap), navTop + i / columns * navStride);
        }
        int bodyLeft = sidebar ? state.left + navWidth + gap : state.left;
        int bodyTop = sidebar ? navTop : navTop + navHeight + gap;
        int bodyWidth = sidebar ? state.width - navWidth - gap : state.width;
        int bodyHeight = Math.max(0, footer - gap - bodyTop);
        state.body = new Bounds(bodyLeft, bodyTop, bodyWidth, bodyHeight);
        int contentTop = bodyTop + inset;
        access.rodel$layout().setHeaderHeight(contentTop);
        access.rodel$layout().setFooterHeight(screen.height - footer);
        Tab current = access.rodel$tabs().getCurrentTab();
        List<Button> bodyActions = List.of();
        state.worldCaptions.clear();
        if (current != null) {
            ScreenRectangle area = new ScreenRectangle(bodyLeft + inset, contentTop, bodyWidth - inset * 2,
                Math.max(0, bodyTop + bodyHeight - inset - contentTop));
            access.rodel$tabs().setTabArea(area);
            if (current instanceof RealmSettings settings) {
                ScrollableLayout scroll = settings.rodel$scroll();
                worldOptionGroup(page, state, ((ScrollContent) scroll).rodel$content(), Math.max(120, area.width() - 10));
                scroll.setMinHeight(0);
                scroll.setMaxHeight(area.height());
                scroll.setScrollbarSpacing(4);
            }
            current.getLayout().arrangeElements();
            List<AbstractWidget> content = new ArrayList<>();
            current.visitChildren(content::add);
            List<AbstractSelectionList<?>> lists = content.stream().filter(AbstractSelectionList.class::isInstance)
                .<AbstractSelectionList<?>>map(widget -> (AbstractSelectionList<?>) widget).toList();
            List<com.mojang.realmsclient.gui.RealmsWorldSlotButton> slots = content.stream()
                .filter(com.mojang.realmsclient.gui.RealmsWorldSlotButton.class::isInstance)
                .map(com.mojang.realmsclient.gui.RealmsWorldSlotButton.class::cast).toList();
            if (!lists.isEmpty()) {
                bodyActions = content.stream().filter(Button.class::isInstance).map(Button.class::cast)
                    .filter(button -> button.visible).toList();
                int naturalHeight = 0;
                for (AbstractSelectionList<?> list : lists) {
                    for (var entry : list.children()) entry.setHeight(entry.getClass().getSimpleName().equals("HeaderEntry")
                        ? page.density().body() + gap : page.density().row());
                    // AbstractSelectionList.contentHeight adds four native GUI units around its entries.
                    naturalHeight = Math.max(naturalHeight, list.children().stream().mapToInt(entry -> entry.getHeight()).sum() + 4);
                }
                int listHeight = Math.min(area.height(), naturalHeight);
                for (AbstractSelectionList<?> list : lists) {
                    resize(list, area.left(), area.top(), area.width(), listHeight);
                }
                state.body = new Bounds(bodyLeft, bodyTop, bodyWidth, listHeight + inset * 2);
                footer = bodyTop + state.body.height + gap;
                managerViewport(screen, state, current, area, content, false);
            } else if (!slots.isEmpty()) {
                bodyActions = content.stream().filter(Button.class::isInstance).map(Button.class::cast)
                    .filter(button -> button.visible && !slots.contains(button)).toList();
                int side = Math.min(page.density().row() * 2, area.width());
                int slotColumns = Math.max(1, Math.min(slots.size(), (area.width() + gap) / (side + gap)));
                int slotRows = (slots.size() + slotColumns - 1) / slotColumns;
                int slotStride = side + page.density().detail() + gap;
                int naturalHeight = slotRows * slotStride;
                // Card dimensions use the available tab area; they must not consume the action row.
                if (naturalHeight > area.height()) {
                    side = Math.max(0, (area.height() / slotRows) - page.density().detail() - gap);
                    slotStride = side + page.density().detail() + gap;
                    naturalHeight = Math.min(area.height(), slotRows * slotStride);
                }
                int slotLeft = area.left() + (area.width() - slotColumns * side - (slotColumns - 1) * gap) / 2;
                for (int i = 0; i < slots.size(); i++) slots.get(i).setRectangle(side, side,
                    slotLeft + i % slotColumns * (side + gap), area.top() + i / slotColumns * slotStride);
                state.body = new Bounds(bodyLeft, bodyTop, bodyWidth, naturalHeight + inset * 2);
                footer = bodyTop + state.body.height + gap;
                managerViewport(screen, state, current, area, content, false);
            } else {
                if (current.getLayout().getWidth() > area.width()) {
                    compactManagerGrids(current.getLayout());
                    current.getLayout().arrangeElements();
                }
                if (current.getLayout().getWidth() > area.width()) {
                    float ratio = area.width() / (float) current.getLayout().getWidth();
                    for (AbstractWidget widget : content) {
                        int width = Math.max(24, Math.round(widget.getWidth() * ratio));
                        if (widget instanceof StringWidget label) label.setMaxWidth(width, StringWidget.TextOverflow.SCROLLING);
                        else if (widget instanceof com.mojang.realmsclient.gui.RealmsWorldSlotButton)
                            widget.setSize(width, width);
                        else if (!(widget instanceof ImageWidget)) widget.setWidth(width);
                    }
                    current.getLayout().arrangeElements();
                }
                FrameLayout.alignInRectangle(current.getLayout(), area, .5f, 0);
                managerViewport(screen, state, current, area, content, current.getLayout().getHeight() > area.height());
            }
        }
        Set<AbstractWidget> bodyWidgets = new HashSet<>();
        if (current != null) current.visitChildren(bodyWidgets::add);
        managerActions(page, state, access, bodyWidgets, bodyActions, footer);
    }

    private static void managerActions(NativeMenuPage page, Composition state, RealmManager access,
                                       Set<AbstractWidget> bodyWidgets, List<Button> bodyActions, int footer) {
        List<Button> outer = buttons(page.screen());
        Button back = outer.stream().filter(button -> !bodyWidgets.contains(button) && button != access.rodel$play()).findFirst().orElse(null);
        if (back != null) back(page, back, state.left, state.header.controlY());
        int playWidth = access.rodel$play() == null ? 0 : Math.min(state.width,
            Math.max(page.density().control(), actionWidth(page, access.rodel$play())));
        int available = state.width - playWidth - (playWidth > 0 ? page.density().gap() : 0);
        int naturalWidth = bodyActions.stream().mapToInt(button -> actionWidth(page, button)).sum()
            + Math.max(0, bodyActions.size() - 1) * page.density().gap();
        if (naturalWidth <= available) {
            int x = state.left;
            for (Button action : bodyActions) {
                int width = actionWidth(page, action);
                place(page, action, x, footer, width, page.density().control(), false);
                x += width + page.density().gap();
            }
        } else row(page, bodyActions, state.left, footer, available, page.density().control(), false);
        if (access.rodel$play() != null) {
            place(page, access.rodel$play(), state.left + state.width - playWidth, footer, playWidth, page.density().control(), true);
        }
    }

    private static void compactManagerGrids(Layout layout) {
        if (layout instanceof GridLayout grid) grid.columnSpacing(8).rowSpacing(12);
        layout.visitChildren(child -> {
            if (child instanceof Layout nested) compactManagerGrids(nested);
        });
    }

    private static void managerViewport(Screen screen, Composition state, Tab tab, ScreenRectangle area,
                                        List<AbstractWidget> nativeWidgets, boolean scroll) {
        var widgets = NativeWidgets.get(screen);
        for (WeakReference<AbstractWidget> reference : state.bodyContainers) {
            AbstractWidget container = reference.get();
            if (container != null) widgets.remove(container);
        }
        state.bodyContainers.clear();
        if (!scroll) {
            for (AbstractWidget widget : nativeWidgets) if (!widgets.contains(widget)) widgets.add(widget);
            return;
        }
        ScrollableLayout viewport = state.bodyScroll.get();
        if (viewport == null || state.scrollingTab.get() != tab || state.scrollWidth != area.width()) {
            FrameLayout frame = new FrameLayout(Math.max(24, area.width() - 10), 0);
            frame.defaultChildLayoutSetting().alignHorizontallyCenter().alignVerticallyTop();
            frame.addChild(tab.getLayout());
            viewport = new ScrollableLayout(Minecraft.getInstance(), frame, area.height());
            state.bodyScroll = new WeakReference<>(viewport);
            state.scrollingTab = new WeakReference<>(tab);
            state.scrollWidth = area.width();
        }
        viewport.setMinHeight(0);
        viewport.setMaxHeight(area.height());
        viewport.setScrollbarSpacing(4);
        viewport.arrangeElements();
        viewport.setX(area.left());
        viewport.setY(area.top());
        widgets.removeAll(nativeWidgets);
        viewport.visitWidgets(container -> {
            if (!widgets.contains(container)) widgets.add(container);
            state.bodyContainers.add(new WeakReference<>(container));
        });
    }

    private static void friends(NativeMenuPage page, Composition state, Friends access) {
        Screen screen = page.screen();
        Tab current = access.rodel$tabs().getCurrentTab();
        if (!(current instanceof FriendTab view) || access.rodel$bar() == null) return;
        int gap = page.density().gap();
        int inset = page.density().inset();
        int control = page.density().control();
        state.width = Math.min(screen.width - inset * 2, 320 + inset * 2);
        state.left = (screen.width - state.width) / 2;
        state.top = inset;
        state.header = PageHeader.measure(page.density(), state.left, state.top, state.width, false);
        int contentOffset = state.header.height() + gap + control + gap;
        int contentWidth = state.width - inset * 2;
        fitFriendTab(page, view, Math.max(0, screen.height - state.top - inset - contentOffset - inset * 2), contentWidth);
        access.rodel$layout().arrangeElements();
        int nativeWidth = access.rodel$layout().getWidth();
        int contentHeight = access.rodel$layout().getHeight();
        state.height = contentHeight + contentOffset + inset * 2;
        int contentTop = state.top + contentOffset + inset;
        access.rodel$layout().setX(state.left + (state.width - nativeWidth) / 2);
        access.rodel$layout().setY(contentTop);
        TabNavigationBar bar = access.rodel$bar();
        int tabsWidth = state.width;
        bar.setRectangle(tabsWidth, control, state.left, state.top + state.header.height() + gap);
        List<? extends net.minecraft.client.gui.components.events.GuiEventListener> tabs = bar.children();
        for (int i = 0; i < tabs.size(); i++)
            if (tabs.get(i) instanceof AbstractWidget widget)
                widget.setRectangle((tabsWidth - gap) / 2, control,
                    bar.getX() + i * ((tabsWidth - gap) / 2 + gap), bar.getY());
        state.body = new Bounds(state.left, state.top + contentOffset, state.width, contentHeight + inset * 2);
        if (view.rodel$addFriend() != null) {
            AbstractWidget add = view.rodel$addFriend();
            state.secondary = new Bounds(state.left + inset, add.getY() - gap / 2, state.width - inset * 2, 1);
        }
        navigation(page, state);
    }

    private static void fitFriendTab(NativeMenuPage page, FriendTab view, int maximum, int width) {
        int gap = page.density().gap();
        AbstractWidget add = view.rodel$addFriend();
        if (add instanceof FriendComposer composer) composer.rodel$arrange(page, width);
        FrameLayout frame = view.rodel$emptyFrame();
        if (frame != null) {
            frame.setMinHeight(Math.min(maximum, page.density().row() * 2));
            frame.arrangeElements();
        }
        view.rodel$entries().visitWidgets(widget -> {
            if (widget instanceof net.minecraft.client.gui.components.AbstractContainerWidget) {
                widget.setWidth(width);
                NetworkRowLayout.arrangeFriend(page, widget);
            }
        });
        if (!NetworkRowLayout.arrangeFriendSections(page, view.rodel$entries(), width))
            view.rodel$entries().spacing(gap).arrangeElements();
        ScrollableLayout scroll = view.rodel$scroll();
        int addHeight = add == null ? 0 : add.getHeight() + gap;
        scroll.setMinWidth(0);
        scroll.setMinHeight(0);
        scroll.setMaxHeight(Math.max(0, maximum - addHeight));
        scroll.arrangeElements();
        if (scroll.getWidth() > width) {
            int rowWidth = Math.max(0, width - (scroll.getWidth() - width));
            view.rodel$entries().visitWidgets(widget -> {
                if (widget instanceof net.minecraft.client.gui.components.AbstractContainerWidget
                    || widget instanceof net.minecraft.client.gui.components.FocusableTextWidget) widget.setWidth(rowWidth);
            });
            scroll.arrangeElements();
        }
        LinearLayout layout = view.rodel$layout();
        layout.removeChildren();
        layout.spacing(gap);
        layout.addChild(scroll);
        if (add != null) layout.addChild(add);
        layout.arrangeElements();
    }

    private static void worldOptions(NativeMenuPage page, Composition state, WorldOptions access) {
        Screen screen = page.screen();
        ScrollableLayout scroll = access.rodel$scroll();
        if (scroll == null) return;
        int control = page.density().control();
        int gap = page.density().gap();
        int inset = page.density().inset() * 2;
        state.width = Math.min(456, screen.width - 24);
        state.left = (screen.width - state.width) / 2;
        Layout content = ((ScrollContent) scroll).rodel$content();
        state.worldCaptions.clear();
        List<LayoutElement> sections = originalElements(state, content);
        int available = state.width - inset * 2;
        int width = available;
        scroll.setMinWidth(0);
        scroll.setMinHeight(0);
        int footerHeight = access.rodel$apply() == null ? 0 : control + gap;
        scroll.setMaxHeight(Math.max(0, screen.height - 24 - control - gap - footerHeight - inset * 2));
        scroll.setScrollbarSpacing(4);
        for (int pass = 0; pass < 3; pass++) {
            if (!(content instanceof LinearLayout column))
                throw new IllegalStateException("Native world options no longer use a column layout");
            column.removeChildren();
            column.defaultCellSetting().padding(0).alignHorizontallyLeft();
            column.spacing(18);
            for (LayoutElement section : sections) {
                if (section instanceof GridLayout group) {
                    worldOptionGroup(page, state, group, width);
                    column.addChild(group);
                } else if (section instanceof net.minecraft.client.gui.components.AbstractStringWidget heading) {
                    heading.visible = false;
                    column.addChild(new WorldText(page, section, heading.getMessage(), Component.empty(), width, true));
                } else column.addChild(section);
            }
            scroll.arrangeElements();
            int overflow = scroll.getWidth() - available;
            if (overflow <= 0) break;
            width = Math.max(120, width - overflow);
        }
        int bodyHeight = scroll.getHeight() + inset * 2;
        state.height = bodyHeight + control + gap + footerHeight;
        state.top = Math.max(12, (screen.height - state.height) / 2);
        state.headerY = state.top + (control - page.density().heading()) / 2;
        int bodyTop = state.top + control + gap;
        scroll.setPosition(state.left + inset, bodyTop + inset);
        state.body = new Bounds(state.left, bodyTop, state.width, bodyHeight);
        List<Button> buttons = buttons(screen);
        Button back = access.rodel$back() != null ? access.rodel$back()
            : buttons.stream().filter(button -> button != access.rodel$apply()).findFirst().orElse(null);
        if (back != null) back(page, back, state.left, state.top);
        if (access.rodel$apply() != null)
            place(page, access.rodel$apply(), state.left + state.width - 140, state.body.y + bodyHeight + gap, 140, control, true);
    }

    private static List<LayoutElement> originalElements(Composition state, Layout group) {
        List<WeakReference<LayoutElement>> original = state.worldGroups.get(group);
        if (original == null) {
            original = new ArrayList<>();
            state.worldGroups.put(group, original);
        }
        Set<LayoutElement> known = java.util.Collections.newSetFromMap(new java.util.IdentityHashMap<>());
        for (WeakReference<LayoutElement> reference : original) {
            LayoutElement element = reference.get();
            if (element != null) known.add(element);
        }
        List<WeakReference<LayoutElement>> saved = original;
        group.visitChildren(child -> {
            if (!(child instanceof WorldOptionRow) && !(child instanceof RegionOptionRow) && !(child instanceof WorldText) && known.add(child))
                saved.add(new WeakReference<>(child));
        });
        List<LayoutElement> elements = original.stream().map(WeakReference::get).filter(java.util.Objects::nonNull).toList();
        return elements;
    }

    private static void worldOptionGroup(NativeMenuPage page, Composition state, Layout group, int width) {
        List<LayoutElement> elements = originalElements(state, group);
        group.removeChildren();
        if (group instanceof GridLayout grid) grid.rowSpacing(8).columnSpacing(0);
        if (group instanceof LinearLayout column) {
            column.defaultCellSetting().padding(0);
            column.spacing(10);
        }
        int row = 0;
        for (LayoutElement element : elements) {
            List<AbstractWidget> controls = new ArrayList<>();
            if (element instanceof AbstractWidget widget) controls.add(widget);
            else if (element instanceof Layout nested) nested.visitWidgets(controls::add);
            AbstractWidget primary = controls.stream().filter(widget -> !NativeMenuPage.iconOnly(widget)
                && (widget instanceof CycleButton<?> || widget instanceof EditBox || widget instanceof Button
                    || widget instanceof AbstractSliderButton)).findFirst().orElse(null);
            List<StringWidget> nativeLabels = controls.stream().filter(StringWidget.class::isInstance).map(StringWidget.class::cast).toList();
            ImageWidget nativeImage = controls.stream().filter(ImageWidget.class::isInstance).map(ImageWidget.class::cast).findFirst().orElse(null);
            if (group instanceof LinearLayout && nativeImage != null && nativeLabels.size() == 2 && primary instanceof Button button) {
                nativeLabels.getFirst().visible = false;
                addOptionRow(group, new RegionOptionRow(page, element, nativeLabels.getFirst().getMessage(),
                    nativeLabels.get(1), nativeImage, button, width), row++);
                continue;
            }
            if (group instanceof LinearLayout && (primary instanceof Button && !(primary instanceof CycleButton<?>)
                || nativeImage != null)) {
                if (element instanceof AbstractWidget widget && widget instanceof Button)
                    widget.setSize(width, page.density().control());
                addOptionRow(group, element, row++);
                continue;
            }
            if (primary == null) {
                if (element instanceof net.minecraft.client.gui.components.AbstractStringWidget heading) {
                    heading.visible = false;
                    addOptionRow(group, new WorldText(page, element, heading.getMessage(), Component.empty(), width, true), row++);
                } else addOptionRow(group, element, row++);
                continue;
            }
            Component caption = page.caption(primary);
            if (caption == null) caption = primary.getMessage();
            if (primary instanceof EditBox) {
                Component nativeLabel = controls.stream().filter(widget -> widget instanceof net.minecraft.client.gui.components.AbstractStringWidget)
                    .map(AbstractWidget::getMessage).filter(text -> !text.getString().isBlank()).findFirst().orElse(null);
                if (nativeLabel != null) caption = nativeLabel;
            }
            var description = Component.empty().append(SettingsText.description(primary));
            for (AbstractWidget widget : controls) {
                if (!(widget instanceof net.minecraft.client.gui.components.AbstractStringWidget)) continue;
                Component text = widget.getMessage();
                if (caption.getString().isBlank()) caption = text;
                else if (!text.getString().isBlank() && !text.getString().equals(caption.getString())
                    && !description.getString().contains(text.getString())) {
                    if (!description.getString().isBlank()) description.append("\n");
                    description.append(text);
                }
                widget.visible = false;
            }
            AbstractWidget lock = controls.stream().filter(widget -> widget instanceof net.minecraft.client.gui.components.LockIconButton)
                .findFirst().orElse(null);
            state.worldCaptions.put(primary, caption);
            addOptionRow(group, new WorldOptionRow(page, element, caption, description, primary, lock, width), row++);
        }
        group.arrangeElements();
    }

    private static void addOptionRow(Layout layout, LayoutElement element, int row) {
        if (layout instanceof GridLayout grid)
            grid.addChild(element, row, 0, LayoutSettings.defaults().alignHorizontallyLeft());
        else if (layout instanceof LinearLayout column) column.addChild(element);
        else throw new IllegalArgumentException("Unsupported native option layout: " + layout.getClass().getName());
    }

    /** Null outside native caption/value rows, including navigation and footer actions. */
    public static Component worldOptionCaption(Screen screen, AbstractWidget widget) {
        Composition state = COMPOSITIONS.get(screen);
        return state == null ? null : state.worldCaptions.get(widget);
    }

    private static void inWorldRules(NativeMenuPage page, Composition state) {
        Screen screen = page.screen();
        int control = page.density().control();
        int gap = page.density().gap();
        int inset = page.density().inset();
        List<Button> buttons = buttons(screen);
        Button back = key(buttons, "gui.cancel", "gui.back");
        if (back != null) back(page, back, state.left, state.top);
        int footer = screen.height - state.top - control;
        int bodyTop = state.top + control + gap;
        int searchTop = bodyTop + inset;
        int listTop = searchTop + control + gap;
        state.body = new Bounds(state.left, bodyTop, state.width, Math.max(0, footer - gap - bodyTop));
        for (AbstractWidget widget : widgets(screen)) {
            if (widget instanceof EditBox)
                widget.setRectangle(state.width - inset * 2, control, state.left + inset, searchTop);
            else if (widget instanceof AbstractSelectionList<?> list)
                resize(list, state.left + inset, listTop, state.width - inset * 2,
                    Math.max(0, footer - gap - inset - listTop));
            else if (widget instanceof net.minecraft.client.gui.components.LoadingDotsWidget)
                widget.setPosition(state.left + (state.width - widget.getWidth()) / 2,
                    listTop + Math.max(0, (footer - gap - inset - listTop - widget.getHeight()) / 2));
        }
        List<Button> actions = buttons.stream().filter(button -> button != back).toList();
        row(page, actions, state.left + state.width - Math.min(160, state.width), footer, Math.min(160, state.width), control, true);
    }

    private static final class WorldOptionRow implements Layout {
        private WorldText text;
        private AbstractWidget control;
        private AbstractWidget lock;
        private SliderValue sliderValue;
        private final int width;
        private int height;
        private final int controlsWidth;
        private final int controlHeight;
        private int x;
        private int y;

        WorldOptionRow(NativeMenuPage page, LayoutElement source, Component caption, Component description,
                       AbstractWidget control, AbstractWidget lock, int width) {
            this.control = control instanceof EditBox editor
                ? new io.github.richasy.rodel.ui.settings.NativeTextBox(page, editor) : control;
            this.lock = lock;
            this.width = width;
            controlsWidth = page.density().tight() ? Math.min(120, Math.max(88, Math.round(width * .35f)))
                : Math.min(160, Math.max(100, Math.round(width * .4f)));
            controlHeight = page.density().control();
            text = new WorldText(page, source, caption, description, Math.max(72, width - controlsWidth - 20), false);
            if (control instanceof AbstractSliderButton) sliderValue = new SliderValue(page, control);
            height = Math.max(controlHeight, text.getHeight()) + (page.density().tight() ? 8 : 12);
        }

        @Override public int getX() { return x; }
        @Override public int getY() { return y; }
        @Override public int getWidth() { return width; }
        @Override public int getHeight() { return height; }
        @Override public void setX(int value) { x = value; arrangeElements(); }
        @Override public void setY(int value) { y = value; arrangeElements(); }

        @Override
        public void arrangeElements() {
            if (control == null) return;
            text.setPosition(x, y + (height - text.getHeight()) / 2);
            int controlX = x + width - controlsWidth;
            int controlY = y + (height - controlHeight) / 2;
            int lockSpace = lock == null ? 0 : controlHeight + 6;
            int valueWidth = sliderValue == null ? 0 : Math.min(64, controlsWidth / 2);
            control.setRectangle(controlsWidth - lockSpace - valueWidth, controlHeight, controlX, controlY);
            if (sliderValue != null) sliderValue.setRectangle(valueWidth - 6, controlHeight, control.getRight() + 6, controlY);
            if (lock != null) lock.setRectangle(controlHeight, controlHeight, x + width - controlHeight, controlY);
        }

        @Override
        public void visitChildren(java.util.function.Consumer<LayoutElement> visitor) {
            if (text != null) visitor.accept(text);
            if (control != null) visitor.accept(control);
            if (lock != null) visitor.accept(lock);
            if (sliderValue != null) visitor.accept(sliderValue);
        }

        @Override
        public void removeChildren() {
            if (control != null) control.setFocused(false);
            if (lock != null) lock.setFocused(false);
            text = null;
            control = null;
            lock = null;
            sliderValue = null;
            height = 0;
        }
    }

    private static final class SliderValue extends AbstractWidget {
        private final NativeMenuPage page;
        private final AbstractWidget slider;

        SliderValue(NativeMenuPage page, AbstractWidget slider) {
            super(0, 0, 1, 24, Component.empty());
            this.page = page;
            this.slider = slider;
            active = false;
        }

        @Override
        protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
            Component value = MenuText.styled(SettingsText.formattedValue(slider.getMessage()));
            int size = page.p(page.controlTextSize());
            float scale = size / (float) Minecraft.getInstance().font.lineHeight;
            value = page.fitValue(slider, value, getWidth() / scale);
            page.beginNativeText();
            try {
                MenuText.extract(graphics, Minecraft.getInstance().font, value,
                    getX(),
                    getY() + (getHeight() - size) / 2f, size, page.theme().ink());
            } finally {
                page.endNativeText();
            }
        }

        @Override protected void updateWidgetNarration(NarrationElementOutput output) {}
    }

    private static final class RegionOptionRow implements Layout {
        private final WorldText caption;
        private final StringWidget value;
        private final ImageWidget quality;
        private final Button action;
        private final int width;
        private final int controlWidth;
        private final int labelWidth;
        private final int controlHeight;
        private final int gap;
        private int x;
        private int y;

        RegionOptionRow(NativeMenuPage page, LayoutElement source, Component title, StringWidget value,
                        ImageWidget quality, Button action, int width) {
            this.value = value;
            this.quality = quality;
            this.action = action;
            this.width = width;
            controlWidth = page.density().tight() ? Math.min(120, Math.max(88, Math.round(width * .35f)))
                : Math.min(160, Math.max(100, Math.round(width * .4f)));
            controlHeight = page.density().control();
            gap = page.density().gap();
            labelWidth = Math.max(1, width - controlWidth - gap * 2);
            caption = new WorldText(page, source, title, Component.empty(), labelWidth, false);
            value.setMaxWidth(Math.max(24, labelWidth - quality.getWidth() - 6), StringWidget.TextOverflow.SCROLLING);
        }

        @Override public int getX() { return x; }
        @Override public int getY() { return y; }
        @Override public int getWidth() { return width; }
        @Override public int getHeight() { return Math.max(controlHeight, caption.getHeight() + 4 + value.getHeight()) + gap * 2; }
        @Override public void setX(int next) { x = next; arrangeElements(); }
        @Override public void setY(int next) { y = next; arrangeElements(); }

        @Override
        public void arrangeElements() {
            int top = y + (getHeight() - caption.getHeight() - 4 - value.getHeight()) / 2;
            caption.setPosition(x, top);
            value.setPosition(x, top + caption.getHeight() + 4);
            quality.setPosition(x + labelWidth - quality.getWidth(), value.getY() + (value.getHeight() - quality.getHeight()) / 2);
            action.setRectangle(controlWidth, controlHeight, x + width - controlWidth, y + (getHeight() - controlHeight) / 2);
        }

        @Override
        public void visitChildren(java.util.function.Consumer<LayoutElement> visitor) {
            visitor.accept(caption);
            visitor.accept(value);
            visitor.accept(quality);
            visitor.accept(action);
        }

        @Override public void removeChildren() { action.setFocused(false); }
    }

    private static final class WorldText extends AbstractWidget {
        private final NativeMenuPage page;
        // The original composite must survive subsequent relayouts, even though only its controls are displayed.
        @SuppressWarnings("unused")
        private final LayoutElement nativeSource;
        private final List<net.minecraft.util.FormattedCharSequence> caption;
        private final List<net.minecraft.util.FormattedCharSequence> description;
        private final boolean section;
        private final int captionSize;
        private final int captionStep;
        private final int detailSize;
        private final int detailStep;

        WorldText(NativeMenuPage page, LayoutElement source, Component title, Component detail, int width, boolean section) {
            super(0, 0, width, 1, detail.getString().isBlank() ? title : Component.empty().append(title).append("\n").append(detail));
            this.page = page;
            nativeSource = source;
            this.section = section;
            captionSize = section ? page.density().tight() ? 7 : 10 : page.density().body();
            captionStep = captionSize + 2;
            detailSize = page.density().tight() ? page.density().detail() : 8;
            detailStep = detailSize + 2;
            Font font = Minecraft.getInstance().font;
            caption = font.split(MenuText.styled(section ? title.copy().withStyle(style -> style.withUnderlined(false)) : title),
                Math.max(1, Math.round(width * 9f / captionSize)));
            description = detail.getString().isBlank() ? List.of()
                : font.split(MenuText.secondary(detail), Math.max(1, Math.round(width * 9f / detailSize)));
            setHeight(caption.size() * captionStep + (description.isEmpty() ? 0 : 4 + description.size() * detailStep));
            active = false;
        }

        @Override
        protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
            page.beginNativeText();
            try {
                drawLines(graphics, caption, getY(), captionSize, captionStep, page.theme().ink());
                if (!description.isEmpty())
                    drawLines(graphics, description, getY() + caption.size() * captionStep + 4, detailSize, detailStep, page.theme().muted());
            } finally {
                page.endNativeText();
            }
        }

        private void drawLines(GuiGraphicsExtractor graphics, List<net.minecraft.util.FormattedCharSequence> lines,
                               int top, int size, int spacing, int color) {
            graphics.pose().pushMatrix();
            try {
                graphics.pose().translate(getX(), top);
                graphics.pose().scale(size / 9f);
                for (int i = 0; i < lines.size(); i++)
                    graphics.text(Minecraft.getInstance().font, lines.get(i), 0, Math.round(i * spacing * 9f / size), color, false);
            } finally {
                graphics.pose().popMatrix();
            }
        }

        @Override
        protected void updateWidgetNarration(NarrationElementOutput output) {
            output.add(NarratedElementType.TITLE, getMessage());
        }
    }

    private static boolean form(Screen screen) {
        String name = screen.getClass().getName();
        return name.equals(GAME + "ManageServerScreen") || name.equals(GAME + "DirectJoinServerScreen")
            || name.equals(REALMS + "RealmsInviteScreen") || name.equals(REALMS + "RealmsSlotOptionsScreen");
    }

    private static void onlineForm(NativeMenuPage page, Composition state) {
        Screen screen = page.screen();
        int inset = page.density().inset() * 2;
        int gap = page.density().gap();
        int controlHeight = page.density().control();
        int labelHeight = page.density().body();
        List<AbstractWidget> original = widgets(screen);
        List<AbstractWidget> fields = original.stream().filter(widget -> widget.visible && (widget instanceof EditBox
            || widget instanceof CycleButton<?> || widget instanceof AbstractSliderButton))
            .sorted(Comparator.comparingInt(AbstractWidget::getY).thenComparingInt(AbstractWidget::getX)).toList();
        List<Button> actions = original.stream().filter(Button.class::isInstance).map(Button.class::cast)
            .filter(button -> button.visible)
            .sorted(Comparator.comparingInt(button -> SettingsText.hasKey(button.getMessage(), "gui.cancel")
                || SettingsText.hasKey(button.getMessage(), "gui.back") ? 0 : 1)).toList();
        Set<Component> captions = new HashSet<>();
        for (AbstractWidget field : fields) if (field instanceof EditBox)
            captions.add(screen instanceof InviteForm invite ? invite.rodel$nameLabel() : field.getMessage());
        List<AbstractWidget> notices = original.stream().filter(widget -> widget.visible && widget instanceof StringWidget
            && !widget.getMessage().equals(screen.getTitle()) && !captions.contains(widget.getMessage()))
            .toList();
        state.width = Math.min(400, screen.width - 24);
        state.left = (screen.width - state.width) / 2;
        state.top = 12;
        state.headerY = state.top + inset;
        int x = state.left + inset;
        int width = state.width - inset * 2;
        int y = state.headerY + page.density().heading() + gap * 2;
        for (AbstractWidget notice : notices) {
            if (notice instanceof StringWidget label) label.setMaxWidth(width, StringWidget.TextOverflow.SCROLLING);
            notice.setPosition(x, y);
            y += notice.getHeight() + gap;
        }
        for (AbstractWidget field : fields) {
            if (field instanceof EditBox) {
                Component label = screen instanceof InviteForm invite ? invite.rodel$nameLabel() : field.getMessage();
                if (!label.getString().isBlank()) {
                    state.labels.add(new Label(label, x, y));
                    state.replacedLabels.add(label);
                }
                field.setRectangle(width, controlHeight, x, y + (label.getString().isBlank() ? 0 : labelHeight + gap));
                y = field.getBottom() + gap;
                if (screen instanceof InviteForm invite && invite.rodel$status() != null) {
                    state.status = invite.rodel$status();
                    state.statusX = x;
                    state.statusY = y;
                    state.statusWidth = width;
                    int statusHeight = statusLines(page, state).size() * (page.density().detail() + 2);
                    if (statusHeight > 0) y = state.statusY + statusHeight + gap;
                }
            } else {
                int slot = Math.min(page.density().tight() ? 120 : 160, width / 2);
                Component caption = page.caption(field);
                state.labels.add(new Label(caption, x, y + (controlHeight - labelHeight) / 2));
                page.externalCaption(field, true);
                int valueWidth = field instanceof AbstractSliderButton ? Math.min(58, slot / 2) : 0;
                field.setRectangle(slot - valueWidth, controlHeight, x + width - slot, y);
                if (field instanceof AbstractSliderButton) {
                    SliderValue value = state.sliderValues.containsKey(field) ? state.sliderValues.get(field).get() : null;
                    if (value == null) {
                        value = new SliderValue(page, field);
                        state.sliderValues.put(field, new WeakReference<>(value));
                    }
                    if (!NativeWidgets.get(screen).contains(value)) NativeWidgets.get(screen).add(value);
                    value.setRectangle(valueWidth - 6, controlHeight, field.getRight() + 6, y);
                }
                y += controlHeight + gap;
            }
        }
        row(page, actions, x, y + gap, width, controlHeight, false);
        if (!actions.isEmpty()) {
            Button primary = actions.stream().filter(button -> !SettingsText.hasKey(button.getMessage(), "gui.cancel")
                && !SettingsText.hasKey(button.getMessage(), "gui.back")).findFirst().orElse(null);
            if (primary != null) ((ThemedButton) primary).rodel$setStyle(page, null, true, false, false);
        }
        state.height = y + gap + (actions.isEmpty() ? 0 : controlHeight) + inset - state.top;
        int top = Math.max(12, (screen.height - state.height) / 2);
        int offset = top - state.top;
        if (offset != 0) {
            Set<AbstractWidget> moved = new HashSet<>(fields);
            moved.addAll(notices);
            moved.addAll(actions);
            state.sliderValues.values().stream().map(WeakReference::get).filter(java.util.Objects::nonNull).forEach(moved::add);
            for (AbstractWidget widget : moved) widget.setY(widget.getY() + offset);
            state.labels.replaceAll(label -> new Label(label.text, label.x, label.y + offset));
            state.statusY += offset;
            state.top = top;
            state.headerY = top + inset;
        }
        state.body = new Bounds(state.left, state.top, state.width, state.height);
    }

    private static void hideReplacedLabels(Screen screen, Composition state) {
        for (AbstractWidget widget : widgets(screen))
            if (widget instanceof StringWidget && (widget.getMessage().equals(screen.getTitle()) || state.replacedLabels.contains(widget.getMessage())))
                widget.visible = false;
    }

    private static void navigation(NativeMenuPage page, Composition state) {
        Screen screen = page.screen();
        Button navigation = state.navigation.get();
        if (navigation == null) {
            WeakReference<Screen> owner = new WeakReference<>(screen);
            navigation = Button.builder(Component.translatable("gui.back"), button -> {
                Screen parent = owner.get();
                if (parent != null) parent.onClose();
            }).build();
            navigation.setTooltip(net.minecraft.client.gui.components.Tooltip.create(Component.translatable("gui.back")));
            state.navigation = new WeakReference<>(navigation);
        }
        if (!NativeWidgets.get(screen).contains(navigation)) NativeWidgets.get(screen).add(navigation);
        back(page, navigation, state.left, state.header == null ? state.top : state.header.controlY());
    }

    private static List<AbstractWidget> widgets(Screen screen) {
        List<AbstractWidget> result = new ArrayList<>();
        Set<AbstractWidget> seen = java.util.Collections.newSetFromMap(new java.util.IdentityHashMap<>());
        for (AbstractWidget widget : NativeWidgets.get(screen))
            if (!(widget instanceof SettingsChoiceList) && seen.add(widget)) result.add(widget);
        for (var child : screen.children())
            if (child instanceof AbstractWidget widget && !(widget instanceof SettingsChoiceList) && seen.add(widget)) result.add(widget);
        return List.copyOf(result);
    }

    private static List<Button> buttons(Screen screen) {
        return widgets(screen).stream().filter(Button.class::isInstance).map(Button.class::cast).toList();
    }

    private static List<AbstractSelectionList<?>> lists(Screen screen) {
        return widgets(screen).stream().filter(AbstractSelectionList.class::isInstance)
            .<AbstractSelectionList<?>>map(widget -> (AbstractSelectionList<?>) widget).toList();
    }

    private static boolean present(Screen screen, AbstractWidget widget) {
        return widget != null && widgets(screen).contains(widget) && widget.visible;
    }

    private static Button key(List<Button> buttons, String... keys) {
        for (Button button : buttons) for (String key : keys) if (SettingsText.hasKey(button.getMessage(), key)) return button;
        return null;
    }

    private static void back(NativeMenuPage page, Button button, int x, int y) {
        button.setRectangle(page.density().control(), page.density().control(), x, y);
        ((ThemedButton) button).rodel$setStyle(page, MenuIcon.BACK, false, true, false);
        button.setTooltip(net.minecraft.client.gui.components.Tooltip.create(button.getMessage()));
    }

    private static void row(NativeMenuPage page, List<? extends AbstractWidget> controls, int x, int y, int width, int height, boolean primary) {
        if (controls.isEmpty()) return;
        int gap = Math.min(page.density().gap(), Math.max(0, width) / Math.max(1, controls.size() - 1));
        int controlWidth = (width - (controls.size() - 1) * gap) / controls.size();
        for (int i = 0; i < controls.size(); i++) place(page, controls.get(i), x + i * (controlWidth + gap), y, controlWidth, height, primary);
    }

    private static int actionWidth(NativeMenuPage page, AbstractWidget control) {
        var font = Minecraft.getInstance().font;
        return (int) Math.ceil(font.width(MenuText.styled(control.getMessage()))
            * page.density().body() / (float) font.lineHeight) + Math.max(page.density().inset() * 2, page.p(28));
    }

    private static void place(NativeMenuPage page, AbstractWidget control, int x, int y, int width, int height, boolean primary) {
        if (control == null) return;
        control.setRectangle(Math.max(0, width), height, x, y);
        if (control instanceof ThemedButton themed && NativeMenuPage.plainButton(control))
            themed.rodel$setStyle(page, null, primary, false, false);
    }

    private static void resize(AbstractSelectionList<?> list, int x, int y, int width, int height) {
        double scroll = list.scrollAmount();
        list.updateSizeAndPosition(width, height, x, y);
        list.setScrollAmount(scroll);
    }

    private static void shiftGroup(List<AbstractWidget> controls, int center, int top) {
        if (controls.isEmpty()) return;
        int left = controls.stream().mapToInt(AbstractWidget::getX).min().orElse(0);
        int right = controls.stream().mapToInt(AbstractWidget::getRight).max().orElse(left);
        int first = controls.stream().mapToInt(AbstractWidget::getY).min().orElse(0);
        int dx = center - (left + right) / 2;
        for (AbstractWidget widget : controls) widget.setPosition(widget.getX() + dx, widget.getY() + top - first);
    }

    /** Paint after backdrop/inset, before the native Screen. True means skip the generic panel. */
    public static boolean panels(NativeMenuPage page, GuiGraphicsExtractor graphics) {
        Composition state = COMPOSITIONS.get(page.screen());
        if (state == null || !supports(page.screen())) return false;
        page.beginNativeText();
        try {
            return paint(page, graphics, state);
        } finally {
            page.endNativeText();
        }
    }

    private static boolean paint(NativeMenuPage page, GuiGraphicsExtractor graphics, Composition state) {
        if (state.body != null) {
            Bounds bounds = state.body;
            page.theme().panel(graphics, bounds.x, bounds.y, bounds.width, bounds.height);
        }
        if (state.secondary != null) {
            Bounds bounds = state.secondary;
            graphics.horizontalLine(bounds.x, bounds.x + bounds.width, bounds.y, page.theme().edge());
        }
        boolean logo = page.screen() instanceof RealmBrowser && widgets(page.screen()).stream()
            .anyMatch(widget -> widget instanceof ImageWidget && widget.getHeight() <= 40);
        if (state.header != null) {
            Component title = page.screen().getTitle();
            if (title.getString().isBlank() && page.screen() instanceof RealmManager)
                title = Component.translatable("rodel.realms.manage.title");
            state.header.draw(page, graphics, title, state.subtitle);
        } else if (!logo) {
            int x = state.left + 34;
            if (page.screen() instanceof Friends) x = state.left + 46;
            if (form(page.screen())) x = state.left + page.density().inset() * 2;
            Component title = page.screen().getTitle();
            if (title.getString().isBlank()
                && page.screen().getClass().getName().equals(REALMS + "RealmsConfigureWorldScreen"))
                title = Component.translatable("rodel.realms.manage.title");
            MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(title), x, state.headerY,
                page.density().tight() ? page.density().heading() : 13, page.theme().ink());
        }
        if (state.subtitle != null && state.header == null) {
            var font = Minecraft.getInstance().font;
            int size = page.density().detail();
            Component subtitle = page.fitLabel(MenuText.secondary(state.subtitle),
                (state.width - 34) * font.lineHeight / (float) size, false);
            MenuText.extract(graphics, font, subtitle, state.left + 34,
                state.top + page.density().heading() + page.density().gap(), size, page.theme().muted());
        }
        for (Label label : state.labels)
            MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(label.text), label.x, label.y,
                page.density().body(), page.theme().ink());
        if (state.status != null) {
            int y = state.statusY;
            int size = page.density().detail();
            for (var line : statusLines(page, state)) {
                graphics.pose().pushMatrix();
                try {
                    graphics.pose().translate(state.statusX, y);
                    graphics.pose().scale(size / (float) Minecraft.getInstance().font.lineHeight);
                    graphics.text(Minecraft.getInstance().font, line, 0, 0, page.theme().ink(), false);
                } finally {
                    graphics.pose().popMatrix();
                }
                y += size + 2;
            }
        }
        return true;
    }

    private static List<net.minecraft.util.FormattedCharSequence> statusLines(NativeMenuPage page, Composition state) {
        if (state.status == null || state.status.getString().isBlank()) return List.of();
        var font = Minecraft.getInstance().font;
        return font.split(MenuText.secondary(state.status),
            Math.max(1, Math.round(state.statusWidth * font.lineHeight / (float) page.density().detail())));
    }

    public static boolean replacesNativeText(Screen screen, Component text) {
        Composition state = COMPOSITIONS.get(screen);
        return state != null && (text.equals(screen.getTitle()) || state.replacedLabels.contains(text)
            || state.status != null && state.status.equals(text));
    }

    public static boolean consumesFriendsHeaderClick(Screen screen, double x, double y) {
        Composition state = COMPOSITIONS.get(screen);
        return state != null && screen instanceof Friends && x >= state.left && x <= state.left + state.width
            && y >= state.top && y < state.top + (state.header == null ? 0 : state.header.height());
    }
}
