// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Provides source-generated serialization metadata for launcher skin summaries.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SkinSummary))]
[JsonSerializable(typeof(SkinProjectInfo[]))]
[JsonSerializable(typeof(SkinCommitReceipt))]
[JsonSerializable(typeof(SkinScopeRequest))]
[JsonSerializable(typeof(SkinScope))]
[JsonSerializable(typeof(SkinProposalInfo))]
public sealed partial class SkinsJsonContext : JsonSerializerContext;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(SkinProject))]
[JsonSerializable(typeof(SkinProposal))]
internal sealed partial class SkinStoreJsonContext : JsonSerializerContext;