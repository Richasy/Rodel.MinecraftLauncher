# Context Compression: Exploratory Design

**Status: preliminary proposal with a runnable prototype; not battle-tested.**

Scope: non-UI conversation context in Rodel.MinecraftLauncher.

This document defines the current experimental approach, its boundaries and
reference material. It is not a losslessness guarantee or a claim of production
readiness. Algorithms and internal checkpoint formats may change.

## 1. Problem and objectives

Long conversations accumulate user requirements, observations, tool results,
assistant claims, and background material. Sending everything indefinitely is
expensive and can reduce the model's ability to find relevant information.
Shortening that history can also remove precisely the condition needed for the
next action.

The objective is to reduce the context needed for **correct continuation**, not
merely to produce a short or fluent summary. Correct continuation includes:

- Keeping user constraints, negation, corrections, dependencies, and uncertainty.
- Distinguishing proposed, prepared, queued, applied, failed, and unknown effects.
- Preserving exact identities, versions, units, arguments, and source references.
- Avoiding fabricated completion or replay of an action to recover its output.
- Supporting multilingual conversations, with Chinese as a primary evaluation
  language rather than a translated afterthought.

Both quality and cost matter. Cost includes selection requests, output and
reasoning tokens, retrieval, and latency, not just the next answer's input.
Human readability is useful for inspection but is not itself a compression
metric. Unreadable text is not necessarily more token-efficient.

## 2. Placement and boundaries

The product dependency remains:

```text
CLI / Sprout -> Core -> internal AI module -> selected model provider
```

Core owns context selection, persistence, provenance validation, and conversation
state. MEAI's FunctionInvokingChatClient drives the actual model/tool loop; Core
applies its per-model policy to that driver's mutable working history. The internal
AI module supplies self-authored IChatClient providers and transport helpers.
Neither UI nor CLI contains a second loop or compression implementation.

Compression is not an authorization mechanism. A selected excerpt, a historical
assistant claim, or a model-generated classification cannot change Core's
permissions, confirmations, or execution state. Those concerns remain governed
by the launcher business layer's exact approvals and effect journal; compression
cannot substitute for mutation or unknown-effect evidence.

Archiving and orchestration run locally. When the selected provider is remote,
the selection input still goes to that provider. "Local archive" does not mean
"all computation and data processing remain on this machine."

## 3. Current prototype pipeline

```text
completed interaction boundary
    -> measure request structure
    -> archive complete private history
    -> identify retained messages and protected observations
    -> build source-backed excerpts
    -> select optional excerpts using packet-local IDs
    -> validate IDs, source ranges, coverage, and budget
    -> assemble checkpoint from original text
    -> atomically install the smaller window
```

### 3.1 Archive before replacement

The complete pre-compaction window is saved atomically in the conversation's
`context-archives` directory, with an identity, a content hash, and references to
earlier archives. User-visible messages and events are not deleted by compaction.

The private archive may contain provider continuation state. The model's
selection input and public archive pages exclude opaque signatures and private
reasoning. A hash checks record consistency; it is not authentication or an
operating-system sandbox.

### 3.2 Retain coherent source material

The current retention policy is intentionally conservative:

| Material | Current treatment | Important limitation |
| --- | --- | --- |
| Product rules, initial goal, latest input | Retained outside optional selection | Large required inputs can still exceed the configured budget |
| Most recent assistant/tool exchange | Kept as a complete group; large completed results may become archive references | A recent reference is not the complete original result |
| Short user messages | Kept whole, including corrections and conditions | The length cutoff is a heuristic, not a semantic classifier |
| Longer user messages | Boundary passages are protected; middle passages can be selected or omitted | Important middle constraints can still be missed |
| JSON-like material and fenced code | Not split at internal sentences or fields | A large atomic block may not fit a selector request |
| Older tool results | Core projects observed status, errors, and source references | Detailed result data may require an explicit archive read |
| Older assistant text | Optional source excerpts, labeled as historical claims | Source authenticity does not make a claim factually correct |

Natural-language splitting uses sentence and paragraph boundaries rather than
English stopwords or an arbitrary character slice. It is still a lightweight
heuristic, not a full multilingual discourse parser.

Exact source references contain an archive ID, message index, UTF-16 offset and
length, role, text, and protection flag. Whole short user messages can be restored
from older fragment representations to keep a correction coherent.

### 3.3 Ask for selection, not replacement facts

The selected service/model receives bounded, tool-free packets of optional
source excerpts, together with the original goal and current question. Packet
IDs are small local integers:

```json
{
  "keep": [0, 3],
  "omit": [1, 2, 4]
}
```

Core requires every supplied ID to appear exactly once. Invented, duplicate,
missing, malformed, or out-of-budget selections are rejected. The model is not
allowed to supply replacement prose as the checkpoint.

Each candidate carries a Core-calculated retention cost. Local integers avoid
making the model repeatedly emit long archive identifiers; stable provenance
references are retained separately.

### 3.4 Assemble and carry forward

Core assembles the selected original text in source order. Carry-forward entries
are validated against their archived role, offsets, and exact text rather than
being rewritten into another summary. Optional entries can be selected again;
this is not a guarantee of monotonic retention of every previously selected fact.

Checkpoint text distinguishes source evidence from task completion and warns
against assuming that a later distinct record replaces an earlier one.
Source-unit, retained-unit, and omitted-unit counts are calculated by Core.
They are accounting measures, not statements that a model understood every
source or proved every omitted passage irrelevant.

An older free-form checkpoint is rebuilt from its referenced originals instead
of treating its summary as authoritative memory.

### 3.5 Commit and recover explicitly

A candidate replaces the active window only after validation and successful
atomic persistence. Cancellation, invalid selection, or storage failure must not
silently install a partial checkpoint or replay a tool.

Token estimates and public model capacities are advisory, not admission gates.
If preflight selection cannot safely shrink the request, Core records the deferred
preflight and may send the unchanged history within the remaining call budget.
There is no ordinary-request history-character limit.

A provider context rejection before published content permits one compaction
recovery attempt. A second rejection stops that attempt; a later successful model
step can establish a new boundary. Selection and title calls share the turn's
strict model-call budget. Completed tools are not replayed, and the driver's
aggregate reply is not appended over the compacted durable history.

## 4. Accounting, inspection, and provisional defaults

Input accounting prefers provider count APIs where supported. Otherwise
Microsoft.ML.Tokenizers o200k counts the full request representation, including
tools and continuation state, and Core calibrates that approximation against
actual usage. Counts expose their source; optional metadata/count failures never
make a locally estimated request unconditionally non-executable. Public catalog
capacities only guide matching provider/model bindings.

Candidate search and source-selection packets still measure request-structure
Unicode characters. The Inference search uses the actual portable MEAI input,
not a second copy of its durable projection. These search costs are not exact
provider tokens or ordinary-request hard limits.

In particular, the serialized length of `\uXXXX` escapes is not the number of
tokens a model consumes for Chinese. Actual provider usage is needed to evaluate
token savings; it still does not directly establish monetary savings.

Current defaults are implementation snapshots, **not validated optimal values**:

| Setting | Current value |
| --- | --- |
| Early compaction trigger | 90% of an input-only capacity, or input plus reserved output at 90% of a shared context window; advisory |
| Candidate-search target | 65% of the structural search budget for preflight, 45% after provider rejection, with retained-content accommodation |
| Whole short-user-message cutoff | 2048 UTF-16 code units |
| Maximum candidate count per selection packet | 128, also constrained by request budget |
| Public checkpoint text preview | At most 8000 UTF-16 code units |
| Explicit archive page | 256 through 16000 UTF-16 code units |

Public turn results expose counts and a marked, bounded preview, not the entire
private excerpt collection. `summaryTruncated` and `summaryCharacters` distinguish
that preview from the complete retained text. The internal field called `summary`
currently contains Core-assembled source text; it should not be interpreted as
free-form model testimony.

Original material can be read through:

- `AssistantService.ReadContextArchiveAsync(AiContextRead)`.
- `ai chat archive read CONVERSATION ARCHIVE`.
- The conversation-scoped `conversation_history_read` model tool.

Disabling model tools does not disable an explicit Core/CLI archive read.
An archive reference alone does not guarantee that the model will fetch it.

## 5. Multilingual requirements

Chinese is a first-class design and evaluation language. The proposal does not
assume English whitespace segmentation, use an English stopword list, or
translate all sources into English before selecting them.

Evaluation needs native-language examples of negation, conditional permission,
ordering, pronouns and omission, corrections, unknown effects, and repeated
compaction. Mixed-language tool names, JSON keys, paths, hashes, IDs, versions,
and units must retain their original spelling and role.

Low entropy is not low task importance: a predictable negation or "not yet"
can determine whether an action is allowed or complete. Conversely, keeping
individual original words does not guarantee that deleting their surrounding
relationships preserves meaning.

A multilingual model label or published Chinese benchmark is only a reason to
investigate a method. It is not evidence that this launcher preserves Chinese
tool workflows. Results must be reported per language and compared against the
same model without compression; averages must not hide a weak language.
Unverified language coverage is not a reason to disable ordinary conversation.

## 6. Reference projects

These are inspected reference revisions, not dependencies or endorsements.
Repository behavior, paper claims, and this prototype's measurements are distinct.
No framework below was installed as part of the product.

| Project and inspected revision | Relevant files | Lessons and boundaries |
| --- | --- | --- |
| [openai/codex](https://github.com/openai/codex/tree/1b89beea82ae60e0dbeacb38f1ad2eae9dfed377) `1b89beea82ae60e0dbeacb38f1ad2eae9dfed377` | `codex-rs/core/src/tasks/compact.rs`; `compact.rs`; `compact_remote_v2_attempt.rs`; `context_manager/normalize.rs` | Provider-dependent remote/native and client-orchestrated paths, captured windows, and tool pairing. Codex is not remote-only. Do not copy silent history dropping or invented tool results. |
| [anomalyco/opencode](https://github.com/anomalyco/opencode/tree/cec70f3ed6b64bf0e23a7c62325850ca25d3409b) `cec70f3ed6b64bf0e23a7c62325850ca25d3409b` | `packages/core/src/session/compaction.ts` | Recent-window retention, rolling checkpoints, budget headroom, and bounded preparation. Do not assume its model-selection or retry policies match this product. |
| [google-gemini/gemini-cli](https://github.com/google-gemini/gemini-cli/tree/ed96faedfd5571f055c44f2c9509d62321d1138d) `ed96faedfd5571f055c44f2c9509d62321d1138d` | `packages/core/src/context/chatCompressionService.ts` | Validate before replacement, preserve the previous state on unsuitable output. Do not import fabricated acknowledgements or assume extra verification calls are free. |
| [microsoft/LLMLingua](https://github.com/microsoft/LLMLingua/tree/5a4c78ae18ab17a98cf997e8259354e546081d64) `5a4c78ae18ab17a98cf997e8259354e546081d64` | `llmlingua/prompt_compressor.py`; `DOCUMENT.md`; `tests/test_llmlingua2.py` | Coarse-to-fine selection, learned keep probabilities, protected tokens and regions. Not mechanical stopword deletion or universal losslessness; classifier weights/runtime and target-tokenizer behavior need independent validation. |
| [langchain-ai/deepagents](https://github.com/langchain-ai/deepagents/tree/ca48a2b49fd3a542ca609037f8e22d56a18745d1) `ca48a2b49fd3a542ca609037f8e22d56a18745d1` | `libs/deepagents/deepagents/middleware/summarization.py`; `_blob_offload.py`; `test_compaction_recovery.py` | Treat large observations separately, retain recent context, offload with references. Its argument clipping and offload-failure policies are not automatically suitable here. |
| [mastra-ai/mastra](https://github.com/mastra-ai/mastra/tree/9689b7b90a70e40c23ca7e9b790a7ebfc7d22022) `9689b7b90a70e40c23ca7e9b790a7ebfc7d22022` | `packages/memory/src/processors/observational-memory/observer-agent.ts`; `reflector-agent.ts`; `anchor-ids.ts` | Explicit task, time, correction and completion signals. Aggressive reflection still risks loss. Cross-thread profiling and the full framework are outside this proposal. |
| [Aider-AI/aider](https://github.com/Aider-AI/aider/tree/5dc9490bb35f9729ef2c95d00a19ccd30c26339c) `5dc9490bb35f9729ef2c95d00a19ccd30c26339c` | `aider/history.py`; `aider/repomap.py` | Message/token-aware tails and structure-aware relevance selection. A repository map is not a conversation compressor, and fallback to another model is not adopted. |
| [microsoft/acon](https://github.com/microsoft/acon/tree/d63f9ae18959dc7215ff62899c94c5e8c56847ae) `d63f9ae18959dc7215ff62899c94c5e8c56847ae` | `src/productive_agents/ctxopt/history_optimizer.py`; `experiments/appworld/prompts/context_opt/prompt_history_v2.jinja`; `experiments/prompt_optimizer/prompts/history_context_reducer_analysis_prompt.jinja` | Optimize history and observations using task-failure feedback. Offline future trajectories can evaluate a method, but a running agent cannot inspect an unknown future. No optimizer/training platform is imported. |
| [chroma-core/context-rot](https://github.com/chroma-core/context-rot/tree/af80a08018f2b7257c0336d04bcd02f936088106) `af80a08018f2b7257c0336d04bcd02f936088106` | `experiments/longmemeval/README.md`; `experiments/niah_extension/README.md` | Evaluate semantic retrieval, distractors, position and focused/full context. This is evaluation guidance, not an implemented compressor. |
| [openai/openai-python](https://github.com/openai/openai-python/tree/6eaa90ee20b40bbe84a5b1a5031d7cf672c7d368) `6eaa90ee20b40bbe84a5b1a5031d7cf672c7d368` | `src/openai/resources/responses/responses.py` | Confirms the dedicated `/responses/compact` interface. The prototype does not implement it, assume compatible endpoints support it, or treat opaque compact items as readable summaries. |

Licenses were checked for the inspected algorithm references: Codex, Gemini CLI
and Aider use Apache-2.0; OpenCode, LLMLingua, Deep Agents and ACON use MIT.
The inspected Mastra memory files are outside `ee/` and covered by Apache-2.0;
its enterprise portions have different terms. Any future code or asset reuse
must recheck the exact revision, component and license rather than treating
"referenced here" as permission to copy the whole repository.

## 7. Research references

| Work | Relevance | Reading/evidence boundary |
| --- | --- | --- |
| [LLMLingua](https://arxiv.org/abs/2310.05736) and [LongLLMLingua](https://arxiv.org/abs/2310.06839) | Budget control, token dependence, relevance and information position | Abstracts and related repository implementation inspected; experiments not reproduced. |
| [LLMLingua-2 v2](https://arxiv.org/html/2403.12968v2), Sections 3-4 and Appendix J | Distilled token keep/discard classification, multilingual encoders, protected word integrity | Methods and Chinese-evaluation discussion read. Published Chinese results motivate investigation, not launcher acceptance. |
| [RECOMP](https://arxiv.org/abs/2310.04408) | Extractive versus abstractive compression, selective augmentation, downstream-task objectives | Abstract checked; full experimental reproduction not performed. Query-irrelevant does not mean permanently useless. |
| [ACON v3](https://arxiv.org/html/2510.00615v3), Sections 3.1-3.3 and limitations | Compare uncompressed success with compressed failure; optimize task reward and context cost | Methods read and code inspected. No convergence or universal fidelity guarantee is inferred. |
| [ACE v3](https://arxiv.org/html/2510.04618v3), Sections 2.2-3.2 | Brevity bias, context collapse, localized updates and deterministic merging | Methods read. Evolving playbooks are not a complete substitute for transactional conversation state. |
| [LongMemEval](https://arxiv.org/abs/2410.10813) | Extraction, multi-session reasoning, temporal reasoning, updates and abstention | Abstract and related Context Rot evaluation setup checked; benchmark not reproduced. |

Published compression ratios and accuracy improvements belong to the authors'
models, datasets and protocols. They are not measurements of this launcher.

## 8. Open questions and replaceable decisions

The current direction is a hypothesis, not a frozen architecture:

- Can protected-state extraction handle constraints in the middle of long
  messages without preserving excessive text or missing conditions?
- When should an old selected excerpt remain pinned, be reselected, or move to
  retrieval-only storage? What prevents gradual loss over many cycles?
- Would source indexing, deterministic projection, or selection reuse reduce
  repeated model work without introducing language-specific blind spots?
- Does a local learned classifier offer a better quality/cost trade-off after
  weights, memory, startup, NativeAOT integration and deployment are included?
- When is provider-native compaction useful, and how can its opaque state and
  recovery behavior coexist with portable evidence?
- How accurate are provider counts and usage-calibrated o200k estimates across
  languages, chat templates and providers, and when should structural search
  costs themselves become target-token-aware?
- How much of the observed benefit survives larger independent datasets,
  natural player conversations, new models and long-horizon tool workflows?

The retention cutoff, packet size, split strategy, representation, model prompt,
ranking, budget thresholds and retrieval strategy may all change. Adding an
option or pluggable interface is not required merely to preserve this freedom.
There is no need to prebuild a generic compression framework.

What remains a product correctness requirement is the distinction between
evidence and generated claims: changing algorithms must not silently change
permissions, invent effects, replay writes, discard the only recoverable source,
or misreport a partial result as complete.

## 9. Implementation and related documents

| Entry point | Current responsibility |
| --- | --- |
| [AiChatRunner](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiChatRunner.cs) | Turn integration, shared budgets, model steps and context-limit recovery |
| [AiTurnDriver](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiTurnDriver.cs) | Official MEAI function loop, Core guard binding and mutable history boundary |
| [AiContextBudget](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiContextBudget.cs), [AiTokenCounter](../src/Rodel.MinecraftLauncher.Assistant/Providers/AiTokenCounter.cs) | Provider/local input counts, capacity evidence and usage calibration |
| [AiContextCompactor](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiContextCompactor.cs) | Planning, request estimates, retained groups and atomic installation |
| [AiContextExtraction](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiContextExtraction.cs) | Source units, coherent messages, selection validation and checkpoint text |
| [AiContextArchives](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiContextArchives.cs) | Full archive consistency and bounded original-text reads |
| [AiContextContracts](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiContextContracts.cs) | Internal records and source-generated serialization |
| [AiChatStore](../src/Rodel.MinecraftLauncher.Core/Application/AI/AiChatStore.cs) | Durable turn snapshots and bounded public checkpoint projection |

Product requirements: [AI specification](AI-SPEC.md).

Usage and API: [AI usage](AI.md), [Core API](CORE_API.md).
