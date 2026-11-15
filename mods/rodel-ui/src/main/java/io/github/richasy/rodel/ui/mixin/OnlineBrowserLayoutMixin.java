package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.multiplayer.JoinMultiplayerScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(JoinMultiplayerScreen.class)
public abstract class OnlineBrowserLayoutMixin implements NetworkMenuLayouts.ServerBrowser {
    @Shadow private Button joinButton;
    @Shadow private Button editButton;
    @Shadow private Button deleteButton;

    @Override public Button rodel$join() { return joinButton; }
    @Override public Button rodel$edit() { return editButton; }
    @Override public Button rodel$delete() { return deleteButton; }
}
