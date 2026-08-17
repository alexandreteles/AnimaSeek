# Case Studies

Use this reference when the user needs precedent, stakeholder rationale, or concrete examples that show how inclusive design decisions have improved product experiences. Do not use it as a general principles file; the main skill already covers the core method. Use this file to make the method credible through specific product stories.

Treat these examples as documented precedents, not as current product status. When a case provides quantitative impact, use the number. When it provides qualitative impact, do not invent metrics.

## How To Use These Cases

Match cases by the design tension, not by industry. A developer-tool case can support an AI writing assistant if both involve interruption, cognitive load, and user control. A packaging case can support a web onboarding flow if both involve first-use wayfinding, access paths, and setup friction.

When presenting a case to stakeholders, use this structure:

1. Name the analogous mismatch.
2. Identify the cognitive, emotional, physical, or contextual demand.
3. Describe the intervention in one sentence.
4. State the measured or observed outcome, if available.
5. Translate the precedent into the user’s product surface.

Avoid copying UI literally. Extract the decision pattern: control over timing, guided task initiation, memory support, adaptable configuration, evidence for trust, multiple access paths, or reduction of context switching.

## Case Index

| Case | Domain | Primary demand | Use to justify |
| --- | --- | --- | --- |
| Visual Studio IntelliCode | Developer tools | Focus | User control over AI suggestions and autocomplete timing |
| Focus across Outlook and Windows | Calendar, OS, notifications | Focus and recall | Cross-app respect for focus time, delayed notifications, breakthrough rules |
| Whitestar Real Estate Management App | Operations workflow | Focus and recall | Consolidated workflows, task tracking, notes, state saving, and reduced context switching |
| Microsoft Copilot in Outlook | AI email assistant | Memory and communication | Summaries, drafting, coaching, citations, tone control, and task initiation support |
| Viva Pulse | Survey creation and reporting | Learning, decision-making, focus, recall, communication | Templates, wizards, progress, scannable content, anonymity, and psychologically safe feedback |
| Adaptive devices and configurations | Hardware and assistive ecosystems | Operability, perceivability, understandability | One-size-fits-one configuration, published setups, input/output flexibility |
| Xbox Adaptive Controller packaging and accessible packaging elements | Physical-to-digital onboarding | Physical access, wayfinding, cognition | Redefining success criteria, multiple access paths, tactile cues, accessible online extensions |

## Developer Tools: Visual Studio IntelliCode

### Scenario

A Microsoft Developer Tools team working on Visual Studio IntelliCode needed to balance discoverability with interruption. The question was not simply whether intelligent code suggestions should exist. The more inclusive question was how suggestions should appear so that developers could benefit from them without losing concentration.

### Mismatch

The team worked with existing users and reframed recruiting around focus needs rather than only around programming language or professional background. Users included people with and without ADHD who had trouble focusing.

The focus barriers were specific:

- Code suggestions appeared too quickly and automatically.
- The hint bar required too much effort to find.
- The tool’s intrusive qualities made sustained concentration harder.

### Intervention

The team co-designed with users and gave developers more control over when intelligent suggestions surfaced. The feature became less cognitively heavy because the system reduced unsolicited interruption and made suggestion timing more respectful.

### Impact

The case provides quantitative evidence:

- Improvements to inline code insertion suggestions led to a 3.5x increase in regular users of the feature.
- Testing on enhanced inline single-line code change suggestions led to a 176% increase in accepted code change suggestions and a 29% increase in regular users.

### Web Translation

Use this case for autocomplete, AI suggestions, command palettes, inline help, recommendation chips, search suggestions, form completion, chat assistants, and developer or creator tools.

Design implications:

- Suggestions should be useful without appearing as constant interruption.
- Fast automation is not automatically better; timing and control affect adoption.
- Discoverability cannot depend on hidden or hard-to-find hint areas.
- Give people control over whether suggestions appear automatically, on demand, or in a quieter mode.
- Measure acceptance, repeat use, task completion, and user-reported focus, not only impressions or suggestion volume.

### Stakeholder Rationale

This case is useful when a team argues that suggestions must appear aggressively to be discovered. The precedent shows that reducing cognitive load and increasing user control can increase feature use and acceptance.

## Focus Time: Outlook And Windows

### Scenario

Outlook and Windows were optimized for connection, scheduling, and communication, but knowledge workers also needed protected time for deep work. The tools people used to manage work time were not sufficiently integrated to help them preserve focus once a focus session had been scheduled.

The case describes an interruption-heavy work context where distractions can negatively affect productivity, mental health, morale, and the ability to feel in control of work time.

### Mismatch

The team met with customers and identified focus and recall as the primary demands. People with and without anxiety struggled with these demands.

Focus barriers included:

- Non-urgent messages and interruptions were distracting.
- People struggled to establish boundaries during focus sessions.
- Outlook focus bookings did not sufficiently connect to Windows behavior.
- Users wanted cross-app understanding and respect for focus time.
- People needed different timers, music, breaks, and operating-system preferences to do their best work.

Recall barriers included:

- Little or no preparation before a focus session made it harder to orient and begin.
- People needed reminders that they had already dedicated time to a specific task.

### Intervention

The team developed features that reduced distractions, encouraged focus time, and improved recall during transitions between meetings and work.

Specific moves included:

- Updated focus-event visuals to reduce anxiety around dense back-to-back calendar blocks.
- Reminders to help people recall that focus time had been reserved for specific tasks.
- Delayed notifications during focus sessions.
- Customizable breakthrough messages so urgent communication could still arrive.

### Impact

The case reports improved productivity and greater user control through customized schedules and workflows. It does not provide a single product-wide quantitative outcome, so do not invent one.

### Web Translation

Use this case for notification systems, calendars, collaboration apps, dashboards, educational tools, support tools, and any interface that competes for attention.

Design implications:

- A focus setting should affect the relevant ecosystem, not only one screen.
- Non-urgent notifications should be delayed, batched, summarized, or moved to the periphery.
- Urgent messages need explicit breakthrough rules.
- Preparation matters: include notes, task selection, reminders, or a start checklist before a focus block begins.
- People need to customize focus supports, not merely turn everything on or off.

### Stakeholder Rationale

This case is useful when a team treats notifications as harmless. It shows that interruption policy is a product behavior issue, not a preference detail. Respecting focus can support productivity, mental health, and perceived control.

## Real Estate Workflows: Whitestar Real Estate Management App

### Scenario

Whitestar needed a new real estate management app to replace outdated and isolated tools. Employees had to process data from banks, government entities, and real estate owners. Some data was structured; some was unstructured. The workflow needed to support due diligence, bidding, data entry, verification, property management, and fast decisions.

Existing workflows could not support simultaneous bids, critical property information, or property managers overseeing as many as thirty properties.

### Mismatch

The team used inclusive design principles to understand employee motivations and cognitive demands. Focus and recall were the top productivity concerns.

Focus barriers included:

- Interruptions from customer calls and newly obtained documents.
- Multitasking during high-stress bidding periods.
- Extended working hours.
- Poor PC performance that caused slow completion or crashes during critical tasks.

Recall barriers included:

- No easy way to document information from calls or customer conversations.
- No consistent file storage locations.
- No way to track and inform the team of phone-based decisions.

### Intervention

The team built an app that reduced distractions, enabled focus, and reduced context switching. The app included:

- An in-app task manager showing upcoming work, including work split across the team.
- Text recognition for extracting data from unstructured documents.
- Work tracking to recall recent documents and resume work.
- In-app notes and comments.
- Data entry tracking and sharing.
- Automatic state saving.
- AI-supported, cloud-based rapid calculation capability.

### Impact

The result was greater focus by reducing interruptions and the need to context-switch between incompatible data sources. Tracking tools and data sharing reduced memorization effort and gave employees more time for valuable work and wellbeing.

### Web Translation

Use this case for enterprise dashboards, CRM tools, operations portals, admin systems, case-management software, workflow apps, internal tools, and document-heavy products.

Design implications:

- Treat fragmented systems as accessibility and cognition problems, not only information architecture problems.
- Build recall into the workflow: recent documents, decision logs, call notes, state saving, and team-visible updates.
- Reduce context switching by consolidating documents, tasks, comments, calculations, and status.
- Performance is part of inclusion. Slow or crashing tools increase stress and cognitive load.
- Let people resume work after interruption without reconstructing context from memory.

### Stakeholder Rationale

This case is useful when stakeholders want to add another dashboard or integration without changing workflow structure. The precedent shows that inclusion often means removing memory burden and system fragmentation, not adding more places to check.

## AI Email Assistance: Microsoft Copilot In Outlook

### Scenario

Outlook is a central work tool for scheduling, triage, communication, and collaboration. The volume and constant influx of email can become overwhelming. Long threads, prioritization, task initiation, and confidence around important messages can affect cognition, productivity, and mental health.

The case specifically frames email overload as a mental-health and cognitive-demand problem, not just an inbox-efficiency problem.

### Mismatch

The key insight was that AI could reduce the cognitive demands required to keep up with work tasks and decrease anxiety. Because email is part of daily work, helping people succeed in Outlook could preserve motivation, confidence, capability, productivity, business impact, and mental health.

### Intervention

The team paired generative AI work with inclusive design for mental health and gave early access to people with mental health conditions so they could assess the product and co-create improvements.

Copilot in Outlook included three experiences:

1. Summary by Copilot: summarizes an email thread, breaks complex information into key points, and helps people refresh memory quickly. Interactive citations make the summary easier to fact-check and can reduce self-doubt.
2. Draft with Copilot: generates custom drafts or suggested replies to help people overcome task paralysis. Tone, sentiment, and clarity controls help reduce anxiety about how a message may be perceived. The Sounds Like Me option models the draft after a person’s usual writing style.
3. Coach by Copilot: gives customized feedback on a person’s draft. Co-creators appreciated that it guides and educates rather than simply doing the work. The case notes feedback that people wanted help integrating the coaching, leading the team to explore a Rewrite action.

### Impact

Co-creators with mental health conditions confirmed that Copilot helped manage memory and communication demands, increased confidence, reduced cognitive overload, reduced overwhelm, and helped prevent burnout.

### Web Translation

Use this case for AI assistants, writing tools, support agents, summarizers, note-taking tools, customer-service drafting, enterprise search, and any AI feature that touches communication.

Design implications:

- AI should reduce memory and communication load while preserving user control.
- Summaries should be structured and fact-checkable when claims matter.
- Drafting tools should support tone, sentiment, clarity, and personal voice.
- Coaching should teach and build confidence, not only criticize.
- AI should include completion-oriented actions that help people close the loop.
- Co-design with people who experience mental-health-related barriers is appropriate for AI workflows that affect confidence, task initiation, or social perception.

### Stakeholder Rationale

This case is useful when a team frames AI value only as speed or automation. The stronger rationale is that AI can preserve agency and self-efficacy by helping people start, understand, check, improve, and complete communication tasks.

## Feedback Workflows: Viva Pulse

### Scenario

Viva Pulse is part of the Microsoft Viva suite. It enables managers and team leads to request quick, actionable feedback through brief team and project-based surveys. The product supports ad-hoc feedback so leaders can understand team needs and address them in the moment.

### Mismatch

Managers and team leads often juggle multiple tasks. Understanding employee sentiment and acting on concerns requires recall, focus, communication, and decision-making. Leaders may need help defining the right questions, understanding survey results, and identifying solutions.

The case frames this as a mental-health concern because unmanaged workload can create negative self-talk, procrastination, frustration, and reduced psychological safety for leaders and teams.

### Intervention

The team reduced cognitive demands in survey creation, decision-making, and reporting, while supporting focus and mental health for survey authors and recipients.

Key design moves included:

- A pop-up that explains the benefits of Pulse and gives clear next steps to support task initiation.
- A collection of survey templates serving different team needs.
- Established science-backed questions to reduce guesswork and increase confidence.
- Search and filter for choosing questions from a pre-written library.
- True customization through editing or writing custom questions.
- A creation wizard with incremental progress.
- A visual progress indicator at the top of the page.
- Clear and consistent navigation with next, back, undo, and save actions.
- Scannable chunks of text organized with headers and visual containers.
- Highlighted tips and optional deeper explanations through modals.
- Confirmation after actions to help people internalize progress.
- Anonymous results to support open feedback and psychological safety.

### Impact

The case reports that generating and sharing reports can support collective data-driven decision-making and improve mental health, success, and productivity for employees and managers. Anonymous results can help people speak openly and build trust, belonging, happiness, and positive environmental change.

### Web Translation

Use this case for forms, survey builders, reporting tools, admin workflows, onboarding wizards, data dashboards, and feedback systems.

Design implications:

- Templates reduce blank-page load.
- Question libraries reduce learning burden but should remain editable.
- Wizards should show progress and preserve back, undo, and save.
- Complex forms need scannable chunks and optional deeper explanations.
- Confirmation after small actions helps maintain motivation.
- Anonymous or privacy-preserving feedback can be a psychological safety feature, not merely a data policy.

### Stakeholder Rationale

This case is useful when stakeholders believe forms are simple and do not need guidance. The precedent shows that even familiar admin tasks can require learning, focus, decision-making, recall, and communication support when the stakes are social and managerial.

## Adaptive Devices, Accessories, And Configurations

### Scenario

Modern product experiences combine digital and physical interactions. People use computers through many configurations of devices, accessories, settings, and augmentations. The hardware guide frames the goal as one-size-fits-one rather than one-size-fits-all.

The case family includes adaptive controllers and accessories, specialized mice, stands, detachable keyboards, high contrast and display scaling, tactile/visual cue kits, pen grips, adjustable hardware, refreshable braille displays, and input hubs with configurable button behavior.

### Mismatch

Standard device assumptions often exclude people whose needs differ in operability, perceivability, or understandability. People may need different input devices, output channels, tactile cues, posture support, display distance, grip support, press timing, button mappings, or settings combinations.

The guide emphasizes that disabled people are experts on the barriers they face, but they may not always know which devices or configurations could help. Awareness, demonstration, and community knowledge are part of access.

### Intervention Patterns

The documented patterns include:

- A 2-in-1 setup that can move closer to the user, supported by a stand, wireless keyboard, and wireless mouse.
- High Contrast mode and increased display scale for perceivability.
- The Surface Adaptive Kit for tactile and visual cues on keyboard, power cable, and ports.
- Specialized mice for people who cannot use a conventional mouse.
- Support materials with demonstrations by disabled users, not only instructions.
- A Surface Pen enclosure that reduces fingertip pressure and discomfort while improving grip and lowering weight.
- Adjustable kickstands, screen angles, and controller thumbsticks.
- Microsoft Adaptive Hub configuration, including different actions based on press timing.
- Humanware Brailliant BI 20X as a refreshable braille display accessory.
- Kensington Expert Mouse as a power-user device whose form can also support people with limited fine motor control.
- Publishing examples and success stories so people can discover setups that may work for them.

### Web Translation

Use this case family for accessibility settings, account preferences, editor configuration, dashboards, hardware setup flows, product onboarding, input mapping, kiosk interfaces, and product support.

Design implications:

- Treat configuration as augmentation.
- Make settings understandable, discoverable, portable, and recoverable.
- Support different input and output combinations: keyboard, pointer, touch, switch, voice, screen reader, braille, magnification, captions, and high contrast.
- Provide presets and examples of successful configurations.
- Explain settings with demonstrations from people who use them.
- Avoid assuming that more options always help; balance functionality with cognitive simplicity.
- Assume someone will need an augmentation, even if the core product works for many users.

### Stakeholder Rationale

This case is useful when a team treats settings as secondary polish. It shows that configuration can be the core accessibility mechanism. It also supports an awareness argument: building a feature is insufficient if people cannot discover, understand, or see someone like them succeeding with it.

## Accessible Packaging And The Xbox Adaptive Controller

### Scenario

Before the Xbox Adaptive Controller, Microsoft packaging success was largely defined by product protection, brand expression, curated unboxing, execution quality, business needs, and cost. The Xbox Adaptive Controller changed that expectation. The product was built for gamers with limited mobility to connect switches, buttons, mounts, and joysticks into a custom setup. Packaging for such a product could not ignore access.

The team had to revisit what packaging success meant: an inclusive product needed packaging that attempted to be accessible as well.

### Mismatch

Traditional packaging can assume fine motor control, strength, vision, grip, safe reach, tool availability, and tolerance for complex sequences. These assumptions create barriers before a person ever reaches the product.

The same mismatch applies to web setup and onboarding. A product can be technically accessible after login but still exclude people through inaccessible purchase, delivery, setup, account creation, download, activation, pairing, or first-run flows.

### Intervention Patterns

The packaging guide documents several patterns that translate into digital product design:

- Simple sequences are better than fewer complicated steps.
- Identifiable elements make entry points and actions discoverable.
- Material choices matter because people improvise when a package is inaccessible.
- Multiple access points let people engage on their own terms.
- Reduced pivot-points lower the number of motions, postures, and axes required.
- Low physical effort increases comfort and safety.
- Size, space, and stability provide room for reach, manipulation, and varied body position.
- Mindful moments align physical and visual cues so each moment leads to the next logical step.
- No-tools-needed design avoids unsafe workarounds.

### Concrete Accessible Elements

Use these as physical-to-digital analogies:

- Outer box seals: make the first moment discoverable with visual and tactile cues, graphic arrows, leverage points, protruding pull tabs, and minimal unnecessary adhesive.
- Tabs: provide visible leverage points; use consistent graphic cues; choose durable materials and test performance.
- Loops: allow multiple engagement styles, but must be strong, accessible, appropriately sized, and given space.
- Die cuts and notches: create egress points for smaller components and focal points for icons or cues.
- Tactility: reinforce calls to action and wayfinding with touch; tactile texture can increase grip and orientation.
- Feature iconography: communicate critical content quickly; support icons with text where possible; test meaning before deployment.
- Wayfinding iconography: communicate lift, pull, discovery, and component identification without relying on language alone.
- Channels and cavities: create negative space so people can get under or around an object when tabs or loops do not fit the situation.
- Braille: can identify products and signal that information is nearby; consistent placement supports discovery.
- QR codes: reduce the need to remember or type URLs; pair with tactile cues, text URLs, high contrast, and mobile-friendly destinations.
- Online extensions: provide accessible equivalents and deeper instructions through well-formatted HTML, captions, transcripts, and visual descriptions.

### Web Translation

Use this case for product setup, onboarding, checkout, account activation, pairing, installation, support, documentation, and any experience that crosses physical and digital surfaces.

Design implications:

- Do not treat the first-use path as decorative. It is part of accessibility.
- Provide multiple paths to the same important action.
- Give clear affordances for where to start and what to do next.
- Use visual, textual, tactile, and digital cues together where the experience crosses into the physical world.
- Provide accessible online instructions, not just a QR code.
- Make digital support pages work for screen readers, keyboard navigation, captions, transcripts, zoom, and mobile use.
- Include safe fallbacks for people without smartphones, stable internet, or fine motor control.

### Stakeholder Rationale

This case is useful when a team says accessibility begins after the product is opened or after the user reaches the app. The precedent shows that access starts at the first point of interaction and that success criteria sometimes need to change when a product is intended to include people who were previously excluded.

## Cautionary AI Precedents

Use these when a stakeholder needs risk rationale for AI governance in a case discussion. They are not product success stories; they are examples of how exclusion can enter AI systems.

- Dataset bias: machine vision or camera tracking that works for only a subset of users because training data excludes races, skin tones, abilities, contexts, or environments.
- Association bias: translation or language systems that reinforce stereotypes, such as gendered assumptions about roles.
- Automation bias: automated conclusions that override human goals, social context, or self-expression, such as beautification filters that enforce a narrow beauty standard.
- Interaction bias: systems that learn from malicious or toxic user input and reflect that behavior back to customers.
- Confirmation bias: personalization that narrows discovery by repeatedly showing people more of what they already bought, chose, or were assumed to prefer.

Use these cautions alongside Copilot-style precedents: AI can reduce cognitive load and increase confidence only when people can understand, check, correct, redirect, and control it.

## Cross-Case Patterns To Extract

### Control over timing

IntelliCode and focus-time features both show that intelligent systems should not assume the right moment to interrupt. Use this pattern for notifications, AI suggestions, autocomplete, alerts, and proactive help.

### Memory support

Focus time, Whitestar, Copilot in Outlook, and Viva Pulse all reduce the need to reconstruct context from memory. Use this pattern for summaries, recent activity, saved state, task lists, notes, citations, progress indicators, and decision logs.

### Task initiation support

Copilot drafts and Viva Pulse templates show that blank-page moments can create exclusion. Use this pattern for prompt starters, templates, examples, guided first steps, and low-risk drafts.

### Psychological safety

Copilot coaching, Viva Pulse anonymity, and mental-health-centered feedback all show that the emotional experience shapes cognition. Use this pattern when the product involves judgment, communication, feedback, management, or social risk.

### Ecosystem behavior

Focus across Outlook and Windows, adaptive devices, and accessible packaging show that inclusion often depends on cross-surface behavior. Use this pattern when the user moves between apps, hardware, documentation, support, and physical setup.

### Discovery without overload

Developer suggestions, device awareness, packaging iconography, and Viva templates all show that discoverability must be balanced with cognitive load. Use this pattern when teams want to expose more features, shortcuts, settings, or AI options.

### Accessible success criteria

The Xbox Adaptive Controller packaging case shows that inclusive products may require teams to change what success means. Use this pattern when stakeholders define success too narrowly around aesthetics, speed, engagement, or cost.

## Stakeholder Objection Map

### “Users will discover it if it is useful.”

Use IntelliCode, adaptive devices, and packaging. Utility is not enough. People need the right timing, visible affordances, demonstrations, and discoverable access points.

### “More automation means more value.”

Use Copilot in Outlook and AI cautions. Automation is valuable when it reduces load while preserving control, fact-checking, authorship, and the ability to redirect.

### “Notifications drive engagement.”

Use focus across Outlook and Windows. Interruptions have cognitive and emotional costs. Better engagement may come from delaying, batching, or letting urgent messages break through intentionally.

### “This is an edge case.”

Use adaptive devices and accessible packaging. A pronounced need often reveals a design pattern that benefits many people across permanent, temporary, and situational contexts.

### “Forms and surveys are straightforward.”

Use Viva Pulse. Survey creation can require learning, decision-making, communication, confidence, and psychological safety. Templates, progress, and editing control can reduce that load.

### “Enterprise users can manage complexity.”

Use Whitestar. Professional workflows can fail when memory burden, fragmented tools, slow systems, and high-stress context combine. Expertise does not eliminate cognitive load.

### “Packaging or setup is not part of the web experience.”

Use Xbox Adaptive Controller packaging and online extensions. Access begins before the app. Setup, QR codes, documentation, product registration, pairing, and support pages are part of the end-to-end experience.

## Case-Based Sentence Patterns

Use concise precedent statements like these:

- “The IntelliCode case shows that making intelligent suggestions more controllable can improve adoption; control is not the opposite of discoverability.”
- “The Outlook and Windows focus case shows that focus settings need ecosystem behavior, delayed notifications, and urgent-message exceptions, not a single mute toggle.”
- “The Whitestar case shows that workflow fragmentation is a cognitive accessibility issue because it increases recall burden and context switching.”
- “The Copilot in Outlook case shows that AI value is strongest when it helps people summarize, draft, coach, check, and complete work while preserving authorship.”
- “The Viva Pulse case shows that templates, progress, and anonymity can reduce cognitive demand and support psychological safety in feedback workflows.”
- “The adaptive-device cases show that configuration, setup examples, and community demonstrations are part of accessibility, not afterthoughts.”
- “The accessible-packaging case shows that success criteria must include the first point of interaction, not just the core product.”

## Guardrails

Do not claim every case had quantitative impact. Only IntelliCode includes the specific adoption and acceptance metrics listed here.

Do not generalize from a product name alone. Always explain the mismatch and design pattern.

Do not use these cases to argue that inclusive design eliminates the need for accessibility testing, clinical expertise, privacy review, security review, or legal compliance.

Do not present “more personalization” as automatically inclusive. Personalization must be understandable, consentful, reversible, and not overwhelming.

Do not use AI precedents to justify silent automation. The inclusive pattern is guided assistance with user control, correction, evidence, and completion support.
