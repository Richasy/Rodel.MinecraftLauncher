// Copyright (c) Richasy. All rights reserved.

using System.Numerics;

namespace Rodel.Minecraft.Seeds;

internal sealed record SeedModelChoice(SeedResourceModel Model, Matrix4x4 Rotation, bool LockUv);