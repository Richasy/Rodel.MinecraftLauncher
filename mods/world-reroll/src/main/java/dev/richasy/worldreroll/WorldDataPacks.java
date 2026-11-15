package dev.richasy.worldreroll;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/** Copies the source world's complete datapack tree without following links or replacing target files. */
final class WorldDataPacks {
    private WorldDataPacks() {}

    static void copy(Path source, Path target) throws IOException {
        if (!Files.exists(source)) return;
        Files.createDirectories(target);
        Path realSource = source.toRealPath();
        Path realTarget = target.toRealPath();
        try (var entries = Files.walk(source)) {
            var iterator = entries.iterator();
            while (iterator.hasNext()) {
                Path entry = iterator.next();
                if (Files.isSymbolicLink(entry) || !entry.toRealPath().startsWith(realSource)) {
                    throw new IOException("Linked datapack entry is not safe to copy: " + entry);
                }
                Path destination = realTarget.resolve(source.relativize(entry)).normalize();
                if (!destination.startsWith(realTarget)) throw new IOException("Datapack path escaped replacement");
                if (Files.isDirectory(entry)) Files.createDirectories(destination);
                else Files.copy(entry, destination);
            }
        }
    }
}
