package io.github.richasy.rodel.ui.settings;

import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.InputWithModifiers;
import io.github.richasy.rodel.ui.MenuContext;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;

public final class SettingsSession {
    final Screen root;
    final MenuContext appearance;
    List<Button> routes = List.of();
    Button selected;
    double sidebarScroll;

    SettingsSession(Screen root, MenuContext appearance) {
        this.root = root;
        this.appearance = appearance;
    }

    void navigate(Button route, InputWithModifiers input) {
        rememberSidebar();
        if (!returnToRoot()) {
            return;
        }
        selected = route;
        if (route != null) {
            route.onPress(input);
        }
    }

    void close() {
        rememberSidebar();
        if (returnToRoot()) {
            NativeSettingsPage page = NativeSettingsPage.get(root);
            if (page != null) page.closeToParent();
            else root.onClose();
        }
    }

    private void rememberSidebar() {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.session == this) page.rememberSidebar();
    }

    private boolean returnToRoot() {
        Minecraft minecraft = Minecraft.getInstance();
        Screen current = minecraft.gui.screen();
        while (current != root) {
            NativeSettingsPage page = NativeSettingsPage.get(current);
            if (page == null || page.session != this) {
                return false;
            }
            page.closeToParent();
            Screen next = minecraft.gui.screen();
            if (next == current) {
                return false;
            }
            current = next;
        }
        // onClose applies pending sliders; removed and native overrides perform their own saves.
        return true;
    }

    Button category(Screen screen, Screen parent) {
        Component title = screen.getTitle();
        for (Button route : routes) {
            if (SettingsText.label(route.getMessage()).getString().equals(SettingsText.label(title).getString())
                || messageKey(route.getMessage()).equals(messageKey(title))) return route;
        }
        NativeSettingsPage ancestor = NativeSettingsPage.get(parent);
        return ancestor != null && ancestor.category != null ? ancestor.category : selected;
    }

    private static String messageKey(Component message) {
        if (!(message.getContents() instanceof TranslatableContents text)) return message.getString();
        String key = text.getKey();
        return key.endsWith(".title") ? key.substring(0, key.length() - 6) : key;
    }
}
