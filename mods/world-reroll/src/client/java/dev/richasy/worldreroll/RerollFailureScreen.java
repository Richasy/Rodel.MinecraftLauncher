package dev.richasy.worldreroll;

import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Keeps retry and return in the same dialog layout as the original reroll confirmation. */
final class RerollFailureScreen extends RerollConfirmScreen {
    private final Runnable back, abandon;

    RerollFailureScreen(Screen parent, Runnable retry, Runnable back, Runnable abandon) {
        super(parent, new RerollLook(), Component.translatable("worldreroll.failed.title"),
                Component.translatable("worldreroll.failed.retry_body"),
                "worldreroll.failed.retry", "gui.back", retry);
        this.back = back;
        this.abandon = abandon;
    }

    /** Escape has the same source-preserving behavior as the return button. */
    @Override public void onClose() { back.run(); }

    /** An unrelated screen transition also revokes this failed attempt's deletion authority. */
    @Override public void removed() { super.removed(); abandon.run(); }
}
