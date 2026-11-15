package io.github.richasy.rodel.ui;

public interface ModernScreen {
    MenuContext rodel$modernContext();
    /** Explicit opt-in for reading-oriented pages that need the regular system font, including native editors. */
    default boolean rodel$regularText() { return false; }
}
