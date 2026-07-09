# Version Log

Last Updated: 2026-07-09

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