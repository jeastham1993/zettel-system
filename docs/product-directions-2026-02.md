# Product Directions: Zettel-System

Last Updated: 2026-02-24

---

## What This Product Actually Is

Most people would call this a "Zettelkasten app." That's underselling it. What's been built is
a **personal publishing engine** built on top of a knowledge base. The Zettelkasten is the
input; the AI content generation is the output. The core promise isn't "organise your notes":

> *"Turn the knowledge you've already accumulated into consistently published content,
> without starting from a blank page."*

This framing shapes what "done" looks like and which gaps are most critical.

---

## The Product Flywheel

The system has a flywheel that only works when all three steps are tight:

```
  ┌──────────────────────────────────────────────────────────────┐
  │                                                              │
  │   Capture           Discover              Publish            │
  │   friction-   →    related         →    content             │
  │   lessly            clusters              reliably           │
  │                                                              │
  │   (more notes = better discovery)                            │
  │   (publishing = motivation to capture more)                  │
  └──────────────────────────────────────────────────────────────┘
```

Currently:

- **Step 1 (capture)** — covered: web UI, email, Telegram, keyboard shortcut
- **Step 2 (discover)** — fully implemented: semantic graph walk, topic clustering
- **Step 3 (publish)** — generation and review UI complete, but **stops just before the
  finish line**: approved content lives in the app and is manually copy-pasted out

**The flywheel has a gap at the last mile.**

---

## Gap Analysis

| Capability | Status | Gap |
|---|---|---|
| Quick capture (web) | ✅ Complete | — |
| Quick capture (mobile) | 📐 Designed, not built | Can't capture on the go |
| Semantic discovery | ✅ Complete | — |
| Content generation | ✅ Complete | — |
| Voice personalization | ✅ Backend + UI | Onboarding UX unclear for new users |
| Scheduling | 📐 Planned, not built | Manual trigger only — no habit formation |
| Review dashboard | ✅ Complete | — |
| **Publishing integrations** | ❌ Missing | Approved content goes nowhere automatically |
| Knowledge health insights | ❌ Missing | No visibility into KB richness or gaps |
| Analytics | ❌ Missing | Can't measure what's working |

---

## Strategic Directions

### Direction 1 — Complete the Publishing Loop

**Priority: Highest. Closest to done.**

**The problem**: A sophisticated content pipeline exists, but the last step — getting approved
content *out* — is manual copy-paste. That breaks the habit loop.

**What's needed**:

- **Ghost/Hugo/markdown file drop** for blog posts (most self-hosters run one of these)
- **Clipboard-first UX** — the export action should be the primary CTA on an approved piece,
  not buried
- **Social scheduling integration** — even a Buffer webhook would be a meaningful step
- **"Mark as Published" status** — closes the loop psychologically; gives the system memory
  of what left it

**Why first**: The content generation feature is the product's unique differentiator. It's
~80% complete. Finishing it makes the whole flywheel spin. Leaving the last 20% incomplete
means users generate content, approve it, and then motivation to keep adding notes stalls.

**Smallest testable version**: Add "Copy to Clipboard" as the primary action on an approved
blog piece (clean markdown). Add "Mark as Published" status. Measure: does marking things
published create a psychological reward that increases note capture?

---

### Direction 2 — Mobile App

**Priority: High. Already fully designed.**

**The problem**: The primary capture use case is mobile — shower thoughts, commute ideas,
things heard in meetings. The current web UI is excellent at the desk; it doesn't exist in
your pocket.

**Why second**: `docs/design-mobile-app.md` is already thorough. The API surface is complete
with zero backend changes needed for v1. This is mostly an execution task with low risk.

**Key PM note**: The mobile app must stay aggressively scoped. The only mobile-first jobs are
**capture** and **inbox triage**. Everything else (graph, content review, import/export)
stays on the web. Resist scope creep.

**Smallest testable version**: Android APK with phases 1-2 from the design plan only —
settings, server connect, quick capture with offline queue. Use it daily for 30 days before
building phases 3-6.

---

### Direction 3 — Scheduling and Habit Formation

**Priority: Medium. Low effort.**

**The problem**: Manual content generation requires the user to remember to trigger it. The
weekly cron is planned but not built. Without it, the product is a tool you use occasionally;
with it, it becomes a system that works for you.

**Design insight**: The most successful personal productivity tools show up for the user, not
the other way around. The scheduler turns this from "I use it when I remember" to "it
surfaces something for me every Monday."

**What's needed**:

- `ContentGenerationScheduler` background service (architecture already exists)
- Simple schedule config UI: a toggle and a day-of-week picker
- Notification when new content is ready for review (even a webhook to ntfy.sh is enough)

**Key PM note**: Don't make the scheduler too aggressive. Weekly is right. The risk is
flooding the review queue, causing the user to start skipping review — which breaks the
approval habit.

---

### Direction 4 — Knowledge Health Dashboard

**Priority: Medium. Novel differentiator.**

**The problem**: The knowledge base is currently a black box. There's no visibility into
which areas are "rich" (many interconnected notes, ripe for generation), which are "thin"
(isolated notes with no backlinks), or which notes have never been used as seeds.

**What this unlocks**: Intentional knowledge building. Instead of adding notes randomly,
you'd be able to see gaps and deliberately add connective tissue.

**What's needed**:

- Dashboard metrics: total notes, % embedded, % with backlinks, % used as seeds
- **Knowledge islands view** — notes with zero backlinks and no wikilinks (orphans)
- **Most connected view** — top 10 notes by backlink count (these are structure notes)
- Indicator on the graph showing cluster density

**Why this matters for generation quality**: The discovery algorithm uses random seeds and
graph walks. Dense areas produce richer content. Knowing where the graph is sparse helps
you decide where to invest writing effort.

---

### Direction 5 — Voice Personalization Onboarding

**Priority: Medium. High quality impact.**

**The problem**: The voice examples system exists, but there's no onboarding. A new user
doesn't know how many examples to add, what kind of writing works best, how to write
effective style notes, or how to tell if the voice calibration is working.

**Why quality is a retention lever**: If the first three generations sound nothing like the
user, they'll stop using the feature. If the first generation sounds surprisingly good,
they'll be hooked. Voice quality is the product's reputation.

**What's needed**:

- Guided onboarding when `VoiceExample` count = 0: "Add 3 examples of your writing to
  get started"
- Guidance copy: "Paste 200-500 word excerpts from your best existing writing"
- **Voice preview feature**: generate a short sample paragraph from a given topic before
  doing a full generation — lets the user calibrate before committing
- Explicit per-medium framing: blog voice and social voice are different; the UI should
  make this obvious

---

## Key Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Generated content sounds generic | Medium | High | Voice quality is the product's core reputation. Voice preview (Direction 5) is the primary mitigation. |
| Scheduling creates notification fatigue | Medium | Medium | Weekly cadence only. Notify on status change, not on a timer. |
| Mobile/web data model divergence | Low | Medium | Notes created on web use Tiptap HTML. Mobile needs `react-native-render-html` — validated early in Phase 3. |
| Content generation produces repetitive topics | Medium | Medium | `UsedSeedNote` tracks this. Add a "too similar to previous" warning in the review UI before approving. |
| Feature drift between mobile and web | Medium | Medium | Mobile scope is explicitly constrained. Enforce it. |

---

## Recommended 90-Day Focus

```
Month 1: Close the Publishing Loop
  Week 1-2  ContentGenerationScheduler + config UI
  Week 3-4  Publishing UX: Mark as Published, improved export flow

Month 2: Voice Quality
  Week 1-2  Voice onboarding flow (guided setup when 0 examples)
  Week 3-4  Voice preview feature (test voice before full generation)

Month 3: Mobile App MVP
  Week 1-2  Phase 1-2: scaffold + quick capture + offline queue
  Week 3-4  Phase 3-4: note list + search
  End       Ship Android APK, use it daily, capture feedback
```

---

## The One Thing

If only one thing comes from this analysis: **complete the publish step**.

The pipeline is impressive. Capture → Embed → Discover → Generate → Review is all there. The
approved content just needs somewhere to go. Without that final step, the system is a content
*draft* machine, not a content *publishing* machine.

The flywheel doesn't spin until approved pieces can leave the system with one action.
