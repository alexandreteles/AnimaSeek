---
name: inclusive-design-system
description: "Design, build, review, and refine inclusive web experiences that reduce physical, cognitive, emotional, social, AI, and contextual exclusion. Use for web UX/UI, product flows, frontends, accessibility reviews, neurodiversity, focus, mental health, onboarding, AI assistants, settings, notifications, forms, dashboards, content-heavy pages, and product-to-digital experiences. Do not use as a substitute for formal legal accessibility compliance, clinical mental-health advice, or medical diagnosis."
license: "AGPL-3.0"
metadata:
  version: "0.1"
  domain: "inclusive web design"
---

# Inclusive Design System

## Overview

Use this skill when creating or auditing web experiences through the Microsoft Inclusive Design lens. The objective is not merely WCAG compliance, although accessibility compliance matters. The objective is to design many ways for people to participate with belonging, control, dignity, and success.

Inclusive design is the method. Accessibility is an attribute of the result. A web product is not inclusive because it has an accessibility checklist; it becomes more inclusive when product decisions are shaped by people with diverse abilities, contexts, motivations, emotions, learning styles, assistive technologies, and lived experiences.

## When To Use This Skill

Use this skill for:

- Web apps, marketing sites, dashboards, forms, onboarding, settings, content systems, checkout flows, support flows, and AI-assisted experiences
- Accessibility, cognitive load, mental health, focus, neurodiversity, learning, recall, decision-making, communication, or notification reviews
- Product flows that combine digital and physical experiences, such as setup, packaging, shipping, device pairing, downloads, product registration, and support
- Research planning, co-design workshops, recruitment screeners, persona spectrum work, and post-launch evaluation
- Requests to make a frontend more usable for people with disabilities, assistive technology users, people under stress, people with limited time, low bandwidth, low confidence, or changing context

Do not use this skill as a substitute for medical diagnosis, clinical mental health guidance, legal accessibility review, or formal conformance testing. Use it to improve design decisions and surface risks; still recommend specialist review where required.

## Core Directive

Design from human diversity, not from the “average user.” Exclusion happens when a design assumes that everyone can see, hear, speak, touch, learn, focus, decide, remember, communicate, move, afford, understand, trust, and persist in the same way.

Treat disability as a mismatch between a person and an environment, system, object, or interaction. In web design, mismatches often appear as hidden navigation, fragile focus order, dense content, inaccessible controls, disruptive notifications, confusing AI behavior, missing feedback, inaccessible privacy choices, motion without control, or one rigid path through a task.

Always work from these principles:

1. Recognize exclusion. Identify where the interface creates mismatched interactions.
2. Learn from diversity. Co-create with people whose needs, abilities, contexts, and adaptations differ from the team’s.
3. Solve for one, extend to many. Design for a pronounced need, then extend the benefit across permanent, temporary, and situational contexts.

## Accessibility Baseline For Web Work

Inclusive design does not replace accessibility implementation. For any web output, include the basics unless the user explicitly scopes them out:

- Semantic HTML landmarks, headings, lists, buttons, links, labels, descriptions, and native form controls where possible
- Keyboard access for every interactive element, logical tab order, visible focus indicators, skip links where useful, and correct focus return after modals, menus, drawers, and route changes
- Programmatic names, roles, states, error messages, loading states, status announcements, and live regions only where appropriate
- Text alternatives for meaningful images and non-text content; decorative imagery hidden from assistive technology
- Sufficient contrast, non-color-dependent meaning, zoom and text scaling support, readable line length, and responsive layout without content loss
- Respect for user settings such as reduced motion, high contrast, color scheme, text size, browser zoom, keyboard-only use, screen readers, switch input, speech input, touch, and low-bandwidth contexts

Never use custom controls, ARIA, animation, canvas, carousels, drag-and-drop, hover-only behavior, or AI-generated content in a way that removes a simpler accessible path.

## Inclusive Web Design Workflow

Start by identifying the person’s motivation, goal, and task. Do not begin with “what feature should we add?” Begin with what people are trying to accomplish and why it matters to them.

Map the flow from intent to completion. For each step, ask what the person must perceive, understand, decide, remember, enter, control, wait for, recover from, trust, and feel safe doing.

Identify mismatches across the persona spectrum: permanent, temporary, and situational. A screen reader user, a person with a migraine, a distracted parent, a person in bright sunlight, a person on public transit, and a first-time customer under deadline pressure may reveal related failures.

Choose the dominant cognitive demands: learning, focus, decision-making, recall, and communication. More than one may apply. Reduce the demand when possible; support it when it cannot be removed.

Co-create early. Do not rely on empathy simulations alone. Bring in people with lived experience before decisions harden, and keep them involved through prototype, launch, evaluation, and revision.

Prototype microinteractions. For each trigger, feedback moment, next step, error, delay, and state transition, define who initiates it, what the person perceives, what control they have, and what happens next.

Evaluate with human-centered metrics. Engagement alone is not a success metric. Measure task completion, perceived control, confidence, cognitive demand, emotional impact, recoverability, flexibility, and whether the experience helps people achieve their goals efficiently.

## Cognitive And Neurodiversity Rules

For any task, motivation must equal or surpass cognitive load. When stress, anxiety, uncertainty, low confidence, sensory load, or time pressure lowers capacity, the same task can become exclusionary.

Do not design around diagnoses unless the project is explicitly clinical and involves appropriate experts. People with the same diagnosis can have different needs, and people with different diagnoses can share the same access needs. Recruit and design around needs and spectrums: focus needs, guidance needs, recall needs, decision support needs, communication preferences, self-efficacy, and risk tolerance.

### Learning

Support trial-and-error, self-guided, and structured learning. Guidance is an approach, not an identity. Provide multiple entry points, examples, walkthroughs, videos or visuals where useful, plain-language help, first-run experiences for multi-step flows, and a predictable way to revisit onboarding. Allow people to skip guidance when they prefer exploration.

### Focus

Protect attention. Reduce interruptions, context switching, visual clutter, noisy motion, and competing calls to action. Provide focus-friendly modes, clear hierarchy, controllable notifications, delayed non-urgent messages, and predictable boundaries between deep work and communication.

### Decision-making

Make consequences, tradeoffs, defaults, and next steps clear. When decisions are high-stakes or irreversible, slow down, explain, confirm, and allow review. When decisions are low-stakes, streamline. Do not bury critical information behind hidden UI or legalese.

### Recall

Do not assume people remember previous steps, locations, passwords, settings, or partial work. Provide save state, drafts, history, breadcrumbs, progress indicators, summaries, recent items, reminders, notes, undo, and “pick up where you left off” affordances.

### Communication

Adapt tone, frequency, format, and timing to the relationship and context. Some people need concise text; others need more context, visual cues, examples, narration, or real-time support. Ask before personalizing deeply, explain what is remembered, and let people change course.

## Mental Health Design Considerations

Use the Preserve, Direct, Customize model:

- Preserve focus, attention, motivation, privacy, agency, psychological safety, and control.
- Direct people through clear, accessible, logical flows with visible progress, clear next steps, decision support, and predictable support paths.
- Customize the experience so people can adapt readability, appearance, notifications, filtering, motion, sound, guidance, and privacy to their shifting needs.

Design for fluctuating capacity. A task that is easy on one day may feel impossible during stress. Reduce cognitive demands, build agency, and avoid patterns that make people blame themselves for system failure.

Celebrate small wins without condescension. Confirmation states, progress markers, and completion feedback should help people feel capable and oriented.

## Pattern Guidance For Web Components

Collections: For inboxes, feeds, long lists, dashboards, files, search results, or threads, use grouping, headings, expandable sections, filters, sorting, search, keyword highlighting, priority cues, and adjustable text or narration options.

Color: Use color intentionally. Do not rely on color alone. Limit bright badges, aggressive red, and unnecessary high contrast that pulls attention away from the task. Provide appearance controls when feasible.

Content: Write scannable, plain-language content. Use headings, short sections, bullets where appropriate, inclusive language, and clear next steps. Test whether the content matches people’s mental models and emotional state.

Confirmation and error states: Confirm success, show progress, prevent dead ends, put the solution first, never blame the person, and use active button text that states what will happen next.

Feedback mechanisms: Place feedback in a predictable location, ask only relevant questions, allow open-ended responses, ask how the product makes people feel, and let people control how much personal information they share.

Loading states: Explain what is loading, why it may take time, and what happens next. Show estimated wait time when possible, code efficiently for different bandwidths, and allow loading animations to be paused or disabled.

Motion and noise: Use motion only when it clarifies. Avoid autoplay. Provide pause, hide, mute, and reduced-motion behavior directly on the element and across the product.

New concepts: Keep top-level explanations brief. Use visuals and examples for complex ideas. Provide wizards for many-step onboarding, but allow skipping and re-entry later.

Pop-ups: Treat pop-ups as interruptions. Use them only when relevant and actionable. Indicate urgency, include available actions in the pop-up, and let people control timing, frequency, and override behavior.

Settings: Put settings where people expect them, explain what each setting does, persist preferences, surface relevant settings in flow, and create psychological safety with transparent privacy and data controls.

Suggestions: Suggestions should follow expressed intent, be immediately actionable, adapt from feedback, and avoid implying that the user is failing when they decline or cannot act.

Timers: Timers can structure tasks but can also create anxiety. Provide pause, save-and-continue, hide/show controls, count-up alternatives, calm visuals, and time-constraint-free options where possible.

Wayfinding: Make location, progress, available options, and best next step obvious. Use consistent navigation, breadcrumbs, back, undo, save, and one clear primary action per step.

## Respecting Focus And Interruptions

Every alert, toast, badge, tooltip, notification, vibration, sound, modal, chat prompt, animation, or AI suggestion competes for attention. Match the interruption to urgency:

- Full attention for urgent, high-consequence, time-sensitive information.
- Partial attention for useful but deferrable status or progress.
- Peripheral or deferred communication for low-urgency tips, promotions, and system updates.

Ask what happens if the person misses the message, what happens if you interrupt them, and whether the system can wait. When in doubt, default to less interruption and more user control.

Adapt to behavior and context. A person may be alone or in a crowd, on fast or slow internet, in sunlight, in a meeting, using assistive technology, emotionally overwhelmed, or recovering from interruption. Let people personalize type, timing, channel, and frequency.

## AI And Automation

AI can reduce cognitive load, initiate tasks, summarize information, support drafting, and increase confidence. It can also trigger anxiety, bias, inaccuracy, over-personalization, privacy concerns, and loss of control.

For AI assistants on the web:

- Provide prompt scaffolds, examples, templates, and low-barrier ways to start.
- Explain privacy, security, limitations, data use, and bias considerations in plain language.
- Keep people in control; AI may guide, educate, draft, summarize, or suggest, but must not override the customer.
- Include visible references or source links where factual checking matters.
- Provide positive feedback as well as improvement feedback.
- Avoid feedback loops; include task-closing actions such as finish, save, send, review, or dismiss.
- Make AI entry points predictable, optional, minimizable, and contextually relevant.

Stress-test AI for dataset bias, associations bias, automation bias, interaction bias, and confirmation bias. Use diverse teams, diverse data, consentful data collection, customer correction loops, safeguards against malicious interaction, and discovery paths that avoid echo chambers.

## Physical, Device, And Input/Output Context

Web experiences live inside physical contexts. People interact through keyboards, touch, screen readers, switches, speech, eye gaze, magnification, braille displays, alternative mice, adaptive controllers, mobile devices, glare, fatigue, limited reach, or one-handed operation.

Design “one-size-fits-one” systems: configurable input, output, layout, density, scale, contrast, captions, transcripts, language, notification channels, and control mappings. Provide useful defaults, but do not make people fight the defaults.

Translate accessible packaging principles to web product flows:

- Simple is best: prefer more simple steps over fewer complicated ones.
- Identifiable elements: make controls, labels, and next actions unmistakable.
- Ready access: provide multiple paths to the same important outcome.
- Reduce pivot-points: avoid drag-only, hover-only, pinch-only, precision-only, or multi-gesture requirements.
- Low effort: minimize physical, cognitive, and emotional work.
- Size, space, stability: support large targets, stable layouts, safe spacing, and predictable containers.
- Mindful moments: align visual, textual, and interaction cues so every moment leads logically to the next.
- No tools needed: do not require add-ons, downloads, special hardware, or unsafe workarounds for core access.

## Research, Co-Design, And Recruiting

Use inclusive design activities across the design process:

- Get oriented: build trust, role-play human-to-computer interactions, learn from experts, and capture research insights.
- Frame: create persona spectrums, persona networks, interaction diaries, and human analogies.
- Ideate: turn mismatches into “How might we…” questions, generate many concepts, design microinteractions, and evaluate technology’s role.
- Iterate: test low-fidelity prototypes with users, observe delight and pain points, and simulate temporary or situational limitations only as a supplement to real participation.
- Optimize: test context and capability matches, situational adaptation, and real-world feasibility.

Recruit by objective. Identify which cognitive areas or access needs matter, write a research plan, and build a focused screener. Include diverse demographics and lived experiences. Use no more than 3-4 cognitive spectrums and 1-2 open-ended questions in a screener unless there is a specific reason. Do not recruit only by medical diagnosis.

Run co-design early, in realistic contexts, with people who face barriers and with cross-functional stakeholders. Listening comes before solutioning. Repeat; the first proposal rarely hits the mark.

## Evaluation And Metrics

Before launch, define human-centered success criteria:

- Can people complete the task with keyboard, screen reader, touch, zoom, slow network, and reduced motion?
- Are motivations, goals, and tasks aligned, or is the product creating disconnected work?
- What cognitive demands remain, and what support reduces them?
- Do people feel successful, safe, confident, respected, and in control?
- Can people recover from errors, interruptions, accidental dismissal, or context switches?
- Are preferences retained and easy to change?
- Does the product gather and act on functional and emotional feedback?
- Does the design avoid optimizing for engagement at the expense of attention and wellbeing?

After launch, evaluate, revise, and close feedback loops. Treat accessibility maturity as a journey: initial, repeatable, defined, managed, optimized.

## Anti-Patterns

Avoid these patterns unless the user explicitly asks to explore them as problems:

- “Normal user” assumptions, one-path flows, and designs based on the team’s abilities
- Diagnosis-first personas, disability simulations as primary evidence, or co-design only after decisions are final
- Dense paragraphs, hidden UI, mystery icons, color-only meaning, auto-playing motion or sound, and spinner-only loading
- Pop-ups, badges, tooltips, or suggestions that interrupt without urgency, control, or relevance
- Engagement metrics that reward distraction, compulsion, or unnecessary time in product
- AI that hides sources, overstates certainty, reinforces stereotypes, automates high-impact decisions, or personalizes without consent
- Settings that are hard to find, unexplained, non-persistent, or disconnected from the moment of need
- Errors that blame the person, dead-end flows, irreversible choices without review, and missing undo
- Forms that require memory, precision, speed, perfect attention, or unnecessary personal disclosure

## Execution Protocol

1. Inspect the product, screenshots, codebase, or brief before prescribing a solution.
2. State the likely motivations, goals, tasks, mismatches, and cognitive demands.
3. Choose an inclusive strategy: preserve, direct, customize, or a combination.
4. Design the smallest accessible path first, then add optional richness.
5. Build or recommend semantic, responsive, configurable components.
6. Include states: default, hover, focus, active, disabled, loading, empty, success, error, interrupted, resumed, and reduced-motion.
7. Add AI only when it reduces demand or supports agency; never because it is available.
8. End with a preflight check and note any areas needing specialist testing or co-design.

## Preflight Checklist

- Did the design identify exclusion as mismatched interaction rather than user failure?
- Did it start from motivation, goal, and task instead of technology?
- Are learning, focus, decision-making, recall, and communication demands understood?
- Are focus, attention, privacy, and control preserved?
- Are next steps, progress, settings, support, errors, and confirmations clear?
- Can people customize readability, motion, sound, notifications, privacy, and guidance where relevant?
- Are AI and automation transparent, optional, source-backed where needed, and under user control?
- Are assistive technology, keyboard, touch, zoom, reduced motion, high contrast, and low bandwidth accounted for?
- Has the work been informed by diverse people and not only by internal assumptions?
- Are metrics based on success, confidence, reduced cognitive demand, and emotional impact, not only engagement?

## References

Load these files as needed. They may be created later; the descriptions below define their intended purpose.

- [references/foundations.md](references/foundations.md) for inclusive design definitions, the three principles, persona spectrum, persona network, mismatch framing, and the distinction between inclusive design and accessibility. Use when setting direction or explaining the methodology.
- [references/research-and-co-creation.md](references/research-and-co-creation.md) for activity-card workflows, co-design sprint structure, recruiting objectives, screeners, and facilitation prompts. Use before research plans, workshops, or prototype testing.
- [references/cognition-and-neurodiversity.md](references/cognition-and-neurodiversity.md) for learning, focus, decision-making, recall, communication, self-efficacy, risk tolerance, and neurodiversity details. Use when cognitive load or neurodiverse needs are central.
- [references/mental-health-patterns.md](references/mental-health-patterns.md) for Preserve/Direct/Customize guidance and detailed component rules for collections, color, content, feedback, loading, motion, pop-ups, settings, suggestions, timers, and wayfinding. Use during component design or audits.
- [references/focus-and-interruptions.md](references/focus-and-interruptions.md) for notification urgency, modality, timing, adaptation, personalization, and mental-cost frameworks. Use when designing alerts, badges, toasts, chat, reminders, or proactive suggestions.
- [references/guidance-and-learning.md](references/guidance-and-learning.md) for guided learning, onboarding, first-run experiences, help content, multiple entry points, and learning-style support. Use for new features, documentation, tutorials, and novice-to-expert flows.
- [references/ai-and-automation.md](references/ai-and-automation.md) for AI assistant behavior, prompt scaffolds, source references, human control, privacy, and bias stress tests. Use for generative AI, recommendations, personalization, summarization, or automated decisions.
- [references/input-output-and-devices.md](references/input-output-and-devices.md) for assistive technology, adaptive input/output, device configuration, high contrast, scaling, tactile or physical context, and one-size-fits-one systems. Use for responsive, multimodal, or hardware-adjacent web work.
- [references/tangible-experience-and-onboarding.md](references/tangible-experience-and-onboarding.md) for translating accessible packaging principles into digital setup, downloads, installation, account creation, returns, shipping, support, and product-to-web experiences. Use when the web flow connects to a physical product or service.
- [references/web-accessibility-baseline.md](references/web-accessibility-baseline.md) for semantic HTML, ARIA, keyboard behavior, focus management, forms, contrast, reduced motion, responsive behavior, and assistive technology implementation. Use before writing or reviewing frontend code.
- [references/content-and-information-architecture.md](references/content-and-information-architecture.md) for plain language, scannability, headings, labels, privacy explanations, collections, search, sort, filter, and navigation models. Use for content-heavy screens and data accumulation.
- [references/evaluation-and-metrics.md](references/evaluation-and-metrics.md) for human-centered success metrics, accessibility maturity, feedback loops, post-launch iteration, and review checklists. Use when defining acceptance criteria or audit reports.
- [references/case-studies.md](references/case-studies.md) for examples from developer tools, focus time, real estate workflows, Copilot in Outlook, Viva Pulse, adaptive devices, and accessible packaging. Use when the user needs precedent or stakeholder rationale.
- [references/implementation-preflight.md](references/implementation-preflight.md) for practical frontend guardrails, component state coverage, testing steps, and inclusive QA prompts. Use at the end of any build or redesign task.
