# AI and Automation

Use this reference when a web experience uses generative AI, recommendations, personalization, summarization, coaching, classification, prediction, ranking, detection, routing, or automated decisions. The goal is to make automated behavior useful without making people feel overruled, surveilled, misrepresented, trapped, blamed, or cognitively overloaded.

AI and automation are interaction behaviors, not only technical capabilities. Treat every generated answer, recommendation, source citation, prediction, alert, suggested reply, rewrite, summary, ranking, auto-fill, or proactive prompt as a designed moment between a person and a system.

## Core Position

AI is inclusive only when it increases a person’s ability to act with confidence and control. It is not inclusive merely because it is powerful, personalized, fast, or novel.

Design AI around five human needs:

- Agency: people can choose, modify, pause, reverse, or refuse the automation.
- Trust: people can understand what the system is doing and where its limits are.
- Safety: people are not exposed to avoidable shame, bias, privacy risk, or psychological harm.
- Orientation: people know what to do next and can recover after interruption or confusion.
- Discovery: people can explore alternatives, not only receive what the system assumes they already want.

AI should guide, draft, summarize, organize, explain, classify, coach, translate, transform, or recommend. It should not silently replace judgment, hide uncertainty, remove user control, or make consequential decisions without review and human accountability.

## Start With The Role Of The Automation

Before designing the UI, name the automation’s role. A vague “AI assistant” will overreach. A precise role creates boundaries.

Common roles:

- Summarizer: condenses information, preserves key points, and exposes sources.
- Drafter: creates a starting point that the person can edit, reject, or finish.
- Coach: improves the person’s own work through feedback and examples.
- Rewriter: transforms tone, length, structure, or format under user direction.
- Recommender: surfaces possible next actions, content, products, settings, or people.
- Organizer: clusters, filters, sorts, labels, or prioritizes accumulated information.
- Explainer: answers questions, defines unfamiliar concepts, or gives step-by-step help.
- Translator: changes language, modality, or level of detail.
- Detector: flags patterns, risks, duplicates, anomalies, or missing information.
- Router: sends a request, case, message, or workflow to a destination.
- Decider: makes or proposes a decision with user, moderator, expert, or organizational review.

For each role, define:

- What the system may do.
- What it must ask before doing.
- What it must never do.
- What evidence or source material it uses.
- What confidence level is required.
- What the user can change.
- What happens when the system is wrong.

## Use AI Only When It Reduces A Real Mismatch

Add automation only when it lowers a cognitive, emotional, physical, social, or contextual barrier. Do not add AI because the feature is technically available.

Good reasons to use AI:

- The person faces a flood of information and needs a digestible summary.
- The person is blocked by task initiation and needs a draft or starting point.
- The person needs help finding relevant items across accumulated data.
- The person wants to communicate but lacks confidence in tone, clarity, or structure.
- The person needs an explanation in a different format or level of detail.
- The person needs recall support after interruption.
- The person needs decision support across tradeoffs and consequences.
- The person needs relevant settings surfaced without digging through menus.

Weak reasons to use AI:

- The product wants more engagement.
- The system can infer something but the person did not ask for help.
- A rule-based, searchable, or user-authored solution would be clearer.
- The task is high-stakes and the automation cannot explain or verify its output.
- The feature reduces work for the company by transferring risk to the user.

## The Human-Control Contract

Every AI feature must make a clear contract with the person. State the contract in the product behavior, not only in documentation.

The contract should answer:

- What can this feature help me do?
- What data does it use?
- What data does it not use?
- Can it be wrong?
- How do I check it?
- How do I edit it?
- How do I undo it?
- How do I stop it from using this input or preference?
- How do I reset or change what it remembers?
- Who, if anyone, can see the input, output, feedback, or training signal?

Required controls:

- Generate, stop, retry, edit, apply, discard, copy, save, close, and undo where relevant.
- Review before send, submit, publish, route, purchase, delete, approve, or escalate.
- Clear “why this?” or “how this was made” affordance for recommendations and decisions.
- Opt out of personalization and training when feasible.
- Reset memory, profile, preference, or recommendation history when feasible.
- Pause or minimize AI surfaces without losing the person’s place.
- Manual path for the same essential task.

For high-impact actions, use a review gate. The system may suggest, prepare, compare, or flag, but a person must confirm with enough context to understand consequences.

## Entry Points And Timing

AI entry points should be predictable, optional, and reachable. They should not behave like surprise pop-ups unless there is a clear, user-benefiting reason.

Use persistent entry points for ongoing assistance: side panel, command area, “Ask” field, toolbar button, inline rewrite menu, or help drawer.

Use contextual entry points when the user has expressed intent or visible friction: after selecting text, opening a long thread, pausing on an empty draft, viewing accumulated results, encountering an error, or entering a complex decision point.

Avoid surfacing AI:

- In the middle of focused typing unless explicitly invoked.
- After every small action.
- As a modal for low-urgency suggestions.
- As an animation that competes with the main task.
- As a nudge that implies failure when declined.
- During moments where privacy expectations are uncertain.

Before a proactive AI suggestion appears, classify its attention cost:

- Full attention: only for urgent, high-consequence, time-sensitive help.
- Partial attention: useful suggestions that can sit inline or in a panel.
- Peripheral attention: background summaries, recommendations, or status that can wait.

If the person ignores, dismisses, or repeatedly declines AI help, reduce frequency and make the control explicit.

## Prompt Scaffolds

Blank prompts create cognitive load. Provide scaffolds that help people start without forcing one learning style.

Offer multiple prompt entry modes:

- Natural language free text.
- Structured fields.
- “Mad libs” sentence frames.
- Example prompts.
- Suggested chips or quick actions.
- Templates by goal.
- Step-by-step wizard for complex tasks.
- Advanced mode for experienced users.

A prompt scaffold should make the goal, source, constraints, audience, format, and finish line explicit.

General scaffold:

```text
Help me [goal] using [source/context].
The audience is [audience].
Use a [tone/format/length].
Keep [constraints].
Before finalizing, show [checks/questions/sources].
```

Summarization scaffold:

```text
Summarize [source] for [purpose].
Include: [decisions / open questions / risks / next actions / deadlines].
Leave out: [details I do not need].
Show links or citations for each important claim.
```

Drafting scaffold:

```text
Draft [message/document] for [recipient/audience].
Goal: [what I want to happen].
Tone: [direct / warm / formal / brief / sounds like me].
Use these facts: [facts].
Do not invent details. Ask if information is missing.
```

Coaching scaffold:

```text
Review my draft for [clarity / tone / accessibility / inclusiveness / brevity].
Tell me what works first.
Then suggest 3 improvements.
Offer a rewrite I can accept or edit.
```

Recommendation scaffold:

```text
Recommend options for [goal].
Prioritize [criteria].
Explain why each option appears.
Include alternatives that differ from my usual pattern.
Do not use [data/preferences] unless I approve.
```

Decision-support scaffold:

```text
Help me compare [choices].
Show tradeoffs, consequences, uncertainty, and what information is missing.
Do not decide for me.
End with questions I should answer before acting.
```

Settings scaffold:

```text
Help me configure this experience for [need: focus / readability / privacy / notifications / guidance].
Show what each setting changes.
Let me preview before applying.
Save only the settings I confirm.
```

Prompt scaffolds must be editable. Never trap people in a wizard when free expression would be faster.

## Source References And Fact Checking

When factual accuracy matters, AI output needs source visibility. Do not make people trust a fluent answer because it sounds complete.

Use source references for:

- Summaries of email threads, documents, chats, tickets, cases, policies, data, or search results.
- Claims about requirements, dates, deadlines, owners, approvals, measurements, or commitments.
- Recommendations based on personal data, usage history, or organizational rules.
- Automated classifications or risk flags.
- Explanations of why content was ranked, hidden, prioritized, or routed.

Reference behavior:

- Link each key claim to the specific source item when feasible.
- Let people open the source without losing the AI output.
- Distinguish “from source,” “inferred,” “uncertain,” and “missing.”
- Preserve source hierarchy: primary source first, then supporting context.
- Mark generated or synthesized text clearly.
- Show enough surrounding context to avoid misleading snippets.
- Provide a “show source details” control for people who need more context.
- Do not cite inaccessible formats as the only way to verify a claim.

For summaries, include an uncertainty pattern:

```text
I found [N] relevant items.
Key points: [...]
Open questions: [...]
Possible missing context: [...]
Sources used: [...]
```

For AI-generated decisions or recommendations, include a rationale pattern:

```text
Recommended because: [...]
Signals used: [...]
Signals not used: [...]
Confidence: [high/medium/low or plain-language equivalent]
Alternative options: [...]
How to change this recommendation: [...]
```

## Privacy, Consent, And Memory

Privacy controls must be designed as part of the journey, not buried in legal text or a remote settings page.

Use consent touchpoints when the system:

- Reads private content.
- Uses personal history, style, identity, behavior, location, calendar, files, messages, biometrics, or accessibility settings.
- Stores preferences or memory.
- Learns from feedback.
- Shares data across products, organizations, people, or models.
- Uses sensitive attributes or proxies that could imply sensitive attributes.
- Makes recommendations that may reveal private information.

Consent language should say:

- What information will be used.
- Why it is needed.
- How long it is retained.
- Who can access it.
- Whether it affects model training, personalization, or only this task.
- What happens if the person declines.
- How to revoke or reset it.

Avoid vague labels such as “improve your experience” when the system is using personal data. State the actual use.

Memory behavior:

- Ask before remembering sensitive or identity-related details.
- Make memory inspectable, editable, and deletable.
- Separate session memory from persistent memory.
- Let people keep convenience without broad surveillance.
- Avoid using inferred emotional state, disability, health, or vulnerability unless the person explicitly asks and the product context supports it safely.
- Never make personalization a condition for access to a core task.

When AI uses accessibility or mental-health-adjacent preferences, frame them as user-controlled configuration, not diagnosis.

## Bias Stress Tests

Run bias stress tests before launch and after meaningful model, data, prompt, policy, ranking, or personalization changes.

### 1. Dataset Bias

Risk: the data used to train, tune, evaluate, retrieve, or rank does not represent the diversity of the people affected.

Stress-test questions:

- Who is in the data, and who is missing?
- Does the dataset reflect the customer base across language, culture, age, disability, assistive technology, skin tone, gender spectrum, geography, income, tech literacy, and context?
- Have results been tested with people who were not represented in the original sample?
- Are labels, taxonomies, and success criteria shaped by a narrow group?
- Does the evaluation data include permanent, temporary, and situational access needs?
- What happens under low bandwidth, high contrast, screen reader use, speech input, keyboard-only use, and noisy or distracting environments?

Mitigations:

- Recruit and compensate diverse co-creators.
- Add missing groups to evaluation sets.
- Treat “unknown” as a signal, not an excuse to generalize.
- Track failures by population and context, not only average performance.
- Include diverse teams in data definition and review.

### 2. Association Bias

Risk: the system reinforces stereotypes, cultural assumptions, or unfair relationships between people, roles, language, appearance, behavior, and outcomes.

Stress-test questions:

- Do generated outputs assume gender, race, ethnicity, ability, family structure, education, class, geography, or language fluency?
- Do translations, rewrites, classifications, images, or recommendations encode stereotypes?
- Does the system treat some communication styles as less professional or less credible?
- Are “normal,” “expert,” or “safe” defaults culturally narrow?
- Are labels and categories respectful and user-understandable?

Mitigations:

- Test with counterfactual prompts and personas.
- Review outputs across languages and communication styles.
- Prefer neutral, inclusive phrasing unless the user specifies otherwise.
- Separate style coaching from identity judgment.
- Include examples that challenge common stereotypes.

### 3. Automation Bias

Risk: an automated conclusion overrides human goals, social context, cultural considerations, or individual intent.

Stress-test questions:

- Would diverse real customers agree with the automated conclusion?
- Does the automation make a person feel corrected, managed, or overruled?
- Is the system optimizing for company efficiency rather than the person’s goal?
- Does the UI make the AI output look more authoritative than it is?
- Can people review, edit, reject, appeal, or undo the output?
- Are high-impact actions blocked until a person confirms?

Mitigations:

- Use “suggested,” “draft,” “possible,” or “recommended” language where appropriate.
- Show uncertainty and missing context.
- Keep original input visible while reviewing changes.
- Provide manual override and escalation.
- Log and review overrides as design feedback, not user failure.

### 4. Interaction Bias

Risk: user interactions, malicious inputs, repeated feedback, or live learning corrupt the system or cause harmful behavior.

Stress-test questions:

- What does the system learn from user behavior?
- Can one user or group poison results for others?
- Can malicious users teach toxic, racist, sexist, ableist, or exclusionary behavior?
- Does feedback create popularity loops that suppress minority choices?
- Are safeguards in place for real-time learning, public prompts, comments, or shared assistants?
- Can the person correct the system without exposing private information?

Mitigations:

- Separate personal adaptation from global learning unless consent and safeguards are clear.
- Moderate training signals before broad use.
- Rate-limit harmful patterns and detect abuse.
- Give people “not relevant,” “wrong,” “harmful,” and “don’t use this” feedback options.
- Review edge cases from underrepresented users, not only high-volume users.

### 5. Confirmation Bias

Risk: personalization narrows the person’s world, repeats their past behavior, or excludes less popular alternatives.

Stress-test questions:

- Does the recommendation system reinforce only what the person or similar users already chose?
- Are alternatives and dissenting perspectives visible?
- Can the person change course without fighting the model?
- Does personalization assume present intent from past behavior?
- Does the system keep recommending something after the person has moved on?
- Can people reset history, change interests, or ask for novelty?

Mitigations:

- Include “different from my usual,” “show alternatives,” and “reset recommendations.”
- Offer diverse options when the decision is exploratory.
- Avoid overfitting on one action, purchase, click, or temporary state.
- Explain why something was recommended.
- Let people mark recommendations as irrelevant, sensitive, or unwanted.

## Generative AI Patterns

### Summarization

Summaries reduce stress when information feels like a flood. They can also distort meaning if they omit context or overstate certainty.

Required behavior:

- Show the source set summarized.
- Preserve decisions, deadlines, owners, risks, open questions, and next actions.
- Let people expand from summary to detail.
- Provide citations or source anchors for key points.
- Mark uncertain or conflicting information.
- Offer levels: brief, detailed, action-only, questions-only, or chronological.
- Avoid hiding dissent, nuance, or minority perspectives.

### Drafting

Drafting helps with task initiation and communication confidence. It can also create anxiety about voice, authenticity, or how the person will be perceived.

Required behavior:

- Generate a draft, not a final send.
- Let people tune tone, length, clarity, sentiment, and formality.
- Provide “sounds like me” only with clear consent and editable style memory.
- Ask for missing facts rather than inventing details.
- Keep the person’s original goal visible.
- Include a review step before sending or publishing.

### Coaching

Coaching should build confidence. It should not only tell people what they did wrong.

Required behavior:

- Start with what works.
- Separate critical issues from optional improvements.
- Provide examples, not just abstract advice.
- Offer an applied rewrite, but keep the person’s ownership clear.
- Avoid harsh tone, shame, or overcorrection.
- Let people choose the coaching dimension: clarity, tone, inclusiveness, brevity, structure, or accessibility.

### Recommendations And Suggestions

Suggestions are interruptions unless explicitly requested. They should appear after intent, not before it.

Required behavior:

- Make suggestions actionable immediately.
- Explain why the suggestion appeared.
- Allow dismiss, snooze, disable, or “not relevant.”
- Do not imply the person failed when they decline.
- Adapt language and frequency to emotional context and prior behavior.
- Include alternatives, not only the top-ranked option.

### Personalization

Personalization should feel like a useful adjustment, not a hidden profile.

Required behavior:

- Use explicit preferences before inferred ones.
- Show what is remembered or inferred.
- Provide reset and edit controls.
- Avoid sensitive inferences.
- Let people operate anonymously or generically where possible.
- Do not make personalized output the only path.

### Automated Decisions

Automated decisions require the highest level of transparency and review.

Required behavior:

- State that a decision or recommendation is automated.
- Explain criteria in plain language.
- Show consequences before action.
- Provide a human review or appeal path where stakes require it.
- Preserve audit trails for accountability.
- Test for disparate impact across populations and contexts.
- Keep irreversible actions manual or explicitly confirmed.

## Mental Health And Emotional Safety

AI can help people feel capable, but it can also trigger anxiety through inaccuracy, bias, judgment, privacy uncertainty, or loss of control.

Design rules:

- Use inclusive, non-blaming language.
- Make the system’s limitations visible without making the user responsible for hidden complexity.
- Confirm small wins: summary created, draft saved, settings applied, source checked.
- Keep progress visible in multi-step AI flows.
- Make “go back,” “undo,” and “restore previous version” available.
- Avoid permanent-seeming consequences until the user confirms.
- Avoid red, urgent, or alarm-like styling for ordinary AI feedback.
- Avoid endless refinement loops; always provide task-closing actions.

A useful coaching flow ends with action, not only critique. Provide “apply,” “rewrite with these changes,” “save draft,” “send after review,” “copy,” or “finish without changes.”

## Cognitive Demand Checklist

Before launching AI or automation, review the dominant cognitive demand.

Learning:

- Does the user know what the AI can and cannot do?
- Are examples, templates, or guided prompts available?
- Can people skip scaffolding and return later?

Focus:

- Does AI appear only when it is relevant to the current task?
- Can people minimize, defer, or silence it?
- Are generated outputs scannable?

Decision-making:

- Are tradeoffs, consequences, uncertainty, and missing information clear?
- Is a human review required for high-impact actions?
- Are alternatives visible?

Recall:

- Does the system preserve context, prior steps, and source material?
- Can people resume after interruption?
- Are summaries, histories, drafts, and recent actions available?

Communication:

- Can people adjust tone, detail, format, language, and channel?
- Does the assistant adapt only with consent?
- Does it avoid assuming communication preferences without asking?

## Accessibility And Multimodal Behavior

AI interfaces must remain accessible when the generated content changes dynamically.

Implementation requirements:

- Use semantic controls for prompts, menus, references, citations, generated cards, and actions.
- Ensure keyboard access for every AI control.
- Maintain visible focus and predictable focus return after generation, dialogs, source previews, and applied changes.
- Announce important generated status changes without overwhelming assistive technology.
- Keep source links and generated actions distinguishable for screen reader users.
- Provide captions and transcripts for AI-generated or AI-selected media.
- Preserve readability under zoom, text scaling, high contrast, forced colors, and reduced motion.
- Do not rely on drag, hover, voice, gesture, timing, color, or animation as the only way to use AI.
- Let people use AI with screen readers, switch input, keyboard, touch, speech input, magnification, braille displays, and low-bandwidth connections.

Avoid spinner-only generation states. Say what is happening, what the system is using, and whether the person can continue working.

## Research And Co-Creation For AI

AI must be tested with people who experience the barriers the feature claims to reduce.

Use these research activities:

- Computer trust: ask what people would trust a computer to do and what they would trust only a human to do.
- Human-to-computer role-play: compare a human interaction to the same interaction performed by a computer and identify breakdowns.
- Human analogy: define whether the AI is acting like an assistant, teacher, coach, clerk, editor, concierge, or evaluator; interview humans who perform that role well.
- Interaction diary: observe where AI interrupts, helps, confuses, or fades into the background.
- Evaluate technology’s role: decide whether AI is the simplest and most appropriate technology for the desired outcome.
- Context and capability match: test the feature across physical, social, cognitive, and environmental contexts.

Recruit by need spectrum, not diagnosis alone. Include people with varied focus needs, guidance preferences, communication preferences, recall strategies, decision-making needs, self-efficacy, risk tolerance, assistive technologies, languages, and privacy attitudes.

AI-specific interview prompts:

- What would you want this assistant to know about you over time?
- What would you never want it to infer or remember?
- When would a suggestion feel helpful?
- When would the same suggestion feel disruptive or judgmental?
- What evidence would you need before trusting a summary or recommendation?
- What controls would make this feel safe to try?
- How would you recover if the AI got this wrong?
- What would make you stop using it?

## Evaluation Metrics

Do not evaluate AI only by adoption, number of generations, acceptance rate, or time spent. High usage can mean dependency, confusion, or repeated failure.

Measure:

- Task completion with and without AI.
- User confidence before and after using AI.
- Perceived control and ability to override.
- Cognitive load and time to recover after interruption.
- Accuracy and source-check success.
- Number and type of edits made to generated output.
- False positives, false negatives, and harmful omissions.
- Dismissal and opt-out reasons.
- Privacy comprehension and consent confidence.
- Bias failures by population, language, context, and assistive technology use.
- Whether the product helps people finish their goal efficiently.
- Emotional impact: stress reduced, anxiety increased, self-blame triggered, motivation preserved.

Close the loop: use feedback to revise prompts, data, ranking, UI placement, source behavior, controls, and consent language.

## Anti-Patterns

Avoid:

- AI that appears as a mandatory gate to a task.
- Output that looks final before user review.
- Uncited factual summaries.
- Hidden personalization.
- Privacy consent only in legalese.
- “Trust me” language without verification.
- Overconfident answers where the source is ambiguous.
- Recommendations that repeat the same narrow pattern.
- Suggestions that appear before user intent.
- AI feedback that only criticizes.
- Automated decisions with no appeal or human review.
- Settings recommendations that cannot be previewed or undone.
- Model behavior that changes across sessions without notice.
- Training on user corrections without consent.
- Using disability, mental health, emotional state, or vulnerability as inferred personalization.
- Measuring success only by engagement or acceptance rate.

## Preflight

Before shipping, confirm:

- The automation has a bounded role.
- The feature reduces a real mismatch.
- The user can complete the task without AI.
- Prompt scaffolds support novice, guided, and expert use.
- Output can be checked against sources where facts matter.
- Privacy and consent are understandable in the flow.
- Memory is inspectable, editable, removable, and optional.
- Proactive suggestions respect attention, context, and dismissal behavior.
- High-impact actions require review and confirmation.
- Dataset, association, automation, interaction, and confirmation bias tests have been run.
- People with diverse cognitive, emotional, physical, social, language, and assistive-technology contexts were included before launch.
- Metrics include confidence, control, cognitive demand, emotional impact, source-checking, bias, and recovery—not just usage.
