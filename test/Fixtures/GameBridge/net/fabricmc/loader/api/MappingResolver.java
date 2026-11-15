package net.fabricmc.loader.api;

/** Test-only shape of the public Fabric mapping boundary used by the bridge. */
public interface MappingResolver {
    String mapClassName(String namespace, String name);
    String unmapClassName(String namespace, String name);
    String mapMethodName(String namespace, String owner, String name, String descriptor);
    String mapFieldName(String namespace, String owner, String name, String descriptor);
}
