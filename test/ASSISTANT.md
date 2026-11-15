# Assistant Testing

The Assistant project protects non-UI risks that the CLI project does not own:
provider streaming, Core authorization and durable effects, source-grounded
checkpoints, and Embedded inference ownership. Follow [GUIDELINES.md](GUIDELINES.md);
purpose comments contain the essential author value/design review.

## Boundaries and Oracles

| Boundary | Real behavior exercised | Substituted dependency and limitation |
| --- | --- | --- |
| Provider wire streams | HTTP, SSE, Unicode, protocol-specific tool assembly, interrupted stream rejection and credential redaction; complete MEAI history serialization preserves standard protected reasoning and opaque provider extensions, permits Core-authored user records, and drops private state after revision changes or content edits | A loopback Kestrel endpoint returns small fixed wire fixtures. This is not real-account acceptance. |
| Official agent loop | Actual MEAI FunctionInvokingChatClient executes two real Core skills and continues across all four HTTP protocols, preserving signatures and separate result correlation. Shared budget stops before an extra final request; post-tool context rejection updates the working list without replay or restoring archived history. Unknown/duplicate calls are audited; incomplete/refused calls never execute | Only the HTTP model is synthetic. `AgentLoopProtocolTests` crosses real conversation storage, Core guards and protocol adapters; it does not claim model judgment or account acceptance. |
| Loop suspension and fatal storage | Actual MEAI dispatch awaits the durable Core exact approval, rejects a wrong fingerprint, resumes after approval or stops on cancellation. A read-only turn file causes real persistence failure after the first controlled effect and blocks the rest of the batch/next model. Ambient agent activity cannot enable unsafe tool telemetry | `AgentLoopGuardTests` uses controlled model and effect delegates to isolate the official-driver boundary. Existing executor/CLI cases separately cover actual launcher permissions and MCP transport. |
| Typed structured output | Four wire protocols on the self-authored IChatClient return fenced JSON parsed by MEAI ChatResponse&lt;T&gt;; source-generated selection schema is included before input budgeting, without hidden augmentation, native-schema requirement or parse retry | Fixed synthetic wire replies exercise our clients and actual MEAI parser, not language understanding. Core still rejects incomplete/repeated IDs and invalid evidence/budgets. |
| Model observability/privacy | Real in-process Activity/Meter listeners see successful/error operations and usage; prompts, replies, tool values/descriptions, credentials, endpoint query and model-file references are excluded. A synthetic unsafe control proves body capture would be detected; explicit request options, caller model identity and diagnostics remain intact | No exporter or external collector is installed. Local generation is substituted for streamed SDK metadata; the HTTP file-reference case uses the actual client. No private files are opened. |
| Checkpoint evaluation/reporting | Real Core archive/extraction/rendering feeds a MEAI evaluator and official disk scenario store; reports are reopened and compare independently expected Chinese evidence with controlled negation, ID and unknown-effect corruptions | Literal evidence coverage, not a semantic LLM score or losslessness guarantee. No judge model/account; response caching is off. Product dependencies exclude evaluation packages. |
| Tool operations | Executor policy, revocation checkpoint, actual settings patches/readback, preserved unknown fields, persisted intents and unknown-effect recovery | A delegate replaces only MCP/CLI transport. Existing CLI tests own command/schema transport. |
| Context checkpoints | Real archive hashes/storage, chained exact excerpts, Chinese corrections, unknown tool observations, invalid-selection preservation; actual HTTP context rejection, one compaction/retry and stop on repeated rejection | Controlled selector replies choose or reject source IDs. These cases do not measure semantic quality, token savings or model judgment. |
| Managed-file safety | Real scoped snapshots, exact patches, UTF-8/UTF-16 BOM/CRLF commits, original backups, source-drift rejection, protected-path/invalid-byte exclusions and durable verification | The already-staged native MCP result is substituted at the Core commit boundary. This does not prove every community-tool operation or subprocess recovery. |
| Managed-web egress | Actual proxy and destination sockets, exact endpoint/port scope across redirects and invocations, cancellation after the target receives a request | Loopback HTTP endpoints replace the public web; no native web component, public DNS or search result ranking is exercised. |
| Skills and external installation | Real skill parsing/reference snapshots, malformed-neighbor isolation, optimistic revision conflicts; skills-only installation, stale-plan rejection, preservation of edited/unowned files and unrelated client configuration | No installed client is needed. An inert executable-path tripwire makes unintended client probing fail. Codex/Claude management-command acceptance is not inferred. |
| Exact decisions | Real durable awaiting-confirmation boundary, wrong-fingerprint rejection, user rejection and recheck of a source changed after review | No native action or App callback; existing executor tests separately establish dispatch permission and effect ownership. |
| Local connection/lifetime | Real configuration persistence, Embedded-only selection, cached engine leases, unload and canceled initialization recovery | A small native-engine substitute avoids personal stores and model weights. It does not prove native generation. |
| Local MEAI messages | Actual local adapter forwards standard protected reasoning, function results and declaration-only tools to IChatClient; real MEAI stream aggregation retains returned content, usage, large numeric arguments, booleans and nulls without leaking protected data as visible deltas | Only the SDK/native client is substituted with fixed updates. This proves the adapter boundary, not a native model's generation or tool quality. |
| Core conversation lifecycle | Actual background runner, two-turn provider continuation, durable events, idempotency, cancellation/busy recovery, protected-key rotation and public export/deletion | Synthetic HTTP replies, not fake conversation results. Native local models and account quality are separate. |
| App terminal observation | The linked production poll drains late paged events when completion occurs after its first event read; the actual projection must retain the tool result and final answer alongside intermediate text | Typed service-read delegates establish the exact interleaving. This is not a native window test; packaged UIA workflows separately exercise approval and visible continuation. |
| CLI AI workflows | Real CLI/Core processes, redirected Unicode input, temporary parameters, observer resume, owned-client exit and actual stdio tools; frozen-source revocation and four-protocol probe/discovery contracts. Context rejection also exercises actual typed selector schema generation/parsing, one retry and Chinese corrections through the host | The model endpoint is synthetic and this build's product server stands in for an independent stdio program. The same cases accept the published NativeAOT CLI, including the selector path. Arbitrary binaries, game launch and public-network behavior are not inferred. |
| App control | Real current-user pipes, root/OS-package/PID/nonce identity, bounded frames, disconnect cancellation, protected discovery and CLI/MCP symbol routing | The App callback is a fake. This is not signed Sprout/MSIX behavior, visible-theme or package-persistence acceptance. |
| Broad CLI authorization | Real persisted exact decisions, revocation/scope checks, mutation journal and no replay | Dispatch is substituted with a controlled real file effect. This gateway is broad permission, not an instance/file-root sandbox or per-command effect proof. |
| Opt-in live Core/CLI combinations | `LiveLocal`: actual Embedded weights, reuse, active cancellation and unload. `LiveLocalTools`: actual SDK/local-model→MCP write/readback→read-only continuation. `LiveOnline`: actual provider-driven MCP effects and stream recovery. `LiveMixed`: local→online/tool→local with exact persisted and answered evidence. | No model/engine/tool-result substitutes. Requires explicit selection and actual dependencies; real responses can fail. Does not establish all languages/models, compression quality, worker PID termination or App behavior. |

Compound scenarios retain one invariant: permissions must not escape their grant,
uncertain writes must not replay, a checkpoint must not rewrite evidence, or a
borrowed engine must remain alive. Cases use isolated directories, ephemeral ports,
controlled completion signals and cooperative deadlines. No sleep establishes
overlap, no public service is required, and no user's model process is stopped.
The small HTTP endpoint is shared as linked source in `test\Common`; there is no
second test framework or duplicated process runner.

Superseded catch-all S1/chat probes and developer-specific reasoning/daemon
launch scripts have been retired. Their valuable configuration, credential,
streaming, history and ownership checks now live in focused cases. Obsolete
task-format compatibility and Client-only local-inference assertions are not
preserved as product contracts. Superseded live-request/manual-agent/proxy runners
and premature App/VM probe sources and duplicate payloads have also been removed locally.
All remaining old AI test sources, child fixtures and compression experiment runners have now
been physically removed, not archived as a second testing system. Valuable invariants moved
to the focused cases above; repeated catch-all flows, obsolete free-summary contracts,
private prior-run continuations and public-search ranking/client-installation smoke checks
were deliberately dropped rather than mechanically translated.
The inference-discovery recovery utility and original backups are retained, not run as tests.
The canonical command tree has no former service-test/skill-check/self-version
aliases; conversation resources and patch-style updates have real process coverage.
App control mutations use a mutation-only enum contract sharing the Core parameter
record, rather than advertising read operations in a write-only schema.
GGUF parsing/loading remains in Inference/llama.cpp. The SDK now accepts absolute files
without acquisition; the launcher retains its existing model field and forwarding path.
`LiveLocal` adds a file variant using SDK-resolved existing weights, alongside the stored-reference variant.
These SDK changes and explicit per-client load settings are merged upstream and pinned at
`c3a4725d10ab9f6f5594ef799ab29113c85f2ac4`; there is no uncommitted SDK overlay.

## Community Source Review

These pinned sources informed the design; they are not copied into the repository.

| Source | Files and adopted pattern | Deliberate limits |
| --- | --- | --- |
| [openai/codex at b0aa1ce](https://github.com/openai/codex/tree/b0aa1ce407b741c9041e7b4dc52b3cc658848935) | `codex-rs/core/tests/suite/compact.rs`, especially `summarize_context_three_requests_and_instructions`: controlled model responses, real request capture and independent expectations across compaction/continuation | Do not treat a model's summary or a smaller request as a source-fidelity oracle. |
| [github/copilot-sdk at b96cd2b](https://github.com/github/copilot-sdk/tree/b96cd2b54f7eb87458fd50f485a751c712e88f62) | `dotnet/test/Unit/ClientSessionLifetimeTests.cs` and `FfiRuntimeHostLifetimeTests.cs`: substitute the external engine boundary while testing actual resource ownership | Do not copy reflection/private-field manipulation or combine unrelated lifetime objectives. |
| [modelcontextprotocol/csharp-sdk at 6fa3825](https://github.com/modelcontextprotocol/csharp-sdk/tree/6fa3825973949a9c4f0cd8af344e15a8db09dc35) | `tests/ModelContextProtocol.Tests/Protocol/CancellationTests.cs`: distinguish pre-send cancellation from in-flight cancellation | Use an actual prepared/factory checkpoint here, not a short delay as evidence of overlap. SDK cancellation tests do not prove our child-process cleanup. |
| [Richasy/Rodel.Inference at c3a4725](https://github.com/Richasy/Rodel.Inference/tree/c3a4725d10ab9f6f5594ef799ab29113c85f2ac4) | `ModelArtifacts.cs`, `SplitModel.cs`, `LlamaServerArguments.cs`, `EmbeddedSession.cs`: typed artifacts, existing shard/worker handling and explicit caller load budgets | No launcher-side GGUF parser, merger or second worker manager; context load settings are separate from history/output limits. |

Local-file failure review also checked [ggml-org/llama.cpp#8264](https://github.com/ggml-org/llama.cpp/issues/8264)
(split-file merge failure) and [ollama/ollama#941](https://github.com/ollama/ollama/issues/941)
(content digest mismatch). These are upstream failure reports, not reproduced current product bugs:
keep shard interpretation with the backend and use source-byte identity rather than trusting a filename.

## Execution and Remaining Evidence

See [README.md](README.md) for source-dependency initialization, test execution and
formatting commands. Regression source and necessary synthetic fixtures belong in
Git; TRX, native outputs, private accounts and exploratory probes do not.

Published NativeAOT execution, real GGUF inference, owned-worker termination,
third-party stdio lifecycle and actual provider-account behavior require separate
artifact/runtime evidence. Default passing tests must not be described as those
acceptance results. Report actual outcomes and missing runtime conditions directly;
do not maintain a separate session log or a growing historical test inventory.

Live cases are regular C# MSTest methods in the existing CLI project, selected by
`LiveLocal`, `LiveLocalTools`, `LiveOnline` or `LiveMixed` plus `RODEL_TEST_LIVE=1`; routine regression
uses `TestCategory!=Live`. They reuse the same process harness, TRX and native-target
selector. There is no continuous `.ps1`/Python runner or dependency on `.local`.
The isolated bindings fix sampling temperature but leave local context unconfigured to exercise Core's 16K application default.
Configuration regressions protect existing explicit choices and reset behavior without rewriting stored user data; online settings remain unaffected.
Exact answer oracles remain intact,
and wire-history assertions distinguish projection loss from a model restatement error.
The current execution target is this device with the automatically built product CLI,
existing weights and explicit account inputs; no separate executable, proxy deployment
or VM environment is required. The NativeAOT product-target override remains optional.

TokenBudgetTests覆盖服务端三种计数协议优先、输入上限与总上下文分离、无容量时超过旧字符门限仍发送、真实usage数值拟合，以及可选选择器失败/最后一次调用额度保留。PublicModelCatalogTests覆盖null本地缓存、坏更新保留last-good、匿名请求、离线logo复用和代理容量仅作参考。ContextRecoveryTests同时覆盖Chat HTTP及Responses流内上下文拒绝的有界恢复；合成端点不等于真实服务验证。
