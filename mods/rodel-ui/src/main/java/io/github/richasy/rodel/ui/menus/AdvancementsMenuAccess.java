package io.github.richasy.rodel.ui.menus;

import java.util.Map;
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.client.gui.screens.advancements.AdvancementTab;
import net.minecraft.client.multiplayer.ClientAdvancements;
import net.minecraft.network.chat.Component;

public interface AdvancementsMenuAccess {
    Map<AdvancementHolder, AdvancementTab> rodel$tabs();
    AdvancementTab rodel$selectedTab();
    ClientAdvancements rodel$advancements();
    Component rodel$emptyLabel();
    void rodel$origin(int left, int top);
}
