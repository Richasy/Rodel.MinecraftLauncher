package io.github.richasy.rodel.bridge;

import io.github.richasy.rodel.input.LegacyHooks;

import java.io.*;
import java.lang.instrument.*;
import java.net.*;
import java.security.ProtectionDomain;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.jar.JarFile;

/**
 * Optional Java 6+ startup agent for read-only game-thread observations through the existing
 * authenticated Core bridge.
 */
public final class LegacyAgent {
    private static final AtomicBoolean started = new AtomicBoolean();

    private LegacyAgent() {}

    /**
     * Installs only observation hooks; missing native evidence leaves gameplay running with
     * unavailable observations.
     */
    public static void premain(String options, Instrumentation instrumentation) {
        if (!started.compareAndSet(false, true)) return;
        try {
            final String session = System.getenv("RODEL_BRIDGE_SESSION"),
                    incarnation = System.getenv("RODEL_BRIDGE_INCARNATION");
            final byte[] secret = BridgeAuthentication.unhex(System.getenv("RODEL_BRIDGE_SECRET"));
            if (!session.matches("[0-9a-f]{32}") || !incarnation.matches("[0-9a-f]{32}")) return;
            final int port = Integer.parseInt(System.getenv("RODEL_BRIDGE_PORT"));
            Properties config = new Properties();
            Reader reader =
                    new InputStreamReader(
                            new FileInputStream(System.getenv("RODEL_BRIDGE_BINDINGS")), "UTF-8");
            try {
                config.load(reader);
            } finally {
                reader.close();
            }
            final LegacySymbols symbols = new LegacySymbols(config);
            instrumentation.appendToBootstrapClassLoaderSearch(
                    new JarFile(System.getenv("RODEL_BRIDGE_INPUT_HOOKS")));
            final LegacyObserver observer = new LegacyObserver(symbols, session, incarnation);
            LegacyHooks.install(observer);
            instrumentation.addTransformer(
                    new ClassFileTransformer() {
                        @Override
                        public byte[] transform(
                                ClassLoader loader,
                                String name,
                                Class<?> redefining,
                                ProtectionDomain domain,
                                byte[] bytes) {
                            if (redefining != null || !symbols.client.equals(name)) return null;
                            try {
                                return new LegacyClassFile(bytes).observeFrames();
                            } catch (Throwable unsupported) {
                                System.err.println(
                                        "[rodel-bridge] native observation hook unavailable");
                                return null;
                            }
                        }
                    },
                    false);
            thread(
                            "rodel-observation-io",
                            new Runnable() {
                                public void run() {
                                    LegacyAgent.run(observer, port, secret, session, incarnation);
                                }
                            })
                    .start();
        } catch (Throwable unavailable) {
            System.err.println("[rodel-bridge] optional legacy observation unavailable");
        }
    }

    private static Thread thread(String name, Runnable action) {
        Thread value = new Thread(action, name);
        value.setDaemon(true);
        return value;
    }

    private static void run(
            final LegacyObserver observer,
            int port,
            byte[] secret,
            String session,
            String incarnation) {
        final ThreadPoolExecutor requests =
                new ThreadPoolExecutor(
                        2,
                        2,
                        0,
                        TimeUnit.SECONDS,
                        new ArrayBlockingQueue<Runnable>(32),
                        new ThreadFactory() {
                            public Thread newThread(Runnable action) {
                                return thread("rodel-observation-request", action);
                            }
                        });
        while (true) {
            try {
                Socket socket = new Socket();
                try {
                    socket.connect(
                            new InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 1500);
                    socket.setTcpNoDelay(true);
                    socket.setSoTimeout(3000);
                    final Wire wire = new Wire(socket.getInputStream(), socket.getOutputStream());
                    BridgeAuthentication.authenticate(wire, secret, session, incarnation);
                    socket.setSoTimeout(0);
                    while (true) {
                        final Map<String, Object> request = wire.read();
                        final String id = Json.string(request, "id");
                        if (id.isEmpty()
                                || id.length() > 64
                                || !"2.0".equals(request.get("jsonrpc"))
                                || request.containsKey("result")
                                || request.containsKey("error"))
                            throw new IOException("INVALID_REQUEST");
                        try {
                            requests.execute(
                                    new Runnable() {
                                        public void run() {
                                            dispatch(wire, observer, request, id);
                                        }
                                    });
                        } catch (RejectedExecutionException busy) {
                            wire.write(error(id, "GAME_BRIDGE_BUSY"));
                        }
                    }
                } finally {
                    socket.close();
                }
            } catch (Exception disconnected) {
                try {
                    Thread.sleep(1000);
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                    return;
                }
            }
        }
    }

    private static void dispatch(
            Wire wire, LegacyObserver observer, Map<String, Object> request, String id) {
        Map<String, Object> response;
        try {
            String method = Json.string(request, "method");
            Object result;
            if (method.equals("bridge.capabilities"))
                result = Json.object("capabilities", Arrays.asList("game.snapshot", "game.query"));
            else if (method.equals("game.snapshot")) result = observer.snapshot();
            else if (method.equals("game.query")) {
                Object parameters = request.get("params");
                if (!(parameters instanceof Map<?, ?>))
                    throw new IllegalArgumentException("INVALID_PARAMS");
                @SuppressWarnings("unchecked")
                Map<String, Object> values = (Map<String, Object>) parameters;
                result = observer.query(values);
            } else throw new IllegalArgumentException("GAME_BRIDGE_METHOD_UNKNOWN");
            response = Json.object("jsonrpc", "2.0", "id", id, "result", result);
        } catch (IllegalArgumentException failure) {
            response =
                    error(
                            id,
                            failure.getMessage() != null && failure.getMessage().matches("[A-Z_]+")
                                    ? failure.getMessage()
                                    : "INVALID_PARAMS");
        } catch (Exception failure) {
            response = error(id, "GAME_BRIDGE_REQUEST_FAILED");
        }
        try {
            wire.write(response);
        } catch (IOException disconnected) {
        }
    }

    private static Map<String, Object> error(String id, String code) {
        return Json.object(
                "jsonrpc", "2.0", "id", id, "error", Json.object("code", -32000, "message", code));
    }
}
