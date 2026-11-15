package io.github.richasy.rodel.bridge;

import java.lang.reflect.*;

/** Supplies a host stderr logger only for the original string/exception-only logging contract. */
final class NativeAncientLog {
    static Object create(Class<?> type) {
        if (!type.isInterface()) return null;
        int messages = 0;
        for (Method method : type.getMethods()) {
            if (!Modifier.isAbstract(method.getModifiers())) continue;
            Class<?>[] parameters = method.getParameterTypes();
            if (method.getReturnType() != void.class
                    || parameters.length < 1
                    || parameters[0] != String.class) return null;
            for (int i = 1; i < parameters.length; i++)
                if (parameters[i] != Throwable.class && parameters[i] != Object[].class)
                    return null;
            messages++;
        }
        if (messages < 3) return null;
        return java.lang.reflect.Proxy.newProxyInstance(
                type.getClassLoader(),
                new Class<?>[] {type},
                (proxy, method, args) -> {
                    if (method.getDeclaringClass() == Object.class) {
                        if (method.getName().equals("toString")) return "Seed exploration logger";
                        if (method.getName().equals("hashCode"))
                            return System.identityHashCode(proxy);
                        if (method.getName().equals("equals")) return proxy == args[0];
                    }
                    System.err.println(args[0]);
                    for (Object value : args)
                        if (value instanceof Throwable)
                            ((Throwable) value).printStackTrace(System.err);
                    return null;
                });
    }
}
