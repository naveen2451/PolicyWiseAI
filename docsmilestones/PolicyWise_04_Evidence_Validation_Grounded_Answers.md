# PolicyWise AI — Revision Notes 02
## LLM Evidence Validation, Grounded Answer Generation and RAG Orchestration

**Scope:** Work completed after hybrid retrieval, through the three successful end-to-end answer scenarios. Synopsis only; no complete class implementations.

## 1. The complete pipeline

```text
User question
    ↓
DocumentRetrievalService: embed query + HYBRID search (topK ≈ 5)
    ↓
IEvidenceValidator: GPT-4.1-mini judges evidence sufficiency
    ↓
 ┌──────────────────┬────────────────────┬───────────────────┐
 │ Sufficient       │ Partial            │ Insufficient      │
 │ Direct supported │ Qualified answer   │ Abstain; do not   │
 │ answer           │ + limitations      │ call generator    │
 └─────────┬────────┴──────────┬─────────┴───────────────────┘
           ↓                   ↓
 Select validated supporting chunk IDs only
           ↓
 IAnswerGenerator: grounded answer + cited chunk IDs
           ↓
 Backend verifies IDs and resolves document/page citations
           ↓
 PolicyAnswerResult → API → eventually React UI
```

## 2. Why evidence validation exists

Retrieval finds the **closest passages**, not necessarily enough evidence to answer. An insurance application must not turn a vaguely related passage into an assertion that a repair or claim is covered.

- **Relevance** asks: Is this passage on-topic?
- **Sufficiency** asks: Can the provided passages support an answer to the actual question?
- **Groundedness** asks: Is each claim in the generated answer supported by the cited text?

We implemented sufficiency assessment before generation; independent claim-level groundedness verification remains a later milestone.

## 3. Evidence validation model and contracts

**Azure resource:** `policywise-ai-resource`  
**Chat deployment:** `gpt-4.1-mini` (separate from `text-embedding-3-small`).  
**Configuration:** `EvidenceValidation:Endpoint`, `EvidenceValidation:DeploymentName`.

Application layer:

- `IEvidenceValidator.ValidateAsync(question, chunks, cancellationToken)`.
- `EvidenceStatus`: `Sufficient`, `Partial`, `Insufficient`.
- `EvidenceValidationResult`: status, `SupportingChunkIds`, concise reason.

Infrastructure:

- `AzureOpenAiEvidenceValidator` using `AzureOpenAIClient`, `GetChatClient`, `AzureCliCredential` for local development.
- Sends the question plus retrieved chunk IDs, source metadata and contents.
- Uses low-temperature generation and a **strict JSON schema** for predictable parsing.
- Instructs the LLM to use only supplied evidence, avoid assumptions, ignore instructions embedded in document text, and return real supporting chunk IDs.

Illustrative response:

```json
{
  "status": "partial",
  "supportingChunkIds": ["<real-retrieved-chunk-id>"],
  "reason": "The schedule lists a repair excess, but does not establish all repair conditions."
}
```

**Important:** The JSON schema enforces response shape, not factual correctness. The backend still validates semantics and source references.

## 4. Validation safeguards

- Reject unrecognised statuses and unknown chunk IDs.
- A `Sufficient` decision must identify supporting evidence.
- `Insufficient` must not be treated as proof that a policy excludes a benefit.
- Empty retrieval can immediately return insufficient evidence without an LLM call.
- Distinguish **model/API failure** (exception/operational error) from **genuine insufficient evidence** (valid model assessment).
- Log evidence status, supporting-chunk count, duration and available token usage; avoid logging PII.
- The LLM's assessment is **fallible**; measure false positives and false abstentions later.

## 5. Evidence-validation tests completed

| User question | Validator assessment | Interpretation |
|---|---|---|
| “What is my windscreen replacement excess?” | **Sufficient** | Schedule explicitly gives **£115** |
| “Can I repair my windscreen if it's broken?” | **Partial** | Schedule gives **£25 repair excess**, but does not fully establish repair/coverage conditions |
| “Does my insurance cover air-conditioning refill?” | **Insufficient** | No supplied clause establishes this coverage |

A legal-protection question also found the schedule entry **Motor Legal Protection £22.28**. This establishes the listed item, not necessarily all legal-protection policy terms.

## 6. Grounded answer generator

Application contracts:

- `IAnswerGenerator.GenerateAsync(question, validatedChunks, cancellationToken)`.
- `GeneratedAnswer`: answer text plus `CitedChunkIds`.
- `AnswerCitation`: `ChunkId`, `DocumentId`, `PageNumbers`.
- `PolicyAnswerResult`: final answer, `EvidenceStatus`, citations.

Infrastructure `AzureOpenAiAnswerGenerator`:

- Reuses the `gpt-4.1-mini` chat deployment.
- Receives **only** chunks approved as supporting evidence, not every retrieved hit.
- Prompts the model to avoid invented coverage, excesses, limits, exclusions and conditions.
- Uses structured JSON (`answer`, `citedChunkIds`).
- Rejects empty answers, empty citation lists and IDs absent from the supplied validated chunks.
- Logs latency, cited chunk count and token usage.

**Key learning:** The model selects which **known chunk IDs** support its response. The backend, not the model, resolves actual document IDs and page numbers from retrieved metadata.

## 7. RAG orchestration in the Application layer

`PolicyQuestionAnswerService` coordinates:

1. Validate question and `topK` input.
2. Call existing hybrid `DocumentRetrievalService`.
3. Call `IEvidenceValidator`.
4. If `Insufficient`, return a cautious response and **skip answer generation**.
5. Otherwise select chunks whose IDs are in `SupportingChunkIds`.
6. If `Partial` but there are no supporting chunks, abstain rather than inventing a qualified answer.
7. Call `IAnswerGenerator` with selected evidence.
8. Verify cited IDs exist in that selected set.
9. Resolve citations from `RetrievedDocumentChunk` metadata.
10. Return `PolicyAnswerResult` and structured diagnostics.

The development endpoint `/api/dev/ask` tests this end-to-end; `/api/dev/evidence/validate` isolates evidence assessment. Both belong in Development-only API configuration.

## 8. Important bug/learning: Partial was initially treated as insufficient

**Initial guard:**

```csharp
if (validation.Status != EvidenceStatus.Sufficient)
    return /* generic insufficient-evidence response */;
```

This meant the windscreen-repair question got `Partial` from the validator but a generic insufficient-evidence message from the orchestrator.

**Revised guard:**

```csharp
if (validation.Status == EvidenceStatus.Insufficient)
    return /* abstain without generation */;
```

We also added a fallback for `Partial` with no supporting chunks and extended the generator prompt to state what is known **and** what remains unconfirmed.

**Learning:** Evidence classification and response policy are distinct responsibilities. A `Partial` result can support a useful, cautious answer without pretending the documents establish complete coverage.

**Design improvement for later:** Pass `EvidenceStatus` and the validator's `Reason` explicitly into the generator, rather than relying on the generator to infer missing aspects from the chunks alone.

## 9. Confirmed end-to-end behaviours

| Scenario | Evidence status | Final-answer behaviour | Citation |
|---|---|---|---|
| Windscreen replacement excess | Sufficient | States **£115** | Schedule, page 2 |
| Broken windscreen repair | Partial | Mentions **£25 repair excess** and says repair eligibility/conditions cannot be fully confirmed | Supporting schedule chunk |
| Air-conditioning refill | Insufficient | Says uploaded documents do not establish coverage; recommends checking policy wording/insurer | None; generation skipped |

These scenarios were tested successfully by the user after the partial-response change.

## 10. Structured logging and cost awareness

Recommended stage events:

```text
PolicyQuestionStarted
RetrievalCompleted (embedding + hybrid search timings)
EvidenceValidationStarted / Completed (status, supporting count, tokens, ms)
AnswerGenerationStarted / Completed (citations, tokens, ms)
PolicyQuestionCompleted OR PolicyQuestionAbstained
```

- Track latency and token consumption **separately** for evidence validation and answer generation.
- Both use GPT-4.1-mini in this PoC; two LLM calls for answerable questions increase cost and latency.
- Future optimisations: fewer/shorter candidate chunks, caching where safe, batch evaluations, model selection, and measuring whether a second validation call is justified.
- Never log complete policy excerpts, personally identifying fields, or sensitive questions in production.

## 11. What has NOT been implemented or proven yet

- **Independent claim-level groundedness verification:** a valid citation ID does not prove the answer correctly interprets that citation.
- Formal retrieval and answer evaluation (Hit@K, MRR, false-abstention rate, hallucination/unsupported-claim rate).
- Semantic reranking and calibrated answerability decisions.
- Production authentication, document-level authorisation, tenant isolation and retrieval-time security filters.
- Citation rendering as clickable links to a controlled PDF/page viewer.
- Full handling of conflicting clauses, versions, multiple policies, and policy effective dates.
- Robust retries/timeouts, observability dashboards, load testing and deployment hardening.
- Stronger `Partial` contract carrying explicit supported/unsupported aspects into generation.

## 12. Revision questions

1. Why can a high-ranking hybrid-search result still be insufficient?
2. How do `Sufficient`, `Partial` and `Insufficient` differ?
3. Why shouldn't the LLM invent document names or page numbers?
4. Why should `Insufficient` skip answer generation?
5. What caused the initial `Partial` → generic insufficient-response behaviour?
6. Why is a valid citation ID not enough to guarantee groundedness?
7. Why do we distinguish a model failure from a valid insufficient-evidence assessment?
8. How do Application interfaces keep Azure-specific SDK code out of orchestration?

**Next milestone:** independent groundedness verification of generated claims against cited evidence, then systematic evaluation and production security.
