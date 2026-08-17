# Tangible Experience and Onboarding

Use this reference when a web flow connects to a physical product, physical service, shipped item, box, printed insert, QR code, device, accessory, installation step, repair process, return process, or support handoff. The digital surface is part of the out-of-box experience, not a separate afterthought.

This file focuses on detailed product-to-web behavior. It does not restate the general inclusive design method, cognitive model, or baseline web accessibility rules except where they change how tangible flows should be designed.

## Core Directive

Design the web journey as a continuation of the tangible journey.

A person may be holding a product, opening a box, scanning a label, unpacking components, trying to install software, creating an account, arranging delivery, troubleshooting a device, returning an item, or asking for repair while under physical, cognitive, emotional, financial, or time pressure. The web flow must reduce that pressure.

The experience should let people answer five questions at every moment:

- What is this object, step, or message?
- What should I do next?
- What happens if I cannot do that next step?
- How can I get help without starting over?
- How do I keep control of my product, data, money, time, and effort?

## Scope

Apply this guidance to:

- Product pages that describe a physical item or service.
- Checkout, shipping, tracking, delivery, pickup, and appointment flows.
- QR-code destinations, short URLs, printed insert destinations, and package-to-web links.
- Setup, first-run, pairing, calibration, registration, warranty, and account creation.
- Downloads, installers, firmware updates, companion apps, and platform-specific setup paths.
- Returns, repairs, exchanges, replacement parts, refunds, trade-ins, subscriptions, and support.
- Product documentation, quick-start pages, help centers, communities, and guided troubleshooting.

Do not use this file for purely digital products unless the flow has tangible constraints such as device installation, physical identity proofing, shipping, printed paperwork, lab samples, prescriptions, field service, appointments, or hardware accessories.

## Product-to-Web Journey Map

Map the whole journey before designing a single screen. Include both visible and hidden handoffs.

1. Before purchase: comparison, physical fit, prerequisites, accessibility information, required accounts, required downloads, shipping options, return policy, and support availability.
2. Purchase and confirmation: order review, address validation, payment, financing, taxes, privacy, shipping expectations, support contact, and cancellation.
3. Shipping and delivery: tracking, timing, delivery instructions, package size and weight, pickup constraints, signature requirements, damage reporting, and lost-package support.
4. First contact with package: label, seal, tab, insert, QR code, short URL, serial number, product key, regulatory label, and visible help path.
5. Setup preparation: required space, tools, connectivity, power, accounts, devices, assistive technology, language, and expected time.
6. Product setup: unpack, identify parts, connect, power on, pair, download, install, configure, update, verify success, save preferences.
7. Account creation or registration: explain why it is needed, what is optional, what is retained, and how to recover access.
8. Ongoing use: settings, personalization, accessibility configuration, maintenance, updates, replacements, support, community, and documentation.
9. Return, repair, or replacement: eligibility, deadlines, proof of purchase, packaging, label, pickup/drop-off, tracking, status, refund, and confirmation.

For each phase, specify: entry points, user motivation, required cognitive demands, required physical actions, likely failure states, recovery path, support path, and state persistence.

## Translate Packaging Principles Into Web Flow Decisions

### Simple is best

Use simple sequential steps rather than fewer but denser steps. A five-step setup with one clear action per screen is usually more inclusive than a two-screen setup that combines account creation, download choice, serial number entry, privacy consent, and device pairing.

Do:

- Break complex setup into labeled stages.
- Show prerequisites before the person starts.
- Keep each screen centered on one decision or action.
- Save progress automatically.
- Let people pause, resume, print, copy, or share instructions.

Avoid:

- “Everything in one form” setup.
- Hidden dependencies revealed only after failure.
- Installer steps that require memory of earlier web instructions.
- Dense troubleshooting pages that combine every product variant in one path.

### Identifiable elements

Every physical and digital entry point must be easy to recognize and understand. A person should not have to infer that a tiny code, sticker, fold, unlabeled icon, region selector, or secondary link is the required next step.

Digital equivalents of physical affordances:

- A tab becomes a visibly clickable button with a clear label.
- A pull direction becomes direct action copy such as “Start setup,” “Download for Windows,” or “Print return label.”
- A tactile cue becomes a semantic heading, landmark, accessible name, and repeated visible signpost.
- A package icon becomes an icon with text support, not an icon alone.
- A label becomes a page title, product name, model, serial-number location guide, and confirmation of the selected product.

### Materials matter

In physical packaging, material affects force, grip, safety, and confidence. In digital flows, the equivalent is reliability, legibility, trust, and durability.

Design digital “materials” that feel safe:

- Use stable URLs that do not expire without warning.
- Make downloads clearly official, versioned, signed where applicable, and recoverable.
- Keep setup instructions available after purchase and after first setup.
- Avoid brittle modals, disappearing banners, unlabeled icons, and links that require exact timing.
- Do not make the person rely on memory, screenshots, or customer-service transcripts as the only record.

### Ready access

Provide one obvious primary starting point and multiple equivalent access paths. These are not contradictions: the primary path reduces hesitation, while alternate paths prevent exclusion.

Examples:

- QR code plus short text URL plus searchable support page.
- “Start setup” on the box insert, order confirmation email, product page, account page, and support page.
- Download chooser by product name, model number, operating system, or order number.
- Return lookup by account, order number, email, serial number, or assisted support.
- Setup instructions available online, printable, and inside the companion app.

### Reduce pivot-points

Physical pivot-points are turns, twists, pulls, rotations, and shifts in posture. Digital pivot-points are mode switches, context switches, device switches, authentication switches, app switches, and repeated back-and-forth between product and website.

Reduce:

- Switching between package, phone, desktop, email, app store, installer, account portal, and support chat.
- Copying codes from one device to another.
- Reading a serial number from the bottom of a mounted or heavy device.
- Re-entering the same address, order number, account, or product identifier.
- Requiring drag, pinch, hover, exact timing, precise pointer control, or simultaneous gestures.

When a switch cannot be avoided, explain why, what will happen, how long it should take, and how to recover.

### Low physical effort

Digital flows can impose physical effort through long typing, precise input, repeated scanning, forced printing, phone-only verification, or required repositioning of a device.

Reduce effort by providing:

- Autofill, paste, password manager support, and copyable codes.
- Large tap targets and generous spacing for mobile setup.
- Serial-number scanning with manual fallback.
- Product detection with manual model selection fallback.
- Return labels that can be printed, shown as a QR code, mailed, or handled at drop-off.
- Support options that do not require voice calls as the only path.

### Size, space, stability

The digital layout should preserve space for approach, reach, zoom, and manipulation just as packaging must preserve space for grasping and removal.

Requirements:

- Mobile pages must work one-handed and while the person is near the product.
- Controls must remain stable when content loads, errors appear, or the keyboard opens.
- Instructions must remain readable at high zoom and with text scaling.
- Critical setup actions must not be crowded near destructive actions.
- Component identification pages must allow large images, zoom, alternate text, and part names.
- Do not place copyable codes, QR codes, or product identifiers in layouts that collapse or truncate.

### Mindful moments

Each physical, visual, and digital cue should lead to the next logical step. A QR code that opens a generic homepage is not a mindful moment. A “Download” button that does not say which platform it targets is not a mindful moment.

At every handoff, show:

- Where the person came from.
- Which product, order, shipment, or return the page refers to.
- What the next step is.
- What alternatives exist.
- How to get help without losing progress.

### No tools needed

Do not require extra tools for core access unless the product genuinely requires them and the requirement is disclosed before purchase.

In digital flows, “tools” include:

- A printer.
- A smartphone as the only access path.
- A QR scanner as the only access path.
- A companion app as the only way to read instructions.
- A social account as the only login option.
- A phone call as the only support path.
- A PDF as the only documentation path.
- A video as the only setup path.
- A credit card for warranty registration when no payment is due.

If a tool is unavoidable, provide an accessible alternative or assisted path.

## Product-to-Web Bridges

### QR codes and short URLs

QR codes are useful because they reduce typing and memory burden, but they must never be the only way to reach critical information.

Design rules:

- Place a short text URL next to the QR code.
- Make the destination mobile-first, fast, and accessible.
- Use a high-contrast QR code and test it from printed samples, not only from screen mockups.
- Label the QR code with nearby text such as “Scan for setup help” rather than generic “Learn more.”
- Use tactile or visual cues on packaging when feasible so low-vision and blind users can locate the code area.
- Preserve context on arrival: product, model, language, region, and setup stage.
- Do not route a setup QR code to a marketing page, product category page, or login wall before explaining why login is needed.

### Printed inserts and quick-start cards

Printed materials are constrained by space. The web destination can add depth, language coverage, and alternative formats.

Rules:

- Keep printed instructions short but complete enough to begin safely.
- Repeat the same step names online so the physical and digital experiences match.
- Use icons and text together.
- Include part names, orientation, and safety warnings in plain language.
- Provide online equivalents that are not video-only, app-only, or PDF-only.
- Make the online page available before the product arrives so a person or caregiver can prepare.

### Serial numbers, product keys, model numbers, and proof of purchase

These identifiers often create exclusion because they are small, hidden, hard to read, hard to copy, or physically inaccessible after installation.

Design rules:

- Show where the identifier appears using text, image, and alternate text.
- Allow paste, scan, upload image, order lookup, or assisted lookup.
- Group characters visibly and tolerate spaces or hyphens.
- Do not auto-advance before screen reader users can confirm input.
- Do not require a serial number when account, order, or device detection can safely identify the product.
- Let people save or copy identifiers after successful registration.

## Digital Setup and Out-of-Box Experience

### Before starting

The first setup page should state:

- Product name and model.
- Estimated time.
- Required items.
- Whether internet is required.
- Whether an account is required or optional.
- Whether a download, app, update, restart, or admin permission is required.
- Whether help is available by chat, text, phone, community, or assisted setup.
- Whether setup can be paused and resumed.

If the product requires physical manipulation, include the physical requirements before the step: lift, plug in, press, hold, rotate, open, remove label, attach mount, insert battery, scan code, or pair accessory.

### During setup

Use a staged flow:

1. Identify product.
2. Prepare space and required items.
3. Connect or assemble.
4. Download or launch software if needed.
5. Pair, register, or authenticate.
6. Configure accessibility and preferences.
7. Verify success.
8. Save instructions and support options.

Each stage should include a “Can’t do this?” or “Need another way?” path. The alternate path must be real, not a dead-end support article.

### After setup

Completion must be explicit. Show what was completed, what remains optional, where to change settings, how to get support, and how to restart the setup guide later.

Include:

- Confirmation that the product is ready.
- Summary of connected devices, registered account, warranty status, and selected preferences.
- Clear next actions: use product, configure, learn, contact support, or save guide.
- A way to export, print, email, or bookmark the setup summary.

## Downloads and Installation

Download flows are tangible because they often require a specific device, operating system, bandwidth, storage, permissions, restarts, and physical product state.

### Download page requirements

A good download page shows:

- Product and model compatibility.
- Platform and version.
- File size.
- Estimated download time if possible.
- Required storage, operating system, permissions, and network access.
- Release date and version history when relevant.
- Whether the download is an installer, firmware, driver, manual, or utility.
- What to do if the download fails.
- Offline, low-bandwidth, or alternate installer options when available.

### Installer guidance

The web page and installer must not contradict each other.

Require:

- Step names that match between page, installer, and support content.
- Warning before restart, device reset, data deletion, or firmware update.
- Save/resume behavior when installation is interrupted.
- Clear progress with meaningful states, not spinner-only waiting.
- Error messages that name the issue and give the best next step.
- Keyboard access, screen reader names, visible focus, and reduced-motion behavior in installer UI where applicable.

### Firmware and device updates

Firmware updates can create anxiety because they may affect expensive physical objects.

Show:

- Why the update is needed.
- What will change.
- Whether the product can be used during the update.
- Whether power must remain connected.
- How long it usually takes.
- What failure looks like and what to do.
- Whether the update can be postponed.

## Account Creation and Product Registration

Do not make account creation the default barrier to physical product use unless it is genuinely necessary.

### Account rules

- Explain why an account is needed before requesting data.
- Separate required account steps from optional registration, marketing, personalization, or subscription steps.
- Offer guest setup or delayed account creation when the product can function without immediate login.
- Support password managers, paste, reveal password, accessible validation, and recovery.
- Provide alternatives to phone-only, email-only, authenticator-only, or QR-only verification.
- Do not use inaccessible CAPTCHA or social login as the only path.
- Avoid short-expiring verification links during long setup tasks.
- Preserve progress after failed verification.

### Privacy and trust

Physical products can collect data from intimate spaces: homes, bodies, movement, location, usage patterns, voice, camera, or health-adjacent signals. Explain this in ordinary language.

For each data request, state:

- What is collected.
- Why it is needed.
- Whether it is optional.
- Who can see it.
- How long it is retained.
- How to turn it off or delete it.
- What product behavior changes if declined.

## Shipping, Delivery, and Pickup

Shipping flows must respect time, money, mobility, memory, and anxiety.

### Tracking and status

Tracking pages should show:

- Current status in plain language.
- What happened, what is happening, and what happens next.
- Delivery window and uncertainty.
- Package size and weight when relevant.
- Signature, ID, pickup, or access requirements.
- How to change delivery instructions.
- What to do if the package is damaged, missing, delayed, or inaccessible.

### Notifications

Use notification channels carefully. Delivery updates may be urgent for one person and disruptive for another.

Provide:

- Channel choices: email, SMS, app, web, phone, or none where possible.
- Urgency labels for delivery attempts, delays, required action, or refund deadlines.
- Batching for non-urgent updates.
- Quiet-hour or focus-aware options.
- A web status page that does not require relying on notifications.

### Delivery instructions

Let people communicate access needs and environmental constraints without disclosing unnecessary personal information.

Examples:

- Leave at accessible entrance.
- Avoid stairs.
- Ring bell, knock, call, text, or do not ring.
- Use pickup point.
- Allow caregiver pickup.
- Request smaller package consolidation or separate parcels if offered.

## Returns, Repairs, Exchanges, and Refunds

Return and repair flows are often time-bound, emotionally charged, and physically demanding. Design them as recovery flows, not blame flows.

### Return initiation

A return flow should make these clear before the person commits:

- Eligibility.
- Deadline.
- Expected refund or replacement timing.
- Whether the original box is required.
- What must be included.
- Whether batteries, liquids, medical items, or regulated components change the process.
- Pickup, drop-off, mail, in-store, and assisted options.
- Whether account sign-in is required or whether order lookup is enough.

### Labels and packaging

Do not require a printer as the only way to return a product.

Offer:

- Printable label.
- QR code label at carrier/drop-off.
- Mailed label.
- Pickup option.
- In-store assisted return.
- Instructions for packaging without the original box when permitted.
- Clear photos or diagrams for packing orientation.
- Weight and handling warnings.

### Repair status

Repair flows need strong recall support. People may wait days or weeks.

Provide:

- Persistent case number and copyable status link.
- Status timeline with completed, current, and next steps.
- Expected time ranges.
- Required action labels.
- Notifications with controllable channels.
- Saved notes, uploaded photos, chat transcripts, and repair history.
- Confirmation when a device is received, repaired, replaced, shipped, refunded, or closed.

## Support and Troubleshooting

Support materials should help people see someone like them succeed, while still providing direct task completion paths.

### Support content format

Prioritize a well-structured HTML page. Videos, widgets, PDFs, chatbots, and apps can supplement but should not be the only way to complete core tasks.

Every support topic should include:

- Product and model scope.
- Symptoms or goal.
- Required items.
- Step-by-step instructions.
- Images with text alternatives.
- Captions, transcripts, and visual descriptions for video.
- Expected result for each step.
- What to do if the step cannot be completed.
- Contact or assisted path.

### Troubleshooting behavior

- Ask only for information needed for the next diagnostic decision.
- Show why a question is being asked.
- Avoid making people repeat the same information across chatbot, form, phone, and email.
- Let people skip a step they physically cannot perform and route to another path.
- Provide “I tried this” tracking so people do not loop.
- Keep human support reachable from automated support.

### Community and demonstrations

When appropriate, include demonstrations by disabled users, alternative setups, and community examples. Make clear which adaptations are official, user-contributed, experimental, or third-party.

Show configurations across different bodies, homes, desks, lighting, mobility needs, assistive technologies, and device combinations. This helps people recognize possible setups and adapt them.

## Configuration as Onboarding

For physical-digital products, settings are not an advanced feature. Settings can be the access path.

During onboarding, surface only the configuration needed to help the person start safely and confidently:

- Input method.
- Output method.
- Text size.
- Contrast or appearance.
- Motion and sound.
- Captions or transcripts.
- Notification timing and channel.
- Privacy and data sharing.
- Device orientation, handedness, or control mapping.
- Press duration, sensitivity, timing, or activation behavior when relevant.

Do not overwhelm people with every setting at once. Offer a short setup wizard, then a predictable full settings area that can be revisited later.

## AI in Setup, Returns, and Support

AI can reduce effort in setup and support, but it must not become another barrier.

Appropriate uses:

- Summarize long instructions.
- Help identify the product or part from safe inputs.
- Suggest the next troubleshooting step.
- Draft a support message.
- Explain return eligibility.
- Translate instructions.
- Convert dense documentation into step-by-step guidance.

Required safeguards:

- AI must be optional.
- Critical claims must be checkable.
- The person must be able to reach non-AI support.
- AI must not invent warranty, safety, repair, medical, legal, or refund terms.
- AI must not silently personalize based on sensitive data.
- AI must ask consent before using uploaded photos, serial numbers, location, or account data.
- AI must allow correction and escalation.

Bias checks:

- Does the product-identification model work across lighting, skin tones, homes, camera quality, and assistive setups?
- Does the support assistant understand nonstandard adaptations and third-party accessories?
- Does personalization over-recommend the same product, accessory, or support path?
- Does automation override a human’s stated goal, such as wanting repair instead of replacement?

## Research and Co-Design for Tangible Flows

Test with people in realistic contexts. Lab testing alone misses barriers that happen when a person is holding a box, managing a child, using one hand, dealing with glare, lacking Wi-Fi, or trying to return a damaged item.

### Recruiting considerations

Include people with varied:

- Vision, hearing, mobility, dexterity, speech, memory, focus, learning, and sensory needs.
- Assistive technologies and adaptive devices.
- Tech literacy and confidence.
- Access to smartphones, printers, broadband, transportation, and social support.
- Languages, cultures, income, housing, work schedules, and caregiving contexts.

Recruit by needs and contexts, not diagnosis alone.

### Activities

Use these prompts during research and design:

- Role-play a human support interaction, then repeat it as a web or chatbot interaction. Identify what breaks.
- List every mismatch in the current product-to-web journey and convert each into a “How might we…” question.
- Prototype the smallest setup moment with paper, physical props, screenshots, or role-play.
- Observe delight and pain points, not only task completion.
- Test situational combinations: low light, noise, one hand, no printer, slow network, no account, high stress, caregiver assistance, or interrupted setup.
- Ask what people improvised. Adaptations reveal design opportunities.

## Acceptance Checklist

A tangible web flow is ready only when the following are true:

- The flow includes the full product journey from purchase or delivery through setup, support, and return.
- The first step is obvious, and equivalent alternate paths exist.
- QR codes have text URL alternatives and mobile-accessible destinations.
- Printed and digital instructions use matching terminology.
- Setup explains prerequisites, time, required tools, account needs, data use, and recovery paths before starting.
- Downloads state platform, version, file size, requirements, and failure recovery.
- Installation and firmware updates explain risk, duration, progress, and recovery.
- Account creation is not forced unless necessary, and optional steps are labeled optional.
- Verification, login, and recovery support memory, assistive technology, and multiple channels.
- Shipping status explains current state, next step, required action, and delivery constraints.
- Returns and repairs do not require a printer, phone call, or original account as the only path unless no alternative exists.
- Support content is available as accessible HTML, not only video, PDF, app, or chatbot.
- Videos have captions, transcripts, and visual descriptions when instructional.
- Icons are paired with text when meaning matters.
- Errors never blame the person and always provide a next step.
- The person can pause, resume, save, print, copy, share, or revisit instructions.
- The flow works with keyboard, screen reader, touch, zoom, text scaling, high contrast, reduced motion, slow network, and mobile.
- Co-design included people who experience relevant barriers in realistic contexts.
- Success metrics include confidence, control, reduced effort, recoverability, and emotional impact.

## Anti-Patterns

Avoid:

- A setup QR code that opens a marketing homepage.
- A return flow that requires a printer.
- A support flow that requires a phone call for people who cannot or do not want to speak.
- A product registration flow that blocks setup without explaining why.
- A download page that hides version, platform, or file size.
- A firmware update that gives no risk, time, or recovery information.
- A serial-number form that rejects spaces, hyphens, paste, or screen-reader-friendly review.
- A chatbot that loops through generic advice and hides human support.
- A video-only setup guide.
- A PDF-only manual.
- A package insert that says “scan here” without a text URL.
- A support article that assumes the person can see the same visual cue, grip the same object, lift the same weight, or access the same side of the product.
- A one-path flow that assumes the person has a smartphone, broadband, printer, account, credit card, perfect memory, quiet space, and uninterrupted time.
