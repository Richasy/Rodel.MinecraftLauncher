package io.github.richasy.rodel.ui.settings;

import java.util.List;
import net.minecraft.network.chat.Component;

public interface NativeCycleChoices {
    List<Choice> rodel$choices();
    void rodel$valueOnlyFormatter();

    record Choice(Object value, Component label, boolean selected, Runnable select) {}
}
