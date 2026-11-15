package fixture;

import java.io.*;
import java.util.*;

/** An independent game-shaped loop: coordinates, camera, velocity, particle and source worlds deliberately disagree. */
public class ClientLoop implements Runnable {
    private static ClientLoop instance;
    private final File root;
    private World scene;
    private Player selected;
    private OwnedPlayer older;
    private FloatPlayer finite;
    private SerializedPlayer primitive;
    private final boolean ownedBounds;
    private final boolean floating;
    private final boolean serialized;
    private final Entity camera;
    private final Particle distraction = new Particle();
    private Server cachedServer;
    private String applied = "A";

    private ClientLoop(File root, String shape) throws IOException {
        this.root = root;
        ownedBounds = shape.equals("owned");
        floating = shape.equals("float");
        serialized = shape.equals("serialized");
        camera = new Entity(null); camera.place(901, 902, 903);
        load("A");
    }
    /** Runs a real Java process and invokes the native loop rather than calling the observer directly. */
    public static void main(String[] args) throws Exception {
        File root = new File(args[0]);
        for (String name : Arrays.asList("A", "B")) {
            File path = new File(root, "saves/" + name);
            if (!path.mkdirs() && !path.isDirectory()) throw new IOException("Fixture directory unavailable");
            OutputStream file = new FileOutputStream(new File(path, "level.dat"));
            try { file.write(1); } finally { file.close(); }
        }
        String shape = args.length > 1 ? args[1] : "constructed";
        // The original native loop may be declared on an abstract/base client while the actual
        // vanilla launcher instantiates a subtype. Runtime-class names cannot prove its run stack.
        instance = !shape.equals("constructed") ? new ClientLoop(root, shape) { } : new ClientLoop(root, shape);
        // A live background utility calls the instrumented client before run(). Its plausible name must never establish game-thread ownership.
        final java.util.concurrent.CountDownLatch attempted = new java.util.concurrent.CountDownLatch(1);
        Thread utility = new Thread(new Runnable() { public void run() {
            instance.backgroundProbe(); attempted.countDown();
            try { while (true) { Thread.sleep(20); instance.backgroundProbe(); } }
            catch (InterruptedException stopped) { Thread.currentThread().interrupt(); }
        } }, "Client thread");
        utility.setDaemon(true); utility.start(); attempted.await();
        Thread thread = new Thread(instance, "independent-native-loop"); thread.start(); thread.join();
    }
    @Override public void run() {
        try { while (!applied.equals("exit")) { frame(); Thread.sleep(20); } }
        catch (InterruptedException stopped) { Thread.currentThread().interrupt(); }
    }
    private void frame() {
        render(0);
        try {
            File control = new File(root, "fixture-control.txt");
            if (!control.isFile()) return;
            String command;
            try { command = read(control).trim(); }
            catch (FileNotFoundException replacing) {
                // This synthetic control snapshot is atomically replaced by the C# test. Old
                // Windows FileInputStream can reject its open during replacement; leave the
                // pending command untouched until the next native frame. Other I/O still fails.
                return;
            }
            if (command.equals(applied)) return;
            if (command.equals("A") || command.equals("B")) load(command);
            else if (command.equals("move")) { if (floating) finite.place(33.125f, 71.5f, -105.75f); else if (serialized) primitive.place(33.125, 71.5, -105.75); else if (ownedBounds) older.place(33.125, 71.5, -105.75); else selected.place(33.125, 71.5, -105.75); }
            else if (command.equals("dimension")) { if (floating) finite.dimension = 1; else if (serialized) primitive.dimension = 1; else if (ownedBounds) older.dimension = 1; else ((Entity)selected).dimension = 1; }
            else if (command.equals("hide")) scene = null;
            else if (command.equals("restore")) scene = cachedServer.dimensions[0];
            else if (command.equals("remote")) {
                // Retain the old integrated server deliberately; a remote socket must revoke local ownership.
                scene = new World(new File(root, "saves/B"));
                if (floating) { finite = new FloatPlayer(scene); finite.network.socket = new java.net.Socket(); }
                else if (serialized) { primitive = new SerializedPlayer(scene); primitive.network.socket = new java.net.Socket(); }
                else if (ownedBounds) { older = new OwnedPlayer(scene); older.network.socket = new java.net.Socket(); }
                else { selected = new Player(scene); selected.network.socket = new java.net.Socket(); }
            } else if (command.equals("menu")) { selected = null; older = null; finite = null; primitive = null; scene = null; }
            applied = command;
        } catch (IOException unavailable) { throw new IllegalStateException(unavailable); }
    }

    // No diagnostic name identifies this client: its real run -> frame -> render(float) path
    // reaches the native display API, requiring descriptor-aware bounded call-graph discovery.
    private void render(float partialTick) { org.lwjgl.opengl.Display.update(); }

    private static String read(File file) throws IOException {
        InputStream input = new FileInputStream(file);
        try {
            ByteArrayOutputStream value = new ByteArrayOutputStream();
            byte[] buffer = new byte[256]; int count;
            while ((count = input.read(buffer)) >= 0) { if (value.size() + count > 1024) throw new IOException("Fixture command too long"); value.write(buffer, 0, count); }
            return new String(value.toByteArray(), "UTF-8");
        } finally { input.close(); }
    }
    private void backgroundProbe() { }
    private void load(String name) {
        scene = new World(new File(root, "saves/" + name));
        if (floating) { finite = new FloatPlayer(scene); finite.place(12.5f, 64, -7.25f); finite.velocity(1001, 1002, 1003); }
        else if (serialized) { primitive = new SerializedPlayer(scene); primitive.place(12.5, 64, -7.25); primitive.velocity(1001, 1002, 1003); }
        else if (ownedBounds) { older = new OwnedPlayer(scene); older.place(12.5, 64, -7.25); older.velocity(1001, 1002, 1003); }
        else { selected = new Player(scene); selected.place(12.5, 64, -7.25); selected.velocity(1001, 1002, 1003); }
        cachedServer = new Server(scene);
    }
    private static class World { final File location; World(File location) { this.location = location; } }
    private static final class Server { final World[] dimensions; Server(World world) { dimensions = new World[] { world }; } }
    private static class Entity {
        private double oldX = 21, oldY = 22, oldZ = 23;
        private double a, b, c, vx, vy, vz;
        private int marker = 123, dimension;
        final UUID identity = UUID.fromString("cf0bc32b-47db-4644-baa5-222222222222");
        final World environment;
        Entity(World world) { environment = world; }
        final void velocity(double x, double y, double z) { vx = x; vy = y; vz = z; }
        final void place(double x, double y, double z) { a = x; b = y; c = z; new Bounds(x - .3, y, z - .3, x + .3, y + 1.8, z + .3); }
        final void serialize(Tag tag) { tag.integer("Dimension", dimension); }
    }
    private static final class Player extends Entity { final Network network = new Network(); Player(World world) { super(world); } }
    // Independent older API shape: positioning mutates an owned box; only the player subtype serializes dimension.
    private static class OwnedEntity {
        private double previousX = 21, previousY = 22, previousZ = 23;
        private double a, b, c, vx, vy, vz;
        private final MutableBounds bounds = new MutableBounds();
        final UUID identity = UUID.fromString("cf0bc32b-47db-4644-baa5-222222222222");
        final World environment;
        OwnedEntity(World world) { environment = world; }
        final void velocity(double x, double y, double z) { vx = x; vy = y; vz = z; }
        final void place(double x, double y, double z) { a = x; b = y; c = z; bounds.reset(x - .3, y, z - .3, x + .3, y + 1.8, z + .3); }
    }
    private static final class OwnedPlayer extends OwnedEntity {
        private int marker = 123, dimension;
        final Network network = new Network();
        OwnedPlayer(World world) { super(world); }
        final void serialize(Tag tag) { tag.integer("Dimension", dimension); }
    }
    private static final class MutableBounds { MutableBounds reset(double a, double b, double c, double d, double e, double f) { return this; } }
    private static class FloatEntity {
        private float a, b, c, vx, vy, vz;
        private final FloatBounds bounds = new FloatBounds();
        final World environment;
        FloatEntity(World world) { environment = world; }
        final void velocity(float x, float y, float z) { vx = x; vy = y; vz = z; }
        final void place(float x, float y, float z) { a = x; b = y; c = z; bounds.reset(x - .3f, y, z - .3f, x + .3f, y + 1.8f, z + .3f); }
    }
    private static final class FloatPlayer extends FloatEntity {
        private int marker = 123, dimension;
        final Network network = new Network();
        FloatPlayer(World world) { super(world); }
        final void serialize(Tag tag) { tag.integer("Dimension", dimension); }
    }
    private static final class FloatBounds { FloatBounds reset(float a, float b, float c, float d, float e, float f) { return this; } }
    // Native pre-beta entities may inline box mutations. Their persistent Pos list independently
    // identifies the position triple; Motion uses equally plausible but deliberately wrong values.
    private static class SerializedEntity {
        private double a, b, c, vx, vy, vz;
        private final RawBounds bounds = new RawBounds();
        final World environment;
        SerializedEntity(World world) { environment = world; }
        final void velocity(double x, double y, double z) { vx = x; vy = y; vz = z; }
        final void place(double x, double y, double z) { a = x; b = y; c = z; bounds.a = x - .3; bounds.b = y; bounds.c = z - .3; bounds.d = x + .3; bounds.e = y + 1.8; bounds.f = z + .3; }
        final void serialize(Tag tag) { tag.list("Motion", list(new double[] {vx, vy, vz})); tag.list("Pos", list(new double[] {a, b, c})); }
        private static TagList list(double[] values) { return new TagList(); }
    }
    private static final class SerializedPlayer extends SerializedEntity {
        private int dimension;
        final Network network = new Network();
        SerializedPlayer(World world) { super(world); }
        final void savePlayer(Tag tag) { tag.integer("Dimension", dimension); }
    }
    private static final class RawBounds { double a, b, c, d, e, f; }
    private static final class Network { java.net.Socket socket; }
    private static class ParticleBase { double a, b, c; void place(double x, double y, double z) { a = x; b = y; c = z; new Bounds(x, y, z, x + 1, y + 1, z + 1); } }
    private static class ParticleMiddle extends ParticleBase { }
    private static final class Particle extends ParticleMiddle { Particle() { place(8001, 8002, 8003); } }
    private static final class Bounds { Bounds(double a, double b, double c, double d, double e, double f) { } }
    private static final class TagList { }
    private static final class Tag { void integer(String key, int value) { } void list(String key, TagList value) { } }
}
