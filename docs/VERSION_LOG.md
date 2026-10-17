# Version Log

Last Updated: 2026-10-17

## 1.1.0 - 2026-10-17

### Removed
- Removed the automated content generation feature in its entirety:
  - Blog/social content schedulers (`BlogContentScheduler`, `SocialContentScheduler`, `ContentSchedulerBase`)
  - `TopicDiscoveryService`, `ContentGenerationService`, and publishing integrations (GitHub, Publer)
  - `ContentController`, `ContentScheduleController`, `VoiceController` API endpoints (`/api/content/*`, `/api/voice/*`)
  - Content review and voice config pages in the web UI
  - Lambda `ContentScheduleHandler` and its EventBridge schedule (Terraform)
  - Database tables: `ContentGenerations`, `ContentPieces`, `VoiceExamples`, `VoiceConfigs`, `UsedSeedNotes`
    (dropped via the `20261017143749_RemoveContentGenerator` migration)
  - KB Health "Never Used as Seeds" section and related telemetry counters
- The `ContentGeneration:Provider/Model/Region/ApiKey` config keys remain —
  they now configure the shared chat LLM (`IChatClient`) used by Chat and KB Health

### Changes
- Database size reduction: content generation tables dropped and no longer written to

## 1.0.0 - 2026-07-09

### New Features
- Added source references feature: Users can now view all notes that reference a specific source
- New API endpoint: `GET /api/notes/sources/{sourceId}/references`
- New UI component: SourceReferences.tsx for displaying notes that reference a source
- Updated NoteView.tsx to show source references for source-type notes
- Updated note page to include source references in both desktop and mobile views

### Changes
- Enhanced INoteService interface with GetNotesBySourceAsync method
- Added getSourceReferences function to the API client
- Updated API reference documentation

### Bug Fixes
- None in this release

## Previous Versions

No previous versions documented.