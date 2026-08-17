# Guidance and Learning

Use this reference when a web experience asks people to learn something, adopt a new feature, complete setup, understand a changed workflow, recover from confusion, or move from novice use to confident use.

This file focuses on the details that are too specific for the core Inclusive Design System skill: guided learning, first-run experiences, onboarding, help content, documentation, tutorials, contextual assistance, and learning-style support.

## Learning Is A Product Demand

Treat learning as a cognitive demand, not as a support-team afterthought. A feature is not ready just because the control exists. It is ready when people with different confidence levels, contexts, mental states, devices, and learning approaches can understand what it is for, how to begin, how to recover, and how to know they succeeded.

Learning demand increases when the product introduces unfamiliar concepts, hidden controls, abstract models, irreversible choices, new vocabulary, multi-step setup, privacy decisions, cross-device configuration, or AI behavior that is hard to predict.

When a task requires learning, first map:

- Motivation: why the person wants to learn or complete the task.
- Goal: what outcome they are trying to reach.
- Task: what they must do in the interface.
- Existing knowledge: what the design assumes they already know.
- Cognitive demands: learning, focus, decision-making, recall, and communication demands required by the task.
- Emotional conditions: confidence, stress, anxiety, time pressure, sense of risk, and self-belief.
- Context: device, bandwidth, environment, assistive technology, interruptions, and offline needs.

Do not begin by asking, “What tutorial should we add?” Begin by asking, “Where does the current experience require knowledge the person may not have?”

## Learning Modes

Design for several learning modes at once. Do not treat any mode as a permanent identity.

### Trial-and-error / tinkering

This person wants to explore directly, discover by doing, and learn from system response. They may reject long onboarding if it blocks the task.

Support them with:

- Safe defaults
- Undo and reset
- Sandboxes or preview modes
- Clear feedback after each action
- Examples they can inspect or copy
- Lightweight hints that can be dismissed
- Low-stakes experimentation
- Fast access to advanced controls once they understand the model

Do not force a full tutorial before they can touch the product.

### Semi-structured / self-guided learning

This person wants direction but not hand-holding. They may prefer recipes, checklists, examples, templates, short videos, annotated screenshots, or a searchable help article.

Support them with:

- Step lists with visible progress
- “Show me how” links at the point of need
- Templates and worked examples
- Searchable documentation
- Short task-based articles
- Decision aids and prerequisites
- Clear estimates of time, effort, and expected outcome

Do not make them infer the full workflow from scattered tooltips.

### Structured / guided learning

This person wants more explicit support before or during action. They may need an expert, instructor-like flow, wizard, walkthrough, or repeated reassurance.

Support them with:

- Guided setup
- Walkthroughs with checkpoints
- Clear prerequisites
- Plain-language explanations
- Visual aids and demonstrations
- Re-entry into onboarding
- Confirmation after each meaningful step
- Accessible help from a person or assistant when available

Do not imply that needing structure means the person is less capable. The need may come from novelty, risk, stress, low confidence, high stakes, or context.

## Guidance Changes Over Time

A person may want structured help for the first task, self-guided help for the second, and direct exploration later. Another person may want guidance throughout. The same person may shift modes when the task becomes high-stakes, time-limited, emotionally loaded, or unfamiliar.

Design guidance as an adjustable layer:

- Start with a choice of paths.
- Let people skip, pause, resume, or restart guidance.
- Let people reduce or increase the amount of guidance.
- Let guidance fade as confidence increases.
- Keep re-entry points predictable.
- Preserve settings across sessions when appropriate.
- Never make “skip” mean “never show me help again.”

## Guidance Journey Model

Use this model for onboarding, tutorials, documentation, support content, and new-feature introduction.

### 1. Discover

People must be able to find help from more than one place.

Provide multiple entry points:

- First-run prompt
- Help menu
- Inline “learn more” links
- Empty states
- Command palette or search
- Settings page
- Documentation hub
- Contextual callout
- Support chat or AI assistant
- Example gallery
- Template picker
- Offline or downloadable material when the task may occur without connectivity

The best entry point depends on context. A person setting up a device, using a mobile browser in a noisy space, or learning a feature without Wi-Fi should not depend on a single online article or video.

### 2. Orient

Before asking someone to act, tell them what they are about to do.

Include:

- The outcome the flow will produce
- Why the task matters
- Estimated time or number of steps
- Required materials, permissions, files, accounts, devices, or data
- Risks or consequences
- Whether the action is reversible
- What happens if they stop midway
- Where to return later

Use orientation screens sparingly. A useful orientation screen helps people choose whether to proceed. It does not market the feature, repeat brand language, or list abstract benefits.

### 3. Choose A Guidance Path

When practical, offer paths such as:

- “Guide me step by step”
- “Show a quick example”
- “Use a template”
- “Let me explore”
- “Read the full documentation”
- “Ask for help”

Make the paths descriptive. Avoid vague choices like “Basic” and “Advanced” unless the distinction is clearly explained.

### 4. Act With Support

During action, place guidance near the relevant control or decision.

Good in-flow guidance:

- Shows only what is needed now
- Explains unfamiliar terms where they appear
- Uses examples tied to the current task
- Allows people to expand for more detail
- Keeps the primary action visible
- Avoids unnecessary modals
- Maintains keyboard and screen-reader flow
- Avoids covering the thing being explained

Use progressive disclosure. Top-level guidance should be brief. Deeper explanation should be available without forcing everyone through it.

### 5. Receive Feedback

Feedback teaches the system model. Every meaningful action should answer:

- Did it work?
- What changed?
- What is still required?
- What should I do next?
- Can I undo or edit it?

Feedback should be visible, specific, and close to the action. Use success states, progress markers, inline validation, confirmation summaries, and next-step prompts. Avoid generic “Done” messages when the consequence is not obvious.

### 6. Recover

Assume people will get interrupted, close a modal, misunderstand a step, lose connection, or decide to revisit the task later.

Provide:

- Save and resume
- Back and undo
- Drafts
- Recent activity
- Breadcrumbs
- “Start over” without data loss when possible
- Re-entry into onboarding
- A visible help path from error states
- A short recap of what has already happened

Recovery is part of guidance. A user who can recover does not need to relearn the whole flow.

### 7. Build Confidence

Guidance should help people feel capable, not dependent.

Build confidence through:

- Small completions
- Plain confirmation
- Positive feedback for what worked
- Optional explanations of why a step mattered
- Practice areas with no consequences
- Examples of successful use from people with similar needs
- Fewer tips as a person demonstrates competence
- Shortcuts and expert controls introduced only after the underlying model is clear

## First-Run Experiences

Use a first-run experience when the product introduces a new concept, multi-step setup, unfamiliar AI behavior, account or privacy decisions, device pairing, cross-app connection, or a workflow where failure is costly.

Do not use first-run experiences as decoration, product marketing, or a tour of every feature.

A first-run experience must include:

- A clear purpose
- A short estimate of effort
- A skippable path
- A re-entry path
- Progress indication
- Back and pause controls where relevant
- Accessible keyboard and screen-reader behavior
- No hover-only instruction
- No auto-advancing content without control
- No essential information hidden in animation

When the first-run flow is closed, provide a return path in a predictable location such as Help, Settings, a feature header, or an empty state. People may need to process information in their own time.

## Onboarding For New Features

Introduce new features contextually. A new-feature prompt is most useful after a person expresses intent or reaches a moment of likely need.

Prefer:

- Inline placement near the relevant action
- A short explanation of what is new
- A concrete example
- “Try it now” and “Not now” actions
- A durable help link
- A way to disable or reduce future prompts

Avoid:

- Global banners unrelated to the current task
- Repeated novelty badges
- Pop-ups that appear before the person understands the page
- Prompts that interrupt text entry, reading, coding, or decision-making
- Red or urgent visual treatment for non-urgent learning content

When a feature changes an existing workflow, include “What changed,” “Why it changed,” “How to do the old task now,” and “How to revert or adjust settings” when applicable.

## Documentation And Help Content

Documentation should be organized around what people are trying to do, not around internal feature names.

A good help article structure:

1. Task title in user language
2. One-sentence outcome
3. Before you start: prerequisites, permissions, devices, files, or risks
4. Steps with one action per step
5. Expected result after major steps
6. Troubleshooting for common failure points
7. Related tasks that reflect likely next needs

Write help content with these rules:

- Use familiar language.
- Define product-specific terms before relying on them.
- Put the most likely answer first.
- Use headings that support scanning.
- Keep paragraphs short.
- Use bullets for procedures and conditions.
- Give examples that map to real goals.
- State what happens next in button labels and links.
- Remove detail that does not change action or understanding.
- Avoid blame, shame, or “obvious” framing.

Content should match the person’s frame of mind. Someone reading a setup guide may be curious. Someone reading an error page may be worried. Someone reading privacy guidance may be cautious. Tone and detail should adjust accordingly.

## Multiple Formats

Do not rely on a single format for learning.

Provide combinations such as:

- Written steps
- Images or diagrams
- Short captioned videos
- Transcripts
- Audio narration when useful
- Interactive demos
- Templates
- Checklists
- Examples
- Side-by-side comparisons
- Glossaries
- Printable or offline instructions

Every media format needs an accessible alternative. A video tutorial needs captions and a transcript. An image needs useful alt text or adjacent explanation. Audio-only instruction needs a text equivalent. Interactive demos must work with keyboard and assistive technology.

## Search, Navigation, And Findability

People who need help should not have to know the correct internal term to find it.

Design help findability with:

- Search that accepts synonyms and user-language queries
- Common task categories
- Role- or goal-based navigation when appropriate
- “Related to what you are doing now” links
- Recent help articles
- Clear breadcrumbs
- Filters when documentation accumulates
- Keyword highlighting in search results
- Expandable sections for long content

Settings and help architecture should be tested against people’s mental models. Use card sorting and co-design when categories are unclear.

## In-Flow Help Patterns

Use these patterns when help belongs inside the product rather than in a separate documentation surface.

### Inline explanation

Use for labels, settings, permissions, terms, and consequences. Keep the short version visible. Let people expand details.

### Example text

Use when a field or prompt is open-ended. Show realistic examples without making them the only acceptable form.

### Empty state guidance

Use when a collection or dashboard has no items yet. State what will appear there, why it matters, and the smallest useful next step.

### Checklist

Use for setup and multi-part tasks. Each item should be independently understandable, show status, and link to the relevant action.

### Wizard

Use for linear, high-dependency setup. Let people go back, review, save, and exit. Do not hide consequences until the final step.

### Coach mark or callout

Use only when the information is immediately relevant. It must be dismissible, keyboard accessible, and not block core content.

### Tooltip

Use for optional clarification, not essential instruction. Tooltips must not require hover only. They must be reachable and dismissible with keyboard and assistive technology.

### Guided demo

Use when a person needs to understand behavior before applying it to real data. Provide a way to exit and a way to repeat.

## AI As A Learning Aid

AI can support learning by summarizing, drafting, explaining, coaching, generating examples, translating jargon, or helping people initiate a task. Use it when it reduces cognitive demand and preserves agency.

AI guidance must:

- Provide prompt starters and examples.
- Use structured prompt fields when blank-page anxiety is likely.
- Explain privacy, data use, limitations, and bias considerations in plain language.
- Offer fact-checkable references when claims matter.
- Include positive feedback, not only corrections.
- Provide task-closing actions such as finish, save, send, apply, or review.
- Let people edit, undo, regenerate, minimize, dismiss, or opt out.
- Avoid pretending to be a human expert.
- Avoid locking people into a single recommended path.

AI tutoring and coaching should teach the person the model when possible. It should not silently do the task in a way that leaves the person less able to understand or control the result.

## Guidance Without Harmful Interruption

Guidance can become a distraction. Treat every learning prompt as an interruption with a cost.

Before showing guidance proactively, ask:

- Did the person request help?
- Did they express intent that makes the help relevant?
- Is there evidence they are stuck?
- Is the message actionable now?
- Can the message wait?
- Can it appear in the periphery instead of a modal?
- Can the person control future frequency?

Use full-attention prompts only for high-consequence learning moments, such as unsafe actions, irreversible choices, privacy exposure, or setup steps that block progress. Use inline or peripheral guidance for tips, suggestions, and optional learning.

## Context And Environment

Guidance should work across contexts. Account for:

- Mobile and desktop
- Touch, keyboard, mouse, pen, switch, voice, and screen reader use
- Low bandwidth
- Offline or no-Wi-Fi moments
- Bright light, noise, public spaces, meetings, and multitasking
- One-handed use
- Fatigue or limited reach
- High contrast, text scaling, reduced motion, and reduced transparency
- Cross-device setup
- Assistive or adaptive hardware

For device setup, configuration, and physical-digital tasks, include demonstrations by disabled users when appropriate. Show real configurations and workflows so people can recognize a setup that may work for them.

## Settings As Learning Surfaces

Settings often teach how a product can adapt. Do not bury learning inside unexplained toggles.

For settings:

- Explain what each setting changes.
- Show examples or previews for visual, motion, sound, privacy, and notification settings.
- Group settings according to user mental models.
- Provide an onboarding wizard for complex customization.
- Let people retain preferences across sessions.
- Surface relevant settings inside the flow, not only in a settings page.
- Keep privacy and data-sharing settings plain, visible, and reversible.

Avoid too many undifferentiated options. Options should expand control, not create a new burden.

## Form And Task Guidance

Forms are learning surfaces when the person does not know what information is expected.

Use:

- Clear labels
- Examples near fields
- Formatting guidance before errors occur
- Inline validation that does not interrupt typing unnecessarily
- Plain error messages with the solution first
- Review screens before high-stakes submission
- Save drafts for long forms
- Progress indicators for multi-step forms
- A visible support path

Do not require memory across steps. Repeat necessary context where decisions are made.

## Tutorials And Novice-To-Expert Flows

Build a path from first success to fluency.

For novice users:

- Reduce prerequisites.
- Start with one meaningful task.
- Provide direct instruction and examples.
- Explain consequences.
- Keep visible progress.
- Confirm success.

For intermediate users:

- Offer templates, patterns, and comparisons.
- Let people choose how much explanation is visible.
- Provide shortcuts after the core model is understood.
- Encourage practice through low-risk tasks.

For expert users:

- Keep guidance out of the way by default.
- Preserve discoverability of advanced actions.
- Provide keyboard shortcuts and dense reference material.
- Keep help searchable and available.
- Avoid removing guidance entirely; experts still need help in unfamiliar or high-stakes contexts.

Do not design a binary “novice mode” and “expert mode” that traps people. Let people move between levels by task and context.

## Evaluation Criteria

Evaluate learning support with human outcomes, not only completion or engagement.

Ask:

- Can people identify the purpose of the feature?
- Can they choose an appropriate learning path?
- Can they complete the first meaningful task?
- Do they know what changed after each step?
- Can they recover after closing the guidance?
- Can they revisit onboarding later?
- Can they use help without losing focus?
- Can they find help using their own words?
- Does the experience support guided, self-guided, and trial-and-error learning?
- Do people feel more capable after the experience?
- Does the design reduce anxiety, confusion, and self-blame?
- Does the design work with assistive technology, zoom, keyboard, reduced motion, and low bandwidth?

Use open-ended feedback as well as task metrics. Ask how the experience made people feel, what they expected, what they feared, what they ignored, and what they wanted to revisit.

## Research And Recruiting For Learning

Recruit by learning need and context, not by diagnosis alone.

Include people who:

- Prefer to tinker first
- Prefer examples or recipes
- Prefer structured guidance
- Feel high stress or anxiety when learning something new
- Have ADHD, ADD, autism, dyslexia, dysgraphia, dyscalculia, or other relevant lived experiences when appropriate
- Have varying tech literacy, language ability, age, education, income, work context, and access to time and support
- Use assistive technology or adaptive devices

Use no more than a few cognitive spectrums in a screener. A practical learn-focused screener can ask:

- “My first step in learning new technology is experimenting and tinkering with it.”
- “How do you approach learning something new for the first time?”
- “If you were cooking a complicated meal for the first time, what would you do?”
- “If you needed to learn a new computer task, how would you approach it?”
- “If you had trouble with a device input method, how would you solve it?”

Include non-technology scenarios. They help reveal learning behavior without overfitting to tech confidence.

## Co-Design Activities

Use these activities to design and test guidance:

- Learn from experts: interview people who experience exclusion and document strengths, goals, adaptations, and interaction challenges.
- Capture research insights: synthesize motivations, mismatches, access methods, human analogies, and design challenges.
- Human-to-computer role-play: compare a supportive human interaction with the product’s current behavior.
- Human analogy: decide whether the product should behave like a tutor, assistant, coach, librarian, concierge, or peer; interview people in those roles.
- Design a microinteraction: map trigger, feedback, user response, and after-state for each guidance moment.
- Evaluate technology’s role: check whether the chosen technology is the simplest and most appropriate way to support learning.
- Low-fidelity prototype: test guidance steps with paper, script, audio, or role-play before building.
- Context and capability match: test the same guidance in different physical, social, and device contexts.
- Situational adaptation: revise the flow for time of day, environment, bandwidth, interruptions, and shifting capability.

## Implementation Guardrails For Web Guidance

- Use semantic headings and landmarks in help surfaces.
- Keep focus order aligned with visual order.
- When opening guided overlays, move focus intentionally and return it when closed.
- Avoid keyboard traps in tours, modals, and coach marks.
- Use `aria-describedby` for persistent field help and accessible descriptions.
- Use live regions sparingly for step completion or dynamic progress.
- Do not put essential instruction only in placeholder text.
- Do not put essential instruction only in icons, color, animation, hover, sound, or images.
- Respect reduced motion for tours, transitions, demos, and loading states.
- Provide captions and transcripts for videos.
- Make screenshots and diagrams understandable through adjacent text.
- Ensure help can be used at high zoom and narrow widths.
- Avoid fragile absolute-positioned callouts that detach from their target.
- Do not cover the control being explained.
- Preserve help availability in error, loading, offline, and empty states.

## Anti-Patterns

Avoid:

- Mandatory one-size-fits-all onboarding
- Feature tours that explain controls before the person has a goal
- “Skip” controls that permanently hide all future help
- Re-entry to onboarding hidden in an unrelated settings area
- Video-only tutorials
- Hover-only tooltips
- Documentation organized by internal feature names only
- Long paragraphs where people need task steps
- Jargon presented as expertise
- Pop-ups that interrupt focused work to teach optional features
- AI help that gives confident answers without sources or user control
- Personalization that narrows options before the person understands the space
- Guidance that assumes high confidence, perfect recall, fast reading, low stress, or uninterrupted time
- Error messages that make people feel responsible for system failure
- Advanced modes that remove recovery and help
- Tutorials that cannot be repeated
- Help content that is unavailable offline when the task requires offline use

## Output Checklist

When producing a design, audit, or implementation plan for guidance and learning, include:

- The likely learning modes affected
- The first meaningful task a person should be able to complete
- Entry points into guidance
- How guidance can be skipped, resumed, repeated, or reduced
- How the product explains time, effort, prerequisites, consequences, and reversibility
- How help content is structured and searched
- How feedback teaches progress and completion
- How recovery works after interruption or confusion
- How settings and customization are explained
- How the design works across formats, devices, assistive technologies, bandwidth, and context
- What to test with co-creators before release
