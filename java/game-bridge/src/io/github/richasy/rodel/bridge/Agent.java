package io.github.richasy.rodel.bridge;

import java.lang.instrument.Instrumentation;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.jar.JarFile;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** Optional startup agent. Its only bytecode hook is the exact native key-state getter; authentication is untouched. */
public final class Agent {
    private static final AtomicBoolean started = new AtomicBoolean();
    private static volatile AiLink aiLink;
    private Agent() { }

    /** Sends one AI action through the current authenticated bridge; no provider configuration is exposed to mods. */
    public static CompletableFuture<String> requestAi(String request) {
        AiLink link = aiLink;
        return link == null ? CompletableFuture.failedFuture(new IllegalStateException("GAME_BRIDGE_DISCONNECTED")) : link.request(request);
    }

    /** Starts daemon bridge workers; bridge initialization failure must not abort the game main class. */
    public static void premain(String options, Instrumentation instrumentation) {
        if (!started.compareAndSet(false, true)) return;
        try {
            String session = System.getenv("RODEL_BRIDGE_SESSION"), incarnation = System.getenv("RODEL_BRIDGE_INCARNATION");
            byte[] secret = HexFormat.of().parseHex(System.getenv("RODEL_BRIDGE_SECRET"));
            if (secret.length != 32 || !session.matches("[0-9a-f]{32}") || !incarnation.matches("[0-9a-f]{32}")) return;
            int port = Integer.parseInt(System.getenv("RODEL_BRIDGE_PORT"));
            Bindings bindings = new Bindings(instrumentation, System.getenv("RODEL_BRIDGE_BINDINGS"));
            instrumentation.appendToBootstrapClassLoaderSearch(new JarFile(System.getenv("RODEL_BRIDGE_INPUT_HOOKS")));
            instrumentation.addTransformer(new KeyInputTransformer(bindings, instrumentation), false);
            GameAccess game = new GameAccess(bindings, session, incarnation);
            threads("rodel-bridge-io").newThread(() -> run(game, port, secret, session, incarnation)).start();
        } catch (Throwable error) { System.err.println("[rodel-bridge] optional bridge initialization failed"); }
    }

    static ThreadFactory threads(String name) { return action -> { Thread thread = new Thread(action, name); thread.setDaemon(true); return thread; }; }

    private static void run(GameAccess game, int port, byte[] secret, String session, String incarnation) {
        ThreadPoolExecutor requests = new ThreadPoolExecutor(2, 2, 0, TimeUnit.SECONDS, new ArrayBlockingQueue<>(32), threads("rodel-bridge-request"));
        while (true) {
            try (Socket socket = new Socket()) {
                socket.connect(new InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 1500);
                socket.setTcpNoDelay(true); socket.setSoTimeout(3000);
                Wire wire = new Wire(socket.getInputStream(), socket.getOutputStream());
                BridgeAuthentication.authenticate(wire, secret, session, incarnation);
                AiLink link = new AiLink(wire, game, requests);
                aiLink = link;
                game.connected();
                socket.setSoTimeout(0);
                while (true) {
                    Map<String, Object> request = wire.read();
                    if (request.containsKey("result") || request.containsKey("error")) { link.response(request); continue; }
                    String id = Json.string(request, "id");
                    if (id.isEmpty() || id.length() > 64 || !"2.0".equals(request.get("jsonrpc")) || request.containsKey("result") || request.containsKey("error")) throw new IllegalArgumentException("INVALID_REQUEST");
                    try { requests.execute(() -> dispatch(wire, game, request, id)); }
                    catch (RejectedExecutionException full) { wire.write(error(id, "GAME_BRIDGE_BUSY")); }
                }
            } catch (Exception error) {
                AiLink link = aiLink;
                aiLink = null;
                if (link != null) link.close();
                game.disconnected();
                // Reauthenticate to the same monitor. Never resend a pending edit or log the secret.
                try { Thread.sleep(1000); } catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); return; }
            }
        }
    }

    private static void dispatch(Wire wire, GameAccess game, Map<String, Object> request, String id) {
        Map<String, Object> response;
        try {
            Object raw = request.get("params");
            @SuppressWarnings("unchecked") Map<String, Object> parameters = raw instanceof Map<?, ?> ? (Map<String, Object>)raw : Map.of();
            Object result = switch (Json.string(request, "method")) {
                case "bridge.capabilities" -> game.capabilities();
                case "game.snapshot" -> game.snapshot();
                case "game.region" -> game.region(parameters);
                case "game.query" -> game.query(parameters);
                case "game.edit" -> game.edit(parameters);
                case "game.operation.show" -> game.operation(parameters, false);
                case "game.operation.cancel" -> game.operation(parameters, true);
                default -> throw new IllegalArgumentException("GAME_BRIDGE_METHOD_UNKNOWN");
            };
            response = Json.object("jsonrpc", "2.0", "id", id, "result", result);
        } catch (IllegalArgumentException error) { response = error(id, error.getMessage() != null && error.getMessage().matches("[A-Z_]+") ? error.getMessage() : "INVALID_PARAMS"); }
        catch (Exception error) { System.err.println("[rodel-bridge] request failed: " + error.getClass().getSimpleName()); response = error(id, "GAME_BRIDGE_REQUEST_FAILED"); }
        try { wire.write(response); } catch (Exception disconnected) { }
    }
    private static Map<String, Object> error(String id, String code) { return Json.object("jsonrpc", "2.0", "id", id, "error", Json.object("code", -32000, "message", code)); }
}
