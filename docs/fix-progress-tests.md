# Test Suite Fixes Progress

## Items

- [x] 1. Extract duplicated test fakes into shared `Fakes/` directory
  - Created `Fakes/FakeSearchService.cs` (merged StubSearchService + FakeSearchService)
  - Created `Fakes/FakeEmbeddingGenerator.cs` (merged 3 versions + ThrowingEmbeddingGenerator)
  - Created `Fakes/FakeEmbeddingQueue.cs` (extracted from ImportServiceTests)
  - Updated 6 test files to use shared fakes, removed private duplicates
- [x] 2. Add WebApplicationFactory-based HTTP integration tests for Notes CRUD
  - Created `Controllers/NotesHttpIntegrationTests.cs`
  - Tests POST/GET/PUT/DELETE /api/notes and /health endpoint
  - Uses Testcontainers PostgreSQL + fake embedding generator
  - 9 test methods covering CRUD operations and error cases
- [x] 3. Add integration tests for DiscoverAsync using Testcontainers
  - Added 5 tests to existing `SearchServiceIntegrationTests.cs`
  - Tests: returns similar notes, excludes recent, handles no embeddings,
    respects limit, averages multiple recent note embeddings
- [x] 4. Add adversarial input tests
  - Created `Services/AdversarialInputTests.cs`
  - Export filename sanitization: path traversal titles, empty/whitespace titles
  - Very long inputs: 10K char titles, 100K char content, long search prefixes
  - Special characters: quotes, HTML tags, emoji, null chars, newlines
  - Unicode filenames in import
  - 16 test methods total
- [x] 5. Add import/export round-trip test
  - Added to `Services/ExportServiceTests.cs`
  - Exports notes to zip, extracts files, imports into fresh DB
  - Verifies content is preserved through the round-trip

## Summary
- All 5 items completed
- Test count: 124 (before) -> 163 (after) = 39 new tests
- All 163 tests pass consistently

## Files Created
- `src/ZettelWeb.Tests/Fakes/FakeSearchService.cs`
- `src/ZettelWeb.Tests/Fakes/FakeEmbeddingGenerator.cs`
- `src/ZettelWeb.Tests/Fakes/FakeEmbeddingQueue.cs`
- `src/ZettelWeb.Tests/Controllers/NotesHttpIntegrationTests.cs`
- `src/ZettelWeb.Tests/Services/AdversarialInputTests.cs`

## Files Modified
- `src/ZettelWeb.Tests/Controllers/SearchControllerTests.cs` - use shared FakeSearchService
- `src/ZettelWeb.Tests/Controllers/NotesControllerTests.cs` - use shared FakeSearchService
- `src/ZettelWeb.Tests/Background/EmbeddingBackgroundServiceTests.cs` - use shared FakeEmbeddingGenerator
- `src/ZettelWeb.Tests/Health/EmbeddingHealthCheckTests.cs` - use shared FakeEmbeddingGenerator
- `src/ZettelWeb.Tests/Services/SearchServiceIntegrationTests.cs` - use shared fakes + DiscoverAsync tests
- `src/ZettelWeb.Tests/Services/ImportServiceTests.cs` - use shared FakeEmbeddingQueue
- `src/ZettelWeb.Tests/Services/ExportServiceTests.cs` - round-trip test
- `src/ZettelWeb.Tests/ZettelWeb.Tests.csproj` - added Microsoft.AspNetCore.Mvc.Testing package
