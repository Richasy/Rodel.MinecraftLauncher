package dev.richasy.worldreroll.fixtures.mixin;

import java.util.List;
import net.minecraft.client.gui.components.ChatComponent;
import net.minecraft.client.multiplayer.chat.GuiMessage;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Reads the actual native message age in isolated game tests; never packaged with the product. */
@Mixin(ChatComponent.class)
public interface ChatHistoryAccess {
    @Accessor("allMessages") List<GuiMessage> worldreroll$messages();
}
