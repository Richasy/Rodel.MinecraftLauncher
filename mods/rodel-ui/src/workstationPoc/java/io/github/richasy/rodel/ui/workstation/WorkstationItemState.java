package io.github.richasy.rodel.ui.workstation;

/** Carries compact-icon sampling through Minecraft's deferred GUI extraction and drawing. */
public interface WorkstationItemState {
    /** Whether this draw is a compact workstation icon, rather than an inventory or HUD item. */
    boolean rodel$isCompactIcon();
}
