# Focus and Interruptions

Use this reference when designing, auditing, or implementing system-initiated communication: notifications, badges, toasts, banners, pop-ups, modals, chat messages, typing indicators, reminders, timers, loading states, teaching UX, AI suggestions, proactive recommendations, status updates, and cross-app alerts.

The purpose is to help useful information reach people at the lowest reasonable mental cost. A communication pattern is inclusive only when it respects attention, emotional state, context, control, and the person’s ability to recover after being interrupted.

## Core model

An interruption is any system behavior that asks for attention the person did not just explicitly give. It can be loud or subtle: a modal, a red badge, a toast, a vibration, a sound, a typing animation, a flashing cursor, a calendar reminder, a browser push message, or a recommendation that appears in the middle of a task.

Treat attention as a limited access need, not as a free product resource. The design problem is not “How do we make people notice this?” The design problem is “What is the least disruptive way to provide the right information at the right time, with enough control to preserve focus?”

A valid interruption must satisfy five conditions:

1. It is relevant to the person’s current or clearly anticipated goal.
2. Its urgency is accurate and visible.
3. Its modality matches the attention required.
4. It provides a clear next step, including dismiss, defer, or opt out where appropriate.
5. Its mental cost is lower than the cost of missing the information.

Do not interrupt for decorative motion, generic education, marketing, vanity engagement, speculative recommendations, or “maybe useful” information.

## Attention need spectrum

People differ in how much contact from a system supports them. The same person may also need different behavior on different days or in different contexts.

Use this spectrum when selecting defaults and customization options:

| Need state | What the person may need | Product behavior |
| --- | --- | --- |
| Isolated | Minimal stimuli, quiet workspace, no clutter, few or no alerts | Silent defaults, batching, focus mode, no motion by default, strong pause/mute controls |
| Informed but in control | Alerts are useful, but timing, form, and frequency must be adjustable | Priority settings, category controls, per-alert actions, digest options, predictable notification center |
| Neutral | No strong preference for timing, style, or mode | Respectful defaults, easy discovery of controls, no assumption that interruptions are harmless |
| High-support | Frequent structure or reminders help maintain momentum | Opt-in reminders, visible but non-intrusive timers, preparation cues, recurring check-ins, easy snooze |

Never equate a diagnosis with a need state. Recruit and design around focus behavior, interruption recovery, sensory sensitivity, task switching, selective attention, and context.

## Communication inventory

Inventory every communication channel before adding a new one.

Visual channels: inline messages, banners, badges, icons, color changes, loading indicators, hover cards, callouts, modals, pop-ups, notification centers, progress indicators, unread states, animation, and layout shifts.

Auditory channels: pings, spoken messages, alarms, media playback, typing sounds, countdown sounds, and screen-reader announcements.

Tactile channels: vibration, haptics, controller feedback, adaptive device feedback, and hardware indicators.

Temporal channels: countdowns, timers, snooze cycles, quiet hours, scheduled reminders, delayed delivery, digest cadence, and escalation windows.

Social channels: chat mentions, read receipts, typing indicators, presence, availability, comments, collaborative cursors, assignment notifications, and meeting nudges.

AI or automation channels: proactive suggestions, generated summaries, recommended actions, next-best actions, smart replies, ranking, prioritization, and automated follow-ups.

For each channel, document trigger, urgency, expected action, duration, persistence, fallback, user controls, assistive technology behavior, and privacy implications.

## Mental-cost framework

Mental cost is the total effort required to notice, interpret, decide, act, and recover from a communication.

Assess every interruption across these dimensions:

| Cost dimension | What to inspect |
| --- | --- |
| Attention capture | Does it pull full attention, partial attention, or peripheral awareness? |
| Task switching | Does it move the person away from their primary task, page, tab, app, or train of thought? |
| Recovery | How hard is it to resume after the message appears or disappears? |
| Memory | Does the person need to remember prior context, future steps, or hidden information? |
| Decision load | Does it require comparison, risk evaluation, or consequence prediction? |
| Emotional load | Could it cause anxiety, self-blame, urgency pressure, confusion, or loss of control? |
| Sensory load | Does it use color, sound, vibration, flashing, motion, or density in ways that can overwhelm? |
| Physical load | Does it require precision, repeated action, quick response, or an interaction mode the person may not use? |
| Privacy and trust | Does it reveal sensitive content, use hidden inference, or require unclear data sharing? |
| Coordination load | Does it create social pressure, meeting pressure, or cross-app boundary issues? |

Use this cost banding:

| Cost band | Description | Allowed use |
| --- | --- | --- |
| 0: Ambient | No action, no focus shift; visible only in context | Background status, subtle inline state, passive availability |
| 1: Peripheral | Noticeable but does not interrupt the task | Low-priority badge, notification center item, muted inline hint |
| 2: Partial | Requires recognition and optional action, but no navigation break | Toast with dismiss/action, banner in current flow, task-relevant suggestion |
| 3: Focus shift | Requires decision or temporary task switch | Time-sensitive reminder, blocking validation before submit, important update |
| 4: Full interruption | Captures focus and requires immediate response | Security, safety, data loss, destructive action prevention, legal or critical account issue |

A message may use a higher band only when the cost of missing it exceeds the interruption cost. If that cannot be demonstrated, reduce the band.

## Urgency and attention matrix

Urgency is not visual intensity. Urgency is the consequence of delay.

| Urgency | Consequence of missing it | Attention level | Typical pattern | Rules |
| --- | --- | --- | --- | --- |
| Critical | Safety, security, account access, active data loss, irreversible harm | Full attention | Modal, assertive alert, persistent banner, urgent push | Must be rare, specific, actionable, and recover focus after resolution |
| Time-sensitive | Delay creates meaningful loss or missed commitment | Partial to full, depending on user settings | Reminder, toast, notification center plus optional breakthrough | Respect focus settings unless breakthrough is user-authorized |
| Task-relevant | Helps complete the current task or recover from friction | Partial or peripheral | Inline hint, contextual toast, suggestion card | Show near the task, after intent, or after relevant inactivity |
| Informational | Useful but safe to miss temporarily | Peripheral | Badge, digest, notification center, status row | Do not use sound, vibration, modal, or aggressive color by default |
| Educational | Introduces a feature, concept, or optimization | Peripheral and permissioned | Teaching UX, first-run card, expandable help | Must be skippable, revisitable, and not required for basic completion |
| Promotional | Serves product engagement more than user goal | None during task | Dedicated surface, settings, newsletter, optional content area | Never interrupt primary work |

When a team requests a high-urgency treatment, ask what harm occurs if the person sees the message later in a digest. If the answer is weak, downgrade the urgency.

## Modality selection

Choose modality by attention need, not by what the system can emit.

Full-attention communication may use a modal, assertive live region, sound, haptic, or focus movement only when the message is critical and immediate. The content must be short, explicit, and action-oriented. Restore focus after the action.

Partial-attention communication should usually use a toast, inline banner, non-blocking reminder, or notification center item. It should not steal keyboard focus. It should include direct actions where useful.

Peripheral communication should stay in the background: muted badges, subtle status changes, notification center entries, summaries, or inline labels. Peripheral does not mean invisible; it means available without demanding full attention.

Avoid over-signaling. Do not combine red color, animation, sound, vibration, modal behavior, and a badge for low-urgency content. Multimodal intensity should be reserved for messages where missing the information creates serious harm.

Do not rely on one sensory channel. Pair color with text, shape, icon, position, or label. Pair sound with visual state. Pair haptics with visible confirmation. Respect user and operating-system preferences for reduced motion, high contrast, sound, notification delivery, and focus mode.

## Timing framework

Interruption timing should be based on task state, context, and consent.

Prefer these moments:

- After a person explicitly asks, searches, selects, or expresses intent.
- After a task completes, when confirmation or next step guidance is expected.
- During a natural transition: page change, completed section, saved draft, meeting end, upload finished, or break between steps.
- After a relevant period of inactivity, when help might restart momentum.
- Before a scheduled focus session, as a preparation cue tied to a specific task.
- After a focus session, as a digest or recovery summary.
- When the person returns to the relevant surface and can act without context switching.

Avoid these moments:

- While the person is typing, reading, composing, presenting, coding, gaming, drawing, completing a form, or making a high-stakes decision.
- During a focus session unless the message meets user-defined breakthrough rules.
- Immediately after an error unless the message provides direct recovery.
- During fast multi-step flows where memory and orientation are already loaded.
- On first page load before the person understands where they are.
- Repeatedly after dismiss, mute, snooze, or “not now.”

Timing rules:

- Batch low-priority messages.
- Summarize repeated events instead of stacking them.
- Apply cool-downs after dismissals.
- Escalate only when the consequence of waiting increases.
- Persist important information in a history or notification center so people do not have to catch it instantly.
- Do not make ephemeral messages carry critical content unless the same content is available elsewhere.

## Adaptation framework

Adaptation means the system learns how to communicate with less mental cost. It does not mean the system silently infers personal conditions or manipulates behavior.

Appropriate adaptation signals include:

- The person’s explicit notification settings.
- Focus mode, quiet hours, mute state, or availability state.
- Repeated dismiss, snooze, disable, or “less like this” actions.
- Repeated use of a suggestion, reminder, or digest.
- Current task stage, such as composing, reviewing, submitting, waiting, or recovering from error.
- Device, viewport, input method, bandwidth, and assistive technology constraints where available without invasive inference.
- Calendar or meeting state when the person has consented to use that information.

Do not adapt based on hidden assumptions about disability, mental health, productivity, competence, or motivation. Do not infer “this person has ADHD” or “this person is anxious.” Adapt to observable and consented preferences such as “typing indicators off,” “badges muted,” “digest at 4 PM,” or “only urgent messages during focus mode.”

Adaptation must be legible. People should understand why a suggestion appeared, how to change its behavior, and how to reset the pattern.

## Personalization requirements

Personalization is an accessibility and mental-health support, not an optional advanced setting.

Minimum controls for interruption-heavy products:

- Turn all non-critical notifications off.
- Control notification categories by source, topic, project, channel, or workflow.
- Choose channel: in-app, push, email, chat, sound, haptic, digest, or none.
- Choose timing: immediate, scheduled, batched, quiet hours, focus mode, snooze, pause until date/time.
- Define breakthrough rules for urgent people, channels, topics, or incident types.
- Adjust frequency: every event, summary, daily digest, weekly digest, or only when mentioned.
- Hide or show badges and unread counts.
- Reduce visual intensity, color intensity, motion, and sound.
- Turn off typing indicators, read receipts, animated presence, and other social-pressure cues where feasible.
- Preview notification behavior before saving settings.
- Reset to defaults and export or transfer preferences where appropriate.

Controls must be available in two places: a predictable settings area and directly from the communication itself. Every non-critical notification should support some version of mute, snooze, turn off this type, reduce frequency, or open settings.

Settings copy must explain the practical result: “Send a digest at 4 PM” is better than “Enable summary mode.”

## Pattern playbook

### Alerts

Use alerts for information the person must notice to continue safely or successfully.

Do:

- State what happened, why it matters, and what to do next.
- Use priority levels that are visible in text, not only color.
- Keep critical alerts persistent until resolved or intentionally dismissed.
- Provide a non-destructive escape where possible.
- Restore focus after resolution.

Do not:

- Use alert styling for routine updates.
- Combine urgent color with vague copy.
- Hide the action in a menu.
- Trigger multiple alerts for the same underlying issue.

### Badges and unread counts

Badges are persistent attention debt. Use them sparingly.

Do:

- Show badges only when there is a meaningful action or status behind them.
- Provide accessible text such as “3 unread comments” or “1 urgent approval.”
- Let people mute counts by source or category.
- Consider capped counts, grouped counts, or “new” labels instead of precise numbers when precision increases pressure without benefit.
- Clear badges predictably when the underlying item is handled.

Do not:

- Use red badges for low-priority content.
- Use badges to manufacture urgency.
- Leave badges visible after the person has dismissed or reviewed the item.
- Rely on color alone to distinguish urgency.

### Toasts

Toasts are for lightweight, time-bound feedback or optional next steps.

Do:

- Keep toasts short and specific.
- Include the most likely action directly in the toast.
- Include dismiss where the toast persists long enough to be annoying.
- Keep a notification history for missed toasts when the content matters.
- Use polite assistive technology announcements for non-critical toasts.

Do not:

- Move keyboard focus to a toast unless the situation is critical.
- Stack several toasts in a way that covers work.
- Put essential instructions only in a toast.
- Auto-dismiss before screen reader users or slower readers can act.

### Modals and pop-ups

Modals are high-cost. Use them when the person must make a decision before continuing or when failing to decide has serious consequences.

Do:

- Use modals for destructive confirmation, security, privacy, irreversible action, or necessary task branching.
- Make urgency and consequence explicit.
- Provide clear primary and secondary actions.
- Trap focus correctly and return focus after close.
- Allow temporary disabling of non-critical pop-ups.

Do not:

- Use pop-ups for generic feature promotion.
- Open pop-ups before the person has engaged with the page.
- Ask for a decision without showing the consequence.
- Trigger a pop-up repeatedly after dismissal.

### Chat and collaboration

Collaboration tools often create social and cognitive pressure. Design chat interruptions to support boundaries.

Do:

- Separate mentions, direct messages, channel activity, reactions, and system messages by priority.
- Let people mute channels, threads, projects, or time windows.
- Provide digests for busy channels.
- Respect focus mode and availability state across the product ecosystem.
- Allow urgent breakthrough only through explicit rules.
- Let people disable or reduce typing indicators, read receipts, animated cursors, and presence effects where feasible.
- Provide send later, remind me, save for later, and follow-up actions.

Do not:

- Treat every chat message as equally urgent.
- Use motion-heavy typing indicators when the person is in another task.
- Shame people for delayed response.
- Reveal sensitive message content in notifications by default.

### Reminders

Reminders support recall and momentum when they are tied to intent.

Do:

- Link reminders to the original task, document, decision, or meeting.
- Provide snooze, reschedule, skip once, pause series, and complete actions.
- Offer preparation cues before deep work: task, materials, time, and expected outcome.
- Offer recovery cues after interruption: where the person left off, last edited item, next step.
- Let people set recurring reminders at a cadence they choose.

Do not:

- Trigger reminders without context.
- Require immediate action if the reminder can be safely deferred.
- Use reminders as disguised promotional nudges.

### Proactive suggestions and AI nudges

Suggestions should appear when they reduce effort, not when they create pressure to optimize.

Do:

- Trigger suggestions after expressed intent or observable friction, not randomly.
- Explain why the suggestion appeared when data or automation is involved.
- Make the suggestion immediately actionable.
- Offer “not now,” “less like this,” “don’t suggest this,” and feedback.
- Adapt language and frequency to preserve motivation.
- Include completion-oriented actions such as “apply,” “save,” “finish,” “send,” or “schedule.”
- Preserve discovery by allowing alternatives, not only the predicted next action.

Do not:

- Suggest actions that imply the person is failing or missing out.
- Keep recommending what the person already dismissed.
- Over-personalize based only on past behavior or popular behavior.
- Hide privacy, data, or automation assumptions.
- Create endless refinement loops.

### Teaching UX and new-feature prompts

Learning support is useful only when it respects readiness.

Do:

- Show brief top-level information first.
- Place details in expandable help, info bubbles, examples, demos, or re-entry points.
- Let people skip, revisit, and complete onboarding later.
- Trigger guidance after interaction or clear intent when possible.
- Support guided, semi-structured, and trial-and-error learners.

Do not:

- Force first-run education before the person has context.
- Make a dismissed tutorial impossible to find later.
- Assume all users want to learn through experimentation.

### Timers

Timers can structure attention or amplify anxiety.

Do:

- Provide pause, extend, save-and-continue, and hide/show options.
- Use visible but non-intrusive timers.
- Consider count-up or progress metaphors when countdown pressure is unnecessary.
- Offer time-constraint-free alternatives when possible.
- Avoid red escalation as the only or default signal.

Do not:

- Use countdowns for pressure when no real deadline exists.
- End sessions without a recovery path.
- Animate timers in ways that continuously steal attention.

### Loading and waiting states

Waiting demands focus and memory.

Do:

- Explain what is loading and why.
- Show estimated wait time or progress where possible.
- Provide useful preparation text for the next step.
- Minimize wait time through efficient implementation.
- Allow people to pause or turn off loading animation where feasible.

Do not:

- Use a spinner alone for long waits.
- Leave people wondering whether the system is working.
- Use distracting shimmers or loops that conflict with reduced-motion preferences.

### Motion, sound, and haptics

Motion and sound are attention capture tools. Use them with restraint.

Do:

- Use motion only when it clarifies change, relationship, cause, or completion.
- Provide pause, stop, hide, mute, and reduce options.
- Apply motion and sound preferences across the product, not only one component.
- Use haptics only when they add meaningful signal and can be disabled.

Do not:

- Use decorative motion in task-heavy surfaces.
- Auto-play sound or video.
- Use blinking, bouncing, or repeated movement as ambient decoration.

## Microinteraction specification

For any alert or suggestion, define the sequence before designing visuals.

Use this template:

- Initiator: user, system, collaborator, automation, or external event.
- Trigger: the exact condition that creates the communication.
- Evidence: why the system believes this communication is needed now.
- Urgency: critical, time-sensitive, task-relevant, informational, educational, or promotional.
- Attention: ambient, peripheral, partial, focus shift, or full interruption.
- Medium: inline, badge, toast, banner, modal, chat, push, email, sound, haptic, digest, or notification center.
- Timing: immediate, delayed, batched, scheduled, idle, transition point, or after expressed intent.
- Copy: what happened, why it matters, and the next step.
- Actions: primary action, secondary action, dismiss, defer, opt out, settings.
- Persistence: ephemeral, retained in history, persistent until resolved, or repeated at chosen cadence.
- Recovery: how the person returns to the original task.
- Personalization: what can be controlled and where.
- Accessibility behavior: focus, keyboard, live region, screen reader copy, reduced motion, contrast, touch target, zoom.
- Privacy: what data is used, what is revealed, and what consent applies.
- Failure mode: what happens if the message is missed, duplicated, delayed, wrong, or dismissed.

If the team cannot fill in trigger, urgency, cost, action, and recovery, the interruption is not ready to ship.

## Web implementation guardrails

Keyboard and focus:

- Never move focus for non-critical toasts, badges, or suggestions.
- Move focus only for blocking dialogs or critical flows where the person must act.
- Return focus to the invoking element or logical next place after a modal closes.
- Ensure dismiss, snooze, action, and settings controls are keyboard reachable.
- Provide Escape behavior for dismissible overlays.

Screen readers and live regions:

- Use `role="status"` or `aria-live="polite"` for non-critical status updates.
- Use `role="alert"` or assertive announcements only for urgent, time-sensitive, or blocking information.
- Do not repeatedly announce counters, timers, typing indicators, or animated changes unless the person opted in or the change is material.
- Give badges meaningful names, not just numbers.
- Ensure notification history is accessible by heading, landmark, and keyboard.

Visual accessibility:

- Do not rely on color alone for priority.
- Avoid red for non-critical states.
- Maintain contrast for text, icons, badges, and status indicators.
- Respect high contrast, forced colors, text scaling, and zoom.
- Keep target sizes and spacing usable for touch and imprecise input.

Motion and media:

- Respect `prefers-reduced-motion`.
- Avoid autoplay.
- Provide pause, stop, hide, captions, transcripts, and descriptions where relevant.
- Avoid flashing or rapid repeated animation.

Push permissions:

- Do not ask for browser notification permission on first page load.
- Explain the value, categories, frequency, and controls before requesting permission.
- Provide an in-app alternative to browser push.
- Make revocation and category control easy to find.

State and persistence:

- Save drafts before interruptions where feasible.
- Retain critical messages in a notification center or activity log.
- Prevent duplicate alerts for the same event.
- Group related messages.
- Preserve the person’s scroll position, form state, and work context after interruption.

## Cross-app and ecosystem behavior

Focus is often broken by systems that do not coordinate. If the product participates in an ecosystem, define cross-surface rules.

Required cross-surface behaviors:

- A focus session in one surface should suppress or downgrade non-urgent alerts in related surfaces.
- Breakthrough messages should follow user-defined allow-lists or categories.
- Delayed notifications should summarize what was held back and why.
- Reminders should include enough context to resume work without searching.
- Preferences should sync when appropriate and consented.
- A dismissed notification should not immediately reappear in another channel.

For multi-device use, support different needs by device. A phone, desktop, wearable, adaptive controller, screen reader, and shared display may each require different delivery modes.

## Research and co-design prompts

Recruit around the relevant focus spectrum. Include people who report difficulty with interruptions, task switching, sensory overload, selective attention, recovering from context switches, or protecting focus time. Include people with varied devices, assistive technologies, work modes, bandwidth, language needs, and environmental contexts.

Useful screener prompts:

- What best describes how you feel about technology interruptions?
- Which alerts or interruptions in your day are helpful, and which are disruptive?
- How do you maintain sustained concentration on an important project?
- Does task switching between tabs, pages, or apps take you out of focus?
- Do technology notifications make focusing difficult?
- How protective are you of focus time?
- What kinds of stimuli are hard to ignore while working, reading, playing, or learning?
- What controls do you wish you had over timing, frequency, sound, motion, badges, or pop-ups?

Co-design activities:

- Microinteraction mapping: storyboard trigger, feedback, action, and recovery.
- Human analogy: compare the system to a server, assistant, teacher, teammate, coach, or receptionist; identify what would be rude, helpful, or respectful in human behavior.
- Context and capability match: test the notification in a noisy room, low bandwidth, mobile, screen reader, high stress, focus session, glare, one-handed use, or meeting context.
- Alert audit: inventory every signal, classify urgency, and remove duplicate or low-value signals.
- Settings card sort: ask participants how they expect notification controls to be grouped and named.

## Evaluation metrics

Do not use engagement alone. A notification system can increase clicks while reducing wellbeing and task success.

Measure:

- Task completion with and without interruptions.
- Time and effort to resume after interruption.
- Self-reported focus, control, anxiety, confidence, and success.
- Dismiss, mute, snooze, opt-out, and “less like this” rates.
- Repeated exposure to the same notification type.
- Number of simultaneous alerts across channels.
- Whether messages are understood without extra help.
- Whether people know how to control notification behavior.
- Whether urgent messages are distinguishable from routine messages.
- Whether screen reader, keyboard, reduced-motion, high-contrast, zoom, and touch users can perceive and control the communication.
- Whether cross-app focus boundaries are respected.

Post-launch, review notification logs for harm signals: rapid dismissals, repeated snoozes, disabled categories, user complaints about stress, missed critical alerts, duplicate alerts, and unusually high interruption volume.

## Decision checklist

Before shipping a communication pattern, answer:

- What user goal does this support?
- What happens if the person misses it?
- What happens if the person sees it at the wrong time?
- Is the message actionable now?
- Can it be inline, batched, summarized, or peripheral instead of interruptive?
- Can the person dismiss, defer, mute, or control it?
- Is urgency shown in text and structure, not only color or motion?
- Does the modality match the urgency?
- Does it respect focus mode, quiet hours, and prior dismissals?
- Does it work without sound, color, motion, hover, precise pointer input, or rapid reaction?
- Does it preserve work state and provide recovery after interruption?
- Does it avoid sensitive inference and explain data use when needed?
- Does it prevent repeated, duplicate, or cross-channel interruptions?
- Has it been tested with people who struggle with interruptions or sensory overload?

## Anti-patterns

Avoid:

- Urgency theater: red, motion, sound, or modal treatment for low-stakes information.
- Badge debt: persistent counts that never clear or have no meaningful action.
- Toast-only critical information.
- Pop-ups that appear before the person has context.
- Suggestions that imply failure or incompetence.
- AI recommendations that repeatedly narrow discovery or reinforce past behavior.
- Hidden personalization based on sensitive assumptions.
- Typing indicators, read receipts, or presence cues that create unnecessary social pressure.
- Notifications that cannot be muted by category.
- Focus mode that suppresses useful preparation or recovery cues but allows promotional alerts.
- Settings that exist but are impossible to understand.
- Interruptions that erase form state, scroll position, draft content, or task context.
- Engagement metrics that reward more interruptions rather than better timing and lower cost.
