# Evaluation And Metrics

Use this file when defining acceptance criteria, release gates, accessibility maturity goals, audit reports, post-launch measurement, or inclusive design scorecards for web products.

Evaluation is not a final checklist. It is the operating system for learning whether a product actually reduces exclusion after people encounter it in real contexts. A product can pass a technical check and still fail people through overload, fear, hidden work, inaccessible recovery, unclear feedback, poor timing, or loss of control.

## Operating Principle

Measure whether people can accomplish what matters to them with agency, confidence, safety, and low unnecessary effort.

Do not define success as high usage alone. High usage can mean value, but it can also mean confusion, compulsion, repeated failure, unnecessary friction, or lack of alternatives. Prefer measures that show whether people complete meaningful tasks efficiently, feel in control, understand what happened, and can recover when the system fails them.

Every metric should answer four questions:

1. Whose experience does this measure?
2. What human goal, cognitive demand, or access need does it represent?
3. What design decision will change if the result is poor?
4. Could optimizing this metric harm attention, privacy, autonomy, trust, or mental health?

If a metric cannot change a decision, remove it.

## Evaluation Lifecycle

Evaluate across the whole product cycle.

| Phase | Evaluation job | Evidence to collect | Decision gate |
|---|---|---|---|
| Discovery | Identify motivations, barriers, contexts, and current adaptations. | Interviews, co-design notes, support tickets, observed workarounds, assistive tech setups, task maps. | The team can name the excluded populations, situations, cognitive demands, and success outcomes. |
| Concept | Test whether the solution direction reduces mismatch. | Low-fidelity prototype observation, role-play, scenario walkthroughs, edge-case review. | The solution works in principle for at least one pronounced access need and extends to adjacent needs. |
| Design | Check component behavior, wayfinding, feedback, cognitive load, emotional state, and customization. | Prototype tests, content review, screen reader/keyboard review, preference-state review. | The critical path has no unresolved blocker or high-risk mismatch. |
| Build | Verify implementation under real browser, device, assistive technology, and network conditions. | Automated tests, manual accessibility tests, browser preference tests, performance data, state coverage. | Release cannot proceed with blockers in core tasks. |
| Launch | Monitor whether real behavior matches intended outcomes. | Feedback, telemetry, support themes, abandonment points, reversions, settings use, qualitative reports. | Launch data confirms no new exclusion pattern is emerging. |
| Post-launch | Revise from real evidence and close the feedback loop. | Backlog closure, before/after outcomes, customer follow-up, updated patterns, maturity movement. | Improvements are communicated, measured, and fed into the design system. |

## Human-Centered Success Metrics

Use a balanced metric set. Do not rely on a single number.

### 1. Access and operability

Measures whether people can reach and use the experience through different inputs, outputs, settings, and contexts.

Useful signals:

- Critical task completion with keyboard only.
- Critical task completion with screen reader and expected announcements.
- Critical task completion at 200% and 400% zoom without content loss or horizontal scrolling where avoidable.
- Success with touch, pointer, speech input, switch-like sequential navigation, and small-screen layouts where relevant.
- Compatibility with high contrast, forced colors, reduced motion, reduced transparency, color scheme, and text scaling preferences.
- Number of inaccessible controls, traps, unlabeled regions, modal focus failures, non-semantic interactions, or status updates that are not announced.
- Ability to complete the task on slow network, interrupted connection, or delayed loading.

Do not count automated accessibility-test pass rate as the full measure. Use it as a baseline signal only.

### 2. Goal completion and agency

Measures whether the product supports the person’s goal rather than only the product’s funnel.

Useful signals:

- Task success rate for the actual goal, not just the screen-level action.
- Time to meaningful completion after excluding time spent reading optional help.
- Number of avoidable steps, repeated actions, forced choices, and mode switches.
- Rate of successful pause, save, resume, undo, back, cancel, dismiss, and retry.
- Number of points where the user cannot tell what will happen next.
- Percentage of participants who report feeling in control of timing, data sharing, notification behavior, and AI assistance.
- Whether the design supports more than one valid route to the same important outcome.

### 3. Cognitive demand

Measures the level and type of thinking required to succeed.

Evaluate learning, focus, decision-making, recall, and communication separately. A product may be strong in one area and exclusionary in another.

Useful signals:

- Learning: first-time success, guidance discovery, ability to skip guidance, ability to revisit help, novice-to-expert progression, and comprehension of new concepts.
- Focus: interruptions per task, recovery after interruption, non-urgent alerts during critical work, context switching, visual noise, and ability to tune the system down.
- Decision-making: clarity of consequences, tradeoffs, defaults, reversibility, and whether people can compare choices without extra research.
- Recall: reliance on memory, availability of summaries, breadcrumbs, saved state, recent activity, reminders, drafts, and task history.
- Communication: fit of tone, format, timing, channel, language, captions, transcripts, and multimodal alternatives.

Add qualitative prompts after each task: “What did you have to remember?”, “Where did you hesitate?”, “What would you need before deciding?”, and “What pulled your attention away?”

### 4. Emotional and mental-health experience

Measures whether the product preserves motivation, confidence, psychological safety, and emotional capacity.

Useful signals:

- Self-reported confidence before and after the task.
- Self-reported feelings of safety, pressure, overwhelm, frustration, self-blame, trust, and control.
- Observed hesitation, abandonment, repeated rereading, compulsive checking, or task paralysis.
- Number of moments where the product confirms success or progress without patronizing.
- Ability to reduce or defer notifications, pop-ups, motion, sound, timers, and non-critical suggestions.
- Privacy clarity: whether people understand what is collected, shared, inferred, retained, or used by AI.
- Availability of low-stress recovery paths after errors, accidental dismissal, lost state, or misunderstanding.

A product that makes people blame themselves is failing even if the final conversion rate is acceptable.

### 5. Trust, privacy, and consent

Measures whether people understand and control system behavior.

Useful signals:

- Comprehension of privacy settings and data-use explanations.
- Presence of consent touchpoints where data collection, personalization, AI training, or sharing changes.
- Ability to opt out, reset, delete, export, minimize, or change personalization.
- Number of high-impact actions taken automatically without preview, confirmation, undo, or human review.
- User trust after AI recommendations, summaries, personalization, or automated decisions.
- Clarity of limitations, uncertainty, and source evidence.

Do not treat silent acceptance as consent.

### 6. Feedback loop health

Measures whether customer input is safe, actionable, and visibly used.

Useful signals:

- Feedback mechanism exists in a predictable location.
- People can choose how much personal information to share.
- Survey questions are relevant to the person’s usage and do not force unnecessary disclosure.
- Open-ended feedback is available for emotional and functional experience.
- Feedback themes are categorized, assigned, acted on, and re-measured.
- Participants or affected communities are told what changed because of feedback where practical.
- Product changes offer notice, explanation, and rollback for meaningful behavior changes.

Feedback that disappears into a queue without action is not a loop.

### 7. AI and automation outcomes

Use when the product includes AI assistants, recommendation systems, personalization, summarization, detection, ranking, or automated decisions.

Useful signals:

- Dataset representativeness for the expected customer base.
- Bias stress-test results for dataset, association, automation, interaction, and confirmation bias.
- Source visibility and fact-checkability for summaries or factual claims.
- User correction paths and whether corrections improve the system safely.
- Frequency of overconfident, incorrect, stereotyped, narrow, or irrelevant output.
- Ability to review, edit, undo, reject, regenerate, minimize, or turn off automation.
- Whether AI helps complete a task instead of creating an endless refinement loop.
- Whether AI preserves discovery and alternative viewpoints instead of narrowing people into prior patterns.

AI success is not only output quality. It is also control, explainability, consent, and reduction of cognitive demand.

### 8. Culture and process outcomes

Inclusive design changes how teams work. Measure the system, not only the screen.

Useful signals:

- Co-design occurs before major decisions are fixed.
- Research includes people who experience the relevant mismatch, not only convenient participants.
- Recruitment is based on need spectrums and context, not diagnosis labels alone.
- Accessibility and inclusive design issues are tracked with owners, severity, due dates, and resolution evidence.
- Design system components have documented accessible behavior and state coverage.
- Product, design, engineering, research, content, data, legal, support, and accessibility roles share accountability.
- Learnings from one project become reusable patterns, not isolated fixes.

## Metric Specification Template

Define each metric before launch.

```text
Metric name:
Human need represented:
Primary task or scenario:
Relevant cognitive demand(s):
Relevant access need(s):
Excluded or underrepresented group(s) to include:
Data source:
Baseline:
Target or decision threshold:
Known risk if optimized too aggressively:
Privacy or consent consideration:
Owner:
Review cadence:
Decision rule if metric fails:
```

Examples of risks:

- Optimizing notification opens may increase interruption.
- Optimizing time in product may reward friction or compulsion.
- Optimizing AI regeneration may encourage endless loops instead of completion.
- Optimizing form completion may hide stress, confusion, or coerced disclosure.
- Optimizing settings usage may indicate the default is not working.

## Accessibility Maturity Model

Use this model to orient an organization, product area, or design system. Score each dimension independently; most teams will be at different levels across research, design, engineering, content, QA, support, and measurement.

| Level | Name | What it looks like | Evidence | Next move |
|---|---|---|---|---|
| 1 | Initial | Accessibility and inclusive design happen ad hoc, often after complaints or late QA. | Issues are found near launch; fixes depend on individual champions; no consistent acceptance criteria. | Name owners, define minimum release gates, and start recording issues in a shared backlog. |
| 2 | Repeatable | Common checks occur on recurring work, but practices are not yet embedded everywhere. | Keyboard checks, automated scans, and design reviews happen on key flows; some reusable fixes exist. | Formalize checklists, component behavior, severity rules, and basic feedback loops. |
| 3 | Defined | Accessible and inclusive design principles are codified and integrated from the start. | Design system patterns include states, content rules, AT behavior, reduced-motion behavior, and customization guidance. | Add outcome metrics, co-design milestones, research screeners, and launch dashboards. |
| 4 | Managed | Measurements track progress and guide prioritization across releases. | Dashboards combine technical issues, human-centered outcomes, support themes, feedback, and maturity movement. | Use data to identify systemic causes, fund roadmap work, and prevent repeated issue classes. |
| 5 | Optimized | Accessibility maturity produces efficiency, innovation, and stronger experiences. | Teams co-create continuously, ship fewer regressions, reuse mature patterns, publish learnings, and discover new product opportunities from constraints. | Keep refining with affected communities and expand the model to new contexts, devices, and interaction modes. |

### Maturity dimensions to score

Score each from 1-5:

- Leadership accountability and funding
- Research and co-design practice
- Recruiting diversity and need-spectrum coverage
- Design system component maturity
- Content and information architecture maturity
- Frontend implementation and QA maturity
- Assistive technology and preference testing coverage
- AI, automation, and data governance where applicable
- Feedback intake, triage, and closure
- Post-launch measurement and iteration
- Support, documentation, and awareness materials
- Cross-product consistency and configuration portability

### Maturity evidence checklist

A maturity claim requires evidence. Acceptable evidence includes:

- Representative research plan and screener.
- Co-design summaries with decisions changed because of participant input.
- Accessibility test logs across browsers, devices, inputs, and assistive technologies.
- Component specifications with keyboard, focus, announcement, reduced-motion, and state behavior.
- Audit findings with severity, ownership, resolution evidence, and regression coverage.
- Human-centered metrics defined before launch.
- Post-launch results compared with the baseline.
- Feedback-loop records showing what was heard, changed, and re-measured.

Do not advance maturity level based only on training completion, intent, or one successful project.

## Feedback Loop Protocol

Use this loop for surveys, ratings, support tickets, interviews, analytics, public feedback, community input, and co-design findings.

1. Provide safe input paths. Offer predictable feedback entry points, anonymous or low-disclosure options, and accessible formats.
2. Scope the prompt. Ask only questions relevant to the person’s usage or task. Include functional and emotional dimensions.
3. Classify by mismatch. Tag feedback by access need, cognitive demand, emotional stressor, component, step, severity, and context.
4. Detect patterns. Look for recurring barriers, not only majority complaints. A small number of severe barriers can outweigh a large number of preferences.
5. Prioritize with harm in mind. Consider access loss, emotional cost, safety, privacy, recoverability, frequency, and business impact.
6. Co-design the fix when the issue reflects lived experience the team does not share.
7. Ship with notice and reversibility when behavior changes may be jarring.
8. Re-measure after release. Compare before/after task success, sentiment, support load, regressions, and affected-community feedback.
9. Close the loop. Update the design system, tell affected stakeholders what changed, and document the new rule.

## Severity Rubric

Use this rubric for audit findings and backlog triage.

| Severity | Definition | Examples | Required response |
|---|---|---|---|
| Blocker | Prevents a group from completing a critical task or creates safety, privacy, legal, or severe emotional harm. | Keyboard trap in checkout; unlabeled required form fields; AI submits high-impact change without review; destructive action without recovery. | Stop release for affected flow or provide an equivalent accessible path before release. |
| High | Allows completion only with major effort, workaround, assistance, or distress. | Screen reader cannot understand progress; timer cannot be paused; repeated context switching; privacy setting unclear; non-urgent pop-up breaks focus. | Fix before release when in a critical path; otherwise assign owner and near-term milestone. |
| Medium | Creates avoidable friction, confusion, or cognitive/emotional load but has a usable recovery path. | Weak headings, vague button labels, hidden help, spinner without context, missing confirmation, overly dense content. | Fix in planned iteration and track recurrence at component level. |
| Low | Minor inconsistency or polish issue that does not substantially block use. | Slightly inconsistent helper text, minor layout discomfort at uncommon width, redundant microcopy. | Bundle with related improvements. |
| Opportunity | Not a defect, but a chance to extend access or reduce effort. | Add saved state, summarize long threads, provide alternate guidance, expose useful settings in context. | Consider in roadmap and validate with affected users. |

Severity should increase when the issue affects irreversible actions, money, health, identity, privacy, employment, education, access to benefits, or time-sensitive tasks.

## Acceptance Criteria Patterns

Acceptance criteria should be written as observable conditions, not intentions.

Use this structure:

```text
Given [person/context/access need],
when [they attempt the task],
then [observable inclusive outcome],
and [proof method].
```

Examples:

```text
Given a keyboard-only user,
when they complete the account setup flow,
then every interactive element is reachable in logical order, focus is visible, and focus returns after dialogs,
and this is verified manually in the supported browsers.
```

```text
Given a person returning after interruption,
when they reopen the form,
then their progress, last completed step, unsaved data status, and next action are clear,
and this is verified in usability testing and state-restoration QA.
```

```text
Given a person using reduced motion,
when loading, navigation, or confirmation states appear,
then essential meaning is available without animation and motion is reduced or removed,
and this is verified through OS/browser preference testing.
```

```text
Given a person receiving an AI-generated summary,
when they need to verify a claim,
then the relevant source or reference is visible and reachable,
and this is verified through content QA and assistive technology review.
```

```text
Given a person under time pressure or stress,
when they encounter an error,
then the message states what happened, the consequence, and the next action without blame,
and this is verified through content review and participant comprehension testing.
```

## Audit Report Structure

Use this structure for inclusive design audits.

```text
Title:
Product or flow:
Date:
Reviewers and roles:
Scope:
Out of scope:
User goals and motivations:
Critical tasks reviewed:
Relevant cognitive demands:
Relevant access needs and contexts:
Methods used:
Browsers, devices, assistive technologies, and preferences tested:
Participants or co-design inputs:
Maturity level assessment:
Top risks:
Findings by severity:
Evidence:
Recommendations:
Acceptance criteria:
Owner and due date:
Regression test needed:
Post-launch metric:
Follow-up date:
```

Each finding should include:

```text
Finding ID:
Severity:
Affected people or contexts:
Mismatch:
Where it occurs:
Evidence:
Why it matters:
Recommended change:
Acceptance criterion:
How to verify:
Owner:
```

Avoid vague findings such as “make it accessible.” Name the mismatch and the observable failure.

## Review Checklists

### Pre-launch inclusive evaluation

- The target human goal is named separately from the product goal.
- The flow has been reviewed for learning, focus, decision-making, recall, and communication demands.
- The critical path can be completed with keyboard, screen reader, touch, zoom, high contrast/forced colors, reduced motion, and slow network where relevant.
- Headings, labels, instructions, errors, confirmations, and button text are understandable without hidden context.
- People can pause, resume, save, undo, go back, retry, dismiss, and recover from interruption where the task requires it.
- Time limits, timers, pop-ups, suggestions, alerts, and AI interventions are justified by urgency and controllable by the person.
- New concepts have guidance for people who want structure and enough freedom for people who prefer exploration.
- Settings and customization options are discoverable, explained, and retained when appropriate.
- The product asks for only necessary data and explains sensitive data use before collection or automation.
- Feedback mechanisms are accessible, predictable, and safe.
- Metrics are defined with baselines, thresholds, owners, and decision rules.

### Accessibility implementation review

- Semantic structure matches the visual and task structure.
- Interactive elements use native controls where possible.
- Accessible names are specific and stable.
- Focus order follows task order.
- Focus is visible and not obscured.
- Dynamic updates are announced only when useful.
- Forms expose labels, descriptions, constraints, validation, and error relationships programmatically.
- Meaning is not conveyed by color, motion, sound, shape, or position alone.
- Media has captions, transcripts, descriptions, and controls as needed.
- Layout reflows without loss at zoom and small widths.
- No required interaction depends on hover, drag, precision, speed, pointer-only input, or a single sensory channel.
- Reduced-motion and user preference states are tested.

### Cognitive and mental-health review

- The interface does not assume high confidence, perfect memory, sustained attention, or high risk tolerance.
- The number of visible choices is appropriate to the decision being made.
- Consequences are clear before consequential actions.
- Critical information is chunked, searchable, filterable, summarized, or progressively disclosed.
- Waiting states say what is happening and what the person can do next.
- Confirmation states show success and completion clearly.
- Errors provide a solution first and do not blame the person.
- Notifications and suggestions do not steal attention without need.
- People can control timing, frequency, channel, and interruption level.
- Changes to familiar behavior include explanation and, where practical, a path to revert.

### Feedback and survey review

- The feedback entry point is easy to find and accessible.
- The person can decide how much personal information to include.
- The survey is scoped to the feature, task, or context the person used.
- The survey includes emotional experience, not only functionality.
- Open-ended responses are allowed.
- The feedback system can identify severe minority barriers, not only high-volume requests.
- Feedback themes are connected to owners and backlog items.
- Participants are not asked to repeatedly report the same issue without visible progress.
- Product changes resulting from feedback are communicated when practical.

### AI and automation review

- The automation supports a named human motivation and task.
- The person can review, edit, reject, undo, or opt out.
- Sources or evidence are shown when factual reliability matters.
- Limitations and uncertainty are explained in plain language.
- Dataset and output evaluation include people outside the assumed majority.
- Bias checks cover dataset, association, automation, interaction, and confirmation bias.
- Personalization does not trap people in repetitive assumptions.
- Privacy and consent touchpoints are available throughout the journey.
- Corrections and feedback are collected safely and used responsibly.
- The AI helps finish tasks rather than creating endless refinement loops.

### Post-launch review

- Baseline and post-launch data are compared.
- Support tickets, feedback, analytics, and qualitative research are reviewed together.
- Drop-off, repeated attempts, rage clicks, undo/retry patterns, dismissals, and setting changes are investigated as possible exclusion signals.
- Accessibility regressions are tracked to component or system causes.
- Severe issues from small populations are not dismissed because they are low volume.
- Product changes are tested with people affected by the original mismatch.
- Metrics are checked for harmful incentives.
- The design system is updated with new rules, examples, and regression tests.
- Follow-up research is scheduled for unresolved assumptions.

## Pattern-Level Metrics

Use these prompts when reviewing common web patterns.

| Pattern | What to measure |
|---|---|
| AI assistants | Prompt-start success, fact-check path, source clarity, perceived control, privacy comprehension, positive plus corrective feedback balance, closure rate, override and undo success, bias stress-test outcomes. |
| Collections | Findability, filter/search/sort comprehension, keyword visibility, scannability, collapse/expand use, prioritization clarity, text scaling and narration support. |
| Color | Contrast, non-color signals, visual sensitivity, badge distraction, emotional tone, high contrast and forced-colors behavior. |
| Content | Heading scan success, comprehension, next-step clarity, reading burden, tone fit for stress or confusion, unnecessary detail removed. |
| Confirmation and errors | Recovery success, perceived blame, consequence clarity, next-action clarity, success recognition, edge-case coverage. |
| Feedback | Safety of disclosure, relevance of survey questions, open-ended signal quality, closure rate, resulting customization improvements. |
| Loading states | Expected wait clarity, anxiety during wait, low-bandwidth performance, ability to continue or prepare, reduced-motion behavior. |
| Motion and noise | Preference compliance, ability to pause/stop/mute, attention cost, usefulness of motion, caption/transcript availability. |
| New concepts | Comprehension, guidance fit, novice success, skip/revisit behavior, terminology clarity, examples and demos. |
| Pop-ups and suggestions | Urgency accuracy, interruption cost, action availability inside message, dismissibility, frequency control, breakthrough-rule clarity. |
| Settings | Discoverability, explanation quality, retention, mental-model match, onboarding support, privacy control comprehension. |
| Timers | Pause/extend/save support, anxiety level, count-up/countdown preference, non-timed alternative availability. |
| Wayfinding | Current location clarity, progress clarity, next action visibility, back/undo/save paths, empty-state orientation. |

## Metric Anti-Patterns

Avoid these measurement patterns:

- Treating high engagement as proof of wellbeing or success.
- Counting accessibility issues without tracking whether critical user journeys work.
- Counting feedback volume without closing the loop.
- Using average task time while ignoring people who abandoned, needed help, or used assistive technology.
- Treating low complaint volume as proof that no barriers exist.
- Asking participants to self-disclose diagnoses when the relevant measure is need, context, or cognitive demand.
- Measuring only happy paths.
- Measuring AI helpfulness without checking fact-checkability, agency, privacy, and bias.
- Shipping large behavior changes without tracking confusion, reversions, support load, or emotional impact.
- Using metrics that create surveillance, pressure, manipulation, or shame.

## Recommended Scorecard

For audits, rate each area from 0-3.

```text
0 = Not present or unknown
1 = Present but fragile, inconsistent, or unverified
2 = Works in the main path with minor gaps
3 = Works across realistic contexts and is validated with affected people
```

Score these areas:

- Critical task access
- Keyboard and focus behavior
- Screen reader and semantic behavior
- Zoom, reflow, contrast, and preferences
- Content clarity and scannability
- Learning and guidance support
- Focus and interruption control
- Decision clarity and consequence visibility
- Recall support and state recovery
- Emotional safety and confidence
- Privacy, consent, and trust
- Feedback intake and loop closure
- AI and automation control where applicable
- Device, input, and context flexibility
- Post-launch measurement and iteration

A low score in a critical path outweighs a high average score.

## Definition Of Done

A feature is ready for inclusive release only when:

- The team can explain who may be excluded and why.
- At least one pronounced mismatch has been validated with people who experience it.
- The critical path has observable accessibility acceptance criteria.
- Cognitive and emotional success criteria are defined, not implied.
- Recovery paths exist for likely errors, interruptions, and accidental actions.
- Feedback and post-launch measurement are ready before launch.
- Owners and decision thresholds are defined for post-launch issues.
- Learnings can be fed back into the design system after release.

