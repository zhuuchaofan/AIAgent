# LifeOS Web

Next.js frontend for the LifeOS / LifeAgent Unified Inbox.

The current home page is the accepted productized flow:

- Google login
- Unified Inbox text input
- Server-side pending action creation
- Confirm / Cancel decision
- Confirmed life records appear in the recent records timeline
- Recent records can be loaded, edited, deleted, and restored after refresh

The home page does not directly write Firestore. All product data access goes through the backend API.

## Getting Started

Install dependencies once, then run the development server:

```bash
npm install
npm run dev
```

Server actions use `API_BASE_URL`. If it is not set, local development defaults to:

```text
http://localhost:5140
```

Open `http://localhost:3000`.

## Current Routes

- `/` - productized LifeOS home / Unified Inbox
- `/knowledge` - document knowledge base
- `/chat` - RAG chat

Old home validation UI and manual ingest UI are no longer part of the current product surface. Backend compatibility endpoints remain owned by the API and tests.

## Validation

```bash
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
```

There is currently no frontend `test` script in `package.json`.

## Deployment

Do not deploy from this README directly. Follow the project deployment skill:

```text
docs/skills/cloud-run-deploy.md
```

Cloud Run environment variables, production write flags, Firestore Rules, and deployment configuration must not be changed without explicit approval.
