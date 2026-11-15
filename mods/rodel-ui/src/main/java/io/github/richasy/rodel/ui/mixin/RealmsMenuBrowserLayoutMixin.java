package io.github.richasy.rodel.ui.mixin;

import com.mojang.realmsclient.RealmsMainScreen;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.Button;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(RealmsMainScreen.class)
public abstract class RealmsMenuBrowserLayoutMixin implements NetworkMenuLayouts.RealmBrowser {
    @Shadow private Button playButton;
    @Shadow private Button backButton;
    @Shadow private Button renewButton;
    @Shadow private Button configureButton;
    @Shadow private Button leaveButton;
    @Shadow private Button addRealmButton;

    @Override public Button rodel$play() { return playButton; }
    @Override public Button rodel$back() { return backButton; }
    @Override public Button rodel$renew() { return renewButton; }
    @Override public Button rodel$configure() { return configureButton; }
    @Override public Button rodel$leave() { return leaveButton; }
    @Override public Button rodel$add() { return addRealmButton; }
}
