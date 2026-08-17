# AGENTS.md

Guidance for AI agents working in this repository.

## Project

AnimaSeek — an iOS-only hard fork of Seeker (Android Soulseek client), written in C# / .NET 10 with a native UIKit interface. Targets iOS 26+, iPhone only. GPL-3.0, free, ad-free, English-only. The iOS head is the sole release target; the Android head remains temporarily as a validation harness.

## Layout

- `Common/` — portable core (session, search, transfers, sharing, persistence). Shared logic goes here, never in a platform head.
- `Seeker.iOS/` — the UIKit app: composition root, view controllers, background tasks, routing.
- `Seeker/` — legacy Android head. Validation only; do not extend.
- `UnitTestCommon/` — portable unit tests. `Seeker.iOS.FileSystemTests/` — on-simulator filesystem harness.
- Docs: `DEVELOPMENT.md` (toolchain, build, simulator, tests), `DESIGN.md` (design system, binding UI decisions, inclusive-design model, parity outcomes, and design-debt ledger — the single UI authority), `PRODUCT.md` (scope), `PORTING.md` (original migration plan), `CHANGELOG.md`.

## Build & test

Toolchain: macOS 26, Xcode 26.6, .NET SDK **10.0.302 exactly** (pinned in `global.json`), .NET iOS workload. Open `Seeker.iOS.slnf`, not the full solution, for iOS work.

- Build the `Debug Mock` configuration for the iOS 26 Apple-silicon simulator; exact `dotnet build` flags, AOT validation, and `xcrun simctl` install/launch commands are in `DEVELOPMENT.md`.
- Unit tests: `dotnet test UnitTestCommon/UnitTestCommon.csproj`.
- `Debug Mock` composes `MockSoulseekClient`; `ANIMASEEK_UI_ROUTE` deep-opens a screen in the simulator for repeatable inspection.

## Rules

- **Default to `Debug Mock`; connect to the live Soulseek network only for explicitly requested validation.** Day-to-day development and automated testing run against `Debug Mock`; live sign-ins use the owner's own account and happen only when the owner asks for a real-network check. Release builds always use the non-mock `Release` configuration.
- The app ships with full AOT and trimming. Avoid reflection- and trim-unsafe patterns in `Common/` and `Seeker.iOS/`; CI enforces the strict flag set.
- UI must keep the established iOS conventions: semantic system colors, Dynamic Type, self-sizing content, stable accessibility identifiers, native light/dark.
- Update `DESIGN.md` (including its “Known deviations and design debt” ledger) and `CHANGELOG.md` when shipping user-visible changes.
- Conventional commit messages (`feat:`, `fix:`, …), as in the existing history.

## Agent configuration — single source of truth

All agent, skill, and hook configuration lives under `.agents/`. The per-harness paths are git-tracked relative symlinks into it — **edit only `.agents/`, never through a symlinked path**:

| Symlink | Target |
| --- | --- |
| `.claude/skills`, `.github/skills` | `.agents/skills/` (skills for every harness) |
| `.claude/settings.json` | `.agents/claude/settings.json` (Claude Code hooks) |
| `.codex/hooks.json` | `.agents/codex/hooks.json` |
| `.github/agents`, `.github/hooks` | `.agents/copilot/agents`, `.agents/copilot/hooks` |

Notes:

- `.agents/skills/impeccable/scripts/lib/provider.mjs` detects the harness at runtime (invocation path, then environment). If the skill is ever re-vendored or upgraded, keep that file dynamic instead of accepting a per-provider hardcoded copy, and upgrade only the `.agents/` copy.
- `.claude/settings.local.json` and `.impeccable/config.local.json` are machine-local and untracked; don't commit or symlink them.
- `.github/workflows/` is CI, not agent config; it stays a real directory.
