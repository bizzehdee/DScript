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

## Documentation

Update **both** `README.md` and the wiki submodule (`wiki/`) whenever public APIs, language features, or user-visible behaviour change. Neither should lag behind the code.

### README.md

`README.md` must stay **minimal**. It contains: a one-paragraph description, a quick example, installation instructions, a short "getting started" C# snippet, and links to the wiki for everything else. It must **not** enumerate every API, method, or language feature — those live in the wiki.

When a change adds or removes a user-visible feature, update `README.md` only if the change affects the getting-started flow. Otherwise update the wiki only.

Wiki base URL: `https://github.com/bizzehdee/DScript/wiki`  
Example page: `https://github.com/bizzehdee/DScript/wiki/Engine`

### Wiki submodule (`wiki/`)

The wiki lives at `C:\code\DScript\wiki\` (git submodule pointing at the GitHub wiki repo).

Update the relevant wiki page(s) alongside any code change:

| Change type | Pages to update |
|---|---|
| New/changed language syntax | `Language.md` |
| New/changed `DScript.Extras` module or method | `Standard-Library.md` |
| New/changed `ScriptEngine` public API | `Engine.md` |
| New/changed module system behaviour | `Modules.md` |
| New/changed resource limits or permissions | `Resource-Limits.md`, `Permissions.md` |
| New/changed host-object injection | `Host-Objects.md` |
| New/changed bytecode / serialisation API | `Bytecode.md` |
| New/changed debugger interface | `Debugger.md` |
| REPL changes | `REPL.md` |

After editing wiki pages, commit them inside the submodule and then record the updated submodule pointer in the main repo:

```
# inside wiki/
git add <changed pages>
git commit -m "<description of what changed>"
git push

# back in the main repo
git add wiki
git commit -m "Update wiki — <description>"
```

### Language server

Keep `DScript.LanguageServer` up to date whenever the lexer, parser, or public API surface changes — hover, completion, go-to-definition, and signature help should reflect the current language.

## Benchmarking

Any change to `DScript/ScriptLex.cs` or anything under `DScript/Vm/` must be benchmarked before and after:

```
dotnet run --project DScript.Benchmark --configuration Release
```

Record the `best ms` column for each workload. Variance between runs is normal; a consistent regression of more than ~5 % on any workload is not acceptable and must be investigated before committing. Note the before/after numbers in the commit message.

## Code style

- Match the style of the surrounding file (existing indentation, brace placement, `var` usage)
- No new warnings. The CI treats warnings as informational but all `CA*` and `CS*` warnings shown in CI output must be resolved before merging
- Do not suppress warnings with `#pragma warning disable` without a comment explaining why

## Commit messages

- Name commits after the thing being changed, not the action (`String gaps` not `Add string methods`)
- One logical change per commit
- Do not bundle unrelated fixes

## Opcodes

New opcodes **must be appended to the end** of the opcode enum. Never insert them in the middle.

Inserting an opcode anywhere other than the end changes the integer values of all subsequent opcodes, silently breaking any saved bytecode files and any external tooling that encodes opcodes as integers.

## Build command

```
dotnet build --configuration Release
```

Both `net8.0` and `net10.0` targets must build cleanly with zero errors.
