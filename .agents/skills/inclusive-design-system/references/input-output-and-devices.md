# Input, Output, and Devices

Use this reference when web work must function across assistive technology, adaptive input and output, changing device configurations, high contrast, scaling, physical context, tactile cues, hardware setup, or product-to-web experiences. This file expands the device and multimodal surface of the Inclusive Design System; do not use it as a replacement for the baseline accessibility file.

## Core Position

Web experiences do not exist only on screens. They are used through operating systems, browsers, keyboards, touchscreens, speech tools, switches, eye gaze, screen readers, braille displays, magnifiers, alternative mice, adaptive controllers, mobile cameras, QR codes, mounts, stands, glare, noise, fatigue, posture, reach, bandwidth, and social context.

Design for one-size-fits-one. Provide strong defaults, then let people configure the system so the input, output, layout, guidance, timing, and physical setup fit them. A configurable digital experience can act as augmentation in the same way that an adjustable stand, alternative mouse, tactile sticker, or adaptive controller can.

The goal is not to guess a person’s disability. The goal is to remove assumptions about how a person must perceive, operate, understand, configure, and recover from the experience.

## Use This Reference When

- A responsive web page must work across phone, tablet, desktop, kiosk, TV, embedded display, or device setup surface.
- A flow connects digital work to physical products, packaging, accessories, pairing, firmware, registration, repair, shipping, or support.
- Users may rely on screen readers, magnification, braille displays, switches, voice control, eye gaze, adaptive controllers, alternative mice, touch, pen, or keyboard-only operation.
- The product includes high contrast, text scaling, density, appearance, motion, sound, notification, input timing, or control-mapping settings.
- The interface uses camera scanning, QR codes, physical labels, tactile cues, hardware buttons, haptics, sound, gestures, or sensor-driven behavior.
- The design must adapt to sunlight, glare, low bandwidth, no Wi-Fi, loud environments, movement, fatigue, limited reach, hands-full use, or sensory overload.

## Design Model

For every relevant step, describe the system in four layers:

1. Device: the hardware, accessory, browser, OS, assistive technology, and physical placement.
2. Input: the way the person initiates, controls, confirms, cancels, edits, and recovers.
3. Output: the way the system communicates state, progress, errors, urgency, and next steps.
4. Configuration: the settings, defaults, saved preferences, and setup guidance that make the experience fit.

A failure in any layer can exclude someone even when the other layers are accessible.

## Input Principles

### Accept multiple input paths

Do not require one physical gesture, one pointer type, one device, or one access method. Provide equivalent paths for:

- Keyboard
- Touch
- Mouse or trackpad
- Pen
- Switch input
- Speech input
- Eye gaze
- Screen reader commands
- Alternative mice and trackballs
- Adaptive controllers
- Mobile camera scanning where appropriate

Avoid any core interaction that is available only through hover, drag, pinch, twist, double-tap, long-press, precise pointer movement, time-limited action, camera scan, biometric recognition, or a single hand posture.

### Make controls tolerant

People vary in strength, dexterity, tremor, coordination, posture, reach, speed, and fatigue. Controls should not require precision beyond the task.

Use:

- Large targets with enough spacing to reduce accidental activation.
- Stable locations for repeated actions.
- Clear active, selected, disabled, expanded, pressed, and loading states.
- Undo, cancel, back, retry, and reset paths.
- Generous timing or no timing requirements.
- Alternative controls for drag-and-drop, sliders, maps, carousels, drawing, cropping, and rearranging.
- Confirmation for destructive or high-consequence actions.

Do not punish slow input, repeated input, missed gestures, or accidental activation.

### Separate initiation from commitment

A person may explore with a screen reader, move focus by keyboard, hover by accident, or dwell through an eye-gaze system. Do not treat exploration as commitment.

For high-impact actions, require an explicit activation or confirmation. For low-impact actions, make reversal immediate.

### Treat timing as an accessibility variable

Some input systems require more time: switch scanning, speech correction, screen reader navigation, magnifier use, motor fatigue, or cognitive processing. Any timeout, press duration, debounce behavior, auto-submit behavior, animation, or transient control can become exclusionary.

Provide ways to pause, extend, persist, or complete later. Avoid controls that disappear before the person can reach them.

## Output Principles

### Communicate in more than one mode

Important system feedback should not depend on only color, only sound, only motion, only haptics, only visual position, or only text embedded in imagery.

Combine modes where appropriate:

- Text labels and descriptions
- Visual state changes
- Screen-reader-readable status text
- Icons with text support
- Captions and transcripts
- Audio descriptions or visual descriptions for instructional media
- Haptic feedback only as an enhancement, never as the only signal
- Persistent status areas for progress and system state

### Match output channel to urgency

Multimodal output can help or harm. A sound, vibration, toast, badge, modal, animation, or flashing indicator may be useful in one context and disruptive in another.

Classify each message before choosing the channel:

- Full attention: urgent, high-consequence, or time-sensitive; may justify modal, assertive status, sound, or haptic output when the user has enabled it.
- Partial attention: useful progress, completion, or warning; should be visible and persistent without blocking the task.
- Peripheral: tips, suggestions, routine updates, promotions, or low-urgency notifications; should not steal focus or override user settings.

Make the urgency visible in the content, not only in the channel.

### Do not overuse assistive announcements

Screen reader and live-region output is still an interruption. Announce dynamic changes only when the information affects the current task, completion state, safety, or user control.

Do not announce decorative animations, every keystroke in custom widgets, repeated loading ticks, hidden layout changes, or low-value suggestions.

### Support output persistence

People may miss output because they are listening to a screen reader, looking away, under glare, using magnification, on a bus, in a meeting, in a loud room, or recovering from interruption.

Critical feedback should remain available long enough to review, copy, share, translate, or revisit. Avoid transient-only success, error, or security messages.

## Assistive Technology Fit

### Build for interoperability

Assistive technology relies on predictable structure, state, and behavior. Do not create a custom interaction if a native element already provides the behavior.

For this reference, the main concern is device fit: the same control should still work when operated by keyboard, screen reader, switch, speech, magnifier, or alternative pointer.

### Design for screen reader and braille output

A screen reader or braille display user may experience the page as a sequence, not as a visual canvas. Ensure that:

- The sequence matches the task order.
- State changes are programmatically available.
- Controls have concise names that make sense out of context.
- Groups, regions, and steps are named.
- Error, success, and progress messages are associated with the affected item.
- Repeated controls are distinguishable.
- Hidden visual-only cues have text equivalents.

Braille display users may read shorter segments at a time, so avoid front-loading all meaning into long labels or verbose status text.

### Design for magnification and low vision

Magnification changes spatial awareness. A person may see only a small part of the interface at once.

Support:

- Persistent orientation cues.
- Local labels near controls.
- Clear section boundaries.
- Stable focus movement.
- Avoidance of off-screen-only feedback.
- No reliance on far-away comparisons or color-only relationships.
- Layouts that do not require horizontal scanning at high zoom.

### Design for speech input

Speech users often speak visible labels or commands. Use labels that are unique, visible, and close to their controls. Avoid many identical buttons called “Edit,” “Open,” or “More” without nearby differentiating text.

Where speech control may be common, avoid unlabeled icon-only controls and hidden command surfaces.

### Design for switch and eye-gaze input

Switch and eye-gaze use can make each activation costly. Reduce activation count. Avoid unnecessary confirmations, nested menus, tiny targets, and controls that change position during scanning or dwell.

For long flows, provide summary actions, shortcuts, saved state, and ways to resume from the current step.

## Configuration As Augmentation

Configuration is not an extra. It is a primary way software adapts to human diversity.

Treat these as configurable dimensions when relevant:

- Text size and line spacing
- Display scale and layout density
- Contrast, color theme, and reduced visual intensity
- Motion, autoplay, and animation intensity
- Sound, captions, transcripts, and audio descriptions
- Notification channel, timing, frequency, and urgency override
- Input timing, press duration, repeat rate, dwell behavior, and debounce tolerance
- Control mapping and shortcut customization
- Reading mode, narration, or simplified view
- Guidance level: trial-and-error, semi-structured, or step-by-step
- Language, units, time format, and localization
- Data sharing, privacy, and personalization scope

### Keep settings discoverable

People may know the barrier they face but not know which device, accessory, or setting solves it. Make relevant settings visible at the moment of need, not only in a distant settings page.

Use contextual entry points such as “Adjust display,” “Change notification behavior,” “Use another input method,” “Need another way to complete this?”, or “Make this easier to read.”

### Balance flexibility with overload

Too many settings can become a barrier. Use progressive disclosure:

- Start with a small number of high-impact presets.
- Explain what each option changes.
- Let people preview changes before committing.
- Offer advanced settings only when needed.
- Allow reset to default.
- Retain preferences across sessions when appropriate.

Avoid settings names that require technical knowledge without explanation.

### Save, share, and reproduce configurations

For hardware-adjacent and assistive setups, the exact configuration can matter. A small change in angle, scale, contrast, press timing, shortcut mapping, or device placement can determine whether the system works.

Where privacy and safety allow, support:

- Saved configuration profiles.
- Exportable or shareable setup notes.
- Support-friendly summaries of relevant settings.
- Setup recipes that show combinations of device, accessory, OS setting, browser setting, and product setting.
- Recovery after device change, browser reset, or account migration.

Do not expose sensitive disability information without explicit consent.

### Do not force personalization through surveillance

Adaptive systems can learn from behavior, but adaptation must not become covert profiling. Prefer explicit settings, transparent suggestions, and reversible changes.

When inferring preferences, state what changed, why it changed, and how to undo or disable it.

## High Contrast, Color, and Scaling

### Respect system and browser preferences

A web interface should remain usable when the person changes browser zoom, OS display scale, high contrast, forced colors, text size, reduced motion, or color scheme.

Do not design only for the default theme at 100% zoom.

### High contrast is not a niche mode

High contrast can support people with low vision, people using devices in bright sunlight, people with visual fatigue, and people who need stronger separation between controls and background.

When designing components:

- Do not rely on subtle shadows, pale borders, low-opacity text, or background images for essential boundaries.
- Use real borders or outlines where needed.
- Ensure focus, selection, hover, error, and disabled states remain visible in forced colors.
- Use icons that can inherit text color rather than fixed decorative colors.
- Avoid meaning conveyed by color alone.
- Test badges, charts, tags, and status indicators without their original palette.

### Scaling must not break the task

Text scaling and zoom should preserve content, controls, order, and completion paths.

Check for:

- Text clipping in buttons, cards, nav, tabs, form fields, and toasts.
- Fixed-height containers that hide content.
- Controls that overlap at high zoom.
- Sticky headers or footers that consume too much viewport.
- Horizontal scrolling caused by rigid layouts.
- Tooltips or popovers that cannot fit on small or zoomed screens.
- Labels separated from inputs after reflow.
- Truncated filenames, serial numbers, device IDs, addresses, and error codes that users may need to copy.

Use responsive reflow, flexible containers, and content-aware sizing.

### Let color support attention without hijacking it

Color can guide, but it can also overload. Bright badges, red counters, flashing indicators, and high-saturation accents may become more disruptive than useful.

Use accent color sparingly for priority, and pair it with text, shape, position, or iconography. Provide ways to mute non-essential badges and reduce visual intensity.

## Physical and Tactile Context

### Account for environment

A person’s capability changes with location and situation. Test the experience against contexts such as:

- At home
- In a library
- In a car as a passenger
- In a city center
- On a bus or train
- In a crowd
- Alone
- With coworkers
- With family
- In bright sunlight
- In a loud room
- With no Wi-Fi or low bandwidth
- While carrying something
- While fatigued or overwhelmed

The design should adapt when capabilities change. A user may not be able to see, hear, speak, touch precisely, remember a previous step, or use both hands.

### Translate tactile cues into digital behavior

Physical products use texture, grip, contrast, raised details, and affordances to show where to act. Web interfaces need equivalent clarity.

Use:

- Obvious entry points.
- Clear control shapes.
- Labels near interaction points.
- Distinct affordances for draggable, tappable, editable, expandable, and selected elements.
- Persistent step indicators.
- Touch targets that feel stable and reachable.
- Visual grouping that mirrors the task structure.
- Optional haptic feedback only when it improves confirmation and has visual/audio/text alternatives.

Do not use invisible gestures, decorative-only icons, or ambiguous surfaces as the only way to proceed.

### Reduce pivot-points

In physical packaging, reducing lift, pull, rotate, and reach lowers barriers. In web work, reduce equivalent mode shifts:

- Switching between tabs, windows, apps, devices, or documents.
- Moving from QR code to mobile page to desktop login without continuity.
- Requiring camera scan plus manual entry plus email verification for one task.
- Moving between mouse, keyboard, touch, and phone without necessity.
- Using drag-only arrangement or precision sliders for core tasks.

Prefer fewer mode changes, not merely fewer steps. A longer sequence of simple, stable steps can be more accessible than a short sequence of complex gestures.

### Provide ready access

Do not force one predetermined path when multiple paths are feasible. For important outcomes, provide alternatives:

- QR code and text URL.
- Mobile and desktop completion.
- Camera scan and manual entry.
- Biometric login and non-biometric login.
- Voice instructions and text instructions.
- Video instructions and accessible HTML instructions.
- Guided setup and direct advanced setup.
- Online help and offline fallback where feasible.

## Hardware-Adjacent Web Flows

### Device setup, pairing, and registration

Setup flows often fail because they assume perfect vision, dexterity, network access, memory, or confidence. For any setup flow:

- Show prerequisites before starting.
- Explain what device, cable, accessory, account, code, app, or network is needed.
- Provide a non-QR path.
- Provide a non-camera path.
- Support screen reader use from the first step.
- Save progress automatically.
- Let the person pause and resume.
- Keep codes visible long enough to copy or review.
- Avoid requiring two devices unless there is no alternative.
- Explain what successful connection looks, sounds, or feels like.
- Provide troubleshooting by symptom, not by internal system terminology only.

### Product-to-web instructions

When web content extends physical packaging or hardware instructions, it must be accessible as a primary experience, not as a secondary convenience.

Use accessible HTML as the anchor format. Videos and interactive widgets can help, but they need captions, transcripts, and visual descriptions. Do not make PDF, image-only instructions, video-only instructions, QR-only links, or app-only flows the only path.

### QR codes and physical labels

QR codes reduce typing and memory load, but they are not sufficient by themselves.

When designing a QR-supported web experience:

- Pair the QR with a readable short URL.
- Ensure the destination works on mobile and desktop.
- Keep the destination accessible without needing the physical packaging again.
- Use high contrast on the physical code and test from printed samples.
- Consider tactile or textual cues on packaging to help blind and low-vision users locate the code.
- Do not place critical information only inside the code destination.

### Support materials

Support materials should show real configurations, not just idealized product shots. Include disabled users demonstrating successful setups when possible, with consent and respectful framing.

Show combinations such as:

- Laptop stand plus detached keyboard and mouse.
- High contrast plus increased display scale.
- Screen reader plus keyboard shortcuts.
- Braille display plus web form flow.
- Switch input plus simplified navigation.
- Alternative mouse plus larger targets.
- Adaptive controller plus remapped controls.

This builds awareness and helps people recognize setups they can adapt for themselves.

## Adaptive Systems and Automation

Adaptive behavior can reduce burden when it respects the person’s goal. It can exclude when it overrides, surprises, narrows, or hides control.

For adaptive device or input/output behavior:

- Ask before changing major settings.
- Explain why a recommendation is being made.
- Show what will change.
- Make changes reversible.
- Avoid framing declined suggestions as user failure.
- Do not infer disability identity from behavior.
- Do not use camera, microphone, biometric, or sensor data without explicit purpose and consent.
- Stress-test any AI or sensor-driven behavior across diverse bodies, skin tones, speech patterns, movement patterns, assistive devices, environments, and contexts.

When a system uses camera tracking, speech recognition, gesture recognition, personalization, or automated setup, test for dataset, association, automation, interaction, and confirmation bias.

## Research and Co-Design Procedure

Use this procedure before finalizing a responsive, multimodal, or hardware-adjacent design.

1. Identify the top two access methods involved today: touch, sight, hearing, voice, keyboard, pointer, switch, scan, or other.
2. Identify which access methods are missing or assumed impossible.
3. List the device, accessory, assistive technology, physical context, and configuration assumptions in the current design.
4. Recruit people by need spectrum and access method, not by diagnosis alone.
5. Include people who use assistive technology for partial or complete vision loss, partial or complete hearing loss, limited upper-extremity use, limited lower-extremity use, speech differences, motor fatigue, and focus-related barriers when relevant.
6. Observe actual adaptations and setup workarounds.
7. Run a context-and-capability match: combine one physical or social context with one temporary or situational limitation, then revise the design.
8. Run situational adaptation: vary physical context, social context, and time of day; list new limitations and adaptations.
9. Evaluate whether the chosen technology is the simplest and most appropriate way to accomplish the result.
10. Retest after changes with the same and different access configurations.

## Test Matrix

At minimum, test the relevant flow with:

- Keyboard only.
- Screen reader plus keyboard.
- Browser zoom and text scaling.
- High contrast or forced-colors mode.
- Reduced motion.
- Touch on a small screen.
- Pointer with low precision.
- Slow or unstable network.
- No sound.
- Captions/transcripts for media.
- Mobile and desktop layouts.
- A path that does not use QR scanning.
- A path that does not use camera, microphone, or biometric input.

For hardware-adjacent work, also test:

- Device not found.
- Pairing failure.
- Firmware or setup delay.
- Wrong cable or missing accessory.
- Code expired.
- User switches device mid-flow.
- User loses packaging or printed instructions.
- User needs support without sharing private disability information.

## Acceptance Criteria

A design passes this reference when:

- Core tasks work through more than one input method.
- Core feedback is available through more than one output mode or has an equivalent.
- High contrast, scaling, zoom, and reduced motion do not break task completion.
- Settings are discoverable, understandable, previewable, reversible, and persistent where appropriate.
- The system does not require special hardware, app downloads, QR scanning, camera use, biometric input, or precise gestures for core access unless an equivalent path exists.
- Physical context has been tested, not merely imagined.
- Assistive technology users can perceive state, navigate in task order, complete actions, and recover from errors.
- Device or setup failures provide plain-language recovery paths.
- Adaptive behavior is consentful, explainable, and reversible.
- Support materials show practical configurations and do not assume one normal setup.

## Anti-Patterns

Avoid:

- Treating desktop, mouse, 100% zoom, default contrast, and visual scanning as the normal baseline.
- Hiding accessibility-relevant settings behind vague labels or deep menus.
- Detecting disability and silently changing the interface.
- QR-only, camera-only, drag-only, hover-only, sound-only, haptic-only, color-only, or video-only paths.
- Tiny touch targets, unstable layouts, disappearing controls, and precision gestures.
- Custom widgets that work for mouse users but fail for keyboard, speech, switch, or screen reader users.
- Transient feedback that cannot be reviewed.
- Instructional content that depends on packaging, images, or video after the user has moved to the web.
- Assuming assistive technology users all use the same tools or need the same output.
- Assuming a purpose-built device removes the need for adjustability, configurability, documentation, or support.
