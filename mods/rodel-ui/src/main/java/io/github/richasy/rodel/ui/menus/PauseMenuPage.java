package io.github.richasy.rodel.ui.menus;

import java.util.ArrayList;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractScrollArea;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.events.ContainerEventHandler;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.components.toasts.NowPlayingToast;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.layouts.SpacerElement;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.network.chat.Component;

public final class PauseMenuPage extends NativeMenuPage {
    private enum Role { RESUME, PROGRESS, STATS, OPTIONS, WORLD, FRIENDS, REPORT, FEEDBACK, BUGS, EXIT, EXTRA }

    private final List<AbstractWidget> actions = new ArrayList<>();
    private final Map<AbstractWidget, Component> messages = new IdentityHashMap<>();
    private final Map<AbstractWidget, Role> roles = new IdentityHashMap<>();
    private final LinearLayout groupLayout = LinearLayout.vertical();
    private final ScrollableLayout scrolling;
    private final List<AbstractWidget> scrollWidgets = new ArrayList<>();
    private List<AbstractWidget> visibleActions = List.of();
    private AbstractWidget resume;
    private AbstractWidget exit;
    private Component worldName = Component.empty();
    private int left;
    private int top;
    private int panelWidth;
    private int panelHeight;
    private int padding;
    private int headerHeight;

    public PauseMenuPage(Screen screen, MenuContext appearance) {
        super(screen, appearance);
        scrolling = new ScrollableLayout(Minecraft.getInstance(), groupLayout, 0, ScrollableLayout.ReserveStrategy.RIGHT);
        scrolling.setScrollbarSpacing(3);
        scrolling.visitWidgets(scrollWidgets::add);
    }

    @Override public void initialized() {
        var roots = List.copyOf(NativeWidgets.get(screen()));
        if (scrollWidgets.stream().noneMatch(roots::contains)) {
            actions.clear();
            messages.clear();
            roles.clear();
            resume = null;
            exit = null;
            capture(roots);
            var client = Minecraft.getInstance();
            var server = client.getSingleplayerServer();
            var remote = client.getCurrentServer();
            worldName = server != null ? Component.literal(server.getWorldData().getLevelName())
                : remote != null ? Component.literal(remote.name) : Component.empty();
        } else {
            capture(roots);
        }
        layout();
    }

    private boolean capture(List<AbstractWidget> roots) {
        boolean changed = false;
        var nativeActions = (PauseMenuAccess) screen();
        for (var widget : roots) {
            if (messages.containsKey(widget) || scrollWidgets.contains(widget)
                || widget instanceof StringWidget && widget.getMessage().equals(screen().getTitle())) continue;
            Role role = widget == nativeActions.rodel$exitButton() ? Role.EXIT
                : widget == nativeActions.rodel$friendsButton() ? Role.FRIENDS
                : role(widget.getMessage());
            actions.add(widget);
            messages.put(widget, widget.getMessage());
            roles.put(widget, role);
            if (role == Role.RESUME) resume = widget;
            if (role == Role.EXIT) exit = widget;
            changed = true;
        }
        return changed;
    }

    private static Role role(Component message) {
        if (SettingsText.hasKey(message, "menu.returnToGame")) return Role.RESUME;
        if (SettingsText.hasKey(message, "gui.advancements")) return Role.PROGRESS;
        if (SettingsText.hasKey(message, "gui.stats")) return Role.STATS;
        if (SettingsText.hasKey(message, "menu.options")) return Role.OPTIONS;
        if (SettingsText.hasKey(message, "options.worldOptions.button")) return Role.WORLD;
        if (SettingsText.hasKey(message, "menu.playerReporting")) return Role.REPORT;
        if (SettingsText.hasKey(message, "menu.sendFeedback")) return Role.FEEDBACK;
        if (SettingsText.hasKey(message, "menu.reportBugs")) return Role.BUGS;
        return Role.EXTRA;
    }

    private List<AbstractWidget> group(Role... kinds) {
        List<AbstractWidget> result = new ArrayList<>();
        for (Role kind : kinds) {
            for (var widget : actions) if (widget.visible && roles.get(widget) == kind) result.add(widget);
        }
        return result;
    }

    private void layout() {
        var focused = screen().getFocused();
        visibleActions = actions.stream().filter(widget -> widget.visible).toList();
        boolean compact = compact();
        padding = density().tight() ? 10 : compact ? 12 : 16;
        headerHeight = density().tight() ? worldName.getString().isBlank() ? 16 : 20
            : worldName.getString().isBlank() ? 20 : compact ? 26 : 32;
        panelWidth = Math.min(density().tight() ? 280 : compact ? 330 : 340, screen().width - 24);
        int contentWidth = panelWidth - padding * 2;
        int actionHeight = density().control();
        int resumeHeight = density().tight() ? 20 : compact ? 26 : 30;
        int gap = density().tight() ? 6 : compact ? 8 : 12;
        int footerGap = density().tight() ? 8 : compact ? 10 : 16;
        int fixed = padding * 2 + headerHeight + gap + (resume == null ? 0 : resumeHeight + gap)
            + (exit == null ? 0 : actionHeight + footerGap);
        int maximumBody = Math.max(24, screen().height - 12 - fixed);
        List<List<AbstractWidget>> groups = List.of(
            group(Role.PROGRESS, Role.STATS), group(Role.OPTIONS, Role.WORLD),
            group(Role.FRIENDS, Role.REPORT), group(Role.EXTRA), group(Role.FEEDBACK, Role.BUGS));
        arrangeGroups(groups, contentWidth);
        int bodyHeight = Math.min(groupLayout.getHeight(), maximumBody);
        if (groupLayout.getHeight() > maximumBody) {
            int reserved = 3;
            for (var widget : scrollWidgets) if (widget instanceof AbstractScrollArea area) reserved += area.scrollbarWidth();
            arrangeGroups(groups, Math.max(40, contentWidth - reserved));
        }
        panelHeight = fixed + bodyHeight;
        left = (screen().width - panelWidth) / 2;
        top = Math.max(6, (screen().height - panelHeight) / 2);
        int x = left + padding;
        int y = top + padding + headerHeight + gap;
        if (resume != null) {
            resume.setRectangle(contentWidth, resumeHeight, x, y);
            style(resume, true);
            y += resumeHeight + gap;
        }
        scrolling.setMinWidth(contentWidth);
        scrolling.setMinHeight(bodyHeight);
        scrolling.setMaxHeight(bodyHeight);
        scrolling.setX(x);
        scrolling.setY(y);
        scrolling.arrangeElements();
        if (exit != null) {
            exit.setRectangle(contentWidth, actionHeight, x, top + panelHeight - padding - actionHeight);
            style(exit, false);
        }
        var roots = NativeWidgets.get(screen());
        roots.clear();
        if (resume != null && resume.visible) roots.add(resume);
        roots.addAll(scrollWidgets);
        if (exit != null && exit.visible) roots.add(exit);
        if (focused instanceof AbstractWidget widget && roots.contains(widget)) screen().setFocused(widget);
        else if (focused instanceof AbstractWidget widget && visibleActions.contains(widget) && !scrollWidgets.isEmpty()
            && scrollWidgets.getFirst() instanceof ContainerEventHandler container) {
            screen().setFocused(scrollWidgets.getFirst());
            container.setFocused(widget);
        } else if (resume != null) screen().setFocused(resume);
    }

    private void arrangeGroups(List<List<AbstractWidget>> groups, int width) {
        groupLayout.removeChildren();
        boolean first = true;
        for (var group : groups) {
            if (group.isEmpty()) continue;
            if (!first) groupLayout.addChild(new SpacerElement(width, density().tight() ? 6 : compact() ? 8 : 10));
            first = false;
            for (int i = 0; i < group.size(); i += 2) {
                if (i > 0) groupLayout.addChild(new SpacerElement(width, compact() ? 4 : 6));
                var row = LinearLayout.horizontal().spacing(8);
                int count = Math.min(2, group.size() - i);
                int buttonWidth = (width - (count - 1) * 8) / count;
                for (int j = 0; j < count; j++) {
                    var widget = group.get(i + j);
                    boolean utility = roles.get(widget) == Role.FEEDBACK || roles.get(widget) == Role.BUGS;
                    int height = density().tight() ? utility ? 14 : 16 : utility ? compact() ? 18 : 20 : compact() ? 22 : 24;
                    widget.setWidth(buttonWidth);
                    widget.setHeight(widget instanceof ThemedButton ? height : Math.max(height, widget.getHeight()));
                    style(widget, false);
                    row.addChild(widget);
                }
                groupLayout.addChild(row);
            }
        }
        groupLayout.arrangeElements();
    }

    private void style(AbstractWidget widget, boolean primary) {
        if (!(widget instanceof ThemedButton themed)) return;
        Component original = messages.get(widget);
        if (roles.get(widget) == Role.OPTIONS || roles.get(widget) == Role.WORLD)
            widget.setMessage(SettingsText.label(original));
        else if (roles.get(widget) == Role.FRIENDS && widget instanceof PauseMenuAccess.RequestCount indicator) {
            int count = indicator.rodel$incomingRequests();
            widget.setMessage(count > 0 ? Component.translatable("rodel.menu.pause.pending_count", original, count) : original);
        } else if (roles.get(widget) == Role.EXIT) {
            widget.setMessage(Minecraft.getInstance().getReportingContext().hasDraftReport()
                ? Component.translatable("rodel.menu.pause.draft_report", original) : original);
        }
        MenuIcon icon = switch (roles.get(widget)) {
            case RESUME -> MenuIcon.PLAY;
            case PROGRESS -> MenuIcon.TROPHY;
            case STATS -> MenuIcon.STATS;
            case OPTIONS -> MenuIcon.SETTINGS;
            case WORLD -> MenuIcon.WORLD;
            case FRIENDS -> MenuIcon.PEOPLE;
            case REPORT -> MenuIcon.FLAG;
            case FEEDBACK -> MenuIcon.CHAT;
            case BUGS -> MenuIcon.BUG;
            case EXIT -> MenuIcon.EXIT;
            case EXTRA -> MenuIcon.ARROW;
        };
        themed.rodel$setStyle(this, icon, primary, false, false);
    }

    @Override protected void style(GuiEventListener listener) {
        for (var widget : actions) style(widget, roles.get(widget) == Role.RESUME);
    }

    @Override protected void panels(GuiGraphicsExtractor graphics) {
        if (capture(List.copyOf(NativeWidgets.get(screen())))
            || !visibleActions.equals(actions.stream().filter(widget -> widget.visible).toList())) layout();
        theme().panel(graphics, left, top, panelWidth, panelHeight);
    }

    public void content(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        beginNativeText();
        try {
            MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(screen().getTitle()),
                left + padding, top + padding, density().tight() ? density().heading() : compact() ? 12 : 14, theme().ink());
            if (!worldName.getString().isBlank()) {
                var font = Minecraft.getInstance().font;
                var label = MenuText.secondary(worldName, theme().muted());
                int size = density().detail();
                int textTop = top + padding + (density().tight() ? 13 : compact() ? 16 : 20);
                int available = Math.round((panelWidth - padding * 2) * font.lineHeight / (float) size);
                boolean trimmed = font.width(label) > available;
                var shown = trimmed ? MenuText.secondary(Component.literal(
                    font.substrByWidth(label, available - font.width("…")).getString() + "…"), theme().muted()) : label;
                MenuText.extract(graphics, font, shown, left + padding, textTop, size, theme().muted());
                if (trimmed && mouseX >= left + padding && mouseX < left + panelWidth - padding
                    && mouseY >= textTop && mouseY < textTop + size)
                    graphics.setTooltipForNextFrame(worldName, mouseX, mouseY);
            }
        } finally {
            endNativeText();
        }
        for (var widget : List.copyOf(NativeWidgets.get(screen())))
            widget.extractRenderState(graphics, mouseX, mouseY, partialTick);
        if (((PauseScreen) screen()).rendersNowPlayingToast()) {
            beginNativeText();
            try {
                NowPlayingToast.extractToast(graphics, Minecraft.getInstance().font);
            } finally {
                endNativeText();
            }
        }
    }
}
