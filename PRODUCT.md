# Product

<!-- impeccable:product-schema 1 -->
<!-- Inference note: this record is derived from the user's explicit build request and the repository's DESIGN.md, UI.md, PORTING.md, and existing source. No additional product interview decisions are assumed. -->

## Platform

ios

## Users

People who already use or want access to the Soulseek network from an iPhone. They may be searching for hard-to-find files, managing long-running transfers, browsing another user's shares, or participating in Soulseek's messaging and chatroom community. The product must remain usable while they are mobile, interrupted, offline, using one hand, or relying on iOS assistive technologies.

## Product Purpose

AnimaSeek is a native iPhone Soulseek client. It lets people sign in, search and browse shared files, start and manage downloads and uploads, maintain wishlists, message people, join chatrooms, manage friends and ignored users, edit profiles, and configure the portable and iOS-specific behavior already exposed by the fork's service layer. Success means the Android client's useful behavior is available through trustworthy native iPhone interaction patterns without misrepresenting iOS background, storage, or network constraints.

## Positioning

AnimaSeek combines the established Seeker/Soulseek functionality with a native iOS 26 UIKit presentation and Files-visible sandbox storage, rather than wrapping or visually translating the Android application.

## Operating Context

The primary path is Home/Login → Search → queue a download → monitor it in Transfers → browse a user's hierarchy. People also move between messages, chatrooms, user profiles, wishlists, settings, imports, and completed-file Open/Share actions. Network state changes, app suspension, keyboard presentation, tab switching, rotation, large collections, and late protocol responses are ordinary operating conditions rather than edge cases.

## Capabilities and Constraints

- The runtime interface is programmatic UIKit in C# on iOS 26+, iPhone-only, with four stable tabs and an independent navigation stack per tab.
- The existing portable core, iOS service graph, persistence, notifications, background coordination, localization, mocks, and AOT/full-link build path are the implementation foundation.
- Controllers consume presentation state and typed commands; protocol clients, transfer managers, global state, raw preferences, and cross-feature routing do not belong in view controllers.
- English is the baseline language, sourced from `Common/Localization/StringResources.resx`; user-facing controller copy is catalog-backed except for the mandated verbatim library notice.
- Downloads and shared files live in the Files-visible app Documents directory. Completed files use system Open/Share flows; Android SAF, MediaStore, playback, service controls, theme variants, and share-intent entry points are out of scope.
- Background state must be truthful: active finite transfers may continue, queued or stalled work can pause, downloads may resume on foregrounding, and uploads may wait for a peer re-request.
- Deterministic Mock-mode builds are the day-to-day development surface; release builds always use the non-mock `Release` configuration, and live-network validation happens only when the owner requests it.

## Brand Commitments

The product name is **AnimaSeek**. **Phoenix** is the approved identity: one simple upright broad-V bird, with a cyan left wing and coral right wing representing the two sides of human connection, a navy central body representing one connected self, and a yellow flame-tail representing renewal, discovery, and new experiences. The complete identity set is staged under `Seeker/Assets/new`, including color, reversed, and monochrome symbols; wordmarks and lockups; and iOS Any, Dark, Tinted, launch, and in-app assets. Production integration is still pending, so `Seeker.iOS/Assets.xcassets` remains unchanged and the app continues to ship its legacy identity. The interface voice is direct, factual, calm, and respectful. The exact `SoulseekClientIdentity.ModifiedLibraryNotice` is required content: it remains verbatim and permanently available under Legal Notices, which is reachable without signing in from Home, Settings, and About.

## Evidence on Hand

- Visual and interaction system, binding UI decisions, parity outcomes, and acceptance criteria: `DESIGN.md`.
- Port status and platform decisions: `PORTING.md`.
- Existing iOS composition and services: `Seeker.iOS/`.
- Portable behavior, models, resources, and deterministic mock client: `Common/`.
- Android behavioral reference and flows: `Seeker/`, `MaestroTests/`, and Fastlane screenshots.
- Existing automated coverage: `UnitTestCommon/` and the simulator filesystem harness described in `DEVELOPMENT.md`.
- No testimonials, commercial claims, usage metrics, or release-performance evidence may be fabricated.

## Product Principles

1. Content and current state come before chrome or decoration.
2. Native iPhone familiarity is a functional requirement, while Android remains the behavioral—not visual—reference.
3. Preserve context through interruption and make offline, queued, paused, failed, and resumable states honest and recoverable.
4. Make high-volume information scannable without shrinking type or hiding essential actions.
5. Close every parity decision explicitly; silent omissions are defects.

## Accessibility & Inclusion

Dynamic Type through accessibility sizes, VoiceOver, Switch Control, Full Keyboard Access, Voice Control, 44 × 44 pt targets, semantic colors, non-color status cues, Light/Dark, Increased Contrast, Reduce Transparency, Reduce Motion, Bold Text, Display Zoom, rotation, and safe-area behavior are release requirements. The UI should minimize recall, explain consequences before destructive or network-affecting actions, retain useful state during refresh or interruption, and provide explicit retry, cancel, undo, or resume paths wherever the underlying operation supports them.
