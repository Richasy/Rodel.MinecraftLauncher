package io.github.richasy.rodel.ui.settings;

import java.util.OptionalDouble;
import net.minecraft.network.chat.Component;

public interface DialogProgressHost {
    Component rodel$progressStatus();
    OptionalDouble rodel$progressValue();
    default Component rodel$progressDetail() { return Component.empty(); }
}
