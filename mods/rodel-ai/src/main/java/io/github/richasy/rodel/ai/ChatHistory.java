package io.github.richasy.rodel.ai;

import net.minecraft.network.chat.Component;

/** Replaces only the exact local AI status component, preserving unrelated game and server messages. */
public interface ChatHistory {
    boolean rai$replace(Component expected, Component replacement);
}
