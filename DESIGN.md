---
name: AnimaSeek
description: Native iPhone Soulseek client built with UIKit and Apple’s Liquid Glass design language.
---

# DESIGN.md — AnimaSeek design system

*Updated 2026-08-17. Recorded from the implemented iOS 26+ UIKit application and reconciled against the shipped source under `Seeker.iOS/`. This document is self-contained: it holds the visual and interaction system, the binding product decisions, the inclusive-design model, the behavioral-parity outcomes, and the verification evidence for the port. Where the pre-implementation contract and the build diverged, the build was inspected and either recorded as the system or listed under “Known deviations and design debt” — nothing was silently canonized.*

## Overview

### Creative north star: content first, controls above

AnimaSeek looks like it belongs on the current iPhone: quiet, direct, information-dense when necessary, and unmistakably native. Search results, folders, conversations, and transfers form a stable **content plane**. Navigation and the few controls that act on that content occupy Apple’s adaptive **Liquid Glass control plane** above it.

This is not a reskin of Android Seeker. The Android application was the behavioral reference; its purple Material palette, app bars, card stacks, elevation values, floating action buttons, chip clouds, ripple effects, and XML dimensions were never design inputs. AnimaSeek’s identity comes from its name, icon, launch mark, language, and reliable utility — not from repainting system controls.

The visual hierarchy, as shipped:

1. **Content** — filenames, usernames, messages, settings, and progress are most prominent.
2. **Current state** — connection, queue, transfer, unread, filter, and error states are visible but not louder than the content unless action is required.
3. **Actions** — one primary action may be prominent; secondary actions remain native and restrained.
4. **Chrome** — tab, navigation, toolbar, search, sheet, and menu surfaces use UIKit’s system-provided appearance. No `UINavigationBarAppearance` or `UITabBarAppearance` customization exists anywhere in the app.

Apple’s [Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/) and [Adopting Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass) remain the upstream authority when this document does not specify a case.

### Binding product decisions

These decisions are fixed. Change them here deliberately rather than letting individual screens drift.

| # | Decision | Resolution (as shipped) |
|---|---|---|
| 1 | UI framework | UIKit in C#, programmatic runtime layout. `LaunchScreen.storyboard` is launch-only. |
| 2 | Platform | iOS 26.0+, iPhone-only, portrait and both landscapes (declared in `Seeker.iOS/Info.plist`, no per-controller restrictions). No pre-iOS-26 fallback branch, no `UIDesignRequiresCompatibility`. |
| 3 | Primary navigation | Four stable tabs — Home, Search, Transfers, Browse — each with an independent `UINavigationController` stack (`UI/App/RootTabBarController.cs`). Tabs never disappear or disable; unavailable content explains itself in place. |
| 4 | Secondary navigation | Messages, Chatrooms, and Users are reached from Home’s community rows; Account, About, and Legal Notices through Settings, exposed as a Home navigation-bar action in **every** account state; Privileges through Account’s privilege summary row. Settings, About, and Legal Notices stay reachable while signed out. Detail screens push; bounded choices use anchored sheets or menus. |
| 5 | Home semantics | Home is the combined signed-out/login, connecting/reconnecting, disconnected, and signed-in dashboard, driven by one enum state renderer. |
| 6 | Language | English-only, through `Common/Localization/StringResources.resx` (`AppStrings` pass-through). Visible production text and accessibility strings are catalog-backed; the single sanctioned literal is the verbatim legal notice. |
| 7 | Development mode | `Debug Mock` composes `MockSoulseekClient` and is the default UI-development configuration. Mock scenarios cover content, empty, loading, error, offline, and high-volume states. |
| 8 | Storage and playback | Downloads and shares live in the Files-visible app Documents directory. Completed files offer Open/Share through iOS; no SAF hierarchy, no in-app playback. |
| 9 | Background truthfulness | The UI describes queued, stalled, active, and resumable work honestly and never implies indefinite background connectivity iOS cannot provide. Active finite transfers continue via continued-processing tasks; queued/stalled work pauses and recovers in the foreground; uploads may wait for a peer re-request. |
| 10 | Presentation boundary | Controllers consume immutable screen snapshots and typed commands. Protocol clients, transfer managers, global state, raw preferences, and cross-feature routing never enter a view controller. |
| 11 | Accessibility | Dynamic Type, VoiceOver semantics and focus, non-color status cues, Reduce Motion/Transparency, and 44 × 44 pt targets are acceptance criteria, not polish. Stable accessibility identifiers are the UI’s test API. |
| 12 | Legal notice | `SoulseekClientIdentity.ModifiedLibraryNotice` renders verbatim as the first entry of the permanent Legal Notices destination, reachable without signing in from Home (via Settings), Settings, and About, with no acknowledgement gate. See “Legal identity notice.” |
| 13 | Parity policy | Every reachable Android behavior was explicitly retained, adapted, replaced, or dropped. Silent omissions are defects. The outcomes and the open remainder are recorded in this document. |
| 14 | Share extension | None ships in the baseline. Android’s exported `ACTION_SEND` “Search Here” entry was dropped; users paste a query or open a supported `slsk://` route. A future extension needs its own target, privacy review, and handoff design. |

### Verification evidence

Recorded at parity closure on the frozen `ios` branch source:

- **231 parity rows**: 195 Implemented, 5 Verified, 9 Pending, 8 Deferred, 14 Dropped. Pending and Deferred rows are carried forward under “Known deviations and design debt”; Dropped rows under “Intentional omissions.”
- Portable tests **627/627**, AOT protocol tests **21/21**, on-simulator filesystem harness **7/7** (`IOS_FS_SEMANTICS_PASS`) on a fresh iPhone 17 Pro simulator.
- Strict no-incremental `Debug Mock` AOT/full-link build: **0 errors**, no linker warning under `ILLinkTreatWarningsAsErrors` (325 compiler/analyzer warnings outstanding). Non-Mock Release simulator gate: 0 errors, 6 aggregate warnings. The linked artifact installed and launched on a fresh simulator with no app error or fault logs.
- Simulator appearance inspection: Home and Settings in Light; Account in Light and Dark; Search, Settings, Transfers, Browse, Messages, Rooms, Users, Profile, About, and Diagnostics in Dark; Settings at an accessibility content size; Search in Dark with Increased Contrast at AX-extra-large text; Messages, Chatrooms, and Search route smokes across Light, Dark, and accessibility text sizes.
- 2026-08-17 debt-closure sweep (iPhone 17 simulator, iOS 26): all thirteen launch routes in Light and Dark at the default type size; Home, Search, Transfers, Settings, Account, Messages, Rooms, and Legal at accessibility-extra-extra-extra-large (Account, Legal, and Rooms also in Dark at that size); Home, Account, Transfers, and Legal with Increased Contrast; a live in-place content-size change on Legal Notices (rows re-measured without reload) and a live Light→Dark flip on Account. Accompanying gate: strict `Debug Mock` AOT/full-link build 0 errors with no linker warning, portable tests 628/628. Conversation-detail screens (message/room bubbles and the room composer) are not reachable through launch routes, so their visuals were not part of this sweep.
- A literal-key scan found no missing `AppStrings`/`StringResources` keys; the resource file passed XML validation with no duplicate names.
- The full named assistive-setting and device matrix (Switch Control, Full Keyboard Access, Voice Control, Bold Text, Button Shapes, Display Zoom, Reduce Transparency, both landscapes on device) was **not** exercised — that is open debt, not covered ground.

The evidence in this document was gathered on `Debug Mock`; nothing in it depends on live-network behavior.

## Inclusive design

AnimaSeek treats disability as a mismatch between a person and the interface, not a property of the person. The app is used mobile, one-handed, interrupted, offline, in sunlight, over slow networks, with assistive technologies, and under time or emotional pressure — those are ordinary operating conditions, not edge cases. Design decisions serve the whole persona spectrum: permanent (a VoiceOver or Switch Control user), temporary (a broken wrist, a migraine), and situational (transit, glare, a download racing a train tunnel).

### Preserve, Direct, Customize

**Preserve** the capacity people already have:

- Focus is protected. There is no decorative or looping motion anywhere in the app; the complete animation inventory is five short fades and scroll assists (see “Icons, imagery, and motion”). Notification *updates* are silent — only genuinely new events ring (see “Notifications and interruptions”). Home state changes de-duplicate their VoiceOver announcements; progress ticks are never announced.
- Control is protected. Destructive actions are explicit, separated, and confirmed in proportion to consequence. Conversation and thread deletion offer an accessible Undo banner. Every long operation exposes cancel; cancellation caused by navigation is never shown as an error. Dismissing a download sheet cannot cancel accepted work — accepted transfers are session-owned and stay observable in Transfers.
- Trust is protected. Background limits are stated honestly (finite continuation, foreground recovery, uploads waiting for a peer). Logs record only timestamps, severities, one-way signatures, and exception type names; accessibility identifiers hash user data (16-char SHA-256 prefixes) so credentials, usernames, messages, and paths never leak into automation artifacts. Credential fields carry no value-derived identifiers or labels.
- Motivation is protected. Empty and error states are calm, explain what happened, and lead with the next step; error copy never blames the person. The interface voice is direct, factual, calm, and respectful.

**Direct** people through the task:

- One primary action per moment: at most one `Filled` button per screen (Sign in, Reconnect, Browse, Send) and at most one prominent glass action on the selection bar. Everything else is tinted, gray, or plain.
- Wayfinding is stable: four fixed tabs, native Back, push-based drill-in with the current path in the title, and a single aggregate badge. Routed destinations receive exactly one VoiceOver screen-changed focus event.
- Validation is inline, next to the field, announced, and mirrored into the field’s accessibility hint; focus moves to the offending field. Submit stays disabled until input can succeed, and in-flight work prevents duplicate submission.
- Choices state their consequences in their wording (“Restore…”, explicit scope counts on batch actions, filtered-versus-all and exact-versus-recursive download choices named in full).

**Customize** through the system and the app:

- System preferences are the primary customization surface and are honored by construction: Dynamic Type (every text style is a preferred style), Light/Dark and Increased Contrast (semantic colors only — the app contains **zero** hex/RGB colors), Reduce Motion (fades skipped or zeroed), Bold Text and Display Zoom (nothing frozen to fixed metrics).
- App settings are a searchable, plain-language, inset-grouped catalog. Every row declares its applicability and applies its live side effect transactionally, rolling persisted values back on failure. Notification permission is requested in context, never at launch, with all authorization states explained.
- Preferences persist: search history, filters, sort orders, grouping modes, recent users, tab selection, and navigation state survive tab switches, relaunch, and restoration.

### Cognitive demands

- **Recall** is minimized: the app remembers rather than the person — persisted wishlists and their durable results, per-location browse filters, retained selection and scroll anchors through live updates, drafts kept on send failure, and “reading position preserved unless already near latest” in message threads with an explicit Jump to Latest control.
- **Decision-making** is supported: capability-driven commands hide or explain unavailable actions instead of failing; batch actions show eligible/selected counts; destructive scope is always named.
- **Learning** is supported by first-use empty states that orient (“what this is, why it’s empty, what to do next”) rather than onboarding gates; nothing is a one-time-only explanation.
- **Attention** is budgeted by the interruption model below.
- **Communication** stays concise and factual; status vocabulary is consistent across screens (the same localized state words in rows, values, and announcements).

### Notifications and interruptions

Every interruption is matched to urgency (`Services/IosNotifier.cs`):

- **Full attention** — `UIAlertController` alerts are reserved for confirmations and critical acknowledgement. New notifications (messages, wishlist hits, completed folders, user-online alerts) post with sound once.
- **Partial attention** — notification *updates* to an existing item re-post silently; foreground notifications show as banners without accumulating in Notification Center. The transient in-app banner (see “Screen states and feedback”) acknowledges results that have no owning surface.
- **Peripheral awareness** — one aggregate tab badge (capped “99+”, with a spoken accessibility value) combines unread messages, unread rooms, unseen wishlist results, and actionable transfers; in-list unread state uses font weight plus an explicit word. VoiceOver announcements are posted only for significant transitions (login/connection change, search completion, join failure, import result, destructive Undo, meaningful transfer completion) — never per progress tick.

Only private-message notifications carry actions (direct reply and Mark as Read); both execute in place without navigating or moving focus, and duplicate callbacks are suppressed by a bounded expiring identity guard.

One caveat is recorded in the debt ledger: the aggregate badge lives on the **Home** tab even though some of its count belongs to Search and Transfers.

### What inclusive evaluation still owes

No co-design or testing with people with lived experience of disability has informed the port; the accessibility work is expert-rule-driven. Before release, run the unexercised assistive matrix (listed under debt) and, where possible, involve real assistive-technology users — simulation is not evidence.

## Colors

### Semantic palette

There are no application hex values, no asset-catalog colors, and no accent-color asset anywhere in the app; the system tint and system appearance are inherited (`Info.plist` sets neither `NSAccentColorName` nor `UIUserInterfaceStyle`, and no code overrides `OverrideUserInterfaceStyle`). Every color is a dynamic `UIColor` semantic role. The default text color is applied centrally in `UI/Components/UIKitFactory.cs`.

Roles in active use:

| Design role | UIKit semantic color | Use |
|---|---|---|
| Primary text | `Label` | Filenames, usernames, titles, values, message text |
| Supporting text | `SecondaryLabel` | Metadata, paths, counts, speeds, timestamps, explanations |
| De-emphasized text | `TertiaryLabel` | Low-priority metadata |
| Link | `Link` | Tappable URLs and link-like text |
| Main canvas | `SystemBackground` | Standard screens and detail content |
| Layered content | `SecondarySystemBackground` | Inputs, banners, content nested above the canvas |
| Grouped canvas / rows | `SystemGroupedBackground`, `SecondarySystemGroupedBackground` | Settings and inset-grouped lists |
| Fills | `TertiarySystemFill`, `SystemGray5` | Message bubbles, subdued containers |
| Divider | `Separator` | Hairline separations |
| App accent | Inherited tint / `SystemBlue` | Interactive controls, the single primary action, outgoing bubbles |
| Status | `SystemGreen`, `SystemOrange`, `SystemRed` | See status table |

Other semantic roles may be adopted when a real need appears; introducing a custom color requires Any, Dark, and Increased Contrast variants and a documented reason.

Never cache the resolved value of a semantic color. If a Core Animation layer needs `CGColor`, resolve it against the view’s trait collection and re-resolve on a `RegisterForTraitChanges` callback — the room composer border is the reference implementation.

### Operational status colors

Color reinforces state; it never communicates alone. Every status color ships with a symbol and/or explicit text, and an accessibility value:

| State | Color | Companion cue as built |
|---|---|---|
| Connected / complete | `SystemGreen` | `checkmark.circle.fill` plus an explicit localized label |
| Disconnected / attention | `SystemOrange` | `wifi.slash` plus status text and a Reconnect action |
| Failed / destructive | `SystemRed` | The error text itself, destructive wording, or a delivery-state word; confirmation for destructive commands |
| Presence | `SystemGreen` / `SystemOrange` / `SecondaryLabel` | Presence symbol plus localized presence text (visible or as the accessibility value) |
| Unread | Tint + `Headline` weight | The literal word (“Unread”) and an unread count in text |
| Transfers | **No color at all** | Status symbol + localized status text + determinate progress bar |

The Transfers list is the reference pattern: state is fully legible in grayscale. Status color is applied locally — to a symbol, a short label, a progress element — never washed over rows or screens.

### Color acceptance rules

- Verify custom foreground/background pairs at ≥ 4.5:1; prefer standard UIKit combinations over engineered pairs. (The outgoing-bubble metadata pair is flagged for audit in the debt ledger.)
- Do not force an app light/dark palette; follow the system appearance.
- Do not use color alone for transfer state, connectivity, validation, selection, unread status, or destructive meaning.

## Typography

San Francisco through preferred text styles only — `UIKitFactory.PreferredFont(...)` is the single choke point, and `UIKitFactory.Label(...)` sets `AdjustsFontForContentSizeCategory = true` and `Lines = 0` unconditionally. There are **no fixed point sizes** in runtime code (the launch surface carries no text outside the outlined wordmark artwork).

Styles as shipped:

| Role | Preferred style | Where |
|---|---|---|
| Screen headings / hero state | `Title1`–`Title2` | Home state headings, About title, profile header |
| Section headings, row titles, unread rows | `Headline` | Section headers (with the `Header` trait), key values |
| Primary reading text | `Body` | List primary text, form values, message bodies, notices |
| Compact status / secondary | `Subheadline` | Chips, list secondary text, field labels |
| Metadata | `Footnote` | Status lines, sizes, speeds, timestamps, settings detail |
| Dense tertiary metadata | `Caption1`/`Caption2` | Room bubble sender and timestamp |
| Undo banner text | `Callout` | Action banners |

Large titles come from the navigation bar (`PrefersLargeTitles`) at tab roots, not from explicit `LargeTitle` labels.

Rules:

- Weight carries hierarchy (regular and semibold; no light weights, no all-caps headings, no letter spacing). The single sanctioned uppercase is data normalization: file-extension metadata badges such as “MP3”.
- Wrapping is the default. Deliberate truncation exists only where the full value survives elsewhere: the toolbar status line (one line, tail), glass action-button titles, two-line conversation previews (full text in the thread), and middle-truncated filenames on Account. Legal documents render in a scrolling, never-truncating text view (`UI/Components/ReadableTextViewController.cs`).
- No fixed cell heights anywhere: every table uses `AutomaticDimension` with an estimate, collection cells self-size, and no `GetHeightForRow` override exists.
- At accessibility sizes, favor vertical reflow over horizontal compression. Rows and forms self-size and re-measure live (verified: Legal Notices reflows an in-place content-size change without reload); chips scroll horizontally rather than wrap, which remains recorded debt.
- Rapidly changing numerics (speed, bytes, queue position, percent) render with monospaced digits: `UIKitFactory.PreferredMonospacedDigitFont(...)` applies the number-spacing font feature to the preferred style — still Dynamic Type — and transfer rows opt in through `FeatureListItem.MonospacedDigits`. Use it for any new in-place-ticking number.

## Layout

### Navigation frame

`RootTabBarController` builds four enum-ordered tabs once, each wrapped in its own navigation controller, with restoration identifiers and the `app.tabs` / `app.tab.{name}` identifier scheme:

| Destination | SF Symbols | Purpose |
|---|---|---|
| Home | `house` / `house.fill` | Login, connection state, identity, community and settings routes |
| Search | `magnifyingglass` | Search creation, history, wishlists, results, result actions |
| Transfers | `arrow.up.arrow.down` | Download/upload state, progress, management |
| Browse | `folder` / `folder.fill` | A user’s shared hierarchy and downloads from it |

- Tab roots use large titles — **except Search**, which opts out (`LargeTitleDisplayMode.Never`) so the stacked search field, chips, and results stay stable; the rationale lives in code. Pushed screens use inline titles, the system Back item, and the interactive-pop gesture.
- `UISearchTab` is not used; Search is a plain tab hosting a `UISearchController` with stacked placement and no auto-focus.
- One aggregate badge on Home (see “Notifications and interruptions”).
- Typed `AppRoute`s cover every destination; the coordinator makes repeated delivery idempotent, dismisses incompatible presentations first, defers authentication-gated routes through a de-duplicated queue that drains after sign-in, and issues one accessibility focus event per route.

### Spatial rules

- Root scroll views pin to the view edges so content moves beneath floating system bars; non-scrolling controls constrain to `SafeAreaLayoutGuide` and `LayoutMarginsGuide`; long-form notices use `ReadableContentGuide` (Home, legal text).
- Composers attach to `KeyboardLayoutGuide` (Messages, Room; the Account form and its bottom status area ride the same guide) with a clamp above the bottom safe area.
- 44 × 44 pt minimum hit regions are enforced centrally — `UIKitFactory.Button` carries a required ≥ 44 pt height constraint, chips enforce `MinimumChipHeight = 44`, and icon-only controls (password visibility, room circular actions) are explicitly constrained to 44 pt. Decorative subviews inside tappable rows disable user interaction so the whole row is the target.
- Self-sizing cells and intrinsic control sizes everywhere; never copy dimensions from Android XML.
- All orientations declared in `Info.plist` are supported with no per-controller overrides; verify compact-height layouts, keyboard, and safe areas when touching layout.
- One primary action, reachable, never duplicated across navigation bar, floating control, and row simultaneously. Persistent bottom space belongs to system-managed elements (tab bar, the selection toolbar, keyboard-safe composers) — no custom persistent bottom action bars.

### Information density

Search, transfer, browse, and message rows are dense but keep one reading order: primary label, state, then metadata. Disclosure and detail screens absorb overflow instead of shrinking type. High-frequency updates reconfigure retained rows without resetting scroll or selection: snapshots coalesce (transfers at a 120 ms cadence), apply with `ReconfigureItems` for retained identities, restore native selection after every apply, and suspend entirely while the view is off-window.

## Elevation & Depth

Semantic depth, not elevation numbers:

| Layer | Treatment | Examples |
|---|---|---|
| Content plane | Semantic backgrounds, grouped backgrounds, separators | Lists, rows, forms, messages, results |
| Raised content | System fills only where separation is needed | Message bubbles, composer field, banners |
| Control plane | System-provided Liquid Glass | Bars, search placement, the selection toolbar’s glass buttons |
| Presentation plane | UIKit presentation styling | Sheets, popovers, menus, alerts |

As shipped, the app contains **zero** custom shadows (`Layer.Shadow*` never written), **zero** blur or glass effect views (`UIVisualEffectView`, `UIGlassEffect`, `UIGlassContainerEffect` unused), and no custom bar backgrounds — with one documented exception: the selection accessory installs a fully transparent `UIToolbarAppearance` to correct a proven system integration defect (the tab bar drawing opaquely over the navigation controller’s toolbar). That exception is sanctioned; any new one needs the same in-code justification.

Glass rules:

- Glass belongs to navigation and important controls. The only explicit glass in the app is the selection toolbar’s button configurations: one `ProminentGlass` emphasized action, `Glass` for the rest. Rows, bubbles, chips, badges, and backgrounds never get glass.
- Standard UIKit bars, presentations, and controls acquire Liquid Glass automatically under the iOS 26 SDK; do not freeze or repaint them.
- A custom glass element is allowed only when no standard control represents the action, and only through APIs the pinned Microsoft.iOS binding actually exposes.
- With Reduce Transparency or Increased Contrast, grouping, boundaries, selection, and text hierarchy must survive without blur or translucent overlap — the app satisfies this largely by construction (no custom translucency), but it remains an acceptance check for new work.

## Shapes

Shape follows component semantics; system controls, bars, menus, sheets, alerts, and list cells own their shape.

As shipped:

- **Capsules** — search chips and the selection-bar buttons (`CornerStyle = Capsule`) and the 44 pt circular send/jump-to-latest controls. Capsules mark compact interactive tokens only, never ordinary text containers.
- **Container-concentric** — the About logo container uses `UICornerConfiguration.CreateContainerConcentric()`; this is the reference for any future custom container.
- **Rounded content wells** — one shared treatment: `UIKitFactory.ApplyRoundedContentCorners(...)` applies a uniform fixed 18 pt `UICornerConfiguration` to the Messages and Room bubbles and the Account image well. No view writes its own `Layer.CornerRadius`.
- **Growing text wells** — `UIKitFactory.ApplyGrowingFieldCorners(...)` pins a uniform fixed 22 pt radius (half the 44 pt resting control height) on the room composer. A capsule tracks the measured height, so a field that grows past one line becomes a lozenge whose half-circle ends overrun the text and steal usable width; the fixed radius keeps a one-line field visually identical to a capsule and a grown field a calm rounded well.
- Native grouped-list geometry for settings; no per-row cards. Avatars and images use system clipping with non-color fallbacks.

The only sanctioned corner treatments are the shared helpers and container-concentric above; a new hard-coded `CornerRadius` value is a defect.

## Components

### Bars and navigation

- `UITabBarController`, `UINavigationController`, `UINavigationBar`, and the selection `UIToolbar`, all system-appearance (single sanctioned exception above).
- `UIBarButtonItem` system items and SF Symbols; tabs navigate, bar items act. A tab never disappears because its content is empty or the account is offline.
- Tab badges only for meaningful actionable counts, capped with system conventions and paired with an accessibility value.

### Lists, rows, and selection

- High-volume surfaces — Search results, Transfers, Browse, Wishlists — use the shared `UI/Components/DiffableFeatureListView.cs`: a `UICollectionView` list layout with a diffable data source, stable semantic hash identities, self-sizing rows, `UIListContentConfiguration` content, a trailing 64 × 4 pt progress accessory whose percent is exposed through the cell’s accessibility value, and selection restoration after every snapshot.
- Social and catalog surfaces — Messages, Chatrooms, Room detail, Users, Settings, Import, Profile, Account, Legal, Diagnostics — use `UITableView` (inset-grouped where appropriate); Messages and Chatrooms drive theirs with diffable data sources. This split is the recorded system (the seed contract wanted collection views everywhere); migrating the remaining manual table sources to diffable is optional refinement, noted in debt.
- Native edit mode with multi-select accessories replaces Android `ActionMode`: explicit selection counts, Select All and Invert with named scope, capability-aware batch commands showing eligible/selected counts, destructive actions separated and confirmed, and a contextual glass selection toolbar (`UI/Components/SelectionAccessory.cs`) that reserves content clearance via `AdditionalSafeAreaInsets`.
- Selection, expansion, filters, and scroll anchors are keyed by stable IDs and survive rapid updates, reordering, and local filtering. Display strings and indices are never identities.

### Search, filtering, and sorting

- `UISearchController` in the navigation item (stacked placement, no navigation-bar hiding during presentation). Settings, Messages compose, Chatrooms, Users, room users, and Browse each get native search where filtering matters; “no match” is a distinct state from “empty.”
- Active search configuration lives in a horizontally scrolling capsule chip bar (`UI/Components/ChipBarView.cs`) — scope, ordering, and filters chips carry `UIMenu`s (`ShowsMenuAsPrimaryAction`), embed their current value in the label and accessibility value, and retain identity across renders. This is a concise summary, not an Android chip cloud.
- Include/exclude text filters, free-slot and locked-result rules, format/bitrate/category smart filters, and sorting reproject already-received results without repeating the network request; “all results filtered” shows visible/total counts with a direct Clear Filters recovery.
- During search: the query is retained, partial results stream at a bounded cadence, cancel is explicit, stale generations are rejected, and prior useful content survives refresh. Empty, filtered-empty, offline, canceled, timed-out, failed, idle, and result-limit states are all distinct.

### Buttons and controls

- `UIButtonConfiguration` at intrinsic size, minimum 44 pt: `Filled` for the single screen primary (Sign in, Reconnect, Browse, Send, Save Profile), `Tinted` for standard emphasized actions, `Gray`/`Plain` for the rest, glass only on the selection bar. Icon-only controls need an unambiguous SF Symbol, an accessibility label, and the 44 pt region.
- Destructive buttons use explicit wording, red tint with a plain (non-prominent) configuration, and confirmation.
- Activity shows within or beside the initiating control (bounded `UIActivityIndicatorView`s, always `IsAccessibilityElement = false`); duplicate submission is prevented; a local action never replaces the whole screen with a spinner.

### Forms and settings

- Native text fields with content types, secure entry with a 44 pt visibility control and AutoFill, switches, steppers, and inset-grouped rows via `UIListContentConfiguration`.
- Validation is inline next to the field, announced, mirrored into the field’s accessibility hint, and moves focus to the field on failed submit. Alerts are reserved for failures requiring acknowledgement. The Home login form is the reference implementation.
- The Account screen is a static inset-grouped form: retained cells (never reloaded, so editing and focus survive), section headers and footers carrying the help copy, action rows with symbol, `Button` trait, and stable identifier that present their picker or form on selection, row availability applied to touch and assistive technology together, one `Filled` primary (Save Profile) in a chrome-free row, and a persistent status/progress area riding the keyboard guide below the table.
- Privileges sit one level below Account behind a value-carrying disclosure row (Privileges — “12 days” / “None”), so glancing at remaining time costs no navigation while the state-dependent actions get their own screen. On the pushed Privileges screen (`UI/Screens/Profile/PrivilegesViewController.cs`), donation is the single acquisition path: Donate to Soulseek opens the account-prefilled slsknet.org donation page in the external browser, with a trailing outward-arrow accessory and an accessibility hint signposting the departure. The Give Privilege Days row exists only while the account holds privileges, and the donations-explainer footer only while it confirmedly holds none — before the first check the screen claims neither state. Privilege time refreshes automatically on load and when the app returns to the foreground (the moment a donation in Safari most likely changed it); Check Remaining Time stays as the visible manual fallback. Unlike the Account form’s never-reloaded cells, this screen re-projects its retained rows through a reload when server state changes: it hosts no editable fields, so no editing state can be lost.
- The settings catalog (`UI/Screens/Settings/`) is searchable (title/subtitle/value/keywords) with a distinct no-match state; every row declares applicability and a typed apply command with transactional side effects and rollback. Read-only platform facts (Documents location, background limits) are honest multiline information rows, not disabled controls. Settings import is a non-mutating parse/preview review with per-item tri-state selection and an atomic committed merge.

### Transfers and progress

- `UIProgressView` for determinate work only; unknown totals are never presented as complete, and Open/Share appear only after the durable final URL commits.
- A transfer row prioritizes filename, localized state text, status symbol, determinate progress, and decision-useful metadata — with no status color at all (see Colors). Folder rows aggregate byte-weighted progress with accessible values.
- Shipped states: not started, queued, active, paused, retrying, waiting-for-peer, failed, denied, timed out, offline, aborted, canceled, completed. **Stalled and background-paused provenance are not yet distinct** — a typed pause reason (User / BackgroundExpiry / Lifecycle) is deferred work, as are separate Cancel/Abort commands and a peer queue-refresh contract; until they exist the UI must not fake those distinctions.
- Actions are capability-driven (retry/pause/clear/open/share per row, batch equivalents with eligible counts) through the row action sheet, edit mode, and the selection toolbar.

### Sheets, menus, alerts, and sharing

- The shipped secondary-action surface is the **action sheet** (`UIAlertControllerStyle.ActionSheet`, popover-anchored on regular widths *only*): row activation on Transfers/Search/Browse, sort and grouping pickers, user actions, and link actions all flow through it, with the current choice named textually. iOS 26 honors a popover anchor on a compact iPhone too, so a compact-width anchor is a defect, not a harmless hint — the room timeline and room user list once anchored to their whole table and rendered a bubble squeezed against the bottom edge with its actions clipped. Compact widths leave `SourceView`/`SourceRect` unset and take the system's own full-width sheet. Modal forms use `FormSheet`/`PageSheet`. This is the recorded system; `UIContextMenuInteraction` and `UISheetPresentationController` detents are absent, and adopting them (context menus as *additive* discoverability, detents for the download review) is optional refinement noted in debt — never a replacement that hides essential actions behind long-press discovery.
- `UIAlertController` alerts are for critical confirmation and acknowledgement.
- `UIActivityViewController` shares links, completed files, exports, profile images, and logs; document pickers handle import and profile images.

### Messages and social screens

- Conversation and room cells follow native list patterns; bubbles use semantic fills (incoming) and tint (outgoing) — never glass — with self-sizing leading/trailing alignment, visible sender/direction metadata, timestamps, delivery-state words, selectable/copyable text, validated `slsk://` link actions (Download, Browse at Location, Copy, Share), and complete accessibility labels.
- Composers ride the keyboard layout guide and keep drafts and focus on failure. Sending/failed/retry states stay visible without blocking reading.
- **One composer, both conversations.** Messages and Room share a single treatment: an unfilled container that floats the field over the transcript (never a filled bar welded to the screen edge), 22 pt side insets matching the tab bar, a growing `UITextView`, and a 44 pt filled circular send control beside it with the jump-to-latest control floating above the composer's trailing edge. Divergence between the two is a defect.
- The **growing field**: Return inserts a line break, the send control is the only way to send, and the field grows from one body line to five — never past a third of the screen, so an accessibility text size cannot swallow the transcript — before it scrolls. It keeps a fixed 22 pt corner radius (see Corners), 16 pt side and 11 pt vertical text insets, a placeholder label the field's own text covers, a hairline `Separator` border re-resolved on interface-style changes, and an 8 pt gap that survives whether the keyboard is up or down. Send is enabled only for a reachable conversation holding a non-empty draft, and focus returns to the field after a send so a conversation does not lose the keyboard every message.
- **Putting the keyboard away** is never a dead end: the transcript keeps `UIScrollViewKeyboardDismissMode.Interactive`, and a tap anywhere on it dismisses the keyboard through `KeyboardDismissTap`. That recognizer only accepts touches while the composer is editing and cancels the touch it consumes, so with the keyboard down ordinary row activation is untouched, and with it up putting the keyboard away never doubles as opening whatever sat under the finger. Sending must not be the only exit.
- **Drag to reply** — a trailing drag on a message row follows the finger, fades in a reply glyph behind it, resists once it passes 56 pt, and quotes the message on release; there is no button to press afterwards. This is `UIKitFactory`'s neighbor `ReplySwipe`, shared by both transcripts and installed on the row's Auto Layout content — never on the cell's content view, whose frame a table assigns directly and which would fight the transform. The gesture begins only on a clear trailing drag (`|vx| > |vy|`), so vertical reading always wins the touch, and it is invisible to VoiceOver, so every row that installs one also carries a Reply custom action and every message action sheet opens with Reply. A trailing `UISwipeActionsConfiguration` is a defect here: it reveals a Reply button that still has to be pressed, which is two gestures for one intention.
- The quote itself is `> {user} said: "{message}"` followed by a blank line, with the caret beneath it and any existing draft preserved below. Newlines inside the quoted message collapse to spaces so the quote stays one line. Member-activity notices carry no reply gesture. Two things keep the drag reliable and both are load-bearing: the transcript defers an incoming-message reload while a drag is in flight (reloading recycles the row out from under it), and auto-following the newest message is skipped while the table is tracking a touch (scrolling cancels the gesture).
- **Following the latest message is a latched decision, not a measurement.** Only a person's own scrolling changes it — `Dragging` or `Decelerating` in `Scrolled`, plus a send, which always returns to the newest message. Measuring "am I near the bottom?" at the moment the keyboard rises reports *no*, because the keyboard shortens the table without anyone scrolling; that is exactly how the transcript used to abandon the conversation halfway up the screen. Every layout pass that changes the table's height therefore re-anchors the newest message while the decision holds, which keeps the conversation pinned through the whole keyboard animation.
- **Anchoring is two unanimated passes over the last row**, not one animated scroll and not a computed content offset. Self-sizing rows report an estimated height until UIKit measures them, so an animated scroll aims at an end that moves while it travels — which is how a long arriving message ended up half-hidden behind the composer — and a content offset derived from `ContentSize` is wrong whenever rows above the viewport are still estimates. The first pass forces the rows it passes to be measured; the second aims at what they turned out to be. An animated request replays that journey from where it started, so it animates toward a destination that is already exact.
- Incoming bursts preserve the reading position unless the transcript is following the latest; a labeled Jump to Latest control appears while reading history.
- Presence, roles, unread, and friend status always carry text/symbol/accessibility equivalents alongside any color.
- Room status events are static self-sizing rows — no pulse or shimmer, ever.

### Screen states and feedback

Every applicable screen defines its states intentionally from the shared model:

```
idle / loading / content / empty / offline / recoverable error / terminal error
```

- `UIContentUnavailableConfiguration` (wrapped by `UI/Components/ContentStateView.cs`) renders full-screen states **only when no usable content exists**, with at most one clear recovery action; the symbol reinforces but never replaces the text. Search ships the richest set (loading-with-cancel, empty, filtered-empty with Clear Filters, offline, timed out, canceled, failed, idle); Import distinguishes seven outcomes including “Nothing to Import” versus “Nothing New.”
- Cached content survives refresh and failure: full-screen loading is reserved for contentless first load, and offline/error guidance appears inline above retained rows with an exact retry action. `UIRefreshControl` only where whole-list refresh is meaningful (Messages, Chatrooms).
- The transient banner (`Services/IosToaster.cs`) is the recorded fallback for results with no owning surface: `SecondarySystemBackground`, square-cornered, shadow-free, top-anchored under the safe area, wrapping `Subheadline` text, 2 s/4 s durations extended to ≥ 8 s under VoiceOver with a sequential readable dwell, announced without stealing focus, bounded FIFO of 8, fades skipped under Reduce Motion. It is not a substitute for inline state, and routine acknowledgements through it are defects (one known: the download-complete success toast).
- Reversible destructive actions (conversation/thread delete) present a persistent self-sizing Undo action banner with an announcement.

### Icons, imagery, and motion

- SF Symbols for all interface actions and status; weight and scale match adjacent text. The AnimaSeek mark appears exactly once in-app (About, container-concentric corners, accessibility-labeled, invert-protected) — never as decoration, never in empty states. The launch surface is a Vacuum Navy brand field carrying the reversed stacked lockup, identical in light and dark: the storyboard shows it first, and `LaunchPlaceholderViewController` reproduces the same frame as the initial window root so cold start never exposes a blank window while the hierarchy is composed. The launch storyboard cannot resolve asset-catalog images in this toolchain, so the splash lockup ships as loose `LaunchSplash` bundle files copied from the catalog's dark stacked lockup.
- The complete custom-motion inventory is five sites: banner fade in/out, the selection-bar fade, and two Reduce-Motion-aware scroll-to-latest assists. Diffable updates animate only when the view is on-window, and reconfigurations of live progress never animate. There is no `UIViewPropertyAnimator`, no Core Animation, no looping or continuous animation.
- Under Reduce Motion: durations become zero (`AccessibleAnimationDuration`), banner fades are skipped, and any future spatial/scale/blur motion must fall back to fade or nothing.

#### Phoenix brand identity

**Phoenix** is the approved AnimaSeek identity: one simple upright broad-V bird built from four flat closed forms. Its Connection Cyan (`#27B9FF`) left wing and Connection Coral (`#FF4C58`) right wing represent the two sides of human connection; its Vacuum Navy (`#071321`) central body represents one connected self; and its Discovery Yellow (`#FFD33D`) flame-tail represents renewal, discovery, and new experiences. Warm White (`#FFF9EE`) completes the identity palette for wordmarks and dark brand fields. Preserve the upright orientation, broad-V silhouette, and cyan-left/coral-right order. Never add ribbons, detached feathers, gradients, strokes, glass, or glow.

`Seeker/Assets/new/source/animaseek-symbol-color.svg` is the canonical symbol contract. Its contours are traced from the user-approved PNG rather than generated independently: `Seeker/Assets/new/master` preserves the original approved raster, the cleaned transparent raster master, and their exact SHA-256 provenance. The approved staged set also includes color, reversed, and monochrome symbols; outlined wordmarks and horizontal/stacked lockups; and iOS Any, Dark, and Tinted app-icon appearances plus launch and in-app assets. Use these colors and forms only for brand identity; never turn them into status colors, decorate routine screens with the mark, or substitute brand art for SF Symbols. UIKit chrome, controls, text, backgrounds, tint, and states remain semantic and system-provided. The identity is integrated in production: `Seeker.iOS/Assets.xcassets` is a copy of the staged `Seeker/Assets/new/ios/AnimaSeekAssets.xcassets` and carries the Any/Dark/Tinted Phoenix app icons, the navy brand tile (`LaunchLogo`, shown only on About), the adaptive stacked lockup (`LaunchLockup`, 1x/2x/3x with a dark appearance), the `BrandNavy` named color backing the launch surface, and the adaptive transparent symbol (`AnimaSeekMark`, reserved for future in-app brand use). The launch splash additionally ships as loose `LaunchSplash@2x/@3x` bundle files (copies of the dark lockup) because the launch storyboard cannot resolve catalog images in this toolchain.

### Accessibility and automation identifiers

- Identifiers follow `<feature>.<element>[.<action-or-state>]` (`app.tabs`, `home.login.username`, `search.results.list`, `legal.modified-library-notice`), with dynamic segments as opaque 16-character SHA-256 prefixes. Identifiers never derive from visible text, indices, credentials, usernames, messages, or paths. `UI/Accessibility/AccessibilityIdentifiers.cs` holds the shared constants; many screens still declare theirs inline (consistent convention, centralization is optional cleanup).
- Patterns in force: composite labels (“{title}. {detail}”), state in `AccessibilityValue` (progress percent, switch state, presence, unread counts, badge counts, current chip value), outcomes in hints (“more actions”, validation errors), `Header` traits on section headings, `Button`/`Selected` traits on actionable rows, decorative views suppressed.
- `Announce` and `FocusScreen` (`UI/Accessibility/AccessibilityExtensions.cs`) are the only announcement paths; significant transitions announce once (Home de-duplicates against its last announced state), progress never does.

### Legal identity notice

`SoulseekClientIdentity.ModifiedLibraryNotice` is required interface content and renders **verbatim** — it is the single sanctioned non-catalog literal. As shipped it leads the permanent Legal Notices destination (`legal.modified-library-notice`), opens into a never-truncating readable text view, reflows at every Dynamic Type size, and stays reachable without signing in from Home (navigation-bar Settings action in every account state), Settings, and About, with no acknowledgement gate. Legal Notices also bundles the license and provenance documents (LICENSE, NOTICE, THIRD-PARTY-NOTICES, .NET and Microsoft.iOS licenses, StringTools, PROVENANCE), each rendered readably and omitted gracefully if absent. Replacing or restyling any of this must preserve verbatim text, permanence, signed-out reachability, and reflow. The source remark in `Common/SoulseekClientIdentity.cs` asks for the notice to stay “visible and prominent”; whether the current three-level route satisfies *prominent* is an open question recorded in the debt ledger — do not bury it further.

## Behavioral parity outcomes

The Android application was the behavioral inventory, never the visual template. Translation rules that produced this design, and remain binding for new work:

| Android construct | iOS resolution |
|---|---|
| ViewPager + bottom navigation | `UITabBarController`, one navigation stack per tab |
| RecyclerView adapters | Diffable lists with stable semantic IDs and self-sizing cells |
| Cards, elevation, FAB, chip clouds | Native lists, system depth, bar/toolbar actions, minimal capsule tokens |
| `ActionMode` batch flows | Native edit mode, selection accessories, contextual glass toolbar |
| Bottom sheets / Material dialogs | Anchored action sheets, form/page sheets, alerts for critical acknowledgement only |
| Toast / snackbar | Inline state first; accessible transient banner second; Undo action banner for reversible deletes |
| `ViewFlipper` state switching | One enum-driven snapshot renderer per screen |
| Shimmer / pulse effects | Static or system progress; nothing decorative |
| Intents and activity shims | Typed `AppRoute`s through an idempotent coordinator |
| Android vector icons | SF Symbols (custom art only for brand) |

### Intentional omissions

Dropped deliberately, with rationale — reintroducing any of these requires a new product decision:

- **App shutdown/close controls** — iOS owns process lifecycle (also drops Android’s close/routing shim activities).
- **`ACTION_SEND` “Search Here” share entry** — no baseline Share Extension; paste or `slsk://` instead.
- **Theme/day-night variants and language selection** — system appearance; English-only fork.
- **SAF folder pickers, manual incomplete-folder rows, file-backed-download toggles** — sandbox Documents storage makes them meaningless implementation details.
- **External shared-folder management and filesystem permission repair** — sharing is sandbox-only; no URI permissions exist.
- **Foreground-service start/stop controls** — background execution is system-managed.
- **In-app playback** — completed files open via Files or another app; Apple Music insertion is unavailable.
- **“Reveal app root in Files”** — no verified API; per-file Open/Share only.
- **Indefinite background queue/keep-alive and background PM/chat** — iOS cannot provide immortal connectivity; the server queues offline PMs; push is out of scope.

### Enduring screen contracts

Each destination keeps the acceptance bar it shipped under:

- **Home/Login** — all account states from one snapshot model; credentials never in logs or identifiers; reconnect/logout race-safe.
- **Search** — thousands of results stay smooth; stale results cannot overwrite a newer query; filters and results survive tab switches.
- **Download review** — queue acceptance returns promptly; selection and focus survive refresh; dismissal never cancels accepted work.
- **Transfers** — capabilities match domain state; destructive scope explicit; updates never jump scroll or focus; relaunch reconciles durable state.
- **Browse** — late responses cannot replace the active location; every former toast-only error is a persistent recoverable state.
- **Settings/Import** — persistence and runtime state stay consistent; every row usable at accessibility sizes; import cancellation/failure recoverable.
- **Messages/Chatrooms/Users/Profile** — notification and deep-link routes open exactly one correct destination; unread/presence/role never color-only; keyboard and scroll anchoring stable; membership destruction confirmed.
- **Global routing** — cold/warm delivery idempotent and safe; direct reply never navigates or moves focus; authentication defers rather than fails.

And every applicable screen must specify and keep: initial/loading, refreshing-with-content, content, empty, filtered-empty, offline, recoverable error, terminal error, permission denied, slow/delayed, cancellation, retry, duplicate delivery, late response, long content, large collections, rapid updates, portrait/landscape, keyboard, tab switching, background/foreground, restoration, Light/Dark, Increased Contrast, Reduce Transparency, Reduce Motion, Bold Text, and all Dynamic Type sizes.

### Change acceptance

A UI change is acceptable when: the affected behaviors and states are named; new presentation behavior has unit coverage; the strict `Debug Mock` AOT/full-link build and relevant flows pass (commands in `DEVELOPMENT.md`); no trim/AOT warning, main-thread violation, or unobserved task is introduced; new visible/assistive text is catalog-backed (the verbatim notice excepted); new interactive elements have identifiers, labels/values, correct focus, and 44 pt regions; screenshots cover intentional visual change; and no Android styling or direct core coupling enters a controller.

## Known deviations and design debt

Honest ledger of what the shipped app does *not* yet satisfy. These are defects or open work — recorded so they are never mistaken for the system.

**Visual system**

- The outgoing-bubble metadata color (white at 84 % on `SystemBlue`) has no verified contrast ratio. (The former non-adaptive white backdrop behind the About logo is resolved: the Phoenix brand tile is opaque full-bleed and the hard-coded background is removed.)

**Interaction and feedback**

- Several routine actions still acknowledge through alerts instead of durable local state, and the download-complete success toast duplicates feedback the Transfers row and completion notification already provide. The shared portable layer routes many more toast calls through `IToaster` than the iOS-local six — its volume is unaudited.
- Context menus and sheet detents are absent app-wide (action sheets carry everything); adopting them is optional additive refinement.
- The room detail's empty state centers itself over the full table height, so at accessibility text sizes its title runs under the navigation bar (observed on 2026-08-19 at accessibility-extra-extra-extra-large). Every other empty state uses `ContentStateView`; this one does not.
- The aggregate badge attributes Search/Transfers counts to the Home tab.
- The selection-bar fade ignores Reduce Motion (low severity — a fade is itself the sanctioned fallback).
- The conversation list’s visible presence cue is symbol+color with the word only in the accessibility value; consider surfacing the text.
- Several table screens allocate a fresh cell per row instead of dequeuing — a scroll-performance concern on long lists.

**Contracts still owed (deferred until their typed commands exist — do not fake in UI)**

- Multiple independently retained active searches (multi-search repository).
- Transfer pause provenance (User / BackgroundExpiry / Lifecycle) for honest stalled/background-paused labels; distinct Cancel versus Abort commands; peer queue-refresh.
- Auto-away (needs an inactivity/lifecycle coordinator).
- External port check (needs a verified HTTPS endpoint; never the cleartext Android endpoint or a broad ATS exception).

**Copy and identity**

- Whether the legal notice’s three-level route satisfies the source remark’s “visible and prominent” is unresolved; a Home/account-surface presentation was the original intent.
- Phoenix production integration is pending: the approved staged set includes color, reversed, and monochrome variants, wordmarks and lockups, and iOS Any, Dark, Tinted, launch, and in-app assets, while the production `Seeker.iOS/Assets.xcassets` catalog remains unchanged and still contains the legacy identity.

**Verification still owed**

- The device-dependent assistive matrix: Switch Control, Full Keyboard Access, Voice Control, Bold Text, Button Shapes, Differentiate Without Color, Display Zoom, Reduce Transparency, and both landscapes on a physical device. The simulator-scriptable subset — Light/Dark, Increased Contrast, Dynamic Type through AX5, live trait changes — was exercised on 2026-08-17 (see “Verification evidence”).
- Conversation-detail verification on 2026-08-19 covered the **room** (reachable through the new `ANIMASEEK_UI_ROOM` launch variable) in Dark at the default size and at accessibility-extra-extra-extra-large, plus a Light pass: action sheet, growing composer with the keyboard raised, swipe-to-reply, and send. The **Messages** thread was verified in Dark at the default size only, and is still not reachable from a launch route (it needs a delivered mock message first). A second Dark pass the same day covered the reworked conversation behavior on both screens: drag-to-reply (held mid-drag and released), tap-to-dismiss over a message row, send, keyboard raise with messages arriving behind it, and row activation with the keyboard down. Owed: Messages in Light, both details under Increased Contrast, the Messages composer at accessibility text sizes, and the drag gesture under VoiceOver and Reduce Motion.
- Measured performance budgets: the 3,000-result search scroll/main-thread budget and the long-session social/browse stress matrix.
- Deterministic named scenario/clock control covering every mock state; ported end-to-end UI automation for `com.animaseek.app`; the full visual-regression matrix.
- Co-design or testing with assistive-technology users with lived experience (none has occurred).
- The Android reference head retires only after replacement evidence covers its validation role.
- 325 compiler/analyzer warnings remain on the strict gate; hold the zero-linker-warning line.

## Do’s and Don’ts

| Do | Don’t |
|---|---|
| Let current UIKit components provide the Apple look. | Freeze the app to an OS screenshot with custom-painted chrome. |
| Use semantic colors, preferred text styles, system spacing, and SF Symbols — runtime code ships with zero hex values and zero fixed font sizes (the launch surface's `BrandNavy` lives in the asset catalog and the storyboard, never in code); keep it that way. | Introduce hex palettes, fixed sizes, hard-coded shadows, or private icons for standard actions. |
| Keep glass on the control plane (today: the selection toolbar only). | Put glass, blur, or elevation on rows, bubbles, chips, or backgrounds. |
| Keep one `Filled` primary action per screen. | Tint every control on one surface. |
| Pair every status color with a symbol, text, progress, and an accessibility value — Transfers proves state works with no color at all. | Depend on red/green, tint, opacity, or animation alone. |
| Let rows self-size and wrap through all Dynamic Type categories. | Fix cell heights, clip required text, or hide actions at accessibility sizes. |
| Show queued, paused, offline, waiting-for-peer, and failed states honestly, with capability-gated recovery. | Present endless spinners, imply background continuity iOS cannot provide, or fake state distinctions whose typed commands don’t exist yet. |
| Acknowledge results in local state first; use the banner only when nothing owns the result, and an Undo banner for reversible deletes. | Use alerts or toasts as routine feedback. |
| Keep destructive actions explicit, separated, confirmed in proportion, and worded with their consequence. | Hide destructive commands beside routine ones or rely on icon-only affordances. |
| Keep new text in the string catalog and new controls at 44 pt with identifiers, labels, and values. | Hard-code visible copy in controllers or derive identifiers from user data. |
| Preserve selection, scroll anchors, drafts, and filters through updates, tab switches, and relaunch. | Reload whole lists, jump scroll position, or make people re-enter state the app already had. |
| Keep the verbatim notice leading the permanent, signed-out-reachable Legal Notices route. | Gate it, truncate it, paraphrase it, or bury it deeper than it already is. |

When a design question is unresolved: choose standard UIKit behavior, verify it in the simulator matrix (Light/Dark, Increased Contrast, accessibility text sizes, both orientations), and record any exception — with its justification — in this document and beside the code that needs it.
