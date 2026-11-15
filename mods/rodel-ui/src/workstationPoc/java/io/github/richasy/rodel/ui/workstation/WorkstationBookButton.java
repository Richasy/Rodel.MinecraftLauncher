package io.github.richasy.rodel.ui.workstation;

import net.minecraft.client.gui.screens.recipebook.RecipeBookComponent;

/** Connects the workstation appearance to its existing recipe-book button. */
public interface WorkstationBookButton {
    /** Binds the button to the palette and live visibility of its owning recipe book. */
    void rodel$bind(WorkstationSurface surface, RecipeBookComponent<?> book);

    /** Marks a native pagination action for the same compact recipe page. */
    void rodel$bindPage(boolean forward);
}
