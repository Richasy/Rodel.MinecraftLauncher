package io.github.richasy.rodel.ui.worlds;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import io.github.richasy.rodel.ui.settings.NativeCycleChoices;
import io.github.richasy.rodel.ui.settings.SettingsText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.tabs.Tab;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import net.minecraft.client.input.InputWithModifiers;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.network.chat.Component;

public final class WorldMoreNavigation {
    public enum Route {
        RULES("selectWorld.gameRules"), EXPERIMENTS("selectWorld.experiments"), PACKS("selectWorld.dataPacks");
        final String key;
        Route(String key) { this.key = key; }
    }

    private final CreateWorldScreen parent;
    private final WorldPage page;
    private final EnumMap<Route, Button> actions = new EnumMap<>(Route.class);
    private Route selected = Route.RULES;
    private int pendingTab = -1;
    private Route pendingRoute;

    WorldMoreNavigation(CreateWorldScreen parent, WorldPage page) { this.parent = parent; this.page = page; }
    public Screen parent() { return parent; }
    public Component title() { return parent.getTitle(); }
    public Route selected() { return selected; }
    void activate(Route route) { selected = route; }
    public Component label(Route route) { return SettingsText.label(actions.get(route).getMessage()); }
    public List<Tab> tabs() { return ((WorldCreateAccess) parent).rodel$tabs().getTabs(); }

    void capture(Tab tab) {
        actions.clear();
        tab.visitChildren(widget -> {
            if (widget instanceof Button button) for (Route route : Route.values())
                if (SettingsText.hasKey(button.getMessage(), route.key)) actions.put(route, button);
        });
        if (actions.size() != Route.values().length) throw new IllegalStateException("Native More navigation is incomplete");
    }

    public static void tick(Screen screen) {
        var page = WorldPage.get(screen);
        if (page != null && page.moreActive()) page.moreNavigation().open();
    }

    private void open() {
        Button action = actions.get(selected);
        if (action == null || !action.active) return;
        action.onPress(new MouseButtonInfo(1, 0));
    }

    public void returningToParent() {
        int tab = pendingTab < 0 ? 0 : pendingTab;
        if (pendingRoute != null) selected = pendingRoute;
        pendingTab = -1;
        pendingRoute = null;
        page.returnToTab(tab);
    }

    public void select(WorldSubpage source, AbstractWidget anchor, Route route, InputWithModifiers input) {
        if (selected == route) return;
        request(source, anchor, 2, route, input);
    }

    public void selectTab(WorldSubpage source, AbstractWidget anchor, int tab, InputWithModifiers input) {
        if (tab == 2) return;
        request(source, anchor, tab, null, input);
    }

    private void request(WorldSubpage source, AbstractWidget anchor, int tab, Route route, InputWithModifiers input) {
        if (!source.dirty()) {
            leave(source, tab, route, false, input);
            return;
        }
        List<NativeCycleChoices.Choice> choices = new ArrayList<>();
        if (source.canApply()) choices.add(new NativeCycleChoices.Choice("apply",
            Component.translatable("rodel.worlds.navigation.apply"), false, () -> leave(source, tab, route, true, input)));
        choices.add(new NativeCycleChoices.Choice("discard",
            Component.translatable("rodel.worlds.navigation.discard"), false, () -> leave(source, tab, route, false, input)));
        choices.add(new NativeCycleChoices.Choice("stay",
            Component.translatable("rodel.worlds.navigation.stay"), true, () -> {}));
        source.confirmNavigation(anchor, choices);
    }

    private void leave(WorldSubpage source, int tab, Route route, boolean apply, InputWithModifiers input) {
        pendingTab = tab;
        pendingRoute = route;
        source.finish(apply, input);
    }
}
