package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.PauseMenuAccess;
import net.minecraft.client.gui.components.FriendsButton;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(FriendsButton.class)
public interface MenuFriendCountMixin extends PauseMenuAccess.RequestCount {
    @Override @Accessor("incomingRequestCount") int rodel$incomingRequests();
}
