package io.github.richasy.rodel.bridge;

import java.security.*;
import java.util.Map;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** Shared protocol-v1 authentication for modern and legacy observation agents. */
final class BridgeAuthentication {
    private BridgeAuthentication() {}

    static void authenticate(Wire wire, byte[] secret, String session, String incarnation)
            throws Exception {
        Map<String, Object> hello = wire.read(4096);
        String host = Json.string(hello, "challenge");
        if (!Long.valueOf(1).equals(hello.get("version"))
                || !session.equals(hello.get("sessionId"))
                || !host.matches("[0-9a-f]{64}")) throw new SecurityException();
        byte[] challenge = new byte[32];
        new SecureRandom().nextBytes(challenge);
        String agent = hex(challenge);
        wire.write(
                Json.object(
                        "version",
                        1,
                        "sessionId",
                        session,
                        "challenge",
                        agent,
                        "proof",
                        proof(secret, "agent", session, incarnation, host, agent)));
        Map<String, Object> accepted = wire.read(4096);
        String actual = Json.string(accepted, "proof");
        if (!Long.valueOf(1).equals(accepted.get("version"))
                || !session.equals(accepted.get("sessionId"))
                || !agent.equals(accepted.get("challenge"))
                || !actual.matches("[0-9a-f]{64}")
                || !MessageDigest.isEqual(
                        unhex(actual),
                        unhex(proof(secret, "host", session, incarnation, host, agent))))
            throw new SecurityException();
    }

    private static String proof(
            byte[] secret,
            String role,
            String session,
            String incarnation,
            String host,
            String agent)
            throws Exception {
        Mac hmac = Mac.getInstance("HmacSHA256");
        hmac.init(new SecretKeySpec(secret, "HmacSHA256"));
        return hex(
                hmac.doFinal(
                        ("rodel-bridge-v1\n"
                                        + role
                                        + "\n"
                                        + session
                                        + "\n"
                                        + incarnation
                                        + "\n"
                                        + host
                                        + "\n"
                                        + agent)
                                .getBytes("UTF-8")));
    }

    static byte[] unhex(String value) {
        if (value == null || value.length() != 64 || !value.matches("[0-9a-fA-F]{64}"))
            throw new IllegalArgumentException("INVALID_SECRET");
        byte[] bytes = new byte[32];
        for (int i = 0; i < bytes.length; i++)
            bytes[i] = (byte) Integer.parseInt(value.substring(i * 2, i * 2 + 2), 16);
        return bytes;
    }

    private static String hex(byte[] bytes) {
        char[] value = new char[bytes.length * 2], digits = "0123456789abcdef".toCharArray();
        for (int i = 0; i < bytes.length; i++) {
            value[i * 2] = digits[(bytes[i] & 255) >>> 4];
            value[i * 2 + 1] = digits[bytes[i] & 15];
        }
        return new String(value);
    }
}
