package io.github.richasy.rodel.ui.mixin;

import java.util.ArrayList;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.layouts.FrameLayout;
import net.minecraft.client.gui.layouts.LinearLayout;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(targets = "net.minecraft.client.gui.screens.friends.FriendsTab")
public abstract class OnlineFriendsTabLayoutMixin implements NetworkMenuLayouts.FriendTab {
    @Shadow @Final private LinearLayout layout;
    @Shadow @Final private LinearLayout friendScrollableContent;
    @Shadow @Final private ScrollableLayout scrollableLayout;
    @Shadow private FrameLayout contentFrame;

    @Override public LinearLayout rodel$layout() { return layout; }
    @Override public LinearLayout rodel$entries() { return friendScrollableContent; }
    @Override public ScrollableLayout rodel$scroll() { return scrollableLayout; }
    @Override public FrameLayout rodel$emptyFrame() { return contentFrame; }
    @Override public AbstractWidget rodel$addFriend() {
        var widgets = new ArrayList<AbstractWidget>();
        layout.visitWidgets(widgets::add);
        return widgets.stream().filter(widget -> widget.getClass().getName().equals(
            "net.minecraft.client.gui.screens.friends.AddFriendWidget")).findFirst().orElse(null);
    }
}
