# PolicyWise AI — RAG PoC Milestone Plan

## 1. Project Overview

**Project:** PolicyWise AI — Personal Insurance RAG Assistant

**Objective:** Build a Retrieval-Augmented Generation (RAG) application that allows users to ask natural-language questions against their personal insurance policy documents and receive accurate, grounded answers with source citations.

### Documents

- Car Insurance Policy
- Home Insurance Policy
- Health Insurance Policy
- Life Insurance Policy

### Technology Stack

| Layer             | Technology                          |
| ----------------- | ----------------------------------- |
| Frontend          | React + TypeScript                  |
| Backend           | ASP.NET Core Web API / C#           |
| AI Platform       | Microsoft Foundry / Azure OpenAI    |
| Document Storage  | Azure Blob Storage                  |
| Search Engine     | Azure AI Search                     |
| Embeddings        | Azure OpenAI Embedding Model        |
| Answer Generation | GPT model via Responses API         |
| Authentication    | Microsoft Entra ID / Azure Identity |

## 2. Architecture

### Document Ingestion Pipeline

```text
Insurance Policy Documents
           |
           v
Azure Blob Storage
           |
           v
.NET Document Processing Service
           |
           v
PDF / Word Text Extraction
           |
           v
Document Chunking
           |
           v
Azure OpenAI Embeddings
           |
           v
Azure AI Search Index
```

### Query and Retrieval Pipeline

```text
React Chat Interface
           |
           v
ASP.NET Core API
           |
           v
Natural Language Question
           |
           v
Query Embedding
           |
           v
Azure AI Search
           |
           v
Relevant Document Chunks
           |
           v
Grounded Prompt Construction
           |
           v
Foundry GPT Model
           |
           v
Answer + Source Citations
           |
           v
React Chat Interface
```

## 3. Implementation Approach

- Upload insurance documents manually through the Azure Portal initially.
- Implement document extraction, chunking, embeddings and indexing using C#.
- Keep ingestion separate from the question-answering pipeline.
- Implement and test one milestone at a time.
- Explore enterprise architecture, accuracy, performance, security and cost optimisation throughout development.
- Compare custom .NET ingestion with Azure AI Search managed indexers later.
- Defer production deployment and advanced frontend features until the core RAG concepts are understood.

---

## Milestone 1 — Azure Foundation and Connectivity

**Goal:** Establish the .NET solution and Azure infrastructure.

### Tasks

- [x] Create PolicyWiseAI .NET solution.
- [x] Create API, Application, Domain and Infrastructure projects.
- [x] Install Azure SDK dependencies.
- [x] Install and authenticate Azure CLI.
- [x] Create Azure resource group.
- [x] Create Azure Blob Storage account.
- [x] Create private `insurance-policies` container.
- [x] Configure Azure RBAC.
- [x] Configure `appsettings.Development.json`.
- [x] Verify ASP.NET Core API health endpoint.
- [x] Verify Blob Storage connectivity using `AzureCliCredential`.
- [ ] Create Azure AI Search service.
- [ ] Verify Azure AI Search connectivity.
- [ ] Verify Foundry model deployments.

### Concepts

- Azure resource organisation.
- Dependency injection and Azure SDK clients.
- `DefaultAzureCredential` vs `AzureCliCredential`.
- Authentication vs authorisation.
- Azure RBAC and private storage.
- Dependency health checks and observability.

**Completion criteria:** The .NET API successfully connects to Blob Storage, Azure AI Search and Foundry.

---

## Milestone 2 — Document Extraction

**Goal:** Read insurance policy documents from Blob Storage and extract their contents.

### Tasks

- [ ] Upload sample insurance policies through Azure Portal.
- [ ] Implement a .NET Blob document reader.
- [ ] Download document streams.
- [ ] Extract text from PDF documents.
- [ ] Extract text from Word documents.
- [ ] Preserve page numbers and document metadata.
- [ ] Handle empty, corrupted and unsupported documents.
- [ ] Explore extraction of tables and scanned PDFs.

### Concepts

- Document parsing.
- Text extraction vs OCR.
- Layout-aware extraction.
- Metadata preservation.
- Streaming vs loading entire files into memory.
- Document versioning and extraction performance.

**Completion criteria:** The .NET API extracts readable text and source metadata from sample documents.

---

## Milestone 3 — Document Chunking

**Goal:** Understand and implement chunking strategies.

### Tasks

- [ ] Implement fixed-size chunking.
- [ ] Implement overlapping chunks.
- [ ] Explore token-based chunking.
- [ ] Implement section-aware chunking.
- [ ] Preserve document, section and page metadata.
- [ ] Compare chunk sizes and overlap.
- [ ] Inspect chunks generated from insurance policies.

### Concepts

- Why chunking is required.
- Chunk size vs retrieval accuracy.
- Context preservation.
- Chunk overlap and duplication.
- Semantic boundaries.
- Insurance clauses, exclusions and endorsements.
- Impact of chunking on embedding and generation costs.

**Completion criteria:** The application generates meaningful chunks with traceable source metadata.

---

## Milestone 4 — Embeddings and Vector Similarity

**Goal:** Understand how text becomes searchable numerical representations.

### Tasks

- [ ] Configure an embedding deployment in Foundry.
- [ ] Generate embeddings from C#.
- [ ] Inspect vector dimensions.
- [ ] Generate embeddings for insurance document chunks.
- [ ] Generate embeddings for user questions.
- [ ] Implement a simple cosine similarity demonstration.
- [ ] Compare semantically similar and unrelated questions.
- [ ] Explore embedding model selection.

### Concepts

- What embeddings represent.
- Semantic similarity.
- Cosine similarity.
- Vector dimensions.
- Embedding model consistency.
- Batch processing.
- Embedding caching.
- Token usage and embedding costs.

**Completion criteria:** The application generates compatible vectors and demonstrates semantic similarity.

---

## Milestone 5 — Azure AI Search Indexing

**Goal:** Store document chunks and embeddings in a searchable index.

### Tasks

- [ ] Design the insurance document index schema.
- [ ] Define document IDs and chunk IDs.
- [ ] Add searchable text fields.
- [ ] Add vector fields.
- [ ] Configure vector search.
- [ ] Add metadata and document ownership fields.
- [ ] Upload chunks and embeddings using the .NET SDK.
- [ ] Verify indexed documents.
- [ ] Explore document updates, deletion and reindexing.

### Concepts

- Azure AI Search architecture.
- Vector indexes.
- Approximate nearest-neighbour search.
- HNSW and vector similarity.
- Searchable vs filterable fields.
- Index storage and vector compression.
- Index schema versioning.
- Incremental indexing.

**Completion criteria:** Insurance policy chunks and vectors are searchable in Azure AI Search.

---

## Milestone 6 — Retrieval Pipeline

**Goal:** Retrieve relevant policy passages using natural-language questions.

### Tasks

- [ ] Create a .NET search endpoint.
- [ ] Generate embeddings for incoming questions.
- [ ] Implement vector similarity search.
- [ ] Implement keyword search.
- [ ] Retrieve top-K relevant chunks.
- [ ] Apply policy-type filters.
- [ ] Inspect relevance scores.
- [ ] Test questions across multiple insurance policies.
- [ ] Measure retrieval latency.

### Concepts

- Query embeddings.
- Similarity search.
- Top-K retrieval.
- Precision@K and Recall@K.
- False positives and missed evidence.
- Metadata filtering.
- Retrieval latency and efficiency.

**Completion criteria:** The API retrieves relevant insurance policy passages without using GPT for answer generation.

---

## Milestone 7 — Grounded Answer Generation

**Goal:** Generate answers using retrieved document evidence.

### Tasks

- [ ] Integrate Foundry GPT through the Responses API.
- [ ] Construct prompts from retrieved chunks.
- [ ] Include source metadata in the model context.
- [ ] Generate natural-language answers.
- [ ] Return document names and page citations.
- [ ] Verify citations against retrieved evidence.
- [ ] Handle insufficient evidence.
- [ ] Prevent unsupported policy coverage claims.
- [ ] Build a basic React chat interface.

### Concepts

- Retrieval-Augmented Generation.
- Grounding vs hallucination.
- Context window management.
- Prompt construction.
- Citation verification.
- Faithfulness and answer relevance.
- Abstention when evidence is missing.
- Token consumption and generation latency.

**Completion criteria:** Users receive grounded answers with verifiable source citations.

---

## Milestone 8 — Advanced Retrieval and RAG Optimisation

**Goal:** Improve retrieval quality, relevance and efficiency.

### Tasks

- [ ] Implement hybrid keyword + vector search.
- [ ] Explore Reciprocal Rank Fusion (RRF).
- [ ] Enable semantic ranking where supported.
- [ ] Compare retrieval and reranking strategies.
- [ ] Experiment with query rewriting.
- [ ] Explore multi-query retrieval.
- [ ] Compare chunk sizes and top-K settings.
- [ ] Explore embedding and retrieval caching.
- [ ] Evaluate contextual compression.

### Concepts

- Hybrid retrieval.
- Semantic reranking.
- Query expansion.
- Retrieval recall vs precision.
- Ranking relevance.
- Reranking latency.
- Search performance tuning.
- Cost vs quality tradeoffs.

**Completion criteria:** Compare retrieval approaches using a repeatable set of insurance questions.

---

## Milestone 9 — Security, Guardrails and Access Control

**Goal:** Understand how enterprise RAG systems protect private documents and produce safe answers.

### Tasks

- [ ] Introduce document ownership metadata.
- [ ] Implement user-scoped retrieval filters.
- [ ] Ensure users cannot retrieve another user's documents.
- [ ] Explore Microsoft Entra authentication.
- [ ] Prevent prompt injection through retrieved documents.
- [ ] Apply input validation and document processing limits.
- [ ] Implement safe answer handling.
- [ ] Avoid sensitive information in application logs.
- [ ] Explore document deletion and access revocation.

### Concepts

- Authentication and authorisation.
- Document-level access control.
- Multi-tenant RAG.
- Retrieval security trimming.
- Prompt injection.
- Data leakage prevention.
- Zero-trust design.
- Secure document lifecycle management.

**Completion criteria:** Retrieval enforces document access permissions, and guardrails are tested against representative attacks.

---

## Milestone 10 — RAG Evaluation, Performance and Cost

**Goal:** Measure the quality and efficiency of the complete RAG system.

### Tasks

- [ ] Create a benchmark dataset of insurance questions and expected evidence.
- [ ] Measure Precision@K, Recall@K and MRR.
- [ ] Evaluate groundedness and answer correctness.
- [ ] Measure embedding latency.
- [ ] Measure Azure AI Search latency.
- [ ] Measure GPT generation latency.
- [ ] Track p50 and p95 response times.
- [ ] Track prompt, completion and embedding tokens.
- [ ] Estimate Azure resource and model costs.
- [ ] Compare chunking and retrieval configurations.
- [ ] Explore OpenTelemetry and distributed tracing.
- [ ] Document performance bottlenecks and improvements.

### Concepts

- RAG evaluation methodology.
- Groundedness vs correctness.
- Retrieval metrics.
- End-to-end latency.
- Observability.
- Token optimisation.
- Index storage costs.
- Performance benchmarking.
- Accuracy vs cost vs latency tradeoffs.

**Completion criteria:** Produce a measurable evaluation report comparing different RAG configurations.

---

## 4. Enterprise Architecture Topics

Although PolicyWise is a learning PoC, the following enterprise topics will be explored alongside relevant milestones:

- Event-driven ingestion using Blob Storage, Event Grid and background workers.
- Incremental document processing and reindexing.
- Idempotency, retries and failure recovery.
- Index versioning and embedding model upgrades.
- Hybrid search and semantic reranking.
- Document-level security and tenant isolation.
- Grounding and citation validation.
- Observability and distributed tracing.
- Retrieval caching and cost optimisation.
- Scaling ingestion independently from query processing.
- Comparing custom .NET pipelines with Azure-managed ingestion.

These are learning extensions, not mandatory infrastructure for the initial PoC.

## 5. Final Expected Outcome

At completion, PolicyWise AI should allow a user to:

1. Maintain insurance documents in Azure Blob Storage.
2. Process and index those documents using a .NET RAG ingestion pipeline.
3. Ask natural-language questions through React.
4. Retrieve relevant evidence from Azure AI Search.
5. Generate grounded answers using Microsoft Foundry.
6. View document-level and page-level citations.
7. Search across multiple insurance policies.
8. Receive an appropriate response when evidence is missing.
9. Access only documents they are authorised to retrieve.
10. Measure retrieval quality, groundedness, latency and cost.

**Primary learning outcome:** Gain practical, enterprise-relevant experience designing, implementing, evaluating and optimising RAG systems using C#, Microsoft Foundry and Azure AI Search.

**Current status:** Milestone 1 is in progress. Blob Storage connectivity has passed. Azure AI Search setup is next.
