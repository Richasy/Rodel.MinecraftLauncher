package io.github.richasy.rodel.ui.menus;

import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.FriendsButton;

public interface PauseMenuAccess {
    interface RequestCount {
        int rodel$incomingRequests();
    }
    Button rodel$exitButton();
    FriendsButton rodel$friendsButton();
}
