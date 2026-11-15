package io.github.richasy.rodel.ai.mixin;

import java.util.List;
import io.github.richasy.rodel.ai.ChatHistory;
import net.minecraft.client.GuiMessage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.ChatComponent;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Final;

/** Replaces the exact local status component; unrelated server and player chat stays intact. */
@Mixin(ChatComponent.class)
public abstract class ChatHistoryMixin implements ChatHistory {
    @Shadow @Final private List<GuiMessage> allMessages;
    @Shadow private void refreshTrimmedMessage() { throw new AssertionError(); }

    @Override
    public boolean rai$replace(Component expected, Component replacement) {
        for (int i = 0; i < allMessages.size(); i++) {
            GuiMessage message = allMessages.get(i);
            if (message.content() == expected) {
                allMessages.set(i, new GuiMessage(Minecraft.getInstance().gui.getGuiTicks(), replacement, message.signature(), message.tag()));
                refreshTrimmedMessage(); return true;
            }
        }
        return false;
    }
}
