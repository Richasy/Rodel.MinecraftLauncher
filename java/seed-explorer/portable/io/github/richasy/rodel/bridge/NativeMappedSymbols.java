package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.function.Function;

/**
 * Resolves actual installed members from official mapping descriptors without remapping or
 * replacing game bytecode.
 */
final class NativeMappedSymbols {
    final Map<String, String> classes = new HashMap<>(),
            names = new HashMap<>(),
            fields = new HashMap<>();
    private final Map<Class<?>, Map<String, Method>> boundMethods = new ConcurrentHashMap<>();
    private final Map<String, String> originalClasses = new HashMap<>();

    NativeMappedSymbols(Path path) throws Exception {
        if (path == null) return;
        if (Files.size(path) > 64L * 1024 * 1024) throw new IOException("SEED_SYMBOL_BUDGET");
        List<String[]> members = new ArrayList<>();
        String owner = null;
        for (String line : Files.readAllLines(path, StandardCharsets.UTF_8)) {
            if (line.startsWith("#") || line.trim().isEmpty()) continue;
            int arrow = line.lastIndexOf(" -> ");
            if (arrow < 0) continue;
            if (!Character.isWhitespace(line.charAt(0))) {
                owner = line.substring(0, arrow);
                classes.put(owner, line.substring(arrow + 4, line.length() - 1));
                continue;
            }
            String source = line.substring(0, arrow).trim().replaceFirst("^[0-9]+:[0-9]+:", "");
            String actual = line.substring(arrow + 4).trim();
            int split = source.indexOf(' ');
            if (split < 0) continue;
            String result = source.substring(0, split), signature = source.substring(split + 1);
            if (signature.contains("(")) {
                int open = signature.indexOf('('), close = signature.indexOf(')');
                members.add(
                        new String[] {
                            owner,
                            result,
                            signature.substring(0, open),
                            signature.substring(open + 1, close),
                            actual
                        });
            } else fields.put(owner + "#" + signature, actual);
        }
        for (Map.Entry<String, String> entry : classes.entrySet())
            originalClasses.put(entry.getValue(), entry.getKey());
        for (String[] m : members) {
            StringBuilder args = new StringBuilder();
            if (!m[3].isEmpty())
                for (String p : m[3].split(",")) {
                    if (args.length() > 0) args.append(',');
                    args.append(mapped(p));
                }
            names.put(classes.get(m[0]) + "#" + m[4] + "(" + args + "):" + mapped(m[1]), m[2]);
        }
    }

    String mapped(String name) {
        if (name.endsWith("[]")) return mapped(name.substring(0, name.length() - 2)) + "[]";
        return classes.getOrDefault(name, name);
    }

    Class<?> type(String name) throws Exception {
        return Class.forName(mapped(name), false, getClass().getClassLoader());
    }

    boolean hasType(String name) throws Exception {
        try {
            type(name);
            return true;
        } catch (ClassNotFoundException missing) {
            return false;
        }
    }

    Object field(String owner, String name) throws Exception {
        Field f = type(owner).getDeclaredField(fields.getOrDefault(owner + "#" + name, name));
        f.setAccessible(true);
        return f.get(null);
    }

    Object field(Object instance, String name) throws Exception {
        for (Class<?> c = instance.getClass(); c != null; c = c.getSuperclass()) {
            String named = originalClasses.getOrDefault(c.getName(), c.getName());
            try {
                Field f = c.getDeclaredField(fields.getOrDefault(named + "#" + name, name));
                f.setAccessible(true);
                return f.get(instance);
            } catch (NoSuchFieldException missing) {
            }
        }
        throw new NoSuchFieldException(name);
    }

    String key(Method m) {
        StringBuilder b =
                new StringBuilder(m.getDeclaringClass().getName() + "#" + m.getName() + "(");
        for (Class<?> p : m.getParameterTypes()) {
            if (b.charAt(b.length() - 1) != '(') b.append(',');
            b.append(p.getTypeName());
        }
        return b.append("):").append(m.getReturnType().getTypeName()).toString();
    }

    boolean matches(Class<?>[] types, Object[] args) {
        if (types.length != args.length) return false;
        for (int i = 0; i < types.length; i++) {
            if (args[i] == null) {
                if (types[i].isPrimitive()) return false;
                continue;
            }
            Class<?> type = types[i];
            if (type.isPrimitive())
                type =
                        type == int.class
                                ? Integer.class
                                : type == long.class
                                        ? Long.class
                                        : type == boolean.class
                                                ? Boolean.class
                                                : type == float.class
                                                        ? Float.class
                                                        : type == double.class
                                                                ? Double.class
                                                                : type == byte.class
                                                                        ? Byte.class
                                                                        : type == short.class
                                                                                ? Short.class
                                                                                : Character.class;
            if (!type.isInstance(args[i])) return false;
        }
        return true;
    }

    Object call(Object receiver, String name, Object... args) throws Exception {
        Class<?> target = receiver instanceof Class<?> ? (Class<?>) receiver : receiver.getClass();
        StringBuilder key =
                new StringBuilder(name)
                        .append(receiver instanceof Class<?> ? ":static" : ":instance");
        for (Object argument : args)
            key.append(';').append(argument == null ? "<null>" : argument.getClass().getName());
        Map<String, Method> bound =
                boundMethods.computeIfAbsent(target, ignored -> new ConcurrentHashMap<>());
        Method cached = bound.get(key.toString());
        if (cached != null) return invoke(cached, receiver, args);
        Map<String, Method> methods = new LinkedHashMap<>();
        for (Class<?> c = target; c != null; c = c.getSuperclass())
            for (Method m : c.getDeclaredMethods())
                methods.putIfAbsent(
                        m.getName() + Arrays.toString(m.getParameterTypes()) + m.getReturnType(),
                        m);
        for (Method m : target.getMethods())
            methods.putIfAbsent(
                    m.getName() + Arrays.toString(m.getParameterTypes()) + m.getReturnType(), m);
        List<Method> candidates = new ArrayList<>();
        for (Method m : methods.values())
            if (!m.isBridge()
                    && (names.getOrDefault(key(m), m.getName()).equals(name))
                    && matches(m.getParameterTypes(), args)
                    && (!(receiver instanceof Class<?>) || Modifier.isStatic(m.getModifiers())))
                candidates.add(m);
        List<Method> all = new ArrayList<>(candidates);
        candidates.removeIf(
                m -> all.stream().anyMatch(other -> other != m && moreSpecific(other, m)));
        if (candidates.size() != 1)
            throw new NoSuchMethodException(
                    target
                            + "."
                            + name
                            + " "
                            + Arrays.toString(args)
                            + " candidates="
                            + candidates);
        Method m = candidates.get(0);
        m.setAccessible(true);
        bound.put(key.toString(), m);
        return invoke(m, receiver, args);
    }

    /** Selects the concrete native overload when its parameter types narrow another descriptor. */
    private static boolean moreSpecific(Method candidate, Method wider) {
        Class<?>[] selected = candidate.getParameterTypes(), broad = wider.getParameterTypes();
        boolean narrower = false;
        for (int i = 0; i < selected.length; i++) {
            if (selected[i] == broad[i]) continue;
            if (!broad[i].isAssignableFrom(selected[i])) return false;
            narrower = true;
        }
        return narrower
                || candidate.getReturnType() != wider.getReturnType()
                        && wider.getReturnType().isAssignableFrom(candidate.getReturnType());
    }

    private static Object invoke(Method m, Object receiver, Object[] args) throws Exception {
        try {
            return m.invoke(receiver instanceof Class<?> ? null : receiver, args);
        } catch (InvocationTargetException e) {
            Throwable c = e.getCause();
            if (c instanceof Exception) throw (Exception) c;
            throw e;
        }
    }

    Object make(String name, Object... args) throws Exception {
        List<Constructor<?>> found = new ArrayList<>();
        for (Constructor<?> c : type(name).getDeclaredConstructors())
            if (matches(c.getParameterTypes(), args)) found.add(c);
        if (found.size() != 1)
            throw new NoSuchMethodException(
                    name + " constructor " + Arrays.toString(args) + " candidates=" + found);
        Constructor<?> c = found.get(0);
        c.setAccessible(true);
        return c.newInstance(args);
    }

    Object function(String name, Function<Object[], Object> body) throws Exception {
        return Proxy.newProxyInstance(
                getClass().getClassLoader(),
                new Class<?>[] {type(name)},
                (p, m, args) -> {
                    if (m.getDeclaringClass() == Object.class)
                        return m.getName().equals("toString")
                                ? "isolated-generator"
                                : m.getName().equals("hashCode")
                                        ? System.identityHashCode(p)
                                        : p == args[0];
                    return body.apply(args == null ? new Object[0] : args);
                });
    }

    Object enumeration(String name, String constant) throws Exception {
        for (Object value : type(name).getEnumConstants())
            if (((Enum<?>) value).name().equals(constant)) return value;
        throw new NoSuchFieldException(constant);
    }

    static Object invokeDefault(Object proxy, Method method, Object[] args) throws Throwable {
        Class<?> owner = method.getDeclaringClass();
        java.lang.invoke.MethodHandles.Lookup lookup;
        try {
            Method factory =
                    java.lang.invoke.MethodHandles.class.getMethod(
                            "privateLookupIn",
                            Class.class,
                            java.lang.invoke.MethodHandles.Lookup.class);
            lookup =
                    (java.lang.invoke.MethodHandles.Lookup)
                            factory.invoke(null, owner, java.lang.invoke.MethodHandles.lookup());
        } catch (NoSuchMethodException java8) {
            Constructor<java.lang.invoke.MethodHandles.Lookup> factory =
                    java.lang.invoke.MethodHandles.Lookup.class.getDeclaredConstructor(
                            Class.class, int.class);
            factory.setAccessible(true);
            lookup = factory.newInstance(owner, 15);
        }
        return lookup.unreflectSpecial(method, owner)
                .bindTo(proxy)
                .invokeWithArguments(args == null ? new Object[0] : args);
    }
}
