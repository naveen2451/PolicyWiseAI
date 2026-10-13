# PolicyWise AI — Revision Notes 01
## After Chunking: Embeddings, Azure AI Search, Indexing and Retrieval

**Project:** Insurance policy RAG learning PoC  
**Scope:** Starts *after* structure-aware PDF chunking. This is a revision synopsis, not a source-code dump.  
**Stack:** React/TypeScript UI; ASP.NET Core (.NET 10) Clean Architecture; Azure Blob Storage; Azure AI Document Intelligence; Azure OpenAI; Azure AI Search.

## 1. Starting point: output of chunking

The ingestion pipeline had already extracted PDF text/Markdown using Document Intelligence `prebuilt-layout` and produced deterministic, structure-aware `DocumentChunk` records. Relevant fields:

- `ChunkId`: stable identifier derived from document/version/content context.
- `DocumentId`, `BlobPath`, `PolicyType`: provenance and filtering metadata.
- `Content`, `SectionTitle`, `PageNumbers`: searchable evidence and citation context.
- `ChunkIndex`, `SourceVersion`, `ChunkingStrategyVersion`: ordering and re-ingestion traceability.

**Key learning:** The embedding model receives *chunk text*, not the whole PDF. Chunk boundaries and preserved headings/page metadata affect later search quality and citations.

## 2. Embedding generation

**Deployment:** Azure OpenAI `text-embedding-3-small`, **1536 dimensions**; Azure resource `policywise-ai-resource` in UK South.

Flow:

```text
DocumentChunk.Content → embedding model → float[1536]
```

- The same embedding deployment/dimensions must be used for indexed document chunks and user questions.
- Embeddings encode semantic similarity; they do **not** determine truth, policy coverage or answerability.
- Application abstraction: `ITextEmbeddingGenerator`; orchestration via `DocumentEmbeddingService`.
- Infrastructure calls Azure OpenAI through `Azure.AI.OpenAI` and local `AzureCliCredential`.
- During local development, explicit `AzureCliCredential` avoided the credential-chain delay observed with `DefaultAzureCredential`. Production should use managed identity and appropriate RBAC.

## 3. Vector index in Azure AI Search

**Service:** `policywise-search-dev` (F0).  
**Index:** `policywise-documents`.

Important schema:

| Field | Purpose |
|---|---|
| `id` | Unique searchable document key (chunk ID) |
| `content` | Full-text searchable chunk content |
| `contentVector` | 1536-dimensional vector for semantic similarity |
| `sectionTitle` | Searchable string heading |
| `documentId`, `documentVersion`, `fileName` | Provenance/version |
| `pageNumbers` | Page citation metadata |
| `policyType`, `blobPath` | Filtering/source location |
| `chunkIndex`, `chunkingStrategyVersion` | Diagnostics and ingestion traceability |

- Vector index uses **HNSW** approximate nearest-neighbour search and **cosine** similarity, with profile `policywise-vector-profile`.
- Vector dimensions in the index must exactly match embedding output.
- A real schema mismatch was fixed: `sectionTitle` must be `Edm.String`, not a string collection.
- Azure AI Search stores both textual content and vector/metadata fields. Blob Storage remains the original PDF source.

## 4. Indexing pipeline

```text
Azure Blob PDF
   → Document Intelligence layout extraction
   → Structure-aware chunks (already implemented)
   → Embeddings for each chunk
   → Azure AI Search index documents
```

- Application coordinates the process; Infrastructure `AzureSearchDocumentIndexer` handles Search SDK details.
- Dev ingestion endpoint: `POST /api/dev/documents/ingest?blobPath=...`.
- Verified ingestion for **3 PDFs**: 2 car documents + 1 life document, 6 pages total.
- Stable chunk IDs and version metadata help reason about re-ingestion. **Production deletion/replacement of obsolete version chunks still needs explicit lifecycle design.**

## 5. Query-time vector retrieval

```text
Question → query embedding → Azure AI Search vector query
         → topK ranked chunks + source metadata
```

- Application `DocumentRetrievalService` generates the question embedding and calls `IDocumentRetriever`.
- Infrastructure `AzureSearchDocumentRetriever` uses `SearchClient` and `VectorizedQuery` over `contentVector`.
- `KNearestNeighborsCount` and `SearchOptions.Size` control candidate retrieval and returned results.
- Optional `policyType` filter uses Azure Search OData filtering; it is **not** a substitute for per-user document authorisation.
- `RetrievedDocumentChunk` holds chunk ID, document ID, policy type, heading, content, pages and score.
- Dev retrieval endpoint: `/api/dev/search/vector?...` (endpoint name may remain from initial vector-only testing).

### Observed pure-vector results

| Question | Observation | Learning |
|---|---|---|
| “What is the windscreen replacement excess?” | Correct policy schedule chunk ranked first; vector score ~0.727; evidence includes **£115 replacement**, **£25 repair** | Semantic retrieval can locate a relevant clause |
| “Does my insurance cover legal protection?” | Irrelevant certificate clause ranked first (~0.637); relevant schedule entry ranked second (~0.621) | Highest semantic similarity is not necessarily the best evidence |

The schedule lists **Motor Legal Protection £22.28**, but a line item alone does not establish all benefits, conditions or exclusions.

## 6. Hybrid search: BM25 + vectors

```text
User question ─┬─ BM25 keyword/full-text matching
               └─ Query embedding → vector matching
                           ↓
                    Azure AI Search
                  fused ranking (RRF)
                           ↓
                       topK chunks
```

- Hybrid retrieval combines exact lexical matches (BM25) with semantic similarity (vectors).
- Azure AI Search fuses ranked lists using **Reciprocal Rank Fusion (RRF)**.
- In the tested policy questions, hybrid retrieval promoted the correct evidence to **rank 1** for both windscreen excess and legal protection.
- A hybrid score such as **0.03333** is an RRF ranking score; **do not compare it numerically with pure-vector cosine scores or interpret it as a confidence percentage**.
- Hybrid is the default retrieval mode in the later evidence-validation and answer-generation workflow.

## 7. Why retrieval alone is not enough

Question: **“Does my insurance cover air-conditioning refill?”**

Hybrid Search still returns nearest chunks, even though the uploaded documents do not contain a supporting clause. This is expected topK behaviour, not proof of coverage or exclusion.

**Crucial distinction:**

- **Relevant**: chunk discusses a related subject.
- **Sufficient**: chunk(s) actually support the requested conclusion.
- **Grounded**: the final answer makes only claims supported by cited evidence.

Search score thresholds alone are unreliable, especially with RRF. We therefore added an LLM evidence-validation stage (covered in Notes 02).

## 8. Performance and observability

Log structured diagnostic data:

- Query length, policy type, topK, retrieval mode.
- Embedding latency, Azure Search latency, end-to-end retrieval latency.
- Ranked chunk IDs/document IDs, headings, page numbers and scores.
- Failures and cancellation paths.

One observed pure-vector request took roughly **2.07 s embedding + 1.72 s search = 3.80 s total**. These are observations, **not** service-level targets or representative benchmarks.

**Privacy:** Avoid logging complete insurance questions, document text or personal details in production.

## 9. Evaluation concepts learned (implementation deferred)

- Retrieval: **Hit@K**, **Recall@K**, **Precision@K**, **MRR**, **nDCG**.
- End-to-end: answerability accuracy, unsupported-answer rate, false abstentions, citation correctness, groundedness.
- Operational: p50/p95 latency, input/output tokens, cost per question.
- Build a small manually labelled question→supporting-chunk test set before optimising search.
- Semantic reranking and other retrieval improvements are potential future experiments, not yet completed milestones.

## 10. Revision questions

1. Why must the index vector dimensions match the embedding model output?
2. Why do we preserve `documentId`, `sectionTitle` and `pageNumbers` on each chunk?
3. What is the difference between cosine similarity and an RRF hybrid score?
4. Why might pure-vector retrieval rank an irrelevant clause first?
5. Why does topK retrieval return results for an unsupported question?
6. How do ingestion-time embeddings differ from query-time embeddings?
7. Why isn't `policyType=car` adequate for access control?

**Next:** `PolicyWise_02_Evidence_Validation_Grounded_Answers.md`.
