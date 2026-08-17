# Web Accessibility Baseline

Use this file before writing or reviewing frontend code. It defines the minimum implementation standard for web pages, flows, and components in this inclusive design system.

This baseline is intentionally code-facing. It assumes design intent has already been considered and focuses on whether the implementation preserves access across keyboard, screen reader, speech input, switch input, touch, pointer, zoom, high contrast, reduced motion, slow network, and mixed-device contexts.

## Non-Negotiable Rule

Start with the simplest accessible path. Add styling, animation, custom behavior, personalization, and AI only after the core path is semantic, operable, understandable, and testable.

A component is not ready when it looks complete. It is ready when people can perceive it, reach it, operate it, understand what changed, recover from mistakes, and complete the task without relying on a single sense, input method, viewport size, speed, or memory state.

## Review Order

Review frontend work in this order:

1. Structure: page title, language, landmarks, headings, regions, reading order.
2. Semantics: correct native elements, roles only where needed, names, descriptions, states.
3. Operation: keyboard path, touch path, pointer path, speech-friendly labels, no traps.
4. Focus: visible focus, logical movement, restoration, route-change behavior, no obscured focus.
5. Forms: labels, instructions, grouping, validation, autocomplete, error recovery.
6. Visual access: contrast, non-color cues, scaling, high contrast, responsive reflow.
7. Motion and time: reduced-motion behavior, pause/stop controls, calm timers, no harmful flashing.
8. Dynamic states: loading, status, errors, success, interruption, resumption, live announcements.
9. Assistive technology: screen reader, speech input, switch-like keyboard use, zoom, and preference testing.
10. Edge cases: empty, long content, localization, slow connection, disabled controls, permissions, unauthenticated state.

## Semantic HTML

Prefer native HTML because native elements carry built-in semantics, expected keyboard behavior, browser integration, and assistive technology support.

Use:

- `<header>`, `<nav>`, `<main>`, `<aside>`, and `<footer>` for page regions.
- A unique `<main>` region for primary content.
- A useful `<title>` for every page and route.
- `lang` on `<html>` and on inline language changes when needed.
- Headings that describe the page outline and let people scan by section.
- Lists for lists, tables for tabular data, buttons for actions, links for navigation.
- Native form controls before custom controls.
- `<details>` and `<summary>` for simple disclosure patterns when their default behavior fits.

Avoid:

- `div` or `span` controls that imitate buttons, links, checkboxes, tabs, or selects.
- Click handlers on non-interactive elements unless equivalent semantics and keyboard behavior are fully implemented.
- Layout tables.
- Empty landmarks, duplicate unlabeled landmarks, and generic region names.
- Heading levels chosen for visual size rather than information structure.

### Button versus link

Use a link when activation navigates to another URL, route, file, anchor, or external destination.

Use a button when activation changes state, opens a dialog, submits a form, dismisses a message, toggles visibility, copies content, runs a command, or starts a process.

Do not use anchors with `href="#"` as buttons. Do not use buttons for ordinary navigation.

### Landmarks

Landmarks must help people skip and orient, not create noise.

- Keep the number of landmarks modest.
- Name repeated landmarks, such as multiple navigation regions.
- Put primary page content inside `<main>`.
- Provide a skip link when repeated navigation precedes main content.
- Keep reading order and visual order aligned unless there is a clear, tested reason not to.

Example skip link pattern:

```html
<a class="skip-link" href="#main">Skip to main content</a>
<main id="main">...</main>
```

```css
.skip-link {
  position: absolute;
  inset-block-start: 0;
  inset-inline-start: 0;
  transform: translateY(-120%);
}
.skip-link:focus-visible {
  transform: translateY(0);
  z-index: 1000;
}
```

## ARIA

ARIA can expose semantics, relationships, and states when native HTML is not enough. It does not add behavior. If a role implies keyboard interaction, state changes, or focus behavior, implement those behaviors in code.

Use ARIA only when one of these is true:

- The native element cannot express the required state or relationship.
- A custom composite widget is unavoidable.
- Dynamic content needs a carefully scoped announcement.
- A landmark, region, or control needs a programmatic name.

Rules:

- Use native elements first.
- Do not change native semantics unless necessary.
- Do not put `aria-hidden="true"` on focusable content or on an ancestor of focusable content.
- Do not put `role="presentation"` or `role="none"` on focusable elements.
- Do not use roles without required child roles, states, and keyboard behavior.
- Do not use `aria-label` to replace visible text when the visible text can be the accessible name.
- Keep visible labels and accessible names aligned so speech input users can say what they see.
- Update `aria-expanded`, `aria-selected`, `aria-checked`, `aria-current`, `aria-invalid`, and `aria-busy` when state changes.
- Use `aria-describedby` for supplemental help or error text, not for the primary label.
- Prefer `aria-labelledby` when visible text already names the control or region.

Bad:

```html
<div role="button" onclick="submitForm()">Submit</div>
```

Better:

```html
<button type="submit">Submit</button>
```

## Accessible Names and Descriptions

Every interactive element needs a clear accessible name.

Acceptable name sources, in preferred order:

1. Visible text content.
2. A connected `<label>` for form controls.
3. `aria-labelledby` pointing to visible text.
4. `aria-label` only when no visible text can name the control.

Do not create names that are vague out of context, such as “Click here,” “More,” “Open,” “Close,” or “Learn more,” unless surrounding programmatic context makes them unique. Prefer “Open billing menu,” “Close filters,” “Learn more about payment plans.”

For icon-only controls:

```html
<button type="button" aria-label="Search">
  <svg aria-hidden="true" focusable="false">...</svg>
</button>
```

For helper text:

```html
<label for="email">Email</label>
<p id="email-help">Use the address where you want receipts sent.</p>
<input id="email" name="email" type="email" autocomplete="email" aria-describedby="email-help">
```

## Keyboard Behavior

Everything interactive must be reachable and operable without a mouse or touch gesture.

Core behavior:

- `Tab` moves to the next focusable item.
- `Shift + Tab` moves to the previous focusable item.
- `Enter` activates links and buttons.
- `Space` activates buttons, checkboxes, and similar controls.
- `Escape` closes dismissible popovers, menus, dialogs, and transient overlays.
- Arrow keys move inside composite widgets only when that pattern is intentionally implemented.
- Focus never enters a dead end.
- Focus never disappears.
- Keyboard operation never requires pointer-only steps.

Do not:

- Use `tabindex` values greater than `0`.
- Trap focus except inside an active modal dialog or equivalent blocking interaction.
- Remove focus indicators.
- Require hover, drag, pinch, swipe, long press, precise pointer placement, or timed response as the only path.
- Put keyboard focus on non-actionable text unless there is a strong reason.

Use `tabindex="0"` only when a custom element must become reachable in normal order. Use `tabindex="-1"` for programmatic focus targets such as headings after route changes or dialog titles.

### Composite widgets

Composite widgets include tabs, listboxes, menus, grids, trees, sliders, and comboboxes. Use them only when a simpler native control is insufficient.

For composite widgets:

- Keep one tab stop for the widget where possible.
- Use arrow keys to move within the widget according to the expected pattern.
- Keep active, selected, checked, expanded, and disabled states synchronized visually and programmatically.
- Do not use menu semantics for ordinary site navigation. Use lists of links for navigation.
- Do not build a custom select, combobox, or date picker unless the team can test it thoroughly with keyboard and assistive technology.

## Focus Management

Focus is orientation. Treat it as part of the visible interface.

### Focus appearance

- Every focusable control needs a visible focus indicator.
- The indicator must be visible against adjacent colors and component backgrounds.
- Prefer a two-layer indicator, such as a light inner ring and dark outer ring, when controls sit on variable backgrounds.
- Do not rely on color alone; combine outline, offset, thickness, shape, or underline.
- Do not let sticky headers, fixed footers, cookie banners, chat launchers, or overlays cover focused content.

Example:

```css
:focus-visible {
  outline: 3px solid CanvasText;
  outline-offset: 3px;
}
@media (forced-colors: active) {
  :focus-visible {
    outline: 3px solid Highlight;
  }
}
```

Use `scroll-margin` or `scroll-padding` so focused anchors and validation targets are not hidden under sticky UI.

### Route changes

On client-side route changes:

- Update the document title.
- Move focus to the page heading, main region, or another clear start point.
- Announce only useful route or status changes.
- Do not leave focus on a removed button or previous-route control.

Pattern:

```html
<h1 tabindex="-1" id="page-title">Billing settings</h1>
```

```js
document.title = 'Billing settings';
document.getElementById('page-title')?.focus();
```

### Dialogs and overlays

For modal dialogs:

- Move focus into the dialog when it opens.
- Focus the dialog title or first meaningful action, depending on task risk.
- Keep `Tab` and `Shift + Tab` inside the dialog while it is modal.
- Provide a visible close button unless closing would be unsafe or impossible by design.
- Close with `Escape` when safe.
- Restore focus to the trigger or next logical location when closed.
- Hide or inert background content from interaction while modal.
- Name the dialog with visible text.

Baseline structure:

```html
<div role="dialog" aria-modal="true" aria-labelledby="dialog-title">
  <h2 id="dialog-title" tabindex="-1">Delete project?</h2>
  <p id="dialog-desc">This removes the project for everyone on the team.</p>
  <button type="button">Cancel</button>
  <button type="button">Delete project</button>
</div>
```

For non-modal popovers, menus, tooltips, and drawers, define the trigger, focus entry, dismissal, focus return, and background interaction explicitly.

## Forms

Forms often fail when they rely on memory, precision, speed, or hidden rules. Make forms explicit, forgiving, and recoverable.

### Labels and grouping

- Every input, select, and textarea needs a persistent programmatic label.
- Do not use placeholder text as the only label.
- Use `<fieldset>` and `<legend>` for related radio buttons, checkboxes, and grouped choices.
- Put instructions before the fields they affect.
- Use examples, formats, and constraints before validation, not only after failure.
- Mark required fields in text as well as visually.

Example:

```html
<fieldset>
  <legend>Contact preference</legend>
  <label><input type="radio" name="contact" value="email"> Email</label>
  <label><input type="radio" name="contact" value="phone"> Phone</label>
</fieldset>
```

### Autocomplete and input purpose

Use meaningful `autocomplete` values for personal information, payment, address, username, and password fields. Allow password managers, copy, paste, and one-time-code entry.

Do not block paste in password, email, confirmation, or code fields. Do not require people to re-enter data already provided unless there is a security or accuracy reason.

### Validation and errors

Validation must be specific, perceivable, and recoverable.

- Validate at submit for complex forms; validate inline only when it helps and does not interrupt typing.
- Keep the user’s entered data.
- Put an error summary before the form when multiple fields fail.
- Link each summary item to the relevant field.
- Put field-level error text near the field.
- Connect errors with `aria-describedby`.
- Use `aria-invalid="true"` only when the field is currently invalid.
- Do not identify errors by red color alone.
- Do not blame the person.
- State the fix first when it is simple.

Example:

```html
<label for="postcode">ZIP code</label>
<p id="postcode-error">Enter a 5-digit ZIP code, such as 98101.</p>
<input id="postcode" name="postcode" inputmode="numeric" autocomplete="postal-code" aria-invalid="true" aria-describedby="postcode-error">
```

### Authentication

Authentication flows must provide at least one path that does not depend on solving puzzles, memorizing unusual strings, transcribing distorted text, or performing high-cognitive-load tasks.

Support:

- Password managers.
- Copy and paste.
- Show/hide password with an accessible button.
- One-time-code autocomplete where available.
- Recovery paths that are clear and reachable from the form.
- Re-authentication without discarding unsaved work.

## Status, Loading, and Dynamic Updates

People need to know what changed, but announcements should not create noise.

Use visible status text first. Add live announcements only for meaningful changes that are not otherwise apparent to assistive technology users.

- Use polite announcements for progress, saved state, filter results, and non-urgent updates.
- Use assertive announcements only for urgent, time-sensitive, or blocking errors.
- Do not place large containers, full pages, or rapidly changing feeds inside live regions.
- Do not announce decorative animation or every keystroke.
- Use `aria-busy="true"` while a region is updating if the intermediate state would be confusing.
- Ensure loading indicators have text, not only a spinner.

Example:

```html
<div role="status" aria-live="polite">Saving changes…</div>
```

For loading states:

- State what is loading.
- Preserve layout stability.
- Preserve user input.
- Show progress or estimated time when feasible.
- Provide cancellation when the wait is long or optional.
- Do not use shimmer or motion as the only indication of progress.

## Color and Contrast

Color must support comprehension without becoming the only channel of meaning.

Baseline thresholds:

- Normal text: at least 4.5:1 contrast against its background.
- Large text: at least 3:1 contrast against its background.
- Icons, focus indicators, input borders, selected states, and meaningful graphical parts: at least 3:1 against adjacent colors.
- Text over images, video, gradients, glass, or translucency must be tested in all expected states and breakpoints.
- Placeholder text must be readable if it conveys useful information.

Use more than color to communicate state:

- Error: color plus text, icon, border, and message.
- Required: visible text plus programmatic relationship.
- Selected/current: visual treatment plus state or text.
- Charts: labels, patterns, direct annotation, table alternative, or summary.

Avoid:

- Low-opacity text for non-disabled content.
- Thin gray outlines as the only control boundary.
- Red-only errors or green-only success.
- Badges or alerts that rely only on aggressive color to demand attention.
- Meaningful color combinations that collapse in forced-colors or high-contrast modes.

### High contrast and forced colors

- Test with forced-colors or high-contrast mode.
- Do not remove outlines, borders, or system colors.
- Use `currentColor` for icons where possible.
- Avoid background images as the only source of meaning.
- Do not use `forced-color-adjust: none` unless preserving a tested essential graphic.

## Text, Media, and Non-Text Content

### Images and icons

- Decorative images: empty alt text and hidden from assistive technology when appropriate.
- Informative images: concise alt text that conveys purpose.
- Functional images: alt or accessible name describes the action, not the image.
- Complex charts, diagrams, screenshots, and infographics: provide nearby explanation, data table, or structured summary.
- Icons next to visible text should usually be hidden from assistive technology to avoid duplicate names.

### Media

For audio or video:

- Provide captions for speech and meaningful sound.
- Provide transcripts for audio and videos where people need text review.
- Provide audio description or equivalent text when visuals carry meaning not available in the audio.
- Do not autoplay sound.
- Provide accessible controls for play, pause, mute, volume, captions, transcript, and fullscreen where relevant.

### Contenteditable and rich text

If using rich text editors:

- Provide a clear label and instructions.
- Preserve keyboard shortcuts without overriding browser and assistive technology commands unnecessarily.
- Expose formatting state where possible.
- Provide a plain-text fallback or markdown/source option when appropriate.
- Test with screen reader and keyboard before shipping.

## Motion, Animation, and Time

Motion can clarify relationships, but it can also steal focus, increase sensory load, or make people ill.

- Respect reduced-motion preferences.
- Remove or simplify non-essential animation when reduced motion is requested.
- Disable smooth scrolling when reduced motion is requested.
- Provide pause, stop, hide, or dismiss controls for moving, blinking, scrolling, or auto-updating content that persists.
- Do not flash content more than three times in one second.
- Do not use motion as the only way to explain a state change.
- Avoid parallax, large zoom effects, scroll-jacking, rapid carousels, and animated backgrounds in task flows.
- Keep loading, success, and error motion subtle and optional.

Example:

```css
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    scroll-behavior: auto !important;
    transition-duration: 0.01ms !important;
  }
}
```

For timers:

- Provide pause, extend, save-and-continue, and restart where feasible.
- Avoid countdown pressure unless time is intrinsic to the task.
- Let people hide or switch timer visualization when feasible.
- Warn before session expiration and allow extension without data loss.

## Responsive Behavior and Zoom

The interface must work when people enlarge text, zoom the browser, use narrow screens, rotate devices, or switch input methods.

- Use fluid layouts and relative units for text and spacing where appropriate.
- Support text resizing to at least 200% without loss of content or function.
- Support narrow viewport reflow down to 320 CSS px without horizontal scrolling except for content that genuinely requires two-dimensional layout, such as maps, data tables, diagrams, and code editors.
- At high zoom, preserve reading order, labels, controls, errors, and primary actions.
- Do not hide essential controls at smaller breakpoints.
- Do not require hover to reveal essential actions.
- Keep line length readable.
- Use responsive images and avoid fixed heights that clip text.
- Support device orientation changes unless orientation is essential.
- Keep sticky UI from covering focused controls or validation targets.

### Target size and spacing

- Interactive targets should be at least 24 by 24 CSS px.
- Prefer at least 44 by 44 CSS px for touch-heavy interfaces.
- If a smaller visual control is unavoidable, increase the hit area or spacing.
- Prevent adjacent controls from being so close that accidental activation is likely.
- Do not make precision dragging the only method for important actions.

## Input Method Diversity

Design for multiple ways to operate the same interface.

### Pointer and touch

- Do not require precise pointer movement.
- Provide alternatives for drag-and-drop, drawing, sorting, sliders, maps, and resizing.
- Do not rely on hover-only content for essential information.
- Provide visible labels and persistent controls for touch contexts.
- Avoid gesture-only actions; provide buttons or menu commands.

### Speech input

Speech input often depends on visible labels and accessible names matching.

- Make visible button and link text specific.
- Do not name a visible “Save” button as “Submit form” programmatically.
- Avoid multiple identical visible labels in the same view unless each has clear surrounding context.
- Use real labels for fields so people can target them by name.

### Switch and sequential input

People using switch-like navigation may traverse focus one item at a time.

- Keep tab order short and purposeful.
- Avoid repeated unnecessary focus stops.
- Provide skip links and section navigation for long pages.
- Avoid focusable disabled controls unless they are intentionally reachable and explained.
- Ensure dismissal controls are reachable before destructive actions.

## Component Patterns

### Disclosure or accordion

Use a button for the trigger.

```html
<button type="button" aria-expanded="false" aria-controls="panel-billing">
  Billing details
</button>
<section id="panel-billing" hidden>...</section>
```

When opened, update `aria-expanded` and remove `hidden`. Do not put the trigger in a non-button heading without preserving button behavior.

### Tabs

Use tabs only when panels are peers and the selected panel replaces the others in the same context. For ordinary navigation, use links.

- Use one tab stop for the active tab when implementing a desktop-style tab widget.
- Use arrow keys to move between tabs.
- Use `Home` and `End` when there are many tabs.
- Ensure each tab names its panel.
- Keep inactive panels hidden from keyboard and assistive technology if they are not available.

If implementation risk is high, use a simple list of anchor links to sections instead.

### Menus

Use menu semantics only for command menus, not site navigation.

For navigation:

```html
<nav aria-label="Primary">
  <ul>
    <li><a href="/products">Products</a></li>
    <li><a href="/support">Support</a></li>
  </ul>
</nav>
```

For a menu button, define trigger state, menu ownership, arrow-key behavior, escape behavior, and focus return.

### Toasts and banners

- Keep messages short and actionable.
- Do not steal focus for non-blocking updates.
- Provide a reachable dismiss control when messages persist.
- Use polite announcements for non-urgent messages.
- Use assertive announcements only when immediate attention is necessary.
- Do not auto-dismiss important information before people can read or act.

### Carousels

Carousels are high-risk. Avoid them for essential content.

If used:

- Do not auto-advance by default.
- Provide pause, next, previous, and direct slide controls.
- Ensure controls are buttons with names.
- Announce slide position without excessive noise.
- Keep focus stable when slides change.
- Ensure all slide content is reachable by keyboard.

### Data tables and grids

For static data, use native tables.

- Use `<caption>` when a title or summary helps.
- Use `<th scope="col">` and `<th scope="row">`.
- Keep sorting controls keyboard-operable and named by column.
- Do not convert tables to inaccessible card stacks at small widths without preserving relationships.

For editable grids, use a tested grid pattern and document keyboard behavior. If that cannot be tested, choose a simpler form or table interaction.

## Hidden Content

Different hiding methods produce different accessibility behavior.

- `display: none`, `hidden`, and `visibility: hidden` remove content from visual display and assistive technology.
- `aria-hidden="true"` removes content from assistive technology only; never apply it to focusable content.
- Visually hidden utility classes should keep content available to assistive technology.
- Offscreen content must not create unexpected focus jumps or horizontal scrolling.

Visually hidden pattern:

```css
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
  border: 0;
}
```

## Disabled, Readonly, and Unavailable States

- Use `disabled` for native controls that cannot be operated and do not need focus.
- Use `readonly` when a value can be read and copied but not edited.
- Use `aria-disabled="true"` only when a custom or intentionally focusable control must remain perceivable in the focus order.
- Explain why a critical action is unavailable and what enables it.
- Do not make disabled text too low-contrast to read when it contains useful information.

## Internationalization and Localization

Accessibility failures often appear after translation or localization.

- Allow text expansion without clipping.
- Do not hard-code line heights or fixed component heights that truncate text.
- Keep icon meaning culturally neutral or label icons with text.
- Use `dir` for right-to-left content.
- Ensure form examples match locale-specific formats.
- Do not rely on English-only visible instructions when the interface is localized.

## Performance and Network Conditions

Slow interfaces increase cognitive demand and can break assistive technology timing.

- Keep initial render fast enough that people know the page is working.
- Avoid layout shifts that move focused or targeted controls.
- Preserve input during reconnects, refreshes, validation, and route changes.
- Provide retry and offline-aware messaging for failed actions.
- Do not make loading indicators indefinite when the system can provide progress or context.

## Assistive Technology Implementation

Automated tools catch only part of the problem. Manual testing is required.

Minimum manual checks:

- Keyboard only: complete the core flow without pointer or touch.
- Screen reader: understand page structure, controls, names, states, errors, and dynamic updates.
- Speech input: activate visible controls by the words shown on screen.
- Zoom and reflow: complete the core flow at high zoom and narrow width.
- High contrast or forced colors: identify controls, states, focus, and selected items.
- Reduced motion: confirm non-essential motion is removed or simplified.
- Touch: activate controls without precision.
- Slow network: understand loading, retry, and saved state.

Representative screen reader coverage:

- Windows with a common screen reader and Chromium or Firefox.
- macOS or iOS with the built-in screen reader and Safari.
- Android with the built-in screen reader and Chrome when mobile use is in scope.

Test tasks, not just components. A component can pass in isolation and fail in a real flow because focus, reading order, error recovery, loading, or routing breaks around it.

## Implementation Anti-Patterns

Do not ship:

- A custom control that lacks native-equivalent keyboard behavior.
- A modal that does not trap and restore focus.
- A drawer that can be opened but not closed by keyboard.
- A tooltip that contains essential information but appears only on hover.
- An icon-only button without an accessible name.
- A form field without a persistent label.
- Error text that is not programmatically connected to the field.
- A spinner without status text.
- A toast that auto-dismisses important information.
- A carousel that auto-advances and cannot be paused.
- A drag-and-drop workflow without a non-drag alternative.
- Text that fails contrast because it sits on imagery, gradients, transparency, or disabled styling.
- Motion that ignores reduced-motion preferences.
- A route change that leaves focus behind.
- Positive `tabindex` values.
- `aria-hidden` on focusable content.
- Meaningful content hidden from assistive technology because it was visually hidden incorrectly.
- A visible label and programmatic name that disagree.

## Acceptance Checklist

Before considering frontend code complete, verify:

- Page title, language, landmarks, headings, and reading order are correct.
- Every interactive element uses the right native element or has complete equivalent semantics and behavior.
- Every control has a clear accessible name and, where needed, description.
- Keyboard users can reach, operate, dismiss, and recover from every interactive state.
- Focus is visible, logical, restored after overlays, and not obscured by sticky UI.
- Forms have labels, groups, autocomplete, instructions, specific errors, summaries, and preserved input.
- Dynamic updates are visible and announced only when useful.
- Text, controls, icons, focus indicators, and meaningful graphics meet the contrast thresholds.
- Meaning is never conveyed by color, motion, shape, sound, or position alone.
- Reduced-motion, high-contrast, zoom, text resizing, and narrow layouts work.
- Touch targets and spacing support people with limited dexterity or one-handed use.
- Dragging, hover, gesture, and timed interactions have accessible alternatives.
- Media includes captions, transcripts, descriptions, and accessible controls where relevant.
- Component states are covered: default, hover, focus, active, selected, expanded, collapsed, disabled, loading, empty, error, success, interrupted, and reduced-motion.
- The core task has been tested manually with keyboard, screen reader, speech-like visible labels, zoom, high contrast, and reduced motion.
