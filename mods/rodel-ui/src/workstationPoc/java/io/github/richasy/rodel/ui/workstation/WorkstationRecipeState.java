package io.github.richasy.rodel.ui.workstation;

/** Lets background rendering follow the native recipe book's narrow layout state. */
public interface WorkstationRecipeState {
    /** Returns whether the recipe book is taking the place of the workstation. */
    boolean rodel$recipeOnly();
}
