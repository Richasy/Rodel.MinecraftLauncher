// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;

namespace Rodel.Minecraft.Seeds;

internal sealed record SeedResourceModel(Dictionary<string, string> Textures, JsonElement[] Elements);