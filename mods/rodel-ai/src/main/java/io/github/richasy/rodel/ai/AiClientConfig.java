package io.github.richasy.rodel.ai;

import java.io.IOException;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Properties;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Owns this mod's client palette and native configuration screen independently of other mods. */
public final class AiClientConfig extends Screen {
    private static final Properties VALUES = load();
    private final Screen parent;
    private boolean selectedDark = dark();
    private Component failure;

    /** Opens a local draft; cancelling leaves the persisted palette unchanged. */
    public AiClientConfig(Screen parent) { super(Component.translatable("rai.config.title")); this.parent = parent; }

    private static Path path() { return Minecraft.getInstance().gameDirectory.toPath().resolve("config/rodel-ai-client.properties"); }

    private static Properties load() {
        var values = new Properties();
        if (Files.exists(path())) try (var input = Files.newInputStream(path())) { values.load(input); }
        catch (IOException | IllegalArgumentException error) {
            org.slf4j.LoggerFactory.getLogger("rodel-ai-client").warn("Unable to read client appearance; using defaults", error);
        }
        return values;
    }

    /** Returns only this mod's saved choice; launcher and other mod themes are never consulted. */
    public static boolean dark() { return Boolean.parseBoolean(VALUES.getProperty("darkTheme", "false")); }

    @Override protected void init() {
        int size = Math.min(260, width - 24), x = (width - size) / 2;
        addRenderableWidget(Button.builder(label(), button -> {
            selectedDark = !selectedDark; button.setMessage(label());
        }).bounds(x, Math.max(36, height / 2 - 24), size, 20).build());
        int y = Math.min(height - 28, height / 2 + 12);
        addRenderableWidget(Button.builder(Component.translatable("gui.done"), button -> save()).bounds(x, y, (size - 4) / 2, 20).build());
        addRenderableWidget(Button.builder(Component.translatable("gui.cancel"), button -> onClose()).bounds(x + (size - 4) / 2 + 4, y, (size - 4) / 2, 20).build());
    }

    private Component label() { return Component.translatable("rai.config.theme", Component.translatable(selectedDark ? "rai.config.dark" : "rai.config.light")); }

    private void save() {
        Path temporary = null;
        try {
            var next = new Properties(); next.putAll(VALUES); next.setProperty("darkTheme", Boolean.toString(selectedDark));
            Files.createDirectories(path().getParent());
            temporary = Files.createTempFile(path().getParent(), "rodel-ai-client-", ".tmp");
            try (var output = Files.newOutputStream(temporary)) { next.store(output, "Client appearance"); }
            try { Files.move(temporary, path(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING); }
            catch (AtomicMoveNotSupportedException unsupported) { Files.move(temporary, path(), StandardCopyOption.REPLACE_EXISTING); }
            VALUES.clear(); VALUES.putAll(next);
            onClose();
        } catch (IOException error) {
            failure = Component.translatable("rai.config.save_failed", error.getMessage());
        } finally {
            if (temporary != null) try { Files.deleteIfExists(temporary); }
            catch (IOException error) { org.slf4j.LoggerFactory.getLogger("rodel-ai-client").warn("Unable to clean this settings attempt", error); }
        }
    }

    @Override public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
        graphics.text(font, title, (width - font.width(title)) / 2, 16, 0xFFFFFFFF, true);
        if (failure != null) {
            int y = Math.min(height - 56, height / 2 + 40);
            for (var line : font.split(failure, Math.max(1, width - 24))) { graphics.text(font, line, 12, y, 0xFFFF8888, true); y += font.lineHeight + 2; }
        }
        super.extractRenderState(graphics, mouseX, mouseY, tick);
    }

    @Override public void onClose() { minecraft.gui.setScreen(parent); }
}
