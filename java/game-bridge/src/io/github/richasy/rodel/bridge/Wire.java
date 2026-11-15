package io.github.richasy.rodel.bridge;

import java.io.*;
import java.nio.*;
import java.nio.charset.*;
import java.util.Map;

/** Shared little-endian framing with bounded allocation and strict UTF-8 decoding. */
final class Wire {
    private static final int MAXIMUM = 2 * 1024 * 1024;
    private static final Charset UTF8 = Charset.forName("UTF-8");
    private final InputStream input;
    private final OutputStream output;
    Wire(InputStream input, OutputStream output) { this.input = input; this.output = output; }
    Map<String, Object> read() throws IOException {
        return read(MAXIMUM);
    }
    Map<String, Object> read(int maximum) throws IOException {
        byte[] prefix = readExactly(4);
        int length = ByteBuffer.wrap(prefix).order(ByteOrder.LITTLE_ENDIAN).getInt();
        if (length <= 0 || length > Math.min(maximum, MAXIMUM)) throw new IOException("FRAME_LENGTH");
        byte[] bytes = readExactly(length);
        String text = UTF8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();
        return Json.parse(text);
    }
    synchronized void write(Map<String, Object> value) throws IOException {
        byte[] bytes = Json.encode(value).getBytes(UTF8);
        if (bytes.length <= 0 || bytes.length > MAXIMUM) throw new IOException("FRAME_LENGTH");
        output.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(bytes.length).array());
        output.write(bytes); output.flush();
    }

    private byte[] readExactly(int length) throws IOException {
        byte[] value = new byte[length];
        new DataInputStream(input).readFully(value);
        return value;
    }
}
