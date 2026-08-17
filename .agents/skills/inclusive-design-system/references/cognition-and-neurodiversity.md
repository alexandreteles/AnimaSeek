# Cognition and Neurodiversity

Use this reference when cognitive load, neurodiverse needs, learning style, focus, decision-making, recall, communication, self-efficacy, or tolerance for risk is central to the web experience.

This file is intentionally deeper than the core skill file. It focuses on how to diagnose cognitive mismatches, translate them into interface requirements, recruit for cognitive diversity, and evaluate whether a product adapts to people rather than forcing people to adapt to the product.

## Core Model

Cognitive exclusion happens when an experience asks for more cognitive effort than a person can or wants to spend in the moment. It is shaped by the interaction of:

- Capabilities: what the person can perceive, process, remember, decide, communicate, or control.
- Motivation: why the task matters, what success means, and how much effort the person is willing to invest.
- Emotion and mental health: stress, anxiety, confidence, overwhelm, burnout, curiosity, joy, frustration, or psychological safety.
- Environment: noise, glare, interruptions, social pressure, limited time, poor connectivity, unfamiliar devices, or competing responsibilities.
- Product behavior: guidance, structure, timing, defaults, hidden UI, errors, personalization, notifications, and recovery paths.

A task succeeds when motivation can meet or exceed cognitive load. When motivation is low, emotional state is strained, confidence is reduced, or the environment is hostile, the same interface can become exclusionary even if it worked well before.

## Do Not Design by Diagnosis

Do not begin with “design for ADHD,” “design for autism,” “design for dyslexia,” or similar diagnosis-first framing unless the project is clinical and qualified experts are involved.

Diagnosis is not a stable proxy for need. People with the same diagnosis can have very different needs, and people with different diagnoses can share the same needs. Use diagnosis only as one recruiting dimension, never as the design requirement itself.

Prefer need-based spectrums:

- Low guidance need ↔ high guidance need
- Tinkering learner ↔ structured learner
- High interruption tolerance ↔ low interruption tolerance
- Rapid decision-maker ↔ information-gathering decision-maker
- Strong recall in this context ↔ high need for memory support
- Concise communication preference ↔ contextual/detail-rich communication preference
- High technology confidence ↔ low self-efficacy
- Exploratory risk tolerance ↔ cautious adoption
- Low sensory sensitivity ↔ high sensory sensitivity

## Working Sequence

For cognition-heavy work, use this sequence before proposing components:

1. Name the motivation. Identify what the person wants to feel or achieve: create, learn, grow, belong, recover, decide, connect, complete, explore, avoid harm, or regain control.
2. Name the goal. Define the outcome the person wants, not the feature the product wants used.
3. Name the task. Identify what must be done in the interface to reach the goal.
4. Inventory cognitive demands. Mark every step that requires learning, focus, decision-making, recall, or communication.
5. Identify load spikes. Look for moments with hidden information, unclear consequences, waiting, interruptions, dense content, multiple choices, memory dependence, or irreversible action.
6. Identify capacity reducers. Consider stress, time pressure, low confidence, sensory overload, unfamiliarity, social risk, fatigue, poor connectivity, and assistive technology context.
7. Decide what to remove, reduce, support, adapt, or recover.
8. Co-create with people who experience the relevant need spectrum.
9. Test the design under interruption, first-time use, return use, low confidence, high stakes, and recovery scenarios.

## Cognitive Demand Map

Use this map to decide which cognitive dimensions need deeper treatment.

| Demand | Trigger question | Common web mismatches | Desired product behavior |
| --- | --- | --- | --- |
| Learning | What new concept, tool, feature, or procedure must the person understand? | Assumes prior expertise; help is detached from task; onboarding is one-time only; guidance cannot be skipped or revisited. | Multiple learning paths, contextual help, examples, re-entry points, clear prerequisites, and progress feedback. |
| Focus | What must the person attend to, ignore, or recover from? | Notifications steal attention; clutter competes with primary action; motion/noise interrupts; context switching is required. | Protect attention, reduce distractions, allow focus controls, preserve state, and support return after interruption. |
| Decision-making | What choice must the person make, and what are the stakes? | Consequences are hidden; too many options appear at once; tradeoffs are unclear; high-stakes actions lack review. | Make critical factors visible, explain consequences, show tradeoffs, allow comparison, and support reversible action. |
| Recall | What must the person remember from previous steps, sessions, or external systems? | Requires memory of passwords, previous decisions, hidden settings, partial work, or where things are stored. | Provide state, summaries, reminders, history, breadcrumbs, recent items, notes, save, undo, and “resume” paths. |
| Communication | What must the person understand, express, or negotiate with the system or other people? | Tone is wrong; system assumes preferences; messages are too brief or too verbose; format is single-channel; personalization lacks consent. | Adapt format, timing, tone, and channel; ask preferences; disclose memory; provide multimodal options. |

## Learning

Learning is required whenever a person must acquire knowledge, master a skill, understand a new feature, interpret a new pattern, or recover after the product changes.

### Learning Spectrum

Design for at least three approaches:

| Learning approach | What it sounds like | Product support |
| --- | --- | --- |
| Trial and error / tinkering | “Let me try it and see what happens.” | Safe sandboxing, undo, reversible actions, visible affordances, quick feedback, optional help. |
| Semi-structured / self-guided | “Show me the recipe, then let me adapt it.” | Examples, templates, checklists, progressive tips, searchable help, in-context explanations. |
| Guided / structured | “Walk me through it before I start.” | Step-by-step flows, demos, tutorials, expert guidance, prerequisite checks, first-run experiences. |

Guidance preference is contextual, not an identity. A person may tinker in familiar areas and need structured guidance when the task is new, high stakes, emotionally loaded, or time constrained.

### Learning Load Indicators

Treat these as signs that learning support is insufficient:

- First-time users must infer the purpose of the page from layout alone.
- Help content opens in a separate context and forces task switching.
- A new feature appears without explaining why it matters or how to leave it.
- The product assumes domain language, internal labels, or expert mental models.
- The only guidance is a long document, dense tooltip, or one-time onboarding modal.
- The person cannot revisit onboarding after closing it.
- A tutorial blocks exploration and cannot be skipped.
- The person must learn multiple new concepts before completing one useful action.

### Learning Design Requirements

For a learning-heavy web flow:

- State the end goal before the procedure.
- Show effort expectations: time, steps, prerequisites, risk, skill level, and expected output.
- Provide multiple entry points: “start now,” “show me how,” “use a template,” “watch/read an example,” and “ask for help.”
- Keep top-level explanation brief, then reveal detail on demand.
- Put guidance next to the task it supports.
- Let people choose format: written, visual, interactive, short, detailed, or example-based.
- Include a predictable re-entry point for onboarding, tours, examples, and tips.
- Allow guidance to fade over time without disappearing permanently.
- Provide progress feedback that confirms learning and completion.
- Use familiar language and allow people to ask questions in their own words where possible.
- Do not punish exploration; pair experimentation with undo and safe failure.

### Learning Questions for Design Review

Ask:

- What does the person have to know before this step?
- Which assumptions would an expert make that a newcomer may not share?
- Can a person succeed without leaving the page for help?
- Can guided learners start safely, and can tinkerers skip safely?
- What happens if someone closes onboarding too early?
- What support exists for return users who forgot the process?
- Does every learning step reinforce the person’s motivation, or only the product’s feature agenda?

## Focus

Focus is required whenever the person must select, sustain, switch, divide, or recover attention.

Focus needs vary by context. A person may need total isolation for one task, ambient stimulation for another, and frequent communication for a collaborative task.

### Focus Spectrum

Use multiple focus states instead of one “focused user” assumption:

| Focus need | Description | Interface implication |
| --- | --- | --- |
| Isolated | Needs minimal stimuli and few interruptions. | Quiet mode, reduced UI density, delayed notifications, hidden non-core surfaces. |
| Informed but in control | Wants useful alerts but controls form and timing. | Configurable notification type, frequency, urgency, channels, and breakthrough rules. |
| Neutral | No strong preference in ordinary conditions. | Respectful defaults; do not make attention-costly choices by default. |
| Ambient / some stimuli | Uses mild sound, motion, or environmental activity to focus. | Optional ambient supports, not forced motion or noise. |
| Multi-stimulus | Works in active environments or while monitoring multiple streams. | Clear prioritization, batching, filtering, and recovery cues. |

### Interruption Attentiveness Model

Model system communication like human attentiveness:

| System behavior | Human analogy | Use when |
| --- | --- | --- |
| Frequent real-time communication | A server checking constantly. | Only for urgent, collaborative, time-sensitive, or explicitly requested updates. |
| Occasional interaction | A server checking at natural milestones. | For progress, status, and non-urgent task support. |
| Self-inquiry | A server waits until called. | For tips, promotions, non-essential suggestions, and exploratory help. |

### Focus Load Indicators

Treat these as focus failures:

- Suggestions appear automatically faster than the person can evaluate them.
- Non-urgent messages appear during a high-focus action.
- Badges, red counters, typing indicators, animation, or sound compete with the primary task.
- Multiple panels demand simultaneous attention without priority.
- The person must move between tabs, pages, apps, or documents to complete one step.
- Long waits provide no orientation, forcing the person to monitor the system.
- Context switching causes lost state, lost scroll position, or lost partially entered data.
- A user cannot distinguish urgent messages from low-value interruptions.

### Focus Design Requirements

For focus-heavy work:

- Classify every system communication by urgency, consequence, and required attention.
- Default non-urgent items to peripheral, batched, summarized, or deferred delivery.
- Provide a focus mode or low-stimulation mode for dense or high-stakes workflows.
- Let people temporarily delay notifications without losing urgent breakthrough messages.
- Provide recovery after interruption: saved state, recent context, reminders, and “resume where you left off.”
- Avoid unnecessary animation, autoplay, flashing, bouncing indicators, and aggressive badges.
- Maintain visible hierarchy so the person knows what matters now.
- Minimize context switching by bringing needed information, references, and actions into the flow.
- Optimize performance; slow UI is a focus and recall burden.
- Make focus settings visible at the moment of interruption, not only in a settings page.

### Interruption Audit

For every alert, toast, modal, tooltip, badge, animation, sound, vibration, chat prompt, AI suggestion, or inline recommendation, define:

- Trigger: what causes it to appear?
- Initiator: user, system, other person, automation, AI, timer, or external event?
- Urgency: urgent, important, useful, optional, promotional, or educational?
- Consequence if missed: harm, delay, confusion, inconvenience, or no meaningful consequence?
- Consequence if shown now: lost focus, anxiety, task abandonment, or helpful momentum?
- Required attention: full, partial, peripheral, or deferred?
- Medium: visual, audio, haptic, inline, modal, notification center, status region, email, chat?
- Control: dismiss, snooze, mute, pause, change channel, change frequency, turn off, or allow breakthrough?
- Recovery: how does the person return to what they were doing?

## Decision-Making

Decision-making is required whenever the person must choose between options, judge tradeoffs, accept risk, interpret consequences, or act under uncertainty.

### Decision Spectrum

Design for varied information needs:

| Decision style | Example behavior | Product support |
| --- | --- | --- |
| Single data point | Needs one key fact to act. | Highlight the deciding factor and remove clutter. |
| Multiple data points | Needs a small set of connected facts. | Compare options, group factors, and summarize tradeoffs. |
| All data points in accord | Needs confidence that the whole system makes sense. | Provide detailed review, dependencies, history, rationale, and consequences. |
| Overwhelmed / avoidant | Too many steps or too much information prevents action. | Break choices into smaller decisions, recommend safe defaults, and offer help. |

### Decision Load Indicators

Decision support is insufficient when:

- The product asks for a choice before explaining stakes or consequences.
- Critical information is split across pages, hidden behind jargon, or buried in legal text.
- Many small choices are presented without showing what they build toward.
- Defaults are unclear or risky.
- The person cannot compare options in the same view.
- The person cannot see what will happen next.
- High-consequence actions lack review, undo, or confirmation.
- The safest option is not clear.
- The system says “recommended” without explaining why.

### Decision Design Requirements

For decision-heavy flows:

- Identify whether the decision is low-stakes, high-stakes, reversible, irreversible, personal, financial, social, privacy-related, or safety-related.
- Put the most important consequence close to the control that triggers it.
- Use active button labels that describe the outcome, not generic verbs.
- Provide compare views for multi-option decisions.
- Show defaults, recommendations, and assumptions plainly.
- Explain tradeoffs without forcing a person to read everything.
- Provide “review before submit” for irreversible or high-consequence choices.
- Let people save, pause, consult, or return later when the decision is complex.
- Reduce decisions that are not essential to the person’s goal.
- Offer safe defaults, but avoid paternalistic automation.
- Never rely on fear, urgency, or confusion to drive decisions.

### Decision Questions for Design Review

Ask:

- What must the person decide here?
- How much information do different people need before acting?
- What could go wrong if they misunderstand this choice?
- Can the person compare options without memorizing them?
- Is the consequence visible before, during, and after the action?
- Are the defaults aligned with the person’s goal, not only the business goal?
- Can a cautious person proceed without feeling trapped?
- Can a confident person move quickly without missing critical risk?

## Recall

Recall is required whenever a person must remember information, location, state, sequence, prior decisions, credentials, instructions, or what happened before an interruption.

### Recall Spectrum

Design for varied recall strategies:

| Recall pattern | Product implication |
| --- | --- |
| Automatic association | Familiar tasks can use stable placement and consistent labels. |
| Reminder supported | Provide cues, summaries, breadcrumbs, recent activity, and reminders. |
| Externalized memory | Support notes, saved state, checklists, history, exports, shared records, and task managers. |
| Recovery after loss | Provide reset, restore, undo, duplicate, and “find again” paths. |

### Recall Load Indicators

Recall support is insufficient when:

- The person must remember a previous screen, setting, password, hidden option, or partial answer.
- Progress disappears after navigation, refresh, session timeout, or interruption.
- Similar items lack distinguishing cues.
- Search results do not highlight matched terms.
- File, record, or task locations are inconsistent.
- Decisions made outside the app are not documented inside the app.
- The person must re-enter information the system already has permission to use.
- Loading or long waits make the person forget what they were doing.
- The interface hides where the person is in a process.

### Recall Design Requirements

For recall-heavy flows:

- Preserve task state automatically.
- Provide drafts, autosave, recent items, history, and “pick up where you left off.”
- Use breadcrumbs, progress indicators, summaries, and review screens.
- Make completed and remaining steps visible.
- Support note-taking and comments in-context for work that depends on phone calls, meetings, or external conversations.
- Provide reminders tied to the person’s goal, not generic nagging.
- Show why a reminder exists and let people change it.
- Use stable labels and locations so memory can develop over time.
- Provide search, filter, sorting, keyword highlights, and meaningful grouping in large collections.
- Let people recover from forgotten credentials without shame, delay, or blame.

### Recall Questions for Design Review

Ask:

- What does the person need to remember to succeed?
- Can the product remember it for them safely and with consent?
- What happens after an interruption?
- What happens after returning days later?
- Can people locate previous work, decisions, files, and settings?
- Can people distinguish similar items without opening each one?
- Does the flow require memory because the design is hiding information?

## Communication

Communication is required whenever a person must receive, express, interpret, personalize, or negotiate information with the system or other people.

Communication is not just language. Timing, tone, modality, pacing, body language analogs, icons, layout, visual emphasis, sound, motion, and personalization all communicate.

### Communication Preference Spectrum

Design for varied preferences:

| Preference | Product support |
| --- | --- |
| Short and concise | Summaries, bullets, compact labels, direct action text. |
| Context-rich | Expandable detail, examples, rationale, consequences, and related links. |
| Real-time | In-flow prompts, live updates, timely guidance, conversational support. |
| Infrequent / batched | Digest views, notification centers, delayed prompts, weekly summaries. |
| Visual | Diagrams, icons with text, screenshots, charts, spatial grouping. |
| Written | Clear text, transcripts, documentation, copyable instructions. |
| Audio or narration | Narration, captions, transcripts, playback controls, mute. |
| Multimodal | Combine text, visual, audio, and interaction cues without relying on one channel. |
| Privacy-protective | Minimal personalization, no implicit memory, clear reset controls. |
| Personalization-positive | Remembered preferences, adaptive tone, saved style, continuity across sessions. |

### Communication Load Indicators

Communication support is insufficient when:

- The product assumes one preferred channel or format.
- Important information is only visual, only auditory, only time-bound, or only hidden in hover.
- Tone is judgmental, patronizing, too abrupt, too vague, or too enthusiastic for the situation.
- The product remembers personal information without explaining what is remembered.
- The product fails to remember preferences after repeated use when the person expects continuity.
- Suggestions appear without relation to expressed intent.
- AI feedback only criticizes and does not name what worked.
- Error messages blame the person instead of explaining the next step.
- Important information cannot be fact-checked.

### Communication Design Requirements

For communication-heavy flows:

- Ask communication preferences when they materially affect experience.
- Let people choose concise, detailed, visual, written, audio, or multimodal support where feasible.
- Explain what the system knows, remembers, infers, or personalizes.
- Use inclusive language and a tone that protects psychological safety.
- Match tone to the person’s likely frame of mind: confused, rushed, worried, frustrated, learning, deciding, or recovering from an error.
- Provide positive feedback as well as correction when coaching or AI is involved.
- Provide visible, clickable evidence for factual summaries and high-stakes claims.
- Allow people to minimize, mute, pause, dismiss, reset, or change communication behavior.
- Do not assume familiarity or trust; build it over repeated successful interactions.

### Communication Questions for Design Review

Ask:

- What is the product trying to say, and why now?
- What does the person need to say back?
- Does the message need to be brief, detailed, visual, procedural, emotional, or evidential?
- Is the tone appropriate to the stakes?
- Is personalization consented, useful, and adjustable?
- Is the same information available through more than one channel when it matters?
- Can the person fact-check or challenge the system?

## Self-Efficacy

Self-efficacy is a person’s belief that they can succeed in a task or solve a problem. It is situational. A person may feel highly capable in one product and incapable in another, or confident on one day and overwhelmed the next.

Low self-efficacy often shows up as:

- “I probably did something wrong.”
- “Technology problems are my fault.”
- “This feels impossible.”
- “I need help before I can start.”
- “I avoid trying because I might break something.”
- “I can’t tell whether the system is working.”

### Self-Efficacy Design Requirements

To build self-efficacy:

- Make the first meaningful action small, safe, and successful.
- Explain what success looks like before asking for effort.
- Confirm accomplishments clearly and specifically.
- Celebrate small wins without infantilizing the person.
- Use no-blame error language and put the solution first.
- Provide undo, reset, try again, and support paths.
- Let people preview the effect of changes before applying them.
- Provide examples of completed outputs.
- Use scaffolding that fades with confidence but remains accessible.
- Avoid dead ends, ambiguous loading, unexplained failures, or hidden prerequisites.
- Treat task paralysis as a design problem, not a character flaw.

### Self-Efficacy Review Prompts

Use these prompts in research or review:

- Can people use an unfamiliar feature with only available help?
- Does the product make people feel capable or dependent?
- When something goes wrong, do people blame themselves or the system?
- Does the interface provide a next step quickly enough to preserve momentum?
- Does completion feedback make the person feel successful?
- Are support paths predictable and non-stigmatizing?

## Tolerance for Risk

Risk tolerance affects whether people try new features, adopt updates, accept AI suggestions, change settings, share data, or make decisions under uncertainty.

A cautious person is not resistant to innovation. They may be protecting productivity, privacy, social standing, safety, or emotional energy.

### Risk Tolerance Spectrum

| Risk attitude | Likely behavior | Product support |
| --- | --- | --- |
| Cautious | Waits until features are tested and clearly useful. | Preview, opt-in, reversibility, migration explanation, changelog, safe defaults. |
| Conditional | Tries when value and recovery are clear. | Guided trial, examples, confidence cues, undo, side-by-side comparison. |
| Exploratory | Tries new features even without certainty. | Sandbox, advanced options, discovery paths, lightweight feedback. |
| Loss-avoidant | Avoids updates or changes that could break work. | Backups, restore points, compatibility notes, rollback, “what changes” summaries. |

### Risk Design Requirements

For risk-sensitive flows:

- Separate “try” from “commit.”
- Provide preview, simulation, or sandbox modes.
- State what will change, what will not change, and how to reverse it.
- Keep old paths available during transition when feasible.
- Provide “not now,” “remind me later,” and “do not show again” where appropriate.
- Avoid forced adoption for non-critical features.
- Use calm language for updates and changes.
- Explain data sharing, privacy, and personalization in plain language.
- Do not imply that declining a suggestion is failure.
- Protect against irreversible choices without review.

## Neurodiversity-Sensitive Web Design

Neurodiversity-sensitive design supports people with varied attention, sensory processing, executive function, language processing, memory, social communication, confidence, and learning needs.

Do not collapse neurodiversity into one pattern. Instead, design configurable systems that respect different and sometimes conflicting needs.

### Common Neurodiverse Need Areas

| Need area | Possible mismatch | Product response |
| --- | --- | --- |
| Attention regulation | Unexpected prompts, clutter, task switching, animation, noisy pages. | Focus mode, prioritization, reduced motion, quiet defaults, notification control. |
| Sensory processing | Bright colors, flashing, sound, dense movement, visual noise. | Appearance controls, muted palettes, pause/hide, reduced motion, no autoplay. |
| Executive function | Difficulty starting, sequencing, prioritizing, or finishing. | Clear next step, templates, checklists, progress markers, task initiation prompts. |
| Working memory | Many steps, hidden context, multi-page decisions. | Summaries, breadcrumbs, state persistence, visible completed/remaining steps. |
| Processing time | Fast auto-dismiss, timed tasks, rapid suggestions, live changes. | Pause, extend, save, replay, manual pacing, no forced time limits. |
| Language processing | Dense prose, ambiguous labels, jargon, idioms. | Plain language, examples, headings, labels, definitions, multiple formats. |
| Social communication | Unclear tone, implied expectations, fear of being judged. | Explicit expectations, tone controls, drafts, coaching, privacy, psychological safety. |
| Change sensitivity | Sudden UI changes, missing old paths, hidden updates. | Contextual introduction, re-entry to onboarding, rollback, change summaries. |

### Neurodiversity Anti-Patterns

Avoid:

- Assuming all neurodivergent people want minimal stimulation.
- Assuming all neurodivergent people need extra explanation.
- Treating guidance need as a fixed identity.
- Treating personalization as universally desired.
- Using bright red badges, motion, or sound as default urgency signals.
- Forcing time limits without pause or extension.
- Moving controls, changing labels, or introducing features without context.
- Auto-advancing steps before the person is ready.
- Measuring success by clicks, time-on-site, or engagement instead of goal completion, confidence, and reduced load.

## Component-Level Cognitive Signatures

Use this section to diagnose which demand a component is likely to stress.

| Component or pattern | Cognitive risk | Design move |
| --- | --- | --- |
| AI assistant | Blank-page anxiety, trust, bias, fact-checking, feedback loops. | Prompt scaffolds, source links, task-closing actions, positive feedback, minimize/opt out. |
| Collection, inbox, feed, dashboard | Overload, prioritization, recall, search burden. | Grouping, headers, expandable sections, search, sort, filter, keyword highlights, summaries. |
| Color and badges | Sensory overload, false urgency, color-only meaning. | Limit aggressive color, pair color with labels/icons, allow appearance controls. |
| Confirmation and error | Uncertainty, self-blame, lost flow. | Specific success feedback, no-blame error text, solution first, next action, consistent states. |
| Content | Executive function, scanning, language processing. | Headings, short sections, plain labels, concise top-level text, expandable detail. |
| Feedback mechanisms | Fear, privacy concern, irrelevant questions. | Predictable location, relevant questions, open-ended responses, emotional experience questions, privacy choice. |
| Loading state | Monitoring burden, anxiety, memory loss. | Estimated wait, reason for wait, next-step preparation, efficient code, animation control. |
| Motion and noise | Attention capture, sensory overload. | Purpose-only motion, pause/hide/mute, reduced-motion support, no autoplay. |
| New concept | Learning burden, change sensitivity. | Brief intro, examples, visuals, optional first-run, skip, re-entry. |
| Pop-up | Flow crash, urgency confusion, memory burden. | Use sparingly, state urgency, include action, allow snooze/mute, respect focus state. |
| Settings | Discovery burden, unclear consequence. | Contextual access, plain explanation, card sorting with users, privacy control, persistence. |
| Suggestion | Interruption, shame, automation pressure. | Surface after expressed intent, make actionable, adapt from feedback, allow decline without penalty. |
| Timer | Anxiety, rigidity, speed pressure. | Pause, extend, save-and-continue, count-up option, hide/show, time-free alternative. |
| Wayfinding | Disorientation, recall burden, low confidence. | Progress, breadcrumbs, consistent navigation, clear primary action, back/undo/save. |

## Adaptive System Requirements

When a product adapts to cognitive needs, the adaptation must itself be understandable and controllable.

Adaptation rules:

- Ask before making strong assumptions about communication, focus, privacy, or learning preferences.
- Let people inspect and change what the system inferred.
- Provide reset and “forget this preference” controls.
- Do not hide core functionality based on inferred ability.
- Use behavior signals carefully; quiet behavior may mean focus, confusion, fatigue, or distrust.
- Make automatic changes reversible.
- Retain preferences across sessions only when useful and consented.
- Keep settings discoverable both globally and at the moment of need.
- Avoid creating echo chambers or narrowing discovery through personalization.

## Recruiting for Cognitive Diversity

Recruiting should be tied to the cognitive demand being studied, not to broad demographic labels alone.

### Recruiting Process

1. Figure out the objective: which cognitive demands affect the experience?
2. Identify who is excluded today and why.
3. Write a research plan: goals, methodology, participant count, time, materials, and safety considerations.
4. Build a focused screener around need spectrums.
5. Include diverse demographics and contexts: age, ethnicity, gender spectrum, income, education, language, physical ability, hybrid/remote/in-person work, culture, assistive technology, and access to time and support.
6. Include no more than 3–4 cognitive spectrums and 1–2 open-ended questions unless there is a specific reason.
7. Speak with potential co-creators before the session to ensure fit, consent, access needs, and psychological safety.
8. Compensate participants and close the feedback loop.

### Screener Prompt Library

Use these as starting points. Select only what fits the research objective.

#### Learning

- My first step in learning new technology is experimenting and tinkering with it. Rate from strongly disagree to strongly agree.
- How do you approach learning something new for the first time?
- If you were cooking a complicated new meal for the first time, what would you do?
- If you needed to learn something new on a computer, how would you approach it?
- If you had trouble with the touchpad on your laptop, how would you solve it?

Recruit across tinkering, self-guided, and structured learners. Include people with high stress or anxiety about learning, ADHD/ADD, autism, dyslexia, dysgraphia, dyscalculia, or other learning-related needs when relevant, but select by the need spectrum.

#### Focus

- What best describes how you feel about technology interruptions?
- How do you maintain sustained concentration while focusing on an important project?
- What alerts and interruptions in your day are disruptive versus helpful?
- Task switching with technology is hard and takes me out of focus. Rate agreement.
- I struggle tuning interruptions out when I want to focus on one specific task for a long period of time. Rate agreement.
- I have a hard time selecting what to pay attention to while there is a lot of stimuli around. Rate agreement.
- Technology notifications make focusing difficult. Rate agreement.

Recruit for interruption sensitivity, sustained concentration needs, selective attention, sensory sensitivity, and context switching needs.

#### Decision-Making

- I spend a lot of time doing research before making important decisions. Rate agreement.
- Before deciding, I gather relevant details to be sure of the direction I am headed. Rate agreement.
- I want to be aware of consequences before deciding or acting with technology. Rate agreement.
- When there are too many steps or too much information, I get overwhelmed and often ask for help or avoid making a decision. Rate agreement.
- I want to understand tradeoffs before making a decision. Rate agreement.

Recruit across rapid decision, detail-gathering, consequence-aware, and overwhelm/avoidance patterns.

#### Recall

- How often do you forget where you put important items?
- How many tries does it take to remember where you put something important?
- Imagine starting a new job with multiple logins. How would you manage usernames and passwords?
- Do you write down passwords, use digital tools, reuse patterns, reset often, or remember without assistance?
- What assistance do you use to remember account information, tasks, or decisions?

Recruit for memory aid use, externalized memory systems, password reset patterns, task tracking, and recovery needs.

#### Communication

- How do you prefer technology to communicate important information such as tips, updates, suggestions, or guidance?
- What do you want technology or an AI assistant to know about you over time to personalize communication?
- I do not want technology to know me or use my information to personalize an experience. Rate agreement.
- If I use a technology multiple times and it does not remember useful information, I get frustrated. Rate agreement.
- I want technology to adapt to my communication preferences over time. Rate agreement.
- I do not want technology to assume communication preferences without asking. Rate agreement.

Recruit across concise/detail preferences, real-time/batched communication, multimodal needs, personalization comfort, privacy concern, and processing-time needs.

#### Self-Efficacy

- I can use unfamiliar technology features when I only have the internet as a reference. Rate agreement.
- I am comfortable using technology. Rate agreement.
- How would you approach a new challenge with technology: fun puzzle, challenging but possible, or impossible?
- When there is a significant problem with your computer, how effective do you feel you can be in solving it yourself?

Recruit for low confidence, self-blame, support dependence, and high-confidence contrast.

#### Risk Tolerance

- I avoid using new apps or features before they are well tested. Rate agreement.
- I worry new features will make it harder to get my job done. Rate agreement.
- I avoid new software features unless I feel certain they will be useful. Rate agreement.
- I enjoy trying new features even if I am not fully confident they will help. Rate agreement.
- I avoid running software updates because I worry the update will break something. Rate agreement.
- I enjoy finding lesser-known features and capabilities. Rate agreement.
- How do you feel about adopting new features?

Recruit across cautious, conditional, exploratory, and loss-avoidant attitudes.

## Cognitive Co-Design Activities

Use these activities when the team needs to understand or generate solutions around cognitive needs.

### Motivation to Demand Map

Create a table with columns for motivation, goal, task, cognitive demand, likely mismatch, and support strategy. Fill it for each major flow.

### Interaction Diary

Observe people in real contexts. Record verbal and nonverbal cues, emotional cues, workarounds, pauses, hesitations, repeated actions, help-seeking, and recovery behavior. Compare human-to-human and human-to-technology interactions.

### Human Analogy

Identify the human role the technology is trying to play: teacher, coach, assistant, receptionist, librarian, analyst, guide, editor, or server. Interview people who perform that role well. Translate what makes them effective into system behavior without pretending the system is human.

### Mismatch to Solution

List mismatches one by one. Convert each to a focused “How might we…” question. Generate multiple ideas before filtering. Work one mismatch at a time to avoid overwhelming participants.

### Microinteraction Audit

For a specific interaction, define:

- Is the sequence user-initiated or system-initiated?
- What is the trigger?
- How does feedback begin?
- How does the person interact with the feedback?
- What happens immediately after feedback completes?
- What if the person ignores, misses, dismisses, or misunderstands it?
- What state is preserved for return?

### Prototype Test

Build low-fidelity prototypes that communicate their own function without explanation. Test for both delight and pain points. Role-play system behavior when needed.

### Context and Capability Match

Test the same flow across combinations of physical context, social context, time of day, connectivity, emotional state, assistive technology, and temporary/situational limitation.

## Cognitive Design Heuristics

Use these heuristics when detailed research is not yet available:

- More simple steps can be easier than fewer complex steps.
- Obvious entry points reduce learning and decision load.
- Stable layout and labels support recall.
- Visible progress protects motivation.
- Contextual settings reduce the cost of self-advocacy.
- Reversible actions reduce risk and support exploration.
- Summaries and previews reduce decision load.
- Filtering and grouping reduce collection overload.
- Quiet defaults protect attention.
- Optional richness respects varied learning and communication needs.
- Good performance is cognitive accessibility.
- Privacy clarity is cognitive and emotional support.
- Recovery paths are part of the primary experience, not edge cases.

## Design Output Format

When producing design recommendations from this reference, include:

1. The primary motivation, goal, and task.
2. The cognitive demands in play.
3. The mismatches likely excluding people.
4. The design moves that remove or reduce load.
5. The supports that remain when load cannot be removed.
6. The customization options needed.
7. The recovery paths after interruption, error, or abandonment.
8. The co-design or testing plan.
9. The acceptance criteria.

## Acceptance Criteria

A cognition-sensitive web experience should meet these criteria:

- The person can understand the purpose of the flow without expert knowledge.
- The person can choose a learning path appropriate to their confidence and context.
- The person can complete the task without unnecessary interruptions.
- The person can recover after interruption without losing state or orientation.
- The person can make decisions with visible consequences and tradeoffs.
- The person does not need to remember hidden information to proceed.
- The person can receive information in a format and frequency that works for them.
- The person can adjust settings at the moment of need.
- The person can try, undo, pause, resume, save, dismiss, and return.
- The person feels capable rather than blamed when something goes wrong.
- The design has been tested with people across relevant need spectrums, not only with average or expert users.

## Review Checklist

Use this checklist at the end of a design or audit:

- Did the work begin with motivation rather than feature availability?
- Are the dominant cognitive demands explicitly named?
- Are diagnosis labels avoided as design requirements?
- Are guidance paths available, skippable, and revisit-able?
- Are interruptions classified by urgency and attention cost?
- Are high-stakes decisions slowed down and low-stakes decisions streamlined?
- Are memory supports built into the flow?
- Are communication preferences askable, adaptable, and reversible?
- Are self-efficacy and risk tolerance supported through safe success, preview, undo, and no-blame recovery?
- Are sensory load and motion/noise controlled?
- Are personalization and AI behavior transparent and consent-aware?
- Are settings understandable, contextual, and persistent where useful?
- Are metrics based on task success, confidence, control, emotional impact, and reduced cognitive demand?
