# Content and Information Architecture

Use this reference for content-heavy screens, accumulated data, long lists, dashboards, inboxes, document libraries, search results, message threads, settings, help surfaces, onboarding copy, privacy explanations, and navigation models.

The purpose is to make information findable, understandable, controllable, and recoverable without assuming perfect attention, memory, confidence, literacy, bandwidth, vision, hearing, motor precision, or prior product knowledge.

## Core Principle

Content architecture is part of the interaction. It is not a layer applied after design.

When information accumulates, people need help to:

- understand what exists
- identify what matters now
- decide what to do next
- find or refind something later
- recover after interruption
- control what the system knows, shows, hides, remembers, or shares

A content-heavy interface fails when it forces people to hold the structure in memory. The structure should be visible, predictable, and usable through multiple paths.

## Start With The Information Job

Before rewriting or reorganizing, identify the job of the screen.

Ask:

- Is the person trying to learn, decide, compare, act, recover, monitor, configure, or verify?
- What information is required immediately, and what can wait?
- What information must be remembered across sessions?
- What is the cost of missing, misunderstanding, or misusing the information?
- What is the most likely emotional state: calm, rushed, confused, anxious, overloaded, skeptical, or frustrated?
- Is the screen serving first-time use, expert use, or both?

Map content to motivation, goal, task, and consequence. Do not map it only to internal data models, team ownership, or implementation convenience.

## Plain Language

Use common, direct language that supports non-technical users and users under stress. Plain language is not simplistic language. It is precise language with unnecessary effort removed.

Write content so the person can answer:

- What is this?
- Why does it matter?
- What happens if I do nothing?
- What happens if I act?
- What information is needed from me?
- Where can I change this later?

Rules:

- Put the most important information first.
- Use short sentences and short paragraphs.
- Prefer active voice when it clarifies responsibility.
- Use the same term for the same concept everywhere.
- Define uncommon terms at the point of use.
- Avoid internal product language, legal shorthand, unexplained acronyms, and idioms.
- Avoid jokes, metaphors, or clever labels in critical flows.
- Avoid vague reassurance such as “Don’t worry” unless the design also explains what is safe and why.
- Avoid blame. Write about system state and next action, not user failure.

For translated or multilingual content, keep sentence structure simple, avoid wordplay, and design layouts that tolerate text expansion.

## Tone And Emotional State

Content should adapt to the person’s frame of mind.

Use calmer, more explicit content when:

- the person may think something has gone wrong
- money, health, legal status, privacy, identity, work, or relationships are affected
- the person is recovering from an error
- the person must choose between options with consequences
- the system is asking for personal information
- the flow is long or unfamiliar

Use concise operational content when:

- the person is repeating a familiar task
- the action is low-risk and reversible
- the surrounding UI already provides enough context

Do not use an upbeat tone to cover uncertainty, risk, missing information, or system failure.

## Scannability

Dense text increases cognitive load. Break content into visible units that match the person’s task.

Use:

- summary first, details later
- descriptive section headings
- short paragraphs
- bulleted lists for parallel items
- ordered lists for sequences
- tables for comparison, not layout
- cards for distinct objects or choices
- expandable sections for optional detail
- visual containers to group related content
- inline examples for unfamiliar concepts
- persistent progress indicators for long flows

Do not hide essential information behind expansion controls. Collapsible sections are for reducing clutter, not burying consequences.

Keep chunks meaningful. A short fragment is not helpful if it separates a label from the context needed to understand it.

## Page Titles, Headings, And Structure

Headings are navigation, not decoration. They should create a useful outline of the page.

Rules:

- Each page or route needs a unique title that identifies the current task or location.
- Put the unique information first in page titles.
- For multi-step flows, include the current step or task stage in the visible title.
- Use one primary page heading for the main subject.
- Nest section headings in logical order.
- Do not skip heading levels to achieve a visual size.
- Do not use styled text as a fake heading.
- Keep headings short, specific, and action-relevant.
- Avoid duplicate headings unless the repeated sections are clearly disambiguated.
- Use headings to separate decisions, requirements, consequences, and support content.

Poor headings:

- “More”
- “Details”
- “Manage”
- “Things to know”
- “Advanced”

Better headings:

- “Files waiting for review”
- “Who can see this profile”
- “Billing changes before renewal”
- “Required documents”
- “Messages filtered out today”

## Labels

Labels carry the burden of recognition. A good label reduces guessing.

Rules:

- Keep visible labels persistent whenever possible.
- Do not rely on placeholder text as a label.
- Use labels that match the visible action or object.
- Name buttons with verb + object when the consequence is not obvious.
- Label icon-only controls with text in the UI or an equivalent programmatic name.
- Use helper text for format, requirements, or consequences.
- Group related controls under a visible group label.
- Place error text near the field or control it affects.
- Do not use “Yes” and “No” if the question is far away or complex. Repeat the consequence in the option label.

Action labels should state the outcome:

- “Save draft” instead of “Save” when final submission is separate.
- “Send invite” instead of “Submit.”
- “Delete 3 selected files” instead of “Delete.”
- “Turn off weekly summary” instead of “Confirm.”

A label is unacceptable if it only works for sighted users, mouse users, expert users, or people who already know the system.

## Links And Calls To Action

People scan links out of context. Link text must describe the destination or result.

Rules:

- Do not use repeated “Learn more,” “Click here,” or “Read more” links without unique context.
- Use links for navigation and buttons for actions that change state.
- Keep one primary action per decision point.
- Place actions near the content they affect.
- Distinguish destructive, irreversible, or privacy-affecting actions with clear text and review.
- Provide a safe exit, save, or resume path for long tasks.

If a page has many possible actions, group them by task, not by internal feature category.

## Privacy, Consent, And Data Explanations

Privacy explanations must be understandable before the person gives data or enables personalization. Do not rely on a single dense policy page.

Every data request should explain:

- what is collected
- why it is needed
- whether it is required or optional
- who or what can access it
- how long it is kept, when relevant
- what the system will infer or personalize from it
- what is shared with other people, services, devices, or organizations
- where to change, export, disconnect, or delete it

Design consent as touchpoints across the journey:

- before collection, explain the reason
- during use, show the current state and who can see it
- after use, provide review and control

Avoid inaccessible legalese in the task flow. Legal text may still exist elsewhere, but the in-flow explanation must be usable on its own.

For AI, recommendations, or adaptive interfaces:

- explain what information is being used for personalization
- show when a recommendation is based on past behavior
- allow people to correct assumptions
- allow people to reset or turn off personalization
- avoid narrowing discovery to what the person already chose before
- do not imply that the system knows intent when it is only guessing

## Collections And Data Accumulation

Collections include inboxes, message threads, files, long lists, media libraries, data dashboards, logs, saved searches, survey templates, notes, and activity histories.

A collection needs more than a list. It needs orientation, organization, and recovery.

Required capabilities:

- clear page title and collection purpose
- total count or useful summary
- visible grouping or categories
- search, sort, and filter when the set can grow
- keyword highlighting in results when possible
- selected item state
- empty, loading, error, and no-results states
- recently viewed, recently changed, or saved state when return is likely
- batch actions with clear selected counts
- ways to hide, collapse, or expand lower-priority information
- readable density options where feasible

Group by the person’s task, not only by object type. For example, “Needs your response,” “Ready to submit,” and “Blocked” may be more useful than “Documents,” “Messages,” and “Settings.”

For mixed structured and unstructured data, provide extraction, summaries, or metadata only when they are reviewable. Do not make inferred structure feel like verified truth.

## Search

Search supports people who know what they want and people who only know fragments.

Search requirements:

- visible search field with a persistent label
- clear scope, such as “Search all files” or “Search within messages”
- forgiving matching for common spelling, pluralization, and naming variants where feasible
- result count and no-results guidance
- highlighted query terms in results where useful
- preserved query after search
- clear way to clear the query
- keyboard-operable suggestions and results
- loading and error states that do not erase the query
- search history only when useful and privacy-appropriate

No-results pages should help the person recover:

- restate the query and active filters
- suggest removing filters
- suggest related terms or categories
- provide a browse path
- provide support when failure has high consequence

Do not make search the only way to find critical information. Provide browsing, navigation, or direct access for important destinations.

## Filters

Filters reduce overload when they match the person’s mental model.

Filter requirements:

- use categories people recognize
- show active filters clearly
- provide “clear all” and per-filter removal
- preserve filters when moving between list and detail views
- show result counts when helpful
- avoid changing the page unexpectedly while focus is inside a control
- make multi-select, single-select, range, and toggle behavior obvious
- avoid hiding critical information by default unless the default is clearly explained
- provide saved filters for repeated workflows when useful

For incoming information, filters should help people control attention. Let people filter by urgency, sender, type, status, date, ownership, unread state, due date, or relevance when those dimensions support the task.

Do not force people to remember which filters are active. Active constraints must remain visible.

## Sorting

Sorting changes orientation. Make it predictable.

Sorting requirements:

- show the current sort field and direction
- provide plain-language sort labels
- preserve sort choice where repeated use is likely
- avoid reordering while a person is selecting or reading unless they requested it
- maintain stable ordering for equal values when possible
- explain defaults when they affect priority or visibility

Good labels:

- “Newest first”
- “Oldest first”
- “Due soonest”
- “Highest priority”
- “Unread first”
- “A to Z”

Avoid labels such as “Ascending” unless the object being sorted is visible and obvious.

## Navigation Models

Choose the navigation model based on the information job.

Use a linear stepper when:

- order matters
- decisions depend on previous entries
- the task has a clear start and end
- progress reduces anxiety
- review is needed before submission

Use a hub-and-spoke model when:

- the person needs to complete independent sections in any order
- status varies by section
- people need to save and return
- the task is too long for one path

Use faceted browse when:

- the set is large
- people approach it from different criteria
- comparison and narrowing matter
- search alone would be brittle

Use a dashboard when:

- monitoring matters more than reading
- the person needs status, exceptions, trends, and next actions
- summaries can lead to detail views

Use documentation navigation when:

- people are learning, troubleshooting, or comparing concepts
- information must support both browsing and direct lookup
- examples, glossary, and related topics reduce support burden

Use a timeline or activity log when:

- sequence, accountability, or recovery matters
- the person needs to know what changed, when, and by whom

Do not force a marketing-page structure onto an app, dashboard, help center, or data workspace.

## Wayfinding

At every point, people should know:

- where they are
- what section or object they are in
- what has changed
- what actions are available
- what the recommended next step is
- how to go back, undo, save, pause, or exit
- how to get help

Wayfinding tools:

- consistent global navigation
- local section navigation
- breadcrumbs for deep hierarchies
- progress indicators for flows
- current-page and current-step markers
- persistent object names in detail views
- “last saved” or “last updated” status
- back paths that return to the previous list state
- empty states that explain where the person is and what to do next

Breadcrumbs are memory support. They should reflect meaningful hierarchy, not every click.

## Progressive Disclosure

Progressive disclosure should reduce demand without hiding what matters.

Use disclosure for:

- optional detail
- advanced configuration
- examples
- troubleshooting
- long explanations
- secondary metadata
- lower-frequency actions

Do not use disclosure for:

- consequences
- costs
- privacy impact
- destructive action warnings
- required fields
- eligibility requirements
- critical errors

When disclosure is used, the collapsed label must describe what is inside. Do not label everything “More.”

## Tables, Dashboards, And Comparisons

Use tables when the person must compare values across items. Use cards when each item is independent and comparison is secondary.

For tables:

- provide a visible title or summary of the table’s purpose
- use meaningful column and row headers
- keep row actions discoverable
- allow sorting only where it helps
- preserve reading order on small screens
- avoid horizontal scrolling for critical tasks when possible
- explain abbreviations and units
- align numbers by place value where useful
- keep status text visible, not color-only

For dashboards:

- show exceptions and required actions before decoration
- distinguish live data, stale data, estimated data, and missing data
- provide a path from summary to source detail
- avoid charts that require color, hover, or precision to understand
- provide text summaries for key chart insights

## Icons, Visual Cues, And Media

Icons can reduce effort when their meaning is established, visible, and supported. They can increase effort when they are ambiguous.

Rules:

- Use established symbols before inventing new ones.
- Pair icons with text when space allows or the meaning is not universally clear.
- Keep critical icons high contrast and large enough to perceive.
- Do not use color as the only signal.
- Use the same icon for the same concept everywhere.
- Test icon meanings with people outside the product team.

For images, video, and interactive demos:

- provide text alternatives for meaningful images
- mark decorative images as decorative in implementation
- provide captions and transcripts for instructional media
- provide descriptions when visual action is necessary to understand the content
- do not make video or an interactive widget the only source of instructions
- prefer a well-structured HTML page for core instructions because it gives people more control

## Help, Guidance, And Learnability

People learn in different ways and may need different guidance depending on confidence, motivation, stakes, and context.

Content-heavy systems should provide:

- multiple entry points into help
- concise explanations in the flow
- detailed guidance on demand
- examples for unfamiliar concepts
- step-by-step paths for complex tasks
- skip and revisit controls for onboarding
- support that remains available after first use
- offline or low-bandwidth alternatives when the task requires them

Ask permission before launching a guided path that changes the person’s flow. Do not trap expert users in onboarding, and do not force new users to infer everything by trial and error.

## Recall And Recovery

Do not require people to remember where they were or what they did.

Support recall with:

- autosave or explicit save points
- draft states
- recent activity
- recently viewed items
- task lists
- notes and comments
- decision history
- audit trails
- saved searches
- persistent filters when useful
- return links that preserve previous list state
- reminders tied to the person’s goal, not arbitrary engagement

Recovery content should answer:

- What was saved?
- What changed?
- What remains incomplete?
- What is blocked?
- What can I do next?

## Feedback, Loading, Empty, And Error States

State content is part of information architecture.

Loading states:

- say what is loading
- preserve the person’s entered data
- show estimated wait time when possible
- avoid unnecessary motion
- provide retry or fallback when loading fails

Empty states:

- distinguish “nothing exists yet” from “nothing matches these filters”
- explain the value of the empty area
- provide the next useful action
- avoid shame or forced cheerfulness

Error states:

- identify the affected item or field
- explain the problem in plain language
- put the solution first when it is simple
- preserve entered data
- provide a best next step
- avoid blame

Success states:

- confirm completion
- show where the result went
- provide undo or review when appropriate
- move the person forward without stealing attention

## Settings And Preferences

Settings are information architecture, not an afterthought.

Settings should be:

- findable from predictable locations
- surfaced in context when they affect the current task
- explained in terms of effect, not implementation
- grouped by user goal
- reversible where possible
- retained across sessions when appropriate
- exportable or shareable when configuration itself is valuable

Avoid overwhelming people with configuration. Offer useful defaults, then let people tune density, appearance, narration, notifications, filters, sort order, privacy, personalization, and guidance.

Use card sorting or similar co-creation methods to validate whether settings are grouped according to the audience’s mental model.

## Data Density And Readability Controls

Content-heavy screens should not assume one ideal density.

Provide controls where feasible for:

- comfortable and compact density
- text size
- line spacing
- contrast or appearance
- showing or hiding metadata
- grouping or ungrouping items
- narration or read-aloud support
- reduced motion in dynamic lists

Never make density controls the only way to make a screen usable. The default must still be readable and operable.

## Implementation Requirements

Build the content structure so assistive technologies, keyboards, touch, zoom, translation, and low-bandwidth environments can use it.

Requirements:

- Use semantic HTML for headings, lists, tables, forms, buttons, links, and landmarks.
- Keep source order aligned with visual reading order.
- Use real buttons for actions and real links for navigation.
- Keep focus order logical after sorting, filtering, expanding, routing, and opening dialogs.
- Provide visible focus indicators.
- Associate labels, helper text, errors, and descriptions with the controls they explain.
- Make dynamic result counts, status messages, and loading completion available without forcing focus jumps.
- Ensure text can resize and reflow without clipping, overlap, or loss of function.
- Preserve functionality across viewport sizes and input modes.
- Avoid hover-only content for required information.
- Avoid drag-only interaction for organizing or selecting information.
- Provide non-color cues for status, category, required fields, charts, and alerts.
- Keep target areas large enough and spaced enough for imprecise input.
- Do not auto-advance, auto-refresh, or reorder important content while someone is reading or acting unless they requested it.

## Content And IA Audit

For a content-heavy screen, audit in this order:

1. Identify the person’s goal and the consequence of failure.
2. List every content object, action, state, and decision on the screen.
3. Remove content that does not support the current job.
4. Separate required information from optional detail.
5. Write a unique page title and primary heading.
6. Create a heading outline that can be understood without visual styling.
7. Rename vague labels, links, and actions.
8. Add missing explanations for risk, privacy, cost, eligibility, and next steps.
9. Add search, sort, filter, grouping, or saved views where the collection can grow.
10. Add recovery paths: back, undo, save, resume, recent activity, and preserved state.
11. Add state content: loading, empty, no results, success, error, interrupted, and resumed.
12. Check that the structure works with keyboard, zoom, reduced motion, high contrast, small screens, and assistive technology.
13. Test the content with people who differ in confidence, language ability, attention, memory, tech literacy, and communication preference.

## Anti-Patterns

Avoid:

- long paragraphs where people need action
- headings that describe the product instead of the task
- labels that only make sense internally
- icon-only controls without a reliable name
- search as the only navigation path
- filters hidden after they are applied
- sort changes that reorder content unexpectedly
- progress indicators without step names
- privacy explanations deferred to a dense legal page
- personalization without clear consent and reset controls
- help that opens in a new context and loses the person’s place
- charts that require color or hover to understand
- empty states that do not explain what happened
- errors that clear input or blame the person
- settings grouped by engineering ownership instead of user goal
- pages that look simple only because required context is missing

## Output Checklist

Before returning a design, audit, or implementation plan, verify:

- The content hierarchy is visible and programmatic.
- The first screenful answers what this is, why it matters, and what to do next.
- Headings create a usable outline.
- Labels and actions are specific enough out of context.
- Critical consequences are not hidden.
- Privacy and personalization are explained at the moment of decision.
- Collections include grouping plus search, sort, or filter when growth is expected.
- Search, filter, and sort states are visible, recoverable, and preserved where useful.
- Navigation matches the task model.
- Long flows include progress, save, back, undo, and resume where relevant.
- State messages are useful and non-blaming.
- Media and icons have text support.
- The experience works without hover, color-only meaning, precise pointer control, or perfect memory.
