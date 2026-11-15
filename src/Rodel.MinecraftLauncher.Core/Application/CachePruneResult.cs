// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>An unchanged, eligible owned cache file.</summary>
/// <param name="Path">Absolute file path.</param>
/// <param name="Bytes">Observed file length.</param>
/// <param name="Reason">Eligibility classification.</param>
public sealed record CachePruneCandidate(string Path, long Bytes, string Reason);

/// <summary>A cache file retained because it cannot safely be reclaimed.</summary>
/// <param name="Path">Absolute file path.</param>
/// <param name="Reason">Retention classification.</param>
public sealed record CacheRetention(string Path, string Reason);

/// <summary>An independently failed cache inspection or removal.</summary>
/// <param name="Path">Affected file.</param>
/// <param name="Warning">Original recoverable error description.</param>
/// <param name="NativeError">Native IO error code, if available.</param>
public sealed record CachePruneWarning(string Path, string Warning, int? NativeError);

/// <summary>A cache reclamation assessment or execution report using the same safety policy.</summary>
/// <param name="Plan">Whether this was non-mutating.</param>
/// <param name="SkippedActiveJobs">Whether active tasks prevented reclaiming files.</param>
/// <param name="SkippedUnknownJobs">Whether unknown task activity required retention.</param>
/// <param name="CacheBusy">Whether an execution could not acquire exclusive cache ownership.</param>
/// <param name="ReclaimedBytes">Bytes actually removed.</param>
/// <param name="CandidateBytes">Bytes assessed as eligible.</param>
/// <param name="Candidates">Eligible file paths.</param>
/// <param name="Details">Eligibility details for each candidate.</param>
/// <param name="Retained">Files retained independently of eligible candidates.</param>
/// <param name="Removed">File paths actually removed.</param>
/// <param name="Warnings">Recoverable per-file failures.</param>
/// <param name="Scope">Explicit limits of the reclamation policy.</param>
public sealed record CachePruneResult(bool Plan, bool SkippedActiveJobs, bool SkippedUnknownJobs, bool CacheBusy, long ReclaimedBytes, long CandidateBytes,
    IReadOnlyList<string> Candidates, IReadOnlyList<CachePruneCandidate> Details, IReadOnlyList<CacheRetention> Retained, IReadOnlyList<string> Removed, IReadOnlyList<CachePruneWarning> Warnings,
    string Scope = "Expired download/metadata caches and owned, unchanged libraries/assets not referenced by any installed version. External roots, unknown files, installed versions and Java runtimes are retained.");