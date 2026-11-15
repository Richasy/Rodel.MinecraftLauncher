package io.github.richasy.rodel.ui.settings;

import net.minecraft.client.gui.screens.packs.TransferableSelectionList;
import net.minecraft.network.chat.Component;

public interface PackSettingsHost {
    TransferableSelectionList rodel$availablePacks();
    TransferableSelectionList rodel$selectedPacks();
    Component rodel$availableTitle();
    Component rodel$selectedTitle();
    int rodel$packRows();
}
