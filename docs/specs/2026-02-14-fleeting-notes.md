# Feature Specification: Fleeting Notes Capture

**Author**: James Eastham
**Date**: 2026-02-14
**Status**: Draft
**Last Updated**: 2026-02-14

---

## Executive Summary

Add fleeting note capture to Zettel-Web so that quick thoughts, links, and ideas
can be captured with minimal friction from any context (web UI, email, Telegram)
and processed later into permanent Zettelkasten notes. The goal is to make
Zettel-Web the **single system** for all knowledge capture, eliminating the need
for Apple Notes, self-emails, and other scattered tools.

---

## Problem Statement

### The Problem

Fleeting thoughts and interesting links occur throughout the day - while reading,
on mobile, at the desk - but the friction of opening Zettel-Web and creating a
full note means most are lost or scattered across other apps. Even when captured
elsewhere, they rarely get transferred into the Zettelkasten.

### Evidence

- Currently uses a mix of other apps, self-emails, and loses thoughts entirely
- Thoughts occur while reading/learning, on mobile, and at the desk
- Both capture speed AND processing are broken - the full pipeline fails
- Estimated 5-10 fleeting thoughts per day worth capturing

### Current State

- Open another app (Apple Notes, etc.) and jot the thought down
- Email/message self as a reminder
- Frequently lose the thought entirely due to friction
- Captured thoughts in other apps rarely get processed into Zettelkasten notes

### Impact of Not Solving

Knowledge leaks. The Zettelkasten only contains what survives the friction of
full note creation. The richest source of ideas - spontaneous thoughts during
reading and daily life - is almost entirely lost.

---

## Users

### Primary Persona: James (sole user)

| Attribute         | Description                                          |
|-------------------|------------------------------------------------------|
| Role              | Knowledge worker / developer                         |
| Technical Level   | Expert                                               |
| Goals             | Build a comprehensive personal knowledge base        |
| Frustrations      | Thoughts scatter across tools, never get processed   |
| Context           | At desk, on mobile, while reading/learning           |

### Fleeting Note Profile

Typical fleeting note: **a link + brief comment** about why it matters.
Sometimes just a sentence or two. Needs to be captured in seconds.

---

## Success Metrics

### Primary Metric

| Metric                          | Current     | Target          | Timeline  |
|---------------------------------|-------------|-----------------|-----------|
| Tools used for knowledge capture | 3-4 apps   | 1 (Zettel-Web)  | 4 weeks   |

### Secondary Metrics

| Metric                          | Target                              |
|---------------------------------|-------------------------------------|
| Fleeting notes captured/day     | 5-10 (matching thought frequency)   |
| Fleeting notes processed/week   | >70% processed within 7 days        |
| Capture-to-done time            | <10 seconds for quick capture       |

### Guardrail Metrics

- Permanent note creation workflow must not be disrupted
- Search must continue to work across all notes (fleeting + permanent)
- App performance must not degrade with growing fleeting note volume

### Validation Approach

Personal usage tracking over 2-4 weeks. Success = James stops using other
apps for capture entirely.

---

## Solution

### Overview

Three capture channels feeding into a unified inbox:

```
[Web UI: Floating Button] ──┐
[Email: Inbound Parse]    ──┼──▶ POST /api/notes (status=fleeting)
[Telegram Bot]            ──┘         │
                                      ▼
                              ┌──────────────┐
                              │  Fleeting     │
                              │  Notes Inbox  │──▶ Process ──▶ Permanent Note
                              └──────────────┘
```

1. **Data Model**: Add `status` field to existing `Note` model
   (`fleeting` | `permanent`). Default: `permanent` (existing behaviour
   unchanged).

2. **In-App Capture**: Floating capture button on every page. Tap → minimal
   form (content + optional tags) → save. Under 10 seconds.

3. **Email Capture**: Dedicated inbound email address. Send a link + comment,
   it becomes a fleeting note. Sender verification against known addresses.

4. **Telegram Bot**: Message the bot with a link/thought, it creates a fleeting
   note. Verified against known Telegram chat ID.

5. **Link Enrichment**: When a URL is detected in fleeting note content,
   automatically fetch page title, summary, and key content. Stored alongside
   the raw message.

6. **Inbox UI**: Dedicated view showing all fleeting notes with age indicators.
   Process a note by expanding it into the full editor (status → permanent).

7. **Stale Note Reminders**: Visual age indicators (e.g., "3 days ago",
   "2 weeks ago" with colour coding) to surface unprocessed notes.

### Why This Approach

- **Status on Note** (not separate model): Fleeting notes ARE notes. Search,
  tags, and embedding all work automatically. Processing is just enriching
  content and flipping status.
- **Floating button** (not Cmd+K): Always visible, zero-discovery friction.
  Works on mobile too.
- **Email + Telegram** (not WhatsApp): Both have simple, free APIs. Email
  leverages an existing habit (self-emailing). Telegram has a clean bot API
  with webhook support. WhatsApp Business API is complex and costly.
- **Full enrichment**: Since the typical note is "link + comment", auto-fetching
  title/summary makes the inbox dramatically more useful for processing later.

### Alternatives Considered

| Alternative        | Pros                    | Cons                         | Why Not          |
|--------------------|-------------------------|------------------------------|------------------|
| Separate model     | Clean separation        | Duplication, migration path  | Unnecessary complexity |
| Tag-based          | No schema change        | Fragile, no dedicated UI     | Tags can be renamed/deleted |
| WhatsApp           | Popular messaging app   | Complex API, Meta approval   | Defer to v2 if needed |
| Cmd+K integration  | No new UI elements      | Hidden, not mobile-friendly  | Floating button is more discoverable |

---

## User Stories

### Epic: Fleeting Note Data Model

#### Story 1: Note Status Field

**As a** knowledge worker
**I want** notes to have a status (fleeting or permanent)
**So that** I can distinguish quick captures from processed notes

**Acceptance Criteria**:
- [ ] Note model has a `Status` field (enum: `Fleeting`, `Permanent`)
- [ ] Default status is `Permanent` (no change to existing note creation)
- [ ] Existing notes are treated as `Permanent`
- [ ] API supports filtering by status: `GET /api/notes?status=fleeting`
- [ ] Status can be updated: `PATCH /api/notes/{id}` with `status` field
- [ ] Search returns both fleeting and permanent notes by default

### Epic: In-App Quick Capture

#### Story 2: Floating Capture Button

**As a** user browsing Zettel-Web
**I want** a persistent capture button visible on every page
**So that** I can capture a fleeting thought in under 10 seconds

**Acceptance Criteria**:
- [ ] Floating action button visible on all pages (bottom-right)
- [ ] Tap opens a minimal capture form (content field + optional tags)
- [ ] Content field supports plain text and auto-detects URLs
- [ ] Submit creates a note with `status=fleeting`
- [ ] Form clears after submission with brief success feedback
- [ ] Works on mobile viewport sizes
- [ ] Keyboard shortcut available (e.g., `Ctrl+Shift+N`)

#### Story 3: Fleeting Notes Inbox

**As a** user processing my captured thoughts
**I want** a dedicated inbox view showing all fleeting notes
**So that** I can review, process, or discard them in a focused session

**Acceptance Criteria**:
- [ ] New route `/inbox` showing all notes with `status=fleeting`
- [ ] Notes sorted by creation date (newest first)
- [ ] Each note shows: content preview, tags, age indicator, source
- [ ] Age indicators with colour coding (green <1d, amber 1-7d, red >7d)
- [ ] "Process" action opens note in full editor
- [ ] "Discard" action deletes the note (with confirmation)
- [ ] "Promote" quick action sets status to `permanent` without editing
- [ ] Count badge on navigation showing unprocessed fleeting notes

### Epic: Email Capture

#### Story 4: Inbound Email Processing

**As a** user away from Zettel-Web
**I want** to email a thought to a dedicated address
**So that** it appears as a fleeting note in my inbox

**Acceptance Criteria**:
- [ ] Dedicated inbound email address (e.g., `capture@<domain>`)
- [ ] Email subject becomes note title (or auto-generated if blank)
- [ ] Email body becomes note content
- [ ] Only accepts emails from configured sender addresses
- [ ] Rejects/ignores emails from unknown senders (no error response)
- [ ] Handles plain text and HTML emails (strips HTML to markdown)
- [ ] Creates note with `status=fleeting` and tag `source:email`

### Epic: Telegram Capture

#### Story 5: Telegram Bot Integration

**As a** user on my phone
**I want** to message a Telegram bot with a thought or link
**So that** it appears as a fleeting note in my inbox

**Acceptance Criteria**:
- [ ] Telegram bot created and configured with webhook
- [ ] Only responds to messages from configured chat ID(s)
- [ ] Text messages become fleeting notes (message text = content)
- [ ] Forwarded messages capture the forwarded content
- [ ] Creates note with `status=fleeting` and tag `source:telegram`
- [ ] Bot sends confirmation reply after successful capture
- [ ] Bot rejects unauthorized users silently

### Epic: Link Enrichment

#### Story 6: Automatic URL Enrichment

**As a** user capturing a link
**I want** the system to automatically fetch the page title and summary
**So that** I have enough context to process the note later

**Acceptance Criteria**:
- [ ] URLs in fleeting note content are detected automatically
- [ ] For each URL: fetch page title, meta description, and key content
- [ ] Enrichment data stored as structured metadata on the note
- [ ] Enrichment happens asynchronously (don't block capture)
- [ ] Graceful degradation if URL is unreachable (keep raw content)
- [ ] Enrichment visible in inbox view (title, summary preview)
- [ ] Enrichment is NOT re-run if content is edited manually

### Epic: Stale Note Reminders

#### Story 7: Age Indicators and Visual Nudges

**As a** user with unprocessed fleeting notes
**I want** visual indicators showing how long notes have been sitting
**So that** I'm nudged to process them before they become stale

**Acceptance Criteria**:
- [ ] Age indicator on each fleeting note (relative time: "2h ago", "3d ago")
- [ ] Colour coding: green (<1 day), amber (1-7 days), red (>7 days)
- [ ] Inbox header shows summary: "5 notes, 2 older than a week"
- [ ] Optional: fleeting note count in main navigation badge

---

## Scope

### In Scope (v1)

- Note `status` field (fleeting/permanent) on existing model
- Floating quick-capture button in web UI
- Fleeting notes inbox page with processing workflow
- Email inbound capture with sender verification
- Telegram bot capture with chat ID verification
- Automatic URL enrichment (title + summary + content extraction)
- Visual age indicators and stale note nudging
- Source tracking (web/email/telegram) via tags

### Out of Scope (v1)

- WhatsApp integration (defer to v2 if email + Telegram proves insufficient)
- Push notifications for stale notes
- Automatic processing suggestions (e.g., "this relates to note X")
- Bulk processing actions
- Mobile native app
- Voice capture / speech-to-text
- Scheduled digest emails of unprocessed notes

### Future Considerations

- AI-assisted processing: suggest which permanent notes a fleeting note
  relates to, auto-generate title, suggest tags
- WhatsApp via Twilio if Telegram isn't used enough
- Apple Shortcuts integration (POST to API endpoint directly)
- RSS/read-later feed → fleeting notes pipeline
- Spaced repetition reminders for processing

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Email parsing is fragile (HTML, attachments, signatures) | Medium | Medium | Start with plain text only, iterate on HTML handling |
| Telegram bot webhook requires public URL | Medium | Low | Use ngrok for dev, Traefik handles prod routing |
| Link enrichment is slow or unreliable | Medium | Low | Async processing, graceful degradation, timeout limits |
| Inbox becomes a graveyard despite reminders | High | High | Strong visual nudging, keep processing friction very low |
| Enrichment fetches malicious content | Low | Medium | Sanitize all fetched content, don't execute scripts, timeout |
| Email inbound service adds cost/complexity | Low | Medium | Use provider free tier (SendGrid 100/day, Mailgun 5k/month) |

---

## Dependencies

| Dependency | Owner | Status | Blocker? |
|------------|-------|--------|----------|
| Inbound email service (SendGrid/Mailgun) | James | Not started | Yes - for email capture |
| Telegram Bot API token | James | Not started | Yes - for Telegram capture |
| Public webhook URL for Telegram | James | Not started | Yes - for Telegram capture |
| URL fetching/parsing library | James | Not started | No - can use built-in HttpClient |

---

## Open Questions

- [ ] Which inbound email provider? (SendGrid, Mailgun, AWS SES)
- [ ] Should enrichment use an LLM to summarize page content, or just
      extract meta tags + first paragraphs?
- [ ] Should fleeting notes get embedded (vector) immediately, or only
      after processing into permanent notes?
- [ ] What email address format? (capture@domain, zettel@domain, etc.)
- [ ] Should the Telegram bot support commands beyond capture?
      (e.g., `/search`, `/recent`, `/inbox count`)

---

## Technical Notes

### Data Model Change

```csharp
public enum NoteStatus
{
    Permanent = 0,  // default - existing behaviour
    Fleeting = 1
}

// Add to Note model:
public NoteStatus Status { get; set; } = NoteStatus.Permanent;
```

### API Changes

```
GET    /api/notes?status=fleeting     # filter by status
POST   /api/notes                     # existing - add optional status field
PATCH  /api/notes/{id}                # existing - add status to updatable fields
POST   /api/notes/fleeting            # dedicated quick-capture endpoint (minimal fields)
POST   /api/webhooks/email            # inbound email webhook
POST   /api/webhooks/telegram         # Telegram bot webhook
```

### Enrichment Architecture

```
Note Created (with URL) ──▶ Background Job ──▶ Fetch URL
                                               Extract title + meta + content
                                               Store as note metadata
                                               Update note (async)
```

### Suggested Implementation Order

1. Data model: Add `Status` field + API filtering
2. In-app: Floating capture button + inbox page
3. Enrichment: URL detection + async fetch + metadata storage
4. Email: Inbound webhook + sender verification
5. Telegram: Bot setup + webhook + chat ID verification
6. Polish: Age indicators, badges, stale note nudging
