# Database Schemas

Last Updated: 2026-10-17 | Migration: `20261017143749_RemoveContentGenerator`

The content generator tables (ContentGenerations, ContentPieces, VoiceExamples,
VoiceConfigs, UsedSeedNotes) were removed by the `RemoveContentGenerator` migration.
For the core Zettelkasten tables (Notes, NoteTags, NoteVersions), see the initial
migration at `src/ZettelWeb/Data/Migrations/20260215182317_InitialCreate.cs`.

---

## Chat Tables

### ChatSessions

Represents a conversation session with the knowledge base.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| Id | `character varying(21)` | NO | -- | PK. Timestamp-based ID. |
| Title | `text` | NO | -- | Title of the chat session. |
| CreatedAt | `timestamp with time zone` | NO | -- | When the session was created (UTC). |
| UpdatedAt | `timestamp with time zone` | NO | -- | When the session was last updated (UTC). |
| Status | `character varying(20)` | NO | `"Active"` | Enum stored as string. |
| ContextNoteIds | `jsonb` | NO | -- | JSON array of note IDs relevant to this conversation. |
| TopicSummary | `text` | YES | -- | LLM-generated summary of the conversation topic. |
| ContextEmbedding | `real[]` | YES | -- | Embedding vector for session context. Cast to `vector` in queries. |
| ContextEmbeddingUpdatedAt | `timestamp with time zone` | YES | -- | When the context embedding was last updated. |

**Primary key:** `PK_ChatSessions (Id)`

**Relationships:**
- One-to-many with `ChatMessages` via `SessionId` (cascade delete)

**Enum: `ChatSessionStatus`**
- `Active` -- Session is ongoing
- `Archived` -- Session has been archived by the user

### ChatMessages

Individual messages in a chat conversation.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| Id | `character varying(21)` | NO | -- | PK. Timestamp-based ID. |
| SessionId | `character varying(21)` | NO | -- | FK to `ChatSessions.Id`. |
| Role | `character varying(20)` | NO | -- | Enum stored as string. |
| Content | `text` | NO | -- | Message content. |
| CreatedAt | `timestamp with time zone` | NO | -- | When the message was created (UTC). |
| ReferenceNoteIds | `jsonb` | NO | -- | JSON array of note IDs referenced in this message. |
| Embedding | `real[]` | YES | -- | Embedding vector for message content. |
| EmbeddingUpdatedAt | `timestamp with time zone` | YES | -- | When the message embedding was last updated. |

**Primary key:** `PK_ChatMessages (Id)`

**Foreign keys:**
- `FK_ChatMessages_ChatSessions_SessionId` -> `ChatSessions.Id` (ON DELETE CASCADE)

**Indexes:**
- `IX_ChatMessages_SessionId` on `SessionId`
- `IX_ChatMessages_CreatedAt` on `CreatedAt`

**Enum: `ChatMessageRole`**
- `User` -- Message sent by the user
- `Assistant` -- Message sent by the assistant/chatbot
- `System` -- System message (context, instructions, etc.)

## Design Decisions

- **Enums as strings**: All enums use `HasConversion<string>()` with
  `HasMaxLength(20)`, consistent with the existing Note entity pattern.

## ID Generation

Chat entities use the same ID format as Notes: a 17-digit UTC timestamp (`yyyyMMddHHmmssfff`) followed by a 4-digit random suffix, totaling 21 characters. IDs are generated at the application layer, not by the database.
