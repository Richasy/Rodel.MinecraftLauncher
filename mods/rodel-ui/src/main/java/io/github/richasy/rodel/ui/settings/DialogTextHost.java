package io.github.richasy.rodel.ui.settings;

import java.util.List;
import net.minecraft.network.chat.Component;

public interface DialogTextHost {
    List<Component> rodel$dialogText();
    default Component rodel$dialogTitle(Component title) { return title; }
}
