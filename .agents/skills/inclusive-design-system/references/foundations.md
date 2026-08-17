# Inclusive Design Foundations

## Purpose

Use this file when a project needs direction, methodology, or explanation before detailed component guidance. It defines the foundational language of the Inclusive Design System: inclusive design, accessibility, exclusion, mismatch, persona spectrum, persona network, and the three Microsoft Inclusive Design principles.

This file is intentionally methodological. It should help an agent or designer explain why a design direction is inclusive, reframe a vague request into an inclusive design problem, or set up a research and product strategy before choosing patterns, writing code, or auditing accessibility details.

## Baseline stance

Do not design from the “average user.” There is no normal user whose senses, body, attention, confidence, memory, language, money, time, network, environment, emotional state, device, and technical knowledge are all stable and fully enabled.

Inclusive design starts from human diversity as a resource for better design. The work is not to create one universal experience that treats everyone the same. The work is to create a diversity of ways for people to participate with agency and belonging.

A web experience is inclusive when it adapts to people rather than forcing people to adapt to it. That adaptation may appear as multiple input methods, flexible guidance, configurable settings, clear language, accessible implementation, respectful defaults, recoverable flows, or a design that acknowledges social and emotional context.

## Core definitions

**Inclusive design** is a design methodology that enables and draws on the full range of human diversity. It includes and learns from people with a range of perspectives, especially people who experience exclusion.

**Accessibility** is the set of qualities that make an experience open to all, and the professional discipline that helps achieve those qualities. Accessibility describes attributes of the product and its implementation.

**The practical distinction:** inclusive design is a method; accessibility is an attribute. Inclusive design should make accessibility more likely, but it does not by itself prove accessibility conformance. Accessibility standards and testing are still required. The best work uses both: inclusive methods to decide what should exist and accessibility practice to ensure it works.

**Exclusion** happens when a product, service, environment, system, or social interaction assumes abilities, contexts, or resources that some people do not have in that moment.

**Disability** is not only a personal health condition. For design work, treat disability as a mismatch between a person and an environment, object, interface, process, or society. The mismatch may be physical, cognitive, emotional, social, sensory, economic, linguistic, temporal, or technical.

**A point of interaction** is any moment where a person must perceive, understand, decide, act, wait, trust, recover, communicate, or coordinate with the system or with other people.

**An adaptation** is a workaround, tool, habit, support network, setting, device, or behavior a person uses to overcome a mismatch. Adaptations are high-value design evidence. They show what the current experience fails to provide and what a better experience might support.

**One-size-fits-one** means creating flexible systems that can be tailored to individual needs. It rejects both rigid “one size fits all” products and over-specific solutions that cannot scale beyond one person.

## The three principles

### 1. Recognize exclusion

Exclusion is often created unintentionally when teams use their own abilities, biases, contexts, and resources as the baseline for design. Recognizing exclusion means naming the mismatch instead of blaming the person.

Use this principle to reframe statements like:

- “The user made an error.” → “The flow did not make the safe next step clear.”
- “The user did not read the instructions.” → “The instructions were not discoverable, scannable, or timed to the task.”
- “This is an edge case.” → “This is a point of exclusion that reveals an assumption.”
- “They need training.” → “The experience may be optimized for one learning mode.”
- “They are distracted.” → “The system may be competing with their focus or context.”

In web design, search for exclusion at points such as navigation, forms, account creation, authentication, payment, search, filtering, file upload, privacy settings, dashboards, notifications, AI suggestions, error handling, help content, and recovery after interruption.

A recognized exclusion should become a design opportunity, not a defect label. Write it as a mismatch statement: “This experience assumes [ability/context/resource], but [person/context] may not have it, causing [barrier or consequence].”

### 2. Learn from diversity

People who experience barriers are the experts in those barriers. Designers are responsible for bringing design skill, not for replacing lived experience with assumptions.

Learning from diversity requires direct engagement. Simulation exercises may build awareness, but they are not a substitute for conversation, observation, co-design, or testing with people who experience the mismatch in real life.

Look for strengths, motivations, adaptations, emotional context, social context, and recurring themes. Do not only ask what is difficult. Ask what works, what people trust, what gives them confidence, what they avoid, what they have adapted, and what they wish the system understood.

For web work, include people with different assistive technologies, devices, learning styles, focus needs, mental health states, language backgrounds, tech literacy, economic access, work modes, and environmental contexts. Include teammates and external participants with diverse lived experience when the team itself is too narrow.

### 3. Solve for one, extend to many

A pronounced mismatch can reveal a broadly useful pattern. Designing for a person with a permanent disability can also benefit people with temporary or situational limitations.

The goal is not to dilute the needs of the person who exposed the mismatch. The goal is to solve deeply enough for that person that the solution becomes robust, flexible, and useful across related circumstances.

Examples of this principle in web design:

- Designing strong captions and transcripts for Deaf users also helps people in noisy environments, people who cannot play audio, and people who prefer reading.
- Designing high-contrast, scalable text for low-vision users also helps people in bright sunlight, tired users, older users, and people reading on low-quality displays.
- Designing clear focus recovery for people who struggle with interruptions also helps anyone returning after a meeting, phone call, timeout, or route change.
- Designing structured guidance for people who prefer guided learning also helps first-time users, people under deadline pressure, and people with low confidence in a new task.

## Mismatch framing

A mismatch is the unit of analysis. Do not begin by asking which demographic group a product is “for.” Begin by asking what interaction the product expects and where that expectation may fail.

A useful mismatch analysis includes:

1. **Motivation:** Why is the person here? What are they trying to express, learn, create, buy, share, solve, recover, protect, or understand?
2. **Goal:** What outcome would satisfy that motivation?
3. **Task:** What actions does the current design require?
4. **Assumption:** What ability, context, resource, or state does the design assume?
5. **Mismatch:** Who lacks that assumption permanently, temporarily, or situationally?
6. **Consequence:** What happens if the mismatch is not addressed?
7. **Adaptation:** How do people currently work around it?
8. **Opportunity:** What design change would reduce or remove the mismatch?

Use this syntax when setting direction:

```text
The experience assumes [assumption] during [interaction].
This excludes people who [permanent/temporary/situational condition or context].
The consequence is [blocked task, extra effort, risk, anxiety, loss of control, mistrust, abandonment].
A better direction is [inclusive design opportunity].
```

### Common web mismatch categories

- **Perception mismatch:** The person cannot see, hear, distinguish, read, or perceive the signal as designed.
- **Operation mismatch:** The person cannot use the required control, gesture, timing, posture, pointer precision, keyboard sequence, or device.
- **Understanding mismatch:** The person cannot infer meaning, hierarchy, jargon, consequences, or next steps.
- **Learning mismatch:** The person needs guidance, examples, or structure while the product expects tinkering.
- **Focus mismatch:** The product interrupts, distracts, auto-updates, animates, or fragments attention.
- **Recall mismatch:** The product expects memory of prior steps, instructions, locations, documents, decisions, or state.
- **Decision mismatch:** The product hides tradeoffs, consequences, defaults, or comparison criteria.
- **Communication mismatch:** The product uses a tone, channel, format, or language that does not fit the person or context.
- **Trust mismatch:** The product hides data use, automates without consent, personalizes too narrowly, or makes opaque AI claims.
- **Social mismatch:** The product assumes people act alone when they may rely on caregivers, coworkers, family, moderators, support staff, or community.
- **Context mismatch:** The product ignores environment, bandwidth, lighting, noise, time pressure, emotional state, fatigue, device constraints, or available privacy.

## Persona Spectrum

The Persona Spectrum maps a related limitation across permanent, temporary, and situational scenarios. It is a tool for understanding how a solution can scale without treating people as interchangeable.

Use it when you need to explain why a design should support more than one access path, when a stakeholder calls a need an edge case, or when a team is stuck designing only for its own abilities.

### How to create one

1. Start with a real person who experiences a permanent or pronounced mismatch.
2. Learn what they are trying to accomplish, what motivates them, what friction appears, and how they adapt.
3. Identify the underlying access dimension rather than only the diagnosis or demographic.
4. Map related temporary and situational scenarios.
5. Extract design implications that support the permanent case first and then extend to related cases.

### Classic spectrum examples

| Access dimension | Permanent | Temporary | Situational |
|---|---|---|---|
| Touch | One arm | Arm injury | New parent holding a baby |
| See | Blind | Cataract | Distracted driver or bright sunlight |
| Hear | Deaf | Ear infection | Bartender or person in a loud crowd |
| Speak | Non-verbal | Laryngitis | Heavy accent or context where speech is not appropriate |

### Web-expanded spectrum examples

| Access dimension | Permanent or pronounced | Temporary | Situational |
|---|---|---|---|
| Learn | Person who needs structured guidance | Person new to the domain | Expert using an unfamiliar feature under pressure |
| Focus | Person with ADHD or sensory sensitivity | Person recovering from stress or poor sleep | Person in a meeting-heavy day with constant notifications |
| Recall | Person with memory-related disability | Person returning after a long interruption | Person switching between tabs, devices, or tasks |
| Decide | Person who needs explicit tradeoffs | Person anxious about making a costly mistake | Person making a decision with limited time or incomplete information |
| Read | Person with dyslexia or low vision | Person with eye strain or migraine | Person reading on a phone outdoors |
| Trust | Person harmed by prior opaque systems | Person using a new product for the first time | Person asked to share sensitive data in public or at work |
| Bandwidth | Person with consistently limited connectivity | Person with a network outage | Person on public transit, roaming, or shared Wi-Fi |

### Output format

```text
Persona Spectrum: [access dimension]
Permanent/pronounced case: [person and mismatch]
Temporary case: [related temporary mismatch]
Situational case: [related situational mismatch]
Shared motivation: [what they are trying to accomplish]
Design implication: [what the system should support]
Primary risk if ignored: [barrier or harm]
```

### Use with care

The Persona Spectrum is not a market-sizing trick. It should not erase the specific needs of disabled people by saying “everyone benefits.” Solve the pronounced need seriously, then identify where the solution also helps others.

## Persona Network

The Persona Network maps the person’s ecosystem. No person uses technology in isolation. People rely on, coordinate with, trust, teach, avoid, protect, and negotiate with others.

Use it when a web experience involves collaboration, care, support, moderation, permissions, identity, privacy, sharing, notifications, handoff, accountability, or social context.

### What to map

Start with one person and list 3–5 key relationships. Include people they rely on, trust, enjoy, report to, care for, coordinate with, or avoid. For each relationship, capture the interaction type and potential mismatches between the person, the interface, and the environment.

Common network roles:

- Helper, caregiver, parent, child, partner, or friend
- Coworker, manager, instructor, support agent, moderator, or administrator
- Stranger, bystander, customer, service provider, or public audience
- Community expert, accessibility advocate, translator, or technical support person
- Automated system, AI assistant, recommender, scheduler, or notification service

### Web implications

A Persona Network can reveal needs that a single-user journey misses:

- Shared accounts, delegated access, caregiver access, or assisted completion
- Privacy controls for what is visible to whom
- Handoffs between devices, people, roles, and channels
- Notifications that should reach one person but not another
- Language, tone, and explanation suitable for multiple audiences
- Audit trails, comments, status, and decision history
- Social risks such as embarrassment, surveillance, outing, harassment, or unwanted disclosure

### Output format

```text
Persona Network for: [person]
Primary motivation: [why the person is using the product]
Key relationships:
- [role/person]: [interaction], [trust/reliance level], [possible mismatch]
- [role/person]: [interaction], [trust/reliance level], [possible mismatch]
Environmental/social context: [where and around whom the task happens]
Design implications: [permissions, sharing, guidance, privacy, notifications, support]
Risks if ignored: [exclusion, dependence, conflict, unsafe disclosure, abandonment]
```

## Inclusive design versus accessibility in decisions

Use this decision model when stakeholders confuse inclusion and accessibility:

- **Inclusive design asks:** Who is being excluded, why, and what can we learn from them?
- **Accessibility asks:** Can people actually perceive, operate, understand, and use the implemented experience?
- **Inclusive design output:** Direction, product decisions, research questions, design principles, flows, and patterns grounded in human diversity.
- **Accessibility output:** Implemented qualities, test results, conformance evidence, assistive-technology support, and issue fixes.

Do not treat accessibility as a late QA layer. Do not treat inclusive design as a substitute for accessibility testing. A product can be standards-aware but still hostile, confusing, or emotionally unsafe. A product can be co-designed but still fail keyboard access, color contrast, screen reader support, or semantic structure. Both failures matter.

## Methodology workflow for direction-setting

Use this sequence before producing a design direction, UX critique, product strategy, or methodology explanation.

1. **Name the product context.** Identify the interface type, task stakes, environment, data sensitivity, and likely user state.
2. **State the motivation.** Write why people are there in human terms, not feature terms.
3. **Map goal and task.** Distinguish the desired outcome from the actions the interface requires.
4. **Find mismatches.** Identify the assumptions built into the current or proposed experience.
5. **Create a Persona Spectrum.** Map at least one access dimension across permanent, temporary, and situational cases.
6. **Create a Persona Network when social context matters.** Map who else affects the interaction.
7. **Choose the design opportunity.** Focus on the mismatch that creates the highest barrier or strongest learning.
8. **Translate to design principles.** State what the product must preserve, reveal, simplify, adapt, or make recoverable.
9. **Separate method from implementation.** Note which decisions come from inclusive methodology and which require accessibility implementation/testing.
10. **Define evidence needed.** Name the co-design, research, prototype, or accessibility checks needed before confidence is high.

## Direction statement template

```text
Inclusive direction:
This experience should support [motivation] for people who [access need/context].
The current or likely mismatch is [mismatch].
We will solve first for [pronounced/permanent case] and extend the benefit to [temporary/situational cases].
The design should provide [multiple ways to participate], preserve [agency/control/trust/focus], and avoid assuming [ability/resource/context].
Accessibility must be verified through [implementation/testing requirements].
```

## Co-design foundation

Co-design is more than usability testing or surveying preferences. It means involving people who experience relevant barriers early enough to shape the direction, not only validate a nearly finished solution.

Good co-design requires humility and structure. Listen before solutioning. Include people from design, engineering, product, marketing, research, support, and leadership when possible so insights are not trapped in one function. Use realistic contexts rather than artificial labs when context affects the task.

The designer is not passive. The designer’s responsibility is to translate lived expertise into proposals, prototypes, tests, refinements, and product decisions in collaboration with the community.

When co-design is not available, be explicit that confidence is limited. Use existing evidence and accessibility best practices, but do not claim lived-experience validation.

## Foundational prompts

Use these prompts to guide analysis:

- What abilities does the design assume are fully available?
- What resources does it assume: time, money, privacy, bandwidth, language, device, social support, confidence, prior knowledge?
- What happens if the person is interrupted, stressed, tired, watched, rushed, or unsure?
- What adaptations do people already use to complete this task?
- Which barriers are physical, cognitive, emotional, social, environmental, or technical?
- Who benefits if the solution works for the most excluded person in the spectrum?
- Who else is part of the person’s ecosystem, and how does the design affect them?
- Which parts of the answer are methodology, and which require accessibility implementation or testing?

## Anti-patterns

Avoid these foundation-level errors:

- Treating disabled people as edge cases instead of design experts.
- Using diagnosis as the design brief instead of identifying needs and mismatches.
- Designing only from internal assumptions, analytics, or stakeholder preference.
- Relying on blindfold, earplug, or other simulations as proof of understanding.
- Claiming one universal path is inclusive because it is simple for the team.
- Claiming personalization solves inclusion while hiding settings or overloading people with choices.
- Calling a product accessible because inclusive design activities were performed.
- Calling a product inclusive because it meets a checklist but ignores agency, dignity, trust, emotional state, or social context.
- Designing for compliance at the end instead of building accessibility in.
- Asking people with lived experience for feedback after decisions are no longer changeable.
- Converting “solve for one, extend to many” into “ignore the one because many might also benefit.”

## Minimum acceptable output when using this foundation

When this file is used to set direction or explain the methodology, include at least:

1. A clear distinction between inclusive design and accessibility.
2. A named mismatch rather than a vague statement about “users.”
3. One Persona Spectrum or a reason it is not applicable.
4. A Persona Network when relationships, permissions, privacy, collaboration, care, or support affect the task.
5. A statement of who should be included in co-design or testing.
6. A note on what still requires accessibility implementation or validation.
