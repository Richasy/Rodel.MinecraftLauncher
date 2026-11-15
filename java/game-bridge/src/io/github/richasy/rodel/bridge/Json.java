package io.github.richasy.rodel.bridge;

import java.io.IOException;
import java.util.*;

/** A bounded JSON value codec. It never loads or instantiates classes named by a peer. */
final class Json {
    private Json() { }

    static Map<String, Object> object(Object... pairs) {
        Map<String, Object> value = new LinkedHashMap<String, Object>();
        for (int i = 0; i < pairs.length; i += 2) if (pairs[i + 1] != null) value.put((String)pairs[i], pairs[i + 1]);
        return value;
    }

    static String string(Map<String, Object> value, String key) {
        Object field = value.get(key);
        if (!(field instanceof String) || ((String)field).length() > 1024) throw new IllegalArgumentException("INVALID_PARAMS");
        return (String)field;
    }

    static Map<String, Object> parse(String text) throws IOException {
        Parser parser = new Parser(text);
        Object value = parser.value(0);
        parser.space();
        if (parser.position != text.length() || !(value instanceof Map<?, ?>)) throw new IOException("INVALID_JSON");
        @SuppressWarnings("unchecked") Map<String, Object> result = (Map<String, Object>)value;
        return result;
    }

    static String encode(Object value) {
        StringBuilder output = new StringBuilder();
        append(output, value, 0);
        return output.toString();
    }

    private static void append(StringBuilder output, Object value, int depth) {
        if (depth > 32) throw new IllegalArgumentException("JSON_DEPTH");
        if (value == null) output.append("null");
        else if (value instanceof String) {
            String text = (String)value;
            output.append('"');
            for (int i = 0; i < text.length(); i++) {
                char c = text.charAt(i);
                switch (c) {
                    case '"': output.append("\\\""); break;
                    case '\\': output.append("\\\\"); break;
                    case '\n': output.append("\\n"); break;
                    case '\r': output.append("\\r"); break;
                    case '\t': output.append("\\t"); break;
                    default: {
                        if (c < 32 || c >= 0xd800 && c <= 0xdfff) {
                            output.append("\\u");
                            String hex = Integer.toHexString(c);
                            for (int zero = hex.length(); zero < 4; zero++) output.append('0');
                            output.append(hex);
                        } else output.append(c);
                    }
                }
            }
            output.append('"');
        } else if (value instanceof Boolean) output.append(value);
        else if (value instanceof Number) {
            Number number = (Number)value;
            if (Double.isNaN(number.doubleValue()) || Double.isInfinite(number.doubleValue())) throw new IllegalArgumentException("NONFINITE_JSON");
            output.append(value);
        } else if (value instanceof Map<?, ?>) {
            Map<?, ?> map = (Map<?, ?>)value;
            output.append('{');
            boolean comma = false;
            for (Map.Entry<?, ?> entry : map.entrySet()) {
                if (!(entry.getKey() instanceof String)) throw new IllegalArgumentException("JSON_KEY");
                if (comma) output.append(',');
                append(output, entry.getKey(), depth + 1); output.append(':'); append(output, entry.getValue(), depth + 1); comma = true;
            }
            output.append('}');
        } else if (value instanceof Iterable<?>) {
            Iterable<?> list = (Iterable<?>)value;
            output.append('['); boolean comma = false;
            for (Object item : list) { if (comma) output.append(','); append(output, item, depth + 1); comma = true; }
            output.append(']');
        } else throw new IllegalArgumentException("JSON_TYPE");
    }

    private static final class Parser {
        private final String text;
        private int position;
        Parser(String text) { this.text = text; }
        void space() { while (position < text.length() && " \r\n\t".indexOf(text.charAt(position)) >= 0) position++; }
        char take() throws IOException { if (position == text.length()) throw new IOException("JSON_EOF"); return text.charAt(position++); }
        Object value(int depth) throws IOException {
            if (depth > 32) throw new IOException("JSON_DEPTH");
            space(); char c = take();
            if (c == '"') return quoted();
            if (c == '{') {
                Map<String, Object> map = new LinkedHashMap<String, Object>(); space();
                if (position < text.length() && text.charAt(position) == '}') { position++; return map; }
                do {
                    space(); if (take() != '"') throw new IOException("JSON_KEY"); String key = quoted();
                    space(); if (take() != ':' || map.containsKey(key)) throw new IOException("JSON_KEY");
                    map.put(key, value(depth + 1)); space(); c = take();
                } while (c == ',');
                if (c != '}') throw new IOException("JSON_OBJECT"); return map;
            }
            if (c == '[') {
                List<Object> list = new ArrayList<Object>(); space();
                if (position < text.length() && text.charAt(position) == ']') { position++; return list; }
                do { list.add(value(depth + 1)); space(); c = take(); } while (c == ',');
                if (c != ']') throw new IOException("JSON_ARRAY"); return list;
            }
            if (c == 't' || c == 'f' || c == 'n') {
                String suffix = c == 't' ? "rue" : c == 'f' ? "alse" : "ull";
                if (!text.startsWith(suffix, position)) throw new IOException("JSON_LITERAL");
                position += suffix.length(); return c == 'n' ? null : c == 't';
            }
            int start = position - 1;
            while (position < text.length() && "0123456789+-.eE".indexOf(text.charAt(position)) >= 0) position++;
            String number = text.substring(start, position);
            if (!number.matches("-?(0|[1-9][0-9]*)(\\.[0-9]+)?([eE][+-]?[0-9]+)?")) throw new IOException("JSON_NUMBER");
            try {
                if (!number.contains(".") && !number.contains("e") && !number.contains("E")) return Long.parseLong(number);
                double value = Double.parseDouble(number); if (Double.isNaN(value) || Double.isInfinite(value)) throw new IOException("JSON_NUMBER"); return value;
            } catch (NumberFormatException error) { throw new IOException("JSON_NUMBER"); }
        }
        String quoted() throws IOException {
            StringBuilder result = new StringBuilder();
            while (true) {
                char c = take();
                if (c == '"') return result.toString();
                if (c < 32) throw new IOException("JSON_STRING");
                if (c == '\\') {
                    char escape = take();
                    switch (escape) {
                        case '"': case '\\': case '/': c = escape; break;
                        case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break;
                        case 'r': c = '\r'; break; case 't': c = '\t'; break;
                        case 'u': {
                            if (position + 4 > text.length()) throw new IOException("JSON_UNICODE");
                            try { c = (char)Integer.parseInt(text.substring(position, position + 4), 16); position += 4; }
                            catch (NumberFormatException error) { throw new IOException("JSON_UNICODE"); }
                            break;
                        }
                        default: throw new IOException("JSON_ESCAPE");
                    }
                }
                result.append(c);
            }
        }
    }
}
