package io.github.richasy.rodel.ui.platform;

import java.nio.file.Path;
import net.minecraft.client.Minecraft;

/** Only loader discovery/configuration differs; menus continue to own their native screen models. */
public final class LoaderPlatform {
    private LoaderPlatform() { }

    public static boolean isModLoaded(String id) {
        try {
            Class<?> fabric = optional("net.fabricmc.loader.api.FabricLoader");
            if (fabric != null) return (Boolean)fabric.getMethod("isModLoaded", String.class).invoke(fabric.getMethod("getInstance").invoke(null), id);
            for (String name : new String[]{"net.neoforged.fml.ModList", "net.minecraftforge.fml.ModList"}) {
                Class<?> type = optional(name);
                if (type != null) {
                    var method = type.getMethod("isLoaded", String.class);
                    Object receiver = java.lang.reflect.Modifier.isStatic(method.getModifiers()) ? null : type.getMethod("get").invoke(null);
                    return (Boolean)method.invoke(receiver, id);
                }
            }
            return false;
        } catch (ReflectiveOperationException error) { throw new IllegalStateException("Unable to inspect the current mod loader", error); }
    }

    public static Path configDirectory() {
        try {
            Class<?> fabric = optional("net.fabricmc.loader.api.FabricLoader");
            if (fabric != null) return (Path)fabric.getMethod("getConfigDir").invoke(fabric.getMethod("getInstance").invoke(null));
            for (String name : new String[]{"net.neoforged.fml.loading.FMLPaths", "net.minecraftforge.fml.loading.FMLPaths"}) {
                Class<?> type = optional(name);
                if (type != null) return (Path)type.getMethod("get").invoke(type.getField("CONFIGDIR").get(null));
            }
            return Minecraft.getInstance().gameDirectory.toPath().resolve("config");
        } catch (ReflectiveOperationException error) { throw new IllegalStateException("Unable to resolve the current loader configuration directory", error); }
    }

    private static Class<?> optional(String name) {
        try { return Class.forName(name, false, LoaderPlatform.class.getClassLoader()); }
        catch (ClassNotFoundException absent) { return null; }
    }
}
