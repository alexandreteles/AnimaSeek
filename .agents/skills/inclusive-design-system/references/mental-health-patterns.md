# Mental Health Patterns

Use this reference during component design, UX audits, and implementation reviews when a web experience may affect a person’s focus, motivation, confidence, stress level, memory, sense of control, or ability to complete a task. It translates the mental-health framework into component-level rules for collections, color, content, feedback, loading, motion, pop-ups, settings, suggestions, timers, and wayfinding.

This file assumes the main skill has already identified the inclusive-design context. Do not restate broad inclusive-design principles in output unless the user needs rationale. Use this file to make concrete component decisions.

## Mental Health Model For Components

Mental health is fluid. A person may have enough capacity for a task one day and too little capacity for the same task under stress, grief, fatigue, anxiety, sensory overload, time pressure, low confidence, or interruption.

Design components as if the person may be:

- Trying to recover from an error or interruption
- Unsure whether they completed a task correctly
- Worried about privacy, judgment, permanence, or consequences
- Low on focus, memory, motivation, or decision capacity
- Using assistive technology, preference settings, low bandwidth, zoom, or reduced motion
- New to the task, returning after a break, or resuming after context switching

The design goal is not to avoid all negative emotion. The goal is to reduce unnecessary stressors, preserve agency, make next steps legible, and let people adapt the experience without extra effort.

## Preserve, Direct, Customize

Use all three lenses, but do not force all three into every component. Choose the lens that addresses the component’s main risk.

### Preserve

Preserve protects the capacity a person already has. Use it when a component could interrupt, overload, destabilize, shame, confuse, or create uncertainty.

Preserve rules:

- Protect focus by reducing unnecessary interruptions, motion, noise, color salience, and competing actions.
- Protect attention by making only the most important information visually dominant.
- Protect control by providing back, undo, dismiss, pause, extend, save, resume, cancel, and clear settings where relevant.
- Protect trust by explaining privacy, data use, automation, and system limits in plain language.
- Protect motivation by confirming progress, celebrating completion without patronizing, and avoiding blame.
- Protect psychological safety by using inclusive, nonjudgmental language and avoiding messages that imply personal failure.

### Direct

Direct keeps people moving through a task. Use it when a component requires sequencing, decision-making, comprehension, memory, or navigation.

Direct rules:

- Put the next best step where people expect it.
- Use one primary action per moment unless the task truly requires comparison.
- Track progress and show how many steps remain when the task spans multiple steps.
- Surface settings, help, and support inside the flow when they are contextually relevant.
- Provide filters, sorting, search, summaries, and categories where information accumulates.
- Make choices understandable by showing consequences, tradeoffs, and what will happen after selection.
- Use active button text that describes the result, not vague labels like “OK” or “Continue” when a consequence matters.

### Customize

Customize lets people adapt the experience to changing needs and preferences. Use it when a component has variability in timing, intensity, readability, sensory load, privacy, frequency, or workflow.

Customize rules:

- Retain preferences across sessions when appropriate and consented.
- Apply preferences globally where possible; avoid making people reset the same preference in each component.
- Place customization controls in predictable locations and, when useful, also surface them in context.
- Provide options for readability, contrast, color, text size, narration, motion, sound, notifications, sorting, filtering, and privacy.
- Let people opt out of proactive behavior such as pop-ups, suggestions, animations, and time pressure.
- Introduce customization with plain explanations and examples, not unexplained toggles.

## Component Audit Flow

Before prescribing fixes, inspect the component through this sequence:

1. Define the person’s goal and the component’s job in the task.
2. Identify the component trigger: user-initiated, system-initiated, timed, data-triggered, error-triggered, or AI-triggered.
3. Identify the attention demand: full attention, partial attention, or peripheral awareness.
4. Identify the cognitive demands: learning, focus, decision-making, recall, and communication.
5. Identify the emotional risk: anxiety, overwhelm, self-blame, uncertainty, loss of control, embarrassment, sensory overload, or avoidance.
6. Check control paths: dismiss, undo, back, pause, extend, save, resume, minimize, mute, hide, opt out, and reset.
7. Check support paths: help text, examples, guidance, progress, confirmation, recovery, and contact/support escalation.
8. Check personalization: whether preferences are discoverable, understandable, reversible, and retained.
9. Check assistive-technology behavior only where it affects the component’s mental-health behavior, such as live-region noise, unexpected focus movement, or motion preferences.
10. Define how the team will know whether the component reduces stress and increases confidence, not only whether it increases engagement.

## Attention And Interruption Rules

Every pop-up, toast, badge, sound, animation, suggestion, tooltip, modal, progress change, chat prompt, or status update has a mental cost.

Use this attention scale:

- Full attention: urgent, high-consequence, time-sensitive, safety/security-related, or necessary to prevent data loss. Use sparingly.
- Partial attention: relevant to the current task but deferrable. Keep it visible without blocking completion.
- Peripheral awareness: useful but not urgent. Use inline status, quiet badges, summaries, or a notification center rather than interruption.

For any system-initiated communication, answer:

- What happens if the person misses it?
- What happens if the system interrupts at the wrong time?
- Can the message wait, batch, summarize, or move to a lower-intensity channel?
- Does the delivery mode match the urgency?
- Can the person control timing, frequency, channel, and override behavior?
- Is the system adapting to context without making opaque assumptions?

## Component Rules

### Collections

Use for inboxes, feeds, search results, files, dashboards, message threads, media libraries, tables, logs, notifications, and any place where information accumulates.

Preserve:

- Reduce visible density with grouping, expandable sections, summaries, and progressive disclosure.
- Keep only the most relevant items or actions prominent; move secondary metadata to lower hierarchy.
- Avoid forcing long scrolling or visual scanning without anchors.
- Provide calm empty states and no-results states that explain what happened and what to try next.
- Avoid infinite feeds when the primary task is completion, review, or recovery.

Direct:

- Provide search, sorting, filtering, and clear category headers.
- Highlight search terms and show result counts.
- Use stable item layouts so people can compare without re-learning each row/card.
- Support bulk actions with review and undo where consequences matter.
- Show applied filters as removable chips or visible controls; include “clear all.”
- Use headings, table captions, and summaries so assistive-technology users can build the same mental map.

Customize:

- Let people save frequent queries, views, filters, sort order, column visibility, and grouping preferences.
- Offer density, text size, contrast, color, and narration options when content is heavy.
- Let people hide non-core panels, columns, notifications, or categories.
- Remember the last useful state when returning, unless privacy or safety requires a reset.

Audit flags:

- The person must remember what filter they used.
- Search results change without explanation.
- The only way to reduce overload is to leave the page.
- Important items are indicated only by color, motion, or position.

### Color

Use for palettes, badges, status, alerts, charts, hierarchy, tags, backgrounds, buttons, and interaction states.

Preserve:

- Limit red or red-like colors to urgent alerts, destructive actions, and errors that need attention.
- Avoid bright, saturated, blinking, or highly contrasting accents for low-urgency states.
- Use a balanced palette that supports the intended emotional state without overwhelming the surface.
- Prefer calm neutrals and controlled accent use for task-heavy experiences.
- Do not use color as the only signal for status, priority, error, selection, or progress.

Direct:

- Use color consistently to identify primary action, status, grouping, and navigation location.
- Pair color with text, iconography, shape, placement, or pattern.
- Keep badges and counters visually quieter unless immediate action is required.
- Make chart color meanings visible through labels, legends, and direct annotation.

Customize:

- Support dark mode, high contrast, forced colors, and accessible contrast settings.
- Provide appearance preferences where color sensitivity or emotional intensity is a known risk.
- Do not assume “calming” colors are accessible; verify contrast and legibility.
- Let users reduce notification color intensity if badges or alerts are distracting.

Audit flags:

- Red badges are used for routine counts.
- A color system rewards urgency even when nothing is urgent.
- The palette looks calm but fails text contrast.
- Status cannot be understood in grayscale, high contrast, or forced-colors mode.

### Content

Use for product copy, labels, instructions, alerts, button text, helper text, onboarding, descriptions, empty states, error messages, privacy explanations, and microcopy.

Preserve:

- Write for the person’s likely frame of mind: confused, worried, hurried, frustrated, learning, deciding, or recovering.
- Use inclusive language that protects dignity and avoids blame.
- Remove unnecessary detail from the main path; put deeper information behind clear disclosure.
- Test whether the language matches the audience’s mental model, not the team’s internal vocabulary.
- Avoid shame-coded phrases such as “you did this wrong,” “invalid,” “failed,” or “not allowed” without a constructive next step.

Direct:

- Use descriptive headings, short paragraphs, bullets, categories, and step labels.
- Put the most important action or consequence first.
- Give every interaction a clear next step or clear endpoint.
- Use button text that says what happens: “Save draft,” “Send report,” “Restore file,” “Cancel subscription.”
- Explain high-consequence choices before the action, not only after an error.

Customize:

- Support text resizing, zoom, contrast preferences, translation/localization, and voice narration when relevant.
- Let people choose concise or detailed guidance where the task stakes vary.
- Provide examples, templates, or sample inputs to reduce blank-page anxiety.
- Allow people to revisit guidance after closing it.

Audit flags:

- The page contains long undifferentiated paragraphs.
- Labels require domain knowledge or internal team language.
- The user must infer what happens after clicking a button.
- Instructions disappear and cannot be reopened.

### Feedback, Confirmations, And Error States

Use for action results, success messages, error states, validation, surveys, ratings, feedback forms, inline status, and post-change reactions.

Preserve:

- Confirm important completions explicitly so people do not need to double-check.
- Use consistent placement, patterns, and colors for success, warning, and error states.
- Celebrate completion and progress in proportion to the task; avoid exaggerated praise.
- Never blame the person for an error.
- Let people choose how much personal information to share when giving feedback.
- When a product changes, give people a way to react and revert where feasible.

Direct:

- Put the solution first when the fix is simple.
- Provide a best next step for every error.
- Test alternative, unexpected, and edge-case paths to avoid dead ends.
- Put feature-level feedback close to the feature being tested and global feedback in a predictable place.
- Scope survey questions to actual usage so feedback does not become a second task.
- Ask how the product made the person feel, not only whether a feature worked.

Customize:

- Match message tone to context: cancellation, completion, failure, privacy, and account changes require different emotional handling.
- Make messages specific when data is available: name the affected item, state, or action.
- Offer open-ended feedback, not only ratings.
- Use feedback to create or refine customization options.

Audit flags:

- Success is implied only by the absence of an error.
- Error copy explains the system but not what to do.
- The survey interrupts completion and asks irrelevant questions.
- Feedback collection feels like surveillance.

### Loading States

Use for progress bars, spinners, skeleton screens, shimmers, async actions, file processing, route transitions, AI generation, uploads, downloads, and dashboards.

Preserve:

- Show what is loading and why it may take time.
- Show estimated wait time or progress when possible.
- Use efficient code and bandwidth-aware loading to reduce wait time.
- Keep patterns and colors consistent across loading states.
- Avoid indefinite spinners as the only signal.
- Preserve entered data and current task state during loading.

Direct:

- Use waiting time to orient, not distract: explain the next step, preparation cue, or fallback.
- Distinguish between “working,” “queued,” “failed,” “timed out,” and “complete.”
- Provide retry, cancel, continue in background, or save-and-return options where possible.
- For deterministic progress, expose progress semantics; for indeterminate work, use a status message that changes only when useful.

Customize:

- Respect reduced-motion preferences.
- Allow people to pause or disable loading animations where feasible.
- Let people choose lower-motion or lower-bandwidth display modes for repeated heavy views.

Audit flags:

- People cannot tell whether the system is working.
- The loader has motion with no reduced-motion alternative.
- A network delay risks data loss.
- The component gives no recovery option after failure.

### Motion And Noise

Use for animation, video, sound, notification audio, typing indicators, transitions, hover effects, progress animation, ambient motion, and haptic equivalents.

Preserve:

- Use motion and sound sparingly; remove anything that interrupts task flow without clarifying the task.
- Avoid autoplay for video, audio, gifs, and looping decorative motion.
- Avoid repeated attention-grabbing movement near task-critical content.
- Avoid motion that implies urgency unless urgency is real.

Direct:

- Use animation only when it improves orientation, causality, spatial continuity, or learnability.
- Keep instructional animation brief and controllable.
- Provide pause, stop, hide, mute, replay, captions, transcripts, and descriptions as appropriate.
- Make the purpose of motion clear; decorative motion should be quiet or removable.

Customize:

- Respect operating-system and browser preferences for reduced motion and sound where available.
- Apply motion and sound preferences across the product, not component by component.
- Let people opt out of typing indicators, animated reactions, ambient video, and notification sounds where feasible.

Audit flags:

- Motion is used to make the interface feel alive but not to explain anything.
- A moving element sits beside reading, form entry, or decision-making content.
- The only way to stop sound or motion is to leave the page.
- Reduced-motion mode still uses large movement or looping animation.

### Pop-ups

Use for modals, toasts, callouts, tooltips, teaching UI, notifications, banners, interstitials, and any unexpected UI element that appears over or near the task.

Preserve:

- Use pop-ups only when information is relevant, actionable, and worth the interruption.
- Time pop-ups based on behavior and context when possible; avoid appearing during typing, reading, selecting, or high-stakes steps.
- Avoid stacking multiple pop-ups or mixing marketing, help, and urgent alerts in the same channel.
- Make dismissal clear, persistent enough to read, and non-punitive.
- Do not use pop-ups to force low-value engagement.

Direct:

- Label urgency and consequence.
- Put available actions or next steps directly inside the pop-up.
- Use modal behavior only when the task must stop; otherwise prefer inline, nonblocking, or peripheral presentation.
- Manage focus predictably for modal dialogs and return focus after close.
- For toasts, provide a persistent place to review missed messages if the information matters.

Customize:

- Let people temporarily disable pop-ups.
- Let people control which pop-ups can override other settings.
- Provide frequency, timing, category, and channel controls.
- Allow notification-free periods and breakthrough rules for urgent messages.

Audit flags:

- The pop-up appears before the person expresses intent.
- The pop-up does not state whether action is urgent.
- Closing the pop-up loses important information.
- A single pop-up can derail the whole flow.

### Settings

Use for preferences, privacy, notifications, appearance, accessibility, AI behavior, account controls, feature-level customization, and system-wide defaults.

Preserve:

- Preserve selected preferences across the experience and across sessions where appropriate.
- Apply global preferences wherever possible.
- Make privacy, data collection, and sharing controls clear enough to support psychological safety.
- Let people control timing and frequency of distractions.
- Avoid resetting preferences after updates unless required, and explain any reset.

Direct:

- Provide contextual settings entry points near the thing being adjusted.
- Also maintain a predictable centralized settings location.
- Explain each setting in plain language, including what changes and what does not.
- Organize settings around the audience’s mental model, not internal architecture.
- Use card sorting or co-design to validate grouping for complex settings.

Customize:

- Provide onboarding or setup wizards for important customization without making the wizard mandatory.
- Let people search settings and discover related controls.
- Offer reset-to-default and undo for preference changes.
- Consider guided recommendations when people say what they struggle with, but do not hide the underlying controls.

Audit flags:

- People have to change the same preference in multiple places.
- Setting labels are abstract and cannot be understood without trying them.
- Privacy controls are hidden behind account or legal language.
- A setting exists but is not available at the moment of frustration.

### Suggestions

Use for recommendations, predicted actions, proactive prompts, AI suggestions, next-best actions, smart replies, templates, nudges, and contextual tips.

Preserve:

- Offer suggestions only when confidence and relevance are high.
- Prefer suggestions after expressed intent rather than before intent.
- Anticipate emotional state; avoid language that implies the person is failing if they ignore the suggestion.
- Provide feedback mechanisms such as useful, not useful, don’t show again, or adjust frequency.
- Do not use suggestions to create unnecessary tasks.

Direct:

- Include enough explanation to evaluate the suggestion without extra clickthrough.
- Make the suggestion immediately actionable.
- Show why the suggestion appears when the reason affects trust or privacy.
- Let the person apply, edit, defer, dismiss, or learn more.
- Avoid endless refinement loops; include task-closing actions.

Customize:

- Let people turn off suggestions or set limits by type, frequency, context, and channel.
- Adapt from explicit feedback, not only inferred behavior.
- Let people reset personalization when suggestions become stale or stressful.
- Respect quiet/focus modes and notification settings.

Audit flags:

- Suggestions appear because the system can predict something, not because the person needs it.
- A declined suggestion returns repeatedly.
- The suggestion requires more work than the original task.
- The copy makes the user feel behind, inefficient, or at fault.

### Timers

Use for session timeouts, countdowns, quizzes, productivity sessions, security windows, limited offers, progress timing, scheduled breaks, and any component that monitors or visualizes time.

Preserve:

- Provide extend, pause, save-and-continue, and resume controls where possible.
- Make timers visible but not visually aggressive.
- Use color carefully; avoid red escalation unless a real urgent consequence is approaching.
- Consider count-up timers or elapsed-time displays when countdowns create stress.
- Preserve entered work before time expires.

Direct:

- Explain what happens when time ends.
- Warn before session expiration and provide a clear recovery path.
- Include a pause button when the task is not inherently time-critical.
- Separate true security needs from artificial urgency.
- Provide time estimates for task planning without making them feel like performance judgments.

Customize:

- Let people show, hide, resize, or change timer visualization.
- Offer less anxiety-inducing metaphors or neutral progress forms.
- Provide time-constraint-free options whenever possible.
- Allow longer time limits where policy and safety allow.

Audit flags:

- A timer expires work without warning or recovery.
- The timer uses red or pulsing animation throughout the flow.
- Time pressure is used to increase conversion rather than support the user.
- The person cannot pause for interruption, access needs, or real-life demands.

### Wayfinding

Use for navigation, tabs, breadcrumbs, wizards, empty states, onboarding paths, multi-step forms, sidebars, IA, page titles, route transitions, and return paths.

Preserve:

- Align visual hierarchy to the person’s motivation and current intent.
- Streamline the page by removing nonessential elements from the primary path.
- Provide progress tracking when a flow has multiple steps.
- Provide back and undo wherever relevant.
- Help memory by visualizing the path taken and supporting return to previous steps.

Direct:

- Make available options explicit.
- In a flow, indicate the best next step with one clear primary action.
- Use a consistent framework for navigation and core actions.
- Break long processes into named steps.
- Use empty states to orient: what this area is for, why it is empty, and what to do next.
- Provide a predictable re-entry point for onboarding, first-run experiences, and help that people dismissed earlier.

Customize:

- Let people hide or reveal non-core features to reduce information overload.
- Support compact and expanded navigation modes.
- Remember where people left off when resuming a task.
- Let people personalize saved views or shortcuts without removing canonical navigation.

Audit flags:

- People cannot tell where they are, what changed, or how to return.
- The flow has no visible step count or completion state.
- There are multiple equally prominent primary actions.
- Closing guidance makes it permanently inaccessible.

## Severity Model For Audits

Use this model when reporting issues:

- Critical: The component can cause data loss, privacy loss, irreversible action, panic, lockout, or inability to complete a core task.
- High: The component creates substantial cognitive or emotional burden, interrupts focus, hides recovery, or blocks people with common access needs.
- Medium: The component is usable but creates avoidable stress, uncertainty, double-checking, or extra steps.
- Low: The component could better support confidence, clarity, customization, or consistency.

For each issue, include:

- Component and state
- Preserve/Direct/Customize lens
- Mental-health risk
- Concrete fix
- Implementation note if relevant
- Success signal to measure after release

## Output Pattern For Component Audits

When using this reference, structure audit output as:

1. Component summary: what the component is trying to help the person do.
2. Main risk: focus loss, overload, uncertainty, lack of control, privacy anxiety, sensory stress, or recovery failure.
3. Findings by severity.
4. Fixes grouped by Preserve, Direct, and Customize.
5. State coverage: default, loading, empty, success, error, interrupted, resumed, reduced-motion, high-contrast, and low-bandwidth where relevant.
6. Metrics: task completion, recovery rate, time-to-confidence, reduced double-checking, lower abandonment, feedback sentiment, preference usage, and user-reported control.

## Preflight Checklist

Before finalizing a component, verify:

- The component does not demand unnecessary attention.
- Urgency, consequence, and next action are clear.
- The person can recover from mistakes, interruptions, waiting, and timeouts.
- Success and completion are explicit.
- The component does not rely on memory, color, motion, sound, speed, or perfect confidence.
- Customization is discoverable, understandable, reversible, and retained.
- Privacy and data-sharing behavior are visible enough to support trust.
- Feedback can be given safely, and the team has a plan to act on it.
- Evaluation includes how people feel, not only what they click.
