package dev.richasy.worldreroll;

import static org.junit.jupiter.api.Assertions.*;
import java.io.IOException;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

/** Protects real datapack copying; identity/session tests do not exercise the copied files or link refusal. */
class WorldDataPacksTest {
    @TempDir Path root;

    /** Missing optional packs remain optional; an actual nested tree retains empty directories and original bytes. */
    @Test void completeTreeIsCopiedWithoutChangingTheSource() throws Exception {
        Path source = root.resolve("source"), target = root.resolve("target");
        WorldDataPacks.copy(source, target);
        assertFalse(Files.exists(target));
        Files.createDirectories(source.resolve("pack/data/example/functions"));
        Files.createDirectories(source.resolve("empty"));
        byte[] archive = {0, 1, 2, -1, 0, 42};
        Files.write(source.resolve("additional.zip"), archive);
        Files.writeString(source.resolve("pack/data/example/functions/start.mcfunction"), "say 保留来源\n");

        WorldDataPacks.copy(source, target);

        assertArrayEquals(archive, Files.readAllBytes(target.resolve("additional.zip")));
        assertArrayEquals(archive, Files.readAllBytes(source.resolve("additional.zip")));
        assertEquals("say 保留来源\n", Files.readString(target.resolve("pack/data/example/functions/start.mcfunction")));
        assertEquals("say 保留来源\n", Files.readString(source.resolve("pack/data/example/functions/start.mcfunction")));
        assertTrue(Files.isDirectory(target.resolve("empty")));
    }

    /** A reserved replacement must not silently overwrite a pre-existing target pack, even when names coincide. */
    @Test void conflictingTargetPackRetainsBothOriginals() throws Exception {
        Path source = Files.createDirectory(root.resolve("source"));
        Path target = Files.createDirectory(root.resolve("target"));
        Files.writeString(source.resolve("pack.zip"), "incoming");
        Files.writeString(target.resolve("pack.zip"), "existing");

        assertThrows(FileAlreadyExistsException.class, () -> WorldDataPacks.copy(source, target));

        assertEquals("incoming", Files.readString(source.resolve("pack.zip")));
        assertEquals("existing", Files.readString(target.resolve("pack.zip")));
    }

    /** Following a linked pack would import data outside the source world; refuse it without touching its referent. */
    @Test void linkedPackIsRejectedAndItsReferentIsPreserved() throws Exception {
        Path source = Files.createDirectory(root.resolve("source"));
        Path outside = Files.writeString(root.resolve("outside.zip"), "unrelated bytes");
        try {
            Files.createSymbolicLink(source.resolve("linked.zip"), outside);
        } catch (IOException | UnsupportedOperationException error) {
            Assumptions.assumeTrue(false, "Creating symbolic links is unavailable: " + error.getMessage());
        }

        Path target = root.resolve("target");
        assertThrows(IOException.class, () -> WorldDataPacks.copy(source, target));
        assertEquals("unrelated bytes", Files.readString(outside));
        assertFalse(Files.exists(target.resolve("linked.zip")));
    }
}
