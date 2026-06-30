# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build, Test, and Run Commands

### Backend (.NET 10, LifeAgent.Api/)

```bash
# Build
dotnet build LifeAgent.Api/

# Run (local dev, with mock services — see env vars below)
dotnet run --project LifeAgent.Api/

# Run tests (xUnit)
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj

# Run a single test
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

### Frontend (Next.js 16, life-agent-web/)

```bash
cd life-agent-web

# Dev server (port 3000)
npm run dev

# Lint
npm run lint --prefix life-agent-web

# Build
npm run build --prefix life-agent-web

# Deploy to Cloud Run
npm run deploy
```

`npm run build --prefix life-agent-web` uses Next.js `next/font` and may need network access to fetch Google Fonts during production builds. If the build fails only on `Failed to fetch Geist/Geist Mono`, rerun with network access or switch to a local font strategy in a separate change.

### Infrastructure (GCP setup)

```bash
# Phase 3 RAG infrastructure (GCS bucket, Cloud Tasks queue, Firestore vector index)
./scripts/setup-phase3-infra.sh [PROJECT_ID] [env]
```

### Firestore Security Rules

Rules are defined in `firestore.rules` (project root). Deploy with Firebase CLI:

```bash
# Deploy rules only
firebase deploy --only firestore:rules

# Dry-run validation (no deployment)
firebase deploy --only firestore:rules --dry-run
```

**⚠️ Cross-project Auth constraint**: The Firestore database is in `copper-affinity-467409-k7` but Firebase Auth is in `my-agent-app-a5e42`. Firestore rules' `request.auth` only validates tokens from the same project. Before deploying rules, Firebase Auth must be enabled on the Firestore project (`copper-affinity-467409-k7`), or the two projects must be unified. See `docs/phase3_5_stabilization_plan.md` for details.

## Environment Variables & Mock Modes

### Backend (set before `dotnet run`)

| Variable | Purpose |
|---|---|
| `USE_MOCK_AUTH=true` | Skips Firebase token verification, injects test user (`test_user_01` for token `mock_local_token_123`) |
| `USE_MOCK_LLM=true` | Uses keyword-based `MockLlmService` instead of Gemini API |
| `FIRESTORE_PROJECT_ID` | GCP project for Firestore data (default: `copper-affinity-467409-k7`) |
| `FIREBASE_PROJECT_ID` | Firebase project for Auth (default: `my-agent-app-a5e42`) |

In **Development** mode, the DI container also registers `MockEmbeddingService` and `MockRagAnswerGenerator` for RAG endpoints automatically — no extra env var needed.

### Frontend (`.env.local` for dev, `.env.production` for deploy)

- `NEXT_PUBLIC_FIREBASE_API_KEY`, `NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN`, `NEXT_PUBLIC_FIREBASE_PROJECT_ID` — Firebase client config
- `API_BASE_URL` — backend API base URL (defaults to `http://localhost:5140` in dev)

## High-Level Architecture

This is a personal life-tracking and knowledge-management application (LifeAgent) deployed on GCP Cloud Run.

### System Diagram

```
Browser (Next.js 16, React 19)
  │  Firebase Auth (Google Sign-In)
  │  Cookie-based token → Authorization: Bearer header to backend
  ▼
LifeAgent.Api (.NET 10 Minimal API)
  │  FirebaseAuthMiddleware (FirebaseAdmin verify, or USE_MOCK_AUTH)
  │  Firestore (multi-tenant: users/{userId}/...)
  │  Cloud Storage (document uploads)
  │  Cloud Tasks (async document processing)
  │  Gemini API (LLM parsing / embeddings / RAG answer generation in production)
```

### Two Google Cloud Projects

This project uses **two different GCP projects**:
1. **`copper-affinity-467409-k7`** — Firestore data, Cloud Storage, Cloud Tasks (billing project for read/write)
2. **`my-agent-app-a5e42`** — Firebase Auth (must match the frontend Firebase app config)

The backend's `Program.cs` distinguishes them: `FirestoreDb.Create(firestoreProjectId)` uses the billing project, while `FirebaseApp.Create(...)` initializes against the auth project.

### Backend: Minimal API + Endpoint Groups

The API uses .NET Minimal API with endpoint grouping (no controllers). Each domain has its own static extension method called from `Program.cs`:

- **`LifeEndpoints`** — `POST /api/life/ingest` (parse free-text into structured event + optional reminder), `GET /api/life/events` (cursor-paginated list), `PUT /api/life/events/{id}`, `DELETE /api/life/events/{id}`
- **`ReminderEndpoints`** — CRUD for reminders under `users/{userId}/reminders/`
- **`DailySummaryEndpoints`** — `POST /api/summary/generate` (aggregates day's events via LLM)
- **`DocumentEndpoints`** — `POST /api/v1/documents` (upload → GCS → Cloud Tasks enqueue), `GET /api/v1/documents`, `DELETE /api/v1/documents/{documentId}`
- **`InternalDocumentEndpoints`** — `POST /internal/api/v1/documents/process` (Cloud Tasks worker callback: text extraction → chunking → embedding → vector store write). Secured by OIDC token validation, not Firebase Auth.
- **`RagChatEndpoints`** — `POST /api/v1/rag/chat` (user question → embed → vector search → LLM answer with citations)
- **`MigrationEndpoints`** — DB migration utilities

**Middleware**: `FirebaseAuthMiddleware` runs first (skips `/health` and `/internal`), then `ExceptionMiddleware`. `/internal` endpoints validate their own OIDC tokens.

### Backend Service Layer (DI)

Services follow an interface-implementation pattern. Key dependencies:

- `ILifeEventService` → `LifeEventService` — Firestore CRUD for life events, cursor pagination, soft delete
- `ILlmService` — Mock in dev, Gemini in prod. Parses free-text into `ParsedEvent` (type, tags, structured data, reminder intent)
- `IRagChatService` → `RagChatService` — full RAG pipeline: query embedding → `IFirestoreVectorStore.FindNearestAsync` → threshold/distance filtering → `IRagAnswerGenerator.GenerateAnswerAsync` → `CitationProcessor` citation validation → persist to `IChatSessionRepository`
- Document ingestion pipeline: `IDocumentTextExtractor` (PdfPig for PDFs) → `IChunker` (BasicChunker) → `IEmbeddingService` (Mock in dev, Gemini `text-embedding-004` in prod) → `IFirestoreVectorStore` (REST-based, writes 768-dim vectors to Firestore `chunks` collection)
- `ICloudTasksService` — enqueues async ingestion jobs with OIDC auth
- `GoogleCloudStorageService` — GCS upload/download/delete; enforces path prefix `users/{userId}/documents/{documentId}/`

### Firestore Data Model

All user data is under `users/{userId}/`:
- `users/{userId}/life_events/{eventId}` — LifeEvent documents
- `users/{userId}/reminders/{reminderId}` — Reminder documents
- `users/{userId}/documents/{documentId}` — KnowledgeDocument metadata
- `users/{userId}/chunks/{chunkId}` — KnowledgeChunk with 768-dim `embedding` vector field (COSINE index)
- `users/{userId}/chat_sessions/{sessionId}/messages/{messageId}` — ChatSession + ChatMessage (RAG conversations)
- `users/{userId}/daily_summaries/{date}` — DailySummary (cached LLM-generated summaries)
- `users/{userId}/agent_runs/{runId}` — AgentRun logs (task execution records)

### Frontend: Next.js App Router + Server Actions

The frontend uses Next.js 16 App Router with `output: 'standalone'` for Cloud Run Docker deployment.

**Server Actions** (`src/app/actions/`): These are `"use server"` functions that proxy to the backend API. They read the Firebase token from cookies and forward it as `Authorization: Bearer`. Files: `auth.ts`, `events.ts`, `dailySummary.ts`, `reminders.ts`, `knowledge.ts`.

**Pages and Components**: Single-page app (`page.tsx`) with three tabs:
1. **生活助理** — `IngestForm`, `Timeline`, `ReminderWidget`, `DailySummaryCard`
2. **知识库管理** — `KnowledgeBase` (document upload/list/delete)
3. **知识库问答** — `RagChat` (chat UI with citations)

**Auth flow**: Client-side `firebase/auth` with `GoogleAuthProvider` → ID token → server action `login(idToken)` sets HttpOnly cookie → subsequent requests read cookie in server actions and forward as Bearer token to backend.

### Development Phases

Project phase definitions are maintained in `docs/lifeos_project_roadmap.md` (single source of truth). Summary:

- **Phase 0** — Project direction & architecture design ✅
- **Phase 1** — Base application layer (Web, API, Auth, Firestore, Cloud Run deployment) ✅
- **Phase 2** — Life data layer (events, reminders, daily summaries, data migration) ✅
- **Phase 3** — Knowledge ingestion layer / RAG (document upload, embedding, vector search, RAG chat with citations) ✅
- **Phase 3.5** — Stabilization, security, testing, cost control (no new business features) 🟡
- **Phase 4** — Agent MVP (daily review agent / life recap agent) ❌ not started
- **Phase 5** — Multi-tool Agent / automation ❌ not started
- **Phase 6** — Long-term memory & proactive LifeOS ❌ not started

**Important**: Phase 3 refers specifically to RAG / knowledge ingestion. The current system is a "RAG MVP", not a full Agent. True Agent capabilities begin at Phase 4.

### Key Notes

- `life-web/` appears to be an empty/abandoned earlier frontend. The active frontend is `life-agent-web/`.
- `InternalsVisibleTo("LifeAgent.Tests")` is set in `Program.cs`, allowing tests to access internal members.
- Mock services (`MockLlmService`, `MockEmbeddingService`, `MockRagAnswerGenerator`) use keyword/rule-based logic — useful for understanding expected behavior without hitting real APIs.
- The `BasicChunker` splits documents by page/section with configurable max chunk size.

## AI Skills & Guidelines

When performing specific operations, Claude Code will automatically match skills defined under `.claude/skills/`. The canonical source of truth for these instructions is maintained in `docs/skills/`:

- **Project Phase Assessment**: Refer to [docs/skills/lifeos-phase-assessment.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/skills/lifeos-phase-assessment.md) (Adapter: `.claude/skills/lifeos-phase-assessment/SKILL.md`)
- **Cloud Run Deployment**: Refer to [docs/skills/cloud-run-deploy.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/skills/cloud-run-deploy.md) (Adapter: `.claude/skills/cloud-run-deploy/SKILL.md`)
