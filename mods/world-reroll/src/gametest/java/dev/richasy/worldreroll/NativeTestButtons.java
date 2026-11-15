package dev.richasy.worldreroll;

import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.events.ContainerEventHandler;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.input.KeyEvent;

/** Invokes the real button callback, including controls hosted by Minecraft's native scroll container. */
final class NativeTestButtons {
    static void click(ClientGameTestContext context, String label) {
        context.runOnClient(m -> {
            Button button = find(m.gui.screen(), label);
            if (button == null || !button.active || !button.visible) throw new AssertionError("Missing enabled native button: " + label);
            button.onPress(new KeyEvent(257, 0, 0));
        });
    }

    private static Button find(ContainerEventHandler container, String label) {
        for (GuiEventListener child : container.children()) {
            if (child instanceof Button button && button.getMessage().getString().equals(label)) return button;
            if (child instanceof ContainerEventHandler nested) {
                Button found = find(nested, label);
                if (found != null) return found;
            }
        }
        return null;
    }
}
