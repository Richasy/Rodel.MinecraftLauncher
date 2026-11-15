package org.lwjgl.opengl;

/** Minimal native-frame API shape for the independent fixture; no graphics or game logic. */
public final class Display {
    private Display() { }

    /** Marks the fixture's actual native frame without requiring an OpenGL context. */
    public static void update() { }
}
