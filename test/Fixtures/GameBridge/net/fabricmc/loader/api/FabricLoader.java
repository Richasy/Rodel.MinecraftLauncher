package net.fabricmc.loader.api;

/** Test-only sentinel: querying this loader before its game entrypoint is a regression. */
public final class FabricLoader {
    public static int calls;
    public static boolean initialized;
    public static MappingResolver resolver;
    public static FabricLoader getInstance() { calls++; if (!initialized) throw new AssertionError("Early Fabric initialization"); return new FabricLoader(); }
    public MappingResolver getMappingResolver() { return resolver; }
}
