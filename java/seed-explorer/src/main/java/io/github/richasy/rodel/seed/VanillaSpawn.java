package io.github.richasy.rodel.seed;

import com.google.gson.Gson;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import net.minecraft.SharedConstants;
import net.minecraft.server.Bootstrap;
import static io.github.richasy.rodel.seed.Protocol.*;
import io.github.richasy.rodel.seed.Protocol.Position;

/** Keeps uninterruptible original new-world initialization in an owned child; cancellation never poisons the map worker. */
final class VanillaSpawn implements AutoCloseable {
    private static final Gson JSON = new Gson();
    private final Path root;
    private final ExecutorService reads = Executors.newVirtualThreadPerTaskExecutor();
    private Process process;
    private Path directory;
    private BufferedReader output;
    private PrintWriter input;

    VanillaSpawn(Path root) { this.root = root.toAbsolutePath().normalize(); }

    Position calculate(SpawnQuery query, Runnable checkpoint) throws Exception {
        try {
            checkpoint.run();
            if (process == null) start(checkpoint);
            input.println(JSON.toJson(query));
            if (input.checkError()) throw new IOException("The original spawn worker input closed.");
            var envelope = JSON.fromJson(line(checkpoint), Envelope.class);
            if (!"result".equals(envelope.kind())) throw new IOException("Original spawn initialization failed: " + envelope.error());
            var position = JSON.fromJson(JSON.toJson(envelope.data()), Position.class);
            // Reusing a JVM has produced different spawn columns. Keep each uncached world's fresh bootstrap
            // aligned with a new original server; completed positions are cached by the owning engine.
            stop();
            return position;
        } catch (Exception error) {
            stop();
            throw error;
        }
    }

    private void start(Runnable checkpoint) throws Exception {
        Files.createDirectories(root);
        directory = Files.createTempDirectory(root, "worker-");
        Path java = Path.of(ProcessHandle.current().info().command().orElseThrow());
        Path silent = java.resolveSibling("javaw.exe");
        if (Files.isRegularFile(silent)) java = silent;
        var arguments = List.of("-Xms64m", "-Xmx2048m", "-XX:ActiveProcessorCount=4", "-Dfile.encoding=UTF-8", "-Duser.dir=" + directory,
                "-cp", System.getProperty("java.class.path"), VanillaSpawn.class.getName(), Long.toString(ProcessHandle.current().pid()), directory.toString());
        Path argumentFile = directory.resolve("java.args");
        Files.writeString(argumentFile, arguments.stream().map(VanillaSpawn::quote).collect(Collectors.joining("\n")), StandardCharsets.UTF_8);
        String launcherFile = argumentFile.toString();
        if (File.separatorChar == '\\' && !launcherFile.startsWith("\\\\?\\")) {
            launcherFile = launcherFile.startsWith("\\\\") ? "\\\\?\\UNC\\" + launcherFile.substring(2) : "\\\\?\\" + launcherFile;
        }
        var builder = new ProcessBuilder(java.toString(), "@" + launcherFile);
        // Inherit the already valid OS working directory. CreateProcess cannot use an extended-length cwd;
        // all child-owned files instead use the explicit absolute workspace, and long arguments live in @argfile.
        builder.redirectError(directory.resolve("spawn.log").toFile());
        process = builder.start();
        output = new BufferedReader(new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8));
        input = new PrintWriter(new OutputStreamWriter(process.getOutputStream(), StandardCharsets.UTF_8), true);
        if (!"ready".equals(line(checkpoint))) throw new IOException("The original spawn worker did not initialize.");
    }

    private String line(Runnable checkpoint) throws Exception {
        var reader = output;
        var pending = reads.submit(() -> reader.readLine());
        while (!pending.isDone()) {
            checkpoint.run();
            Thread.sleep(20);
        }
        checkpoint.run();
        var line = pending.get();
        if (line == null || line.length() > 65_536) throw new IOException("The original spawn worker returned no valid response. " + diagnostic());
        return line;
    }

    private String diagnostic() {
        try (var file = new RandomAccessFile(directory.resolve("spawn.log").toFile(), "r")) {
            int count = (int)Math.min(8192, file.length());
            byte[] tail = new byte[count];
            file.seek(file.length() - count); file.readFully(tail);
            return new String(tail, StandardCharsets.UTF_8);
        } catch (IOException error) { return error.toString(); }
    }

    private static String quote(String value) {
        return "\"" + value.replace("\\", "\\\\").replace("\"", "\\\"").replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t") + "\"";
    }

    private void stop() throws Exception {
        if (process != null) {
            process.destroyForcibly();
            process.waitFor();
            process = null;
        }
        if (input != null) { input.close(); input = null; }
        if (output != null) { output.close(); output = null; }
        if (directory != null) {
            if (!directory.startsWith(root) || directory.equals(root)) throw new IOException("Invalid spawn cleanup scope.");
            try (var files = Files.walk(directory)) {
                for (var path : files.sorted(Comparator.reverseOrder()).toList()) Files.deleteIfExists(path);
            }
            directory = null;
        }
    }

    @Override public void close() throws Exception {
        try { stop(); } finally { reads.close(); }
    }

    /** Internal child entry point; reads only fixed original spawn queries in a newly owned workspace. */
    public static void main(String[] args) throws Exception {
        var protocol = System.out;
        System.setOut(new PrintStream(System.err, true, StandardCharsets.UTF_8));
        if (args.length != 2) throw new IllegalArgumentException("The spawn worker requires its owning process and isolated workspace.");
        var parent = ProcessHandle.of(Long.parseLong(args[0])).orElseThrow();
        var watcher = new Thread(() -> { parent.onExit().join(); Runtime.getRuntime().halt(0); }, "spawn-parent-lifetime");
        watcher.setDaemon(true); watcher.start();
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        var output = new PrintWriter(new OutputStreamWriter(protocol, StandardCharsets.UTF_8), true);
        output.println("ready");
        try (var input = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = input.readLine()) != null) {
                if (line.length() > 65_536) throw new IOException("Spawn query exceeds the internal limit.");
                try {
                    var position = VanillaRegion.spawn(Path.of(args[1]).resolve("worlds"), JSON.fromJson(line, SpawnQuery.class), new AtomicBoolean());
                    output.println(JSON.toJson(new Envelope(null, "result", position, null)));
                } catch (Throwable error) {
                    error.printStackTrace(System.err);
                    output.println(JSON.toJson(new Envelope(null, "error", null, error.getClass().getSimpleName() + ": " + error.getMessage())));
                }
            }
        }
        System.exit(0);
    }
}
