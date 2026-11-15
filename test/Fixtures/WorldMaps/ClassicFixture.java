// Regeneration-only oracle: Java's own ObjectOutputStream supplies handles and field ordering.
import java.io.*;
import java.nio.file.*;
import java.util.zip.GZIPOutputStream;

public final class ClassicFixture {
    private static final class Level implements Serializable {
        private static final long serialVersionUID = 1L;
        int width = 3, depth = 4, height = 2, xSpawn = 1, ySpawn = 2, zSpawn = 0;
        String name = "Synthetic Classic";
        byte[] blocks = new byte[24];
        Level() { blocks[((3 * height) + 1) * width + 2] = 42; }
    }
    public static void main(String[] args) throws Exception {
        try (DataOutputStream output = new DataOutputStream(new GZIPOutputStream(Files.newOutputStream(Paths.get(args[0]))))) {
            output.writeInt(0x271bb788); output.writeByte(2);
            ObjectOutputStream objects = new ObjectOutputStream(output); objects.writeObject(new Level()); objects.flush();
        }
    }
}
