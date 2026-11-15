# Test Design Guidelines

## Scope and Interpretation

These guidelines define expectations for designing, reviewing, and maintaining automated tests. They are independent of any particular application, source layout, test class, or test framework. Examples are hypothetical and do not require corresponding features or tests to exist.

**Must** identifies requirements for trustworthy results, safety, and maintainability. **Should**, **prefer**, and **consider** describe defaults that can be adapted to the risk being tested. Explain meaningful departures near the relevant decision; do not introduce approval gates or paperwork merely to justify a reasonable choice.

Keep concrete commands, implementation details, execution results, and coverage inventories in separate operational documentation. A test plan or coverage report is not evidence that a behavior has been verified.

## 1. Choose Behaviors Worth Protecting

- Every test must have a clear purpose: the behavior it protects, the plausible regression it detects, and the consequence of that regression. Prioritize risks such as data loss, unintended actions, lost user choices, broken contracts, and failed recovery.
- Define expectations through observable results: returned values, state transitions, protocol fields, durable effects, or recovery outcomes. Derive expected results from an explicit contract or independent fixture, not by copying the implementation's output back into the assertion.
- Review existing coverage before adding a test. Choose a boundary that detects the regression reliably at a reasonable cost; there is no requirement to create a test for every command, class, or method.
- Do not mechanically test getters, data containers, constants, every text string, or behavior owned by a dependency. Simple code can still deserve a test when it participates in a meaningful transformation, constraint, or known defect.
- For a discovered defect, prefer the smallest regression test that reproduces it. Extend or correct an existing test when it already expresses the relevant behavior instead of duplicating the scenario.

### Required Review Before Adding Regression Tests

Every proposed regression test must receive a value and design review **before implementation**, including tests proposed by an automated assistant. The author must resolve the following points before adding the case:

- Identify the observable behavior or established contract being protected, a plausible incorrect implementation, and the meaningful consequence of that failure. A test is not justified merely because code changed, a defect was introduced during the current work, or a larger passing-test count would make the work look complete.
- Compare the proposal with existing coverage. State the distinct evidence it would add and why extending an existing case would not be sufficient. Do not add duplicates or mechanically test getters, fixtures, dependency defaults, or the shape of the chosen fix.
- Explain how the assertions distinguish the incorrect behavior from the intended behavior. Expectations must come from the contract or an independent oracle, not simply reproduce production logic or confirm that the fixture ran. When practical, check a known failing version or a controlled fault; do not require production mutation for every test or claim demonstrated regression detection without evidence.
- For concurrency, cancellation, timing, or recovery cases, identify the actual boundary and how execution reaches it. Starting tasks, repeating operations, sleeping, or observing one successful read does not establish overlap or exercise a failure window. If the proposed check could pass without reaching the relevant condition, redesign it or do not add it as a regression test.
- Check responsibility, readability, execution cost, isolation, and cleanup. Choose the smallest useful boundary and keep failure attribution clear. The test must not introduce a speculative framework or substantial implementation duplication merely to support itself.

Only implement a proposed case after this review establishes useful regression evidence. Otherwise, omit it, merge it into existing coverage, or redesign it; do not weaken the purpose to justify keeping it. A defect introduced during current work can warrant a regression test when it breaks a meaningful contract, but its origin neither proves nor disproves the test's value.

Keep the essential review rationale in the existing purpose or setup comments. This is a required author review, not a requirement for a separate report, a test-count target, or human approval before routine implementation. Follow an explicit user-requested approval boundary when one exists. After implementation, review the actual assertions and setup again; a passing run does not replace this check.

The following questions guide design; they do not require a separate form or registry:

| Question | Design implication |
| --- | --- |
| What plausible defect would make this test fail? | Increasing coverage alone is not a sufficient purpose. |
| Why this boundary? | A transformation may need a focused test; integration claims require crossing the relevant boundary. |
| What distinguishes it from existing tests? | Identify a different failure mechanism or contract, rather than a renamed repetition. |
| What would a pass establish? | State the real path and substituted dependencies without extrapolating to untested capabilities. |

## 2. Organize Test Files and Manage Overlap

- Organize test files around behavior boundaries, state models, or failure responsibilities. A short class-level or file-level comment must describe the primary focus and, where useful, neighboring boundaries. Do not mechanically mirror production classes or collect unrelated responsibilities in a catch-all suite.
- Allow overlap when each test adds distinct evidence. A focused test might verify a conversion rule, an integration test the wiring across a boundary, and a release test the behavior of a packaged artifact. Higher-level tests need not repeat every lower-level input combination.
- When setup, actions, and principal expectations substantially overlap, consider merging tests or using meaningful parameterized variants of the same invariant. Shared dependencies, similar-looking code, or a common entry point alone are not reasons to merge different responsibilities.
- Split when objectives or failure attribution become mixed, not when a file reaches an arbitrary line count. Merge to remove redundant maintenance, not merely to reduce file count.
- Separate reusable execution infrastructure from test cases. Extract genuinely shared capabilities rather than building speculative base classes, interface layers, or a generic test workflow engine.
- Do not require one test class per production class or one test project per feature. New directories, categories, and projects should serve actual dependency, execution, or ownership boundaries.

## 3. Make Intent Clear and Keep Methods Focused

- Every test must include a clear purpose comment explaining the behavior or regression risk and its expected outcome. Plain comments or documentation comments are acceptable; no particular syntax is required. A descriptive name does not replace this purpose comment.
- Names should identify the scenario and expected result. Avoid names consisting only of an identifier or a generic success label. A `Behavior_Condition_Outcome` pattern is useful, but not mandatory.
- Explain complex setup, substituted boundaries, non-obvious data choices, and important workflow checkpoints. Do not narrate every line or treat Arrange/Act/Assert labels alone as an explanation of intent.
- Avoid large test methods. A reader should see the preconditions, important actions, and principal expectations without following several layers of helpers.
- When a method accumulates unrelated objectives, branching, repetition, or elaborate setup, first narrow the scenario. Then extract small, clearly named preparation, execution, or verification helpers where they improve readability. Moving the same oversized method into a helper is not a solution.
- Do not impose fixed line limits or assertion counts. One behavior may require several related assertions. Keep principal behavioral assertions visible in the case; repeated checks may use explicit assertion helpers without hiding an entire business workflow.
- Maintain test code with the same care as production code: readable names, understandable data, and useful diagnostics. Avoid reproducing substantial parsing, transaction, or business logic just because it lives in a test.

## 4. Keep Compound Tests Coherent

Compound tests may contain multiple actions and checkpoints. They should protect one coherent objective, transaction, or recovery invariant rather than satisfy an arbitrary limit on operation count.

The following abstract examples illustrate design choices, not required test cases:

| Coherent compound scenario | Reason to split or simplify |
| --- | --- |
| Acquire a resource, encounter a dependent failure, release the resource, and verify that retry succeeds. | Add unrelated validation, reporting, and authorization scenarios to the same method. |
| Start an operation, cancel at a controlled checkpoint, and verify the promised final state. | Combine every cancellation point and recovery policy through many branches. |
| Create an artifact, reopen it independently, and verify its documented meaning. | Also exercise unrelated artifact types that require different fixtures and expectations. |

- Check necessary results at important stages so that a failed prerequisite does not become a confusing assertion failure much later.
- Do not split preparation, execution, and recovery into tests that depend on preceding test results. Cases should normally run, rerun, and filter independently. Expensive read-only fixtures may be shared; uncontrolled mutable business state must not be.
- When a workflow becomes long, determine whether it contains multiple objectives before extracting helpers. A complete journey is not a justification for an oversized method.
- Use parameterization for meaningful variants of the same flow and expectation rule. Separate, descriptive cases are often clearer when variants need substantially different setup or branching.

## 5. Select Boundaries and Test Doubles by Risk

Do not prescribe numerical ratios of unit, integration, and workflow tests. Prefer the least expensive approach that provides sufficient confidence, while retaining higher-level tests for claims that lower-level tests cannot establish. The test pyramid is a cost model, not a quota.

| Verification objective | Often suitable boundary |
| --- | --- |
| Non-trivial transformations, interacting constraints, or state transitions | Focused calls with minimal synthetic input. |
| Process startup, argument transport, stream handling, or persistence | A real process or storage boundary in an isolated environment. |
| Communication between components | Actual requests across the relevant interface, with checks of the contract and resulting effects. |
| External-service failures and recovery | Real local behavior against a controlled service endpoint or suitable test double. |
| Packaging, runtime resources, or release-specific behavior | The actual distributed artifact, using selected risk-relevant scenarios. |

- Mocks, stubs, fakes, and synthetic data are permitted. Use the simplest approach that controls the required conditions; neither a mocking framework nor the elimination of real collaborators is mandatory.
- Explain what each double replaces and what remains real. Replacing the persistence operation cannot establish that persistence works. A synthetic worker can establish argument forwarding without establishing the behavior of a real external application.
- Interaction assertions are valuable when the interaction itself is part of the contract, such as preventing an unauthorized request. Incidental internal call counts or ordering should not replace outcome assertions.
- Default regression tests should not depend on public services, private accounts, personal data, or a developer's existing machine state. Tests that genuinely need external systems or special capabilities may exist, but must identify their prerequisites and evidence separately.
- Exposing an internal boundary, using dependency injection, refactoring, or adopting a tool can be appropriate when it solves a real testability problem. Do not redesign production architecture merely to accommodate a preferred test style.

## 6. Use Reliable Oracles and Safe Execution

- For mutating operations, check necessary effects rather than only a success message. For rejection, failure, or cancellation, verify preservation and unintended side effects in proportion to risk. Not every read-only test needs a complete storage snapshot.
- Keep synthetic fixtures small and representative. Include unknown fields, unrelated state, or additional artifacts when their preservation is relevant. Fixtures must not contain credentials, private data, or unauthorized third-party material.
- Isolate writable storage. When shell behavior is not the subject, pass process arguments separately rather than interpolating a shell command. Confine or reliably restore changes to environment, culture, working directory, handles, and other shared state.
- Prefer readiness signals, state transitions, events, or controlled barriers for synchronization, with reasonable deadlines. Do not assume completion after an arbitrary sleep. Controlled clocks and elapsed-time assertions remain appropriate when timing itself is under test.
- Cancellation and timeouts must account for ongoing work, stream readers, and cleanup; a framework timeout does not necessarily stop underlying processes. Clean up only resources owned by the test, and do not silently report success when cleanup fails.
- Parallel tests need independent resources. Shared-state cases may use explicit coordination or local serialization with a reason. Do not force concurrency for speed or disable all concurrency to conceal isolation defects.
- Failures should expose enough context to diagnose the failed stage, such as inputs, exit status, captured streams, and relevant state. Retain only necessary, sanitized evidence when retention is useful; successful runs do not require indefinite artifact storage.
- Prefer stable structure and essential semantics over incidental layout, random identifiers, or exact durations. Precise comparisons and snapshots are appropriate when formatting, bytes, or visual output are themselves the contract.

## 7. Validate and Maintain the Tests

- For high-risk safeguards or tests with a history of false confidence, consider demonstrating failure with controlled faults, known regressions, or targeted negative checks. Mutation testing is optional; changing production code is not a prerequisite for every test.
- Fix synchronization, isolation, and environment causes of flakiness instead of retrying until green. Testing a product's retry behavior is a different concern. If an external probe needs retries, preserve attempts and initial failures rather than presenting it as a stable regression pass.
- Explain skipped or quarantined tests, their conditions, and how they can be restored. Do not convert infrastructure failures, broken fixtures, or failing tests into successful results. Do not require tracking tickets or approval procedures without a practical need.
- Maintain regression source and necessary small fixtures under version control, with a documented execution entry point. Keep exploratory probes, reports, build outputs, and machine-specific data separate according to the applicable storage policy. Tests must not require another developer's untracked directory to run.
- Report actual cases, data-driven variants, failures, skips, and execution conditions. Distinguish runtime modes, packaged artifacts, doubles, and real external validation. An assertion count is not a test count, and a pass does not cover scenarios that were not run.
- Use coverage to discover gaps, not as proof of quality. Do not prescribe universal coverage targets, suite sizes, execution times, or full-suite runs for every edit. Choose verification based on impact and risk while honoring applicable required checks.
- Update tests when intended behavior changes. Merge, rewrite, or remove redundant, ineffective, or obsolete tests rather than preserving them because of past effort. Preserve meaningful boundary coverage when removing duplication.

## 8. Review Proportionately

Use these questions as review prompts, not mandatory per-case paperwork or automatic rejection criteria for optional techniques:

- Are the purpose, failure mechanism, and intent comments clear?
- Are file ownership and neighboring cases distinct, and is overlap justified?
- Does the method retain one coherent objective with readable steps and assertions?
- Are expectations independent, doubles appropriately scoped, and side-effect checks sufficient for the risk?
- Can the case run independently, reliably, and safely, with useful failure diagnostics?
- Does reporting distinguish implementation, execution, skips, and remaining gaps honestly?

## References and Adaptation

These sources inform the guidelines; they are not specifications to copy wholesale. Check tool and framework compatibility before adopting APIs described in current documentation. Reading a newer example does not require upgrading dependencies.

| Source | Principles adopted and limits |
| --- | --- |
| [Microsoft Learn: .NET testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices) | Clear scenarios, minimal inputs, independent expectations, and cautious use of coverage. Single-action guidance does not prohibit coherent recovery workflows. |
| [Microsoft Learn: Writing MSTest tests](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests) | Asynchronous tests, data-driven techniques, and organization tools used when helpful, without mandating a comment format or parameterizing every variant. |
| [Microsoft Learn: MSTest execution control](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-controlling-execution) | Isolation, appropriate concurrency, timeouts, cooperative cancellation, cautious retries, and documented skips; not blanket adoption of every extension or API. |
| [Ham Vocke: The Practical Test Pyramid](https://martinfowler.com/articles/practical-test-pyramid.html) | Multiple granularities, observable behavior, boundary integration, selected valuable journeys, and removal of duplication; no fixed proportions or exhaustive class-by-class test inventory. |
| [Martin Fowler: Test Double](https://martinfowler.com/bliki/TestDouble.html) | Make the purpose and evidence of stubs, fakes, and mocks clear without requiring a particular mocking philosophy. |
| [Command Line Interface Guidelines](https://clig.dev/) | Exit status, composable output, validation, and recovery as observable contracts; generic conventions do not override an explicitly defined interface. |

Maintain this document as design guidance, not an expanding feature backlog or an inventory of particular tests. Revise it using maintenance experience without coupling its rules to current implementation names or locations.