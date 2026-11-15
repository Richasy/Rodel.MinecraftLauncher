package io.github.richasy.rodel.ui.worlds;

import net.minecraft.client.gui.components.tabs.MenuTabBar;
import net.minecraft.client.gui.components.tabs.TabManager;

public interface WorldCreateAccess {
    MenuTabBar rodel$tabs();
    TabManager rodel$tabManager();
}
