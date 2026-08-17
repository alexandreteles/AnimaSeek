# Implementation Preflight

Use this file at the end of a build, redesign, or frontend audit. It turns inclusive design intent into practical release checks for working web interfaces. Review the implemented product, not only the mockup.

This is a preflight, not a substitute for formal accessibility conformance testing, security review, privacy review, clinical review, or legal review. It is meant to catch inclusive-design failures before handoff or release.

## Pass Standard

A build is not ready if a person cannot complete the primary task through keyboard-only use, assistive technology, zoom, reduced motion, high contrast or forced colors, touch, and at least one low-bandwidth or interrupted context.

A build is not ready if it preserves visual polish by removing control, clarity, feedback, or recovery.

A build is not ready if a critical state exists only in design files and has not been implemented, tested, and connected to the person’s next action.

Classify every finding:

- **Blocker:** prevents task completion, hides critical information, traps focus, causes data loss, removes privacy control, or creates an unsafe or high-stress path.
- **Major:** increases cognitive, emotional, or physical demand enough that some people will abandon, ask for help, or lose trust.
- **Minor:** creates friction but has a clear alternative path.
- **Monitor:** acceptable for release only if feedback, analytics, support tickets, and human testing are planned.

## Preflight Setup

Before checking details, make a quick inventory:

- Primary flows: entry, task start, task progress, completion, interruption, return, cancellation, recovery.
- Page types: landing, dashboard, collection, detail view, form, wizard, settings, support, AI-assisted surface, error route.
- Components: buttons, links, inputs, lists, tables, cards, menus, dialogs, drawers, popovers, toasts, tabs, media, timers, uploads, search, filters, pagination.
- Dynamic moments: validation, loading, optimistic updates, route changes, status messages, saved state, suggestions, notifications, AI output.
- Human contexts: first-time use, expert use, stress, fatigue, distraction, low confidence, low bandwidth, small screen, glare, public setting, one-handed use.
- Input/output paths: keyboard, pointer, touch, screen reader, magnification, speech input, switch-like sequential navigation, reduced motion, forced colors, text scaling.
- Data risk: personal data, inferred data, AI prompts, training data, consent, sharing, deletion, retention, and privacy settings.

Record the build version, browser, device, viewport sizes, assistive technologies, preference settings, and test date.

## Practical Frontend Guardrails

### Structure

Use semantic structure that remains understandable after CSS, JavaScript, or imagery fails.

- One visible page purpose is clear near the top.
- Heading levels are ordered and describe sections, not visual size.
- Landmarks are distinct and named when more than one of the same type exists.
- Lists, tables, forms, and navigation are represented with their correct elements.
- The DOM reading order matches the visual and task order.
- Decorative imagery is ignored by assistive technology; meaningful imagery has useful text alternatives.

### Interactive Elements

Controls must expose what they are, what they do, and what state they are in.

- Use native controls before custom widgets.
- Use links for navigation and buttons for actions.
- Every control has a programmatic name that matches or includes the visible label.
- Icons are not the only label for high-consequence actions.
- Disabled controls explain why they are unavailable or how to make them available when that is not obvious.
- Custom controls expose role, state, value, keyboard behavior, and focus behavior equivalent to the native pattern.

### Keyboard and Focus

The keyboard path must be complete, predictable, and recoverable.

- All interactive elements can be reached and operated without a mouse.
- Focus order follows the task sequence.
- Focus indicators are visible in default, dark, high-contrast, and forced-color modes.
- Modals, menus, drawers, and popovers move focus intentionally, contain focus only while appropriate, and return focus when closed.
- Route changes, async updates, validation, and inserted content do not strand focus.
- Skip links or equivalent bypass mechanisms exist when repeated content would slow task completion.
- No interaction depends only on hover, drag, pinch, fine pointer movement, timing, or spatial memory.

### Feedback and Status

People should not have to infer whether the system worked.

- Every action has feedback: immediate acknowledgement, progress when delayed, and clear completion.
- Error, success, loading, saved, unsaved, offline, and retry states are visible and programmatically available when relevant.
- Live announcements are used for meaningful dynamic changes only; avoid noisy repeated announcements.
- Confirmation messages state what happened, what remains possible, and whether the person can undo or continue.
- Error messages put the fix first, avoid blame, and keep the person in the flow.

### Forms and Data Entry

Forms must tolerate uncertainty, interruption, and varied input precision.

- Labels remain visible or programmatically associated at all times.
- Required fields, formats, constraints, and consequences are clear before submission.
- Validation is timely, specific, and available to assistive technology.
- People can correct errors without re-entering unrelated information.
- Long forms preserve drafts or warn before data loss.
- Destructive or irreversible submissions include review, confirmation, undo, or recovery.
- Autocomplete, input modes, and sensible defaults reduce effort without removing choice.
- Optional personal disclosure is visibly optional and never hidden as a condition of help unless it is truly required.

### Wayfinding

The person should always know where they are, what is available, and what to do next.

- Navigation is consistent across the product.
- The current page, current step, selected tab, expanded section, and active filter are exposed visually and programmatically.
- Multi-step flows show progress and remaining steps.
- There is one obvious primary action per step when a flow asks for a decision.
- Back, cancel, undo, save, resume, and support paths are present where the cost of losing context is high.
- Empty states explain what is missing and provide the next useful action.

### Content and Guidance

Content should reduce cognitive demand at the moment of use.

- Headings, labels, helper text, and actions use familiar language.
- Dense content is chunked into meaningful sections.
- Help content is available without forcing people to leave the task.
- Guidance can be skipped, revisited, and consumed in more than one format when the task is complex.
- New concepts are introduced contextually, with brief top-level explanations and optional detail.
- Button text names the result of the action, not just the action mechanic.
- The tone accounts for likely emotional state: confused, anxious, frustrated, learning, deciding, or recovering.

### Visual Perception

Visual design must not be the only access path.

- Color is never the sole signal for status, selection, error, urgency, or category.
- Text, icons, charts, and controls remain legible with increased text size, browser zoom, high contrast, dark mode, and forced colors.
- Layout does not require horizontal scrolling for ordinary reading or task completion at narrow widths.
- Important controls remain identifiable when images fail or are blocked.
- Badges, red accents, and high-saturation warnings are reserved for true importance, not attention farming.

### Motion, Audio, and Media

Motion and sound should clarify, not steal attention.

- Motion is disabled or replaced when reduced motion is requested.
- Autoplaying video, animation, sound, and GIF-like loops can be paused, hidden, muted, or avoided.
- Motion is not required to understand state changes.
- Audio-only or video-only information has a text or visual equivalent.
- Captions, transcripts, and descriptions are available for instructional or essential media.
- Loading animations do not become the only sign that the system is working.

### Loading, Latency, and Offline States

Waiting increases focus and memory demands. Treat latency as part of the experience.

- The interface says what is loading and, where possible, why it may take time.
- Long waits include estimated time, progress, or a useful preparation cue.
- Slow network and failed network states have retry, save, or continue-later paths.
- Optimistic updates can be reversed or reconciled if the request fails.
- Skeletons and shimmers do not obscure orientation, layout, or task purpose.
- People can pause or disable decorative loading motion.
- Critical flows preserve state during refresh, route changes, and temporary disconnection.

### Interruptions, Notifications, Pop-ups, and Suggestions

Treat every interruption as a cost.

- The urgency level is clear: full attention, partial attention, peripheral, or deferrable.
- Non-urgent interruptions do not open modals, steal focus, play sound, or block the task.
- Pop-ups are relevant, actionable, dismissible, and limited in frequency.
- Suggestions appear after expressed intent or clear need, not as generic pressure.
- Available actions are inside the notification or suggestion.
- People can control timing, channel, frequency, and whether urgent items can break through.
- The system can recover if the person misses, dismisses, or cannot act on the message.

### Settings and Customization

Customization is part of implementation quality, not a decorative layer.

- Important preferences are available in context and in a predictable settings area.
- Settings explain what they do before requiring experimentation.
- Preferences persist across sessions when appropriate and consented.
- Readability, density, motion, sound, notifications, privacy, and guidance preferences are considered for complex products.
- Defaults are usable without configuration; configuration improves fit rather than fixing a broken default.
- Reset and opt-out paths are clear.

### Device, Input, and Output Fit

Assume people combine devices, accessories, and adaptations.

- Targets are large enough and spaced enough for touch, tremor, low precision, and one-handed use.
- Interactions do not require two hands, simultaneous gestures, precise drag, or sustained pressure unless an alternative exists.
- The interface works with detached keyboards, alternative mice, screen magnification, screen readers, speech input, and switch-like sequential navigation.
- Text and layout scale without clipping controls or hiding task-critical content.
- Instructions do not assume a specific device posture, pointer type, or sensory channel.
- Any online setup, support, or packaging extension is accessible before the person needs it.

### AI, Automation, and Personalization

Automated behavior must increase agency, not replace it.

- AI entry points are predictable, optional, and minimizable.
- Prompt scaffolds, examples, templates, or structured fields reduce blank-page effort.
- AI outputs are editable, dismissible, repeatable, and reversible when applied.
- Factual or consequential outputs include a path to verify sources or inspect assumptions.
- The product states relevant limitations, privacy implications, data use, and consent choices in plain language.
- AI does not silently override user decisions, settings, accessibility needs, or privacy preferences.
- Personalization does not trap people in prior behavior; alternate paths and reset controls exist.
- Feedback or correction mechanisms are available for biased, inaccurate, irrelevant, or harmful output.

## Component State Coverage

For every component, test the states that can occur in real use. Do not approve a component that only has a default state.

Minimum state set:

- Resting/default
- Hover, where hover exists
- Focus-visible
- Active/pressed
- Selected/current
- Expanded/collapsed
- Checked/unchecked/mixed, where applicable
- Enabled/disabled
- Read-only/editable, where applicable
- Empty/populated
- Loading/pending
- Optimistic update
- Success/completed
- Warning
- Error/invalid
- Interrupted/resumed
- Offline/retry
- Long text/localized text
- Reduced motion
- High contrast/forced colors
- Zoomed/text-scaled
- Keyboard-only
- Screen-reader path
- Touch/coarse pointer

Additional component checks:

- **Buttons and links:** action versus navigation is correct; focus state is visible; icon-only controls have names; destructive variants include confirmation or undo.
- **Inputs:** label, help text, constraints, errors, autocomplete, input mode, required status, and validation all work together.
- **Search, filters, and sorting:** selected criteria are visible, removable, announced, and preserved when moving through results.
- **Collections, lists, and tables:** headings, grouping, filters, sorting, pagination, empty state, and result counts reduce search burden.
- **Navigation, tabs, and wizards:** current location, order, progress, and return paths are clear.
- **Menus, selects, comboboxes, and autocomplete:** keyboard behavior, active option, selected value, filtering, no-results state, and escape behavior are implemented.
- **Dialogs, drawers, and popovers:** focus movement, dismiss behavior, title, description, background inertness, scroll behavior, and focus return are verified.
- **Toasts and banners:** urgency, duration, dismissibility, action availability, and announcement behavior match the message importance.
- **Cards and tiles:** the clickable area, heading, metadata, status, and nested actions do not conflict.
- **Uploads and downloads:** format limits, progress, failure, retry, cancel, malware/security messaging, and completed state are clear.
- **Media:** captions, transcripts, keyboard controls, volume, pause, speed, fullscreen, and reduced-motion behavior are present as needed.
- **Timers:** pause, extend, hide, save-and-continue, count-up alternatives, and time-free paths are considered.
- **AI assistants:** empty prompt, prompt helper, streaming, stopped generation, regeneration, error, source verification, applied output, undo, feedback, and privacy states are tested.
- **Settings:** default, changed, saved, unsaved, reset, inherited, unavailable, permission-required, and privacy-sensitive states are covered.

## Microinteraction Review

For each critical interaction, answer these questions in the implementation, not only in design notes:

1. Who initiates it: person or system?
2. What visible and programmatic trigger starts it?
3. What feedback appears immediately?
4. What happens if it takes longer than expected?
5. What happens if it fails?
6. What can the person do next?
7. How can they undo, cancel, retry, save, or resume?
8. What must they remember, and how does the interface reduce that memory demand?
9. What competing interruptions could happen at the same time?
10. What state is preserved after navigation, refresh, closing, or disconnection?

## Testing Steps

### 1. Static implementation review

Inspect the built DOM and component code.

- Verify semantic elements before ARIA additions.
- Verify names, roles, states, relationships, and descriptions.
- Check that generated IDs, labels, and error messages remain stable.
- Check that conditional rendering does not remove focus targets unexpectedly.
- Check that hidden content is hidden correctly both visually and programmatically.
- Check that design tokens and CSS do not override browser or OS accessibility settings.

### 2. Keyboard walkthrough

Complete the primary task without a mouse.

- Start from the browser address bar.
- Tab through every interactive element.
- Use Enter, Space, Escape, arrows, Home/End, Page Up/Page Down where the pattern requires them.
- Trigger menus, dialogs, validation, route changes, and dynamic updates.
- Confirm focus never disappears, loops unexpectedly, or lands behind overlays.
- Confirm visible focus is never hidden by sticky headers, scroll containers, or animation.

### 3. Assistive technology smoke test

Run at least one screen-reader path for each high-risk flow.

- Confirm page title and first heading orient the person.
- Confirm controls are announced with name, role, state, and useful context.
- Confirm errors, loading, completion, and important status changes are available.
- Confirm tables, lists, regions, dialogs, tabs, and menus expose their structure.
- Confirm repeated announcements do not create noise.
- Confirm the person can recover after closing a dialog, submitting a form, or navigating back.

### 4. Visual preference test

Test the same task under visual changes.

- Browser zoom at 200% or higher where feasible.
- Increased text size.
- Narrow viewport.
- High contrast or forced colors.
- Dark and light color schemes where supported.
- Images blocked or slow-loading.
- Color-blindness-sensitive status checks: no meaning by color alone.

### 5. Motion, sound, and attention test

Check attention cost deliberately.

- Enable reduced motion and verify animation alternatives.
- Mute sound and verify no information is lost.
- Pause, stop, hide, or disable moving elements.
- Trigger each alert, toast, badge, modal, tooltip, suggestion, and notification.
- Classify each interruption by urgency and confirm it does not exceed the attention it deserves.

### 6. Responsive and physical-context test

Test how the implementation behaves outside the default desktop context.

- Small screen, large screen, and awkward intermediate widths.
- Touch-only operation.
- Coarse pointer and imprecise taps.
- One-handed sequence where relevant.
- Glare-prone or low-contrast context using high brightness and high contrast mode.
- Slow connection and offline interruption during the primary task.

### 7. State injection test

Force states that may be rare but harmful when broken.

- Empty account, no results, too many results, partial data, stale data.
- Long names, long labels, localization expansion, missing optional fields.
- Expired session, permission denied, server error, validation error, upload failure.
- Duplicate submission, rapid repeated click, back button, refresh, deep link, interrupted route transition.
- AI refusal, low-confidence answer, hallucination report, unsafe request, missing source, prompt too long.

### 8. Content and emotional-state review

Read every message as if the person is tired, stressed, confused, or recovering from a mistake.

- Remove blame.
- Put the fix before explanation when the fix is simple.
- Say what happened and what to do next.
- Reduce non-essential detail.
- Keep reassurance factual, not patronizing.
- Confirm success in a way that lets the person stop thinking about the task.

### 9. Human validation

When the risk is material, automated and internal testing are not enough.

- Run the flow with people who experience relevant barriers.
- Include people who rely on assistive technology and people who experience cognitive, focus, learning, recall, communication, emotional, or physical mismatches.
- Observe both delight and pain points.
- Ask what felt controllable, confusing, stressful, respectful, and recoverable.
- Integrate findings before release or document why the risk remains.

## Inclusive QA Prompts

Use these prompts during QA review, standup, design critique, or final handoff.

### Motivation and task fit

- Does this implementation help people complete their goal, or does it mostly serve product instrumentation, promotion, or novelty?
- What is the smallest successful path through the task?
- What steps feel like product chores rather than user goals?
- What happens if the person starts with low confidence or low motivation?

### Learning

- Can a first-time person understand how to start without guessing?
- Can an expert skip guidance and continue quickly?
- Can guidance be revisited later?
- Does help require leaving the flow, switching tabs, or remembering hidden instructions?

### Focus

- What competes for attention on this screen?
- Which interruptions are genuinely urgent?
- Can the person delay, mute, batch, dismiss, or control interruptions?
- Does the implementation preserve focus after async updates, overlays, and route changes?

### Decision-making

- Are choices, tradeoffs, risks, and consequences visible before action?
- Is there one clear next action where the flow expects progress?
- Can the person compare options without holding too much in memory?
- Can they reverse or review high-consequence choices?

### Recall

- Can the person return after interruption and know what they were doing?
- Are breadcrumbs, progress, saved state, recent items, drafts, or summaries available where needed?
- Does the interface require the person to remember values, instructions, or prior steps unnecessarily?
- What happens after refresh, back, close, timeout, or reconnect?

### Communication

- Is the tone clear, respectful, and appropriate to the person’s state?
- Are status messages available visually and programmatically?
- Can people use the product through written, visual, audio, or assistive channels as relevant?
- Does any message imply failure when the person declines a suggestion or cannot act on it?

### Mental health and control

- Does this flow increase or reduce anxiety, self-blame, pressure, or uncertainty?
- Does the person know what information is shared and how to change that choice?
- Are privacy and data settings understandable without legal or technical expertise?
- Does the product celebrate completion without creating pressure for unnecessary continued engagement?

### Permanent, temporary, and situational contexts

- Can the task be completed by someone who cannot see, hear, speak, touch precisely, or sustain attention in the same way as the team?
- What changes in bright light, public space, noisy space, low bandwidth, fatigue, stress, or one-handed use?
- Is there more than one path to the primary outcome?
- Have simulations been used only as a supplement to real participation, not as a replacement?

### AI and automation

- What can the AI change, and what remains under human control?
- Can the person verify, edit, dismiss, undo, or report the output?
- Could the system reinforce stereotypes, narrow discovery, learn from malicious input, or over-personalize?
- Are privacy, consent, and data use visible at the moment they matter?

### Metrics and release learning

- What post-launch signals will show reduced cognitive demand, successful recovery, confidence, and emotional impact?
- Are feedback channels predictable and safe?
- Are feedback questions relevant to the person’s actual usage?
- Who owns fixes when data shows stress, abandonment, repeated errors, or accessibility failure?

## Release Report Template

Use this structure in final build notes:

- **Scope checked:** pages, flows, components, states, browsers, devices, settings, assistive technologies.
- **Primary task result:** pass, partial pass, or fail.
- **Blockers:** issue, affected users/context, evidence, fix, owner.
- **Major risks:** issue, likely mismatch, temporary mitigation, release decision.
- **Component state gaps:** missing or untested states.
- **Preference coverage:** zoom, text scaling, high contrast/forced colors, reduced motion, sound, dark/light, notification settings.
- **Recovery coverage:** errors, undo, retry, save, resume, timeout, offline, back/refresh.
- **AI and data coverage:** consent, privacy, source verification, correction, undo, opt-out.
- **Human validation:** who was included, what was learned, what changed.
- **Post-launch plan:** feedback loop, metrics, monitoring, next iteration.

## Hard Stop List

Do not ship until fixed or formally accepted as a release risk:

- No keyboard path for the primary task.
- Focus is invisible, trapped, lost, or moved without purpose.
- Screen-reader users cannot identify or operate critical controls.
- Errors, loading, completion, or saved state are not perceivable.
- A destructive action can happen without review, undo, or recovery.
- A required form loses entered data after validation, refresh, or navigation.
- Meaning depends only on color, motion, audio, hover, or pointer precision.
- A modal, toast, suggestion, or notification blocks progress without urgency or control.
- Reduced motion, high contrast, zoom, or text scaling breaks the task.
- AI or automation applies consequential changes without human review or undo.
- Privacy or data-sharing choices are hidden, unclear, or unavailable at the point of need.
- The flow cannot recover from slow network, failed request, session timeout, or interruption.
