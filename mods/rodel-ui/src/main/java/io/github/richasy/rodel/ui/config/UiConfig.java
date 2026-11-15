package io.github.richasy.rodel.ui.config;

import io.github.richasy.rodel.ui.platform.LoaderPlatform;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.EnumSet;
import java.util.Properties;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Keeps the startup choices fixed for the lifetime of the native screens and font atlases. */
public final class UiConfig {
    private static final Logger LOGGER = LoggerFactory.getLogger("Rodel UI");
    private static final String FILE_NAME = "rodel-ui.properties";

    /** Player-facing groups of native UI effects, with stable keys in the instance config file. */
    public enum Effect {
        TITLE("titleScreen"),
        SETTINGS("settings"),
        WORLDS("worlds"),
        MENUS("menus"),
        MOD_BROWSER("modBrowser"),
        FONT_SMOOTHING("fontSmoothing");

        private final String key;

        Effect(String key) { this.key = key; }

        /** Identifies this effect in the game's language resources. */
        public String label() { return "rodel.config." + key; }
    }

    private UiConfig() {}

    private static final class Startup {
        private static final EnumSet<Effect> ENABLED = saved();
    }

    /** Returns whether the effect was enabled at startup, without reading disk during rendering. */
    public static boolean enabled(Effect effect) {
        return Startup.ENABLED.contains(effect);
    }

    /** Returns an editable snapshot of the saved choices, including changes pending a restart. */
    public static EnumSet<Effect> saved() {
        EnumSet<Effect> enabled = EnumSet.allOf(Effect.class);
        Path file = file();
        try {
            Properties properties = read(file);
            for (Effect effect : Effect.values()) {
                if ("false".equalsIgnoreCase(properties.getProperty(effect.key))) enabled.remove(effect);
            }
        } catch (IOException error) {
            LOGGER.warn("Could not read Rodel UI settings from {}", file, error);
        }
        return enabled;
    }

    /** Saves the selected effects for the next start, preserving unrecognized keys and the previous file on failure. */
    public static void save(EnumSet<Effect> enabled) throws IOException {
        Path file = file();
        Properties properties = read(file);
        for (Effect effect : Effect.values()) {
            properties.setProperty(effect.key, Boolean.toString(enabled.contains(effect)));
        }
        Files.createDirectories(file.getParent());
        Path pending = Files.createTempFile(file.getParent(), "rodel-ui-", ".tmp");
        try {
            try (var output = Files.newOutputStream(pending)) {
                properties.store(output, "Rodel UI client effects. Restart Minecraft after editing.");
            }
            Files.move(pending, file, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
        } finally {
            Files.deleteIfExists(pending);
        }
    }

    private static Path file() {
        return LoaderPlatform.configDirectory().resolve(FILE_NAME);
    }

    private static Properties read(Path file) throws IOException {
        try (var input = Files.newInputStream(file)) {
            var properties = new Properties();
            properties.load(input);
            return properties;
        } catch (NoSuchFileException missing) {
            return new Properties();
        } catch (IllegalArgumentException error) {
            throw new IOException("Invalid Rodel UI settings file", error);
        }
    }
}
