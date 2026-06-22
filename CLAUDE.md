# DScript — Claude Code rules

## Testing

### Before every commit
Run the full test suite and confirm it is green before staging any commit:

```
dotnet test DScript.Test --configuration Release
```

Do not commit if any test fails. Fix the failure first.

### Bug fixes
Every bug fix must be accompanied by a new unit test that:
- reproduces the incorrect behaviour before the fix (i.e. it would have failed on the unfixed code)
- passes after the fix

Place the test in the most relevant existing test file in `DScript.Test/`, or create a new `*Tests.cs` file if no suitable file exists. Name the test to describe the observed misbehaviour, e.g. `AddNative_DoesNotRejectContextualKeywordInDottedName`.

### New functionality
Every new public API, language feature, or native library method added to `DScript` or `DScript.Extras` must have accompanying unit tests covering:
- the happy path
- at least one edge case (empty input, boundary value, optional parameter omitted, etc.)
- error / exception cases where applicable

### Test project
- Framework: **NUnit 4** (`DScript.Test/`)
- Tests live in `DScript.Test/` — add to an existing `*Tests.cs` file when the feature fits, create a new file when it does not
- Follow the existing `[TestFixture]` / `[Test]` / `[TestCase]` patterns already in the project
- Helper method for running script: `RunScript(string code)` pattern used throughout existing tests

## Coverage

The CI enforces a **90 % line-coverage gate** on `DScript` and `DScript.Extras`. The gate runs via Coverlet (msbuild integration) as part of `dotnet test`. If your changes cause coverage to drop below 90 %, the build fails.

Run coverage locally before committing:

```
dotnet test DScript.Test --configuration Release \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=opencover \
  /p:Threshold=90 \
  /p:ThresholdType=line \
  /p:ThresholdStat=Total \
  /p:Exclude="[DScript.Benchmark]*,[DScript.Demo]*,[DScript.Repl]*,[DScript.LanguageServer]*"
```

If new code is inherently difficult to cover (e.g. platform-specific branches, internal infrastructure), add `[ExcludeFromCodeCoverage]` with a comment explaining why rather than lowering the global threshold.

## Code style

- Match the style of the surrounding file (existing indentation, brace placement, `var` usage)
- No new warnings. The CI treats warnings as informational but all `CA*` and `CS*` warnings shown in CI output must be resolved before merging
- Do not suppress warnings with `#pragma warning disable` without a comment explaining why

## Commit messages

- Name commits after the thing being changed, not the action (`String gaps` not `Add string methods`)
- One logical change per commit
- Do not bundle unrelated fixes

## Build command

```
dotnet build --configuration Release
```

Both `net8.0` and `net10.0` targets must build cleanly with zero errors.
