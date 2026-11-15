package io.github.richasy.rodel.ui.mixin;

import java.io.IOException;
import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import com.mojang.blaze3d.Blaze3D;
import io.github.richasy.rodel.ui.UiContexts;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.toasts.SystemToast;
import net.minecraft.network.chat.Component;
import net.minecraft.util.Util;
import org.slf4j.LoggerFactory;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Blaze3D.class)
public abstract class FolderOpenMixin {
    @Inject(method = "openUri", at = @At("HEAD"), cancellable = true)
    private static void rodel$openDirectory(URI uri, CallbackInfo callback) {
        Minecraft minecraft = Minecraft.getInstance();
        if (!System.getProperty("os.name").startsWith("Windows")
            || UiContexts.owned(minecraft.gui.screen()) == null
            || !"file".equalsIgnoreCase(uri.getScheme()) || uri.isOpaque()
            || uri.getRawQuery() != null || uri.getRawFragment() != null) return;
        Path path = Path.of(uri);
        if (!Files.isDirectory(path)) return;
        Path directory = path.toAbsolutePath().normalize();
        callback.cancel();
        Util.nonCriticalIoPool().execute(() -> {
            try {
                String windows = System.getenv("WINDIR");
                if (windows == null || windows.isBlank()) throw new IOException("WINDIR is missing");
                new ProcessBuilder(Path.of(windows, "explorer.exe").toString(), directory.toString()).start();
                LoggerFactory.getLogger("Rodel UI").info("Requested Windows folder open: {}", directory);
            } catch (IOException error) {
                LoggerFactory.getLogger("Rodel UI").error("Could not open folder {}", directory, error);
                minecraft.execute(() -> SystemToast.add(minecraft.gui.toastManager(),
                    SystemToast.SystemToastId.PACK_LOAD_FAILURE,
                    Component.translatable("rodel.menu.folder.error"), Component.literal(directory.toString())));
            }
        });
    }
}
