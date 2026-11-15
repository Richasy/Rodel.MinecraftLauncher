package net.minecraft.nbt;

/** Carries a synthetic task serializer's opaque result; it does not implement or test Minecraft NBT. */
public final class CompoundTag {
    public String value;
    @Override public String toString() { return value; }
}
