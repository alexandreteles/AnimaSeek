# AnimaSeek — Phoenix identity

The Phoenix is the approved AnimaSeek identity: one simple rising bird whose open cyan and coral wings represent human connection, while its yellow flame-tail represents renewal and the discovery of new experiences. The four broad forms remain readable at app-icon scale and preserve a loose bird-family relationship with Soulseek and Seeker without reusing either logo.

## Raster-first geometry

The user-approved PNG is the geometry source of truth. It is preserved byte-for-byte at `master/animaseek-phoenix-approved-source.png`; because the generated preview contains a baked-in checkerboard, `master/animaseek-phoenix-raster-master.png` is the cleaned transparent production master. The SVG contours were traced from that raster geometry, not used to generate it. Exact hashes and lineage are recorded in `master/animaseek-phoenix-provenance.json`.

Rebuild the package with Python 3.14+ plus Pillow and NumPy:

```sh
python tools/build_phoenix_identity.py /path/to/approved-preview.png
```

## Files

- `master/` contains the untouched approved PNG, cleaned transparent raster master, and provenance metadata.
- `source/` contains self-contained traced SVG symbols, app-icon appearances, outlined wordmarks, and horizontal/stacked lockups.
- `ios/AnimaSeekAssets.xcassets/` is a ready-to-copy iOS asset catalog with Any, Dark, and Tinted app-icon appearances, the launch stacked lockup (normal and reversed, 1x/2x/3x), the `BrandNavy` launch-field color, and launch and in-app marks. The production launch storyboard cannot resolve catalog images, so `Seeker.iOS/Resources/LaunchSplash@2x/@3x.png` ship as loose copies of the dark lockup.
- `raster/` contains large transparent PNG exports for documentation, repositories, and distribution pages.
- `preview/` contains the identity sheet and 40/60/120 px recognition check.
- `tools/` contains the deterministic raster segmentation, contour tracing, and packaging script.

This package is the identity source of truth; the production catalog under `Seeker.iOS/Assets.xcassets` is a copy of `ios/AnimaSeekAssets.xcassets`. When artwork changes, rebuild this package first and re-copy the catalog.

## Palette

| Role | Value | Meaning |
|---|---|---|
| Vacuum Navy | `#071321` | Focus and the single connected self |
| Connection Cyan | `#27B9FF` | One open wing and one side of a connection |
| Connection Coral | `#FF4C58` | The complementary open wing |
| Discovery Yellow | `#FFD33D` | Renewal and unfamiliar possibility |
| Warm White | `#FFF9EE` | Body and wordmark on dark brand fields |

These colors belong to brand artwork only. UIKit controls, text, backgrounds, and states continue to use the semantic system colors documented in `DESIGN.md`.

## Usage

- Use `animaseek-app-icon-any.svg` or its Any PNG for the standard Home Screen appearance. Supply the full square artwork; iOS applies its own icon mask.
- Use the transparent Dark file and opaque grayscale Tinted file in their matching asset-catalog appearance wells.
- Use `animaseek-symbol-color.svg` on light or neutral backgrounds and `animaseek-symbol-reversed.svg` on dark backgrounds.
- Use monochrome variants when reproduction allows only one ink or one UI tint.
- Keep clear space around the symbol equal to at least one half of the body width.
- Keep the standalone symbol at least 40 px tall and the horizontal lockup at least 160 px wide. Below those sizes, use the app icon rather than the standalone mark.
- Preserve the upright orientation and the cyan-left/coral-right wing order.

Do not add outlines, gradients, glass, glow, shadows, detached feathers, sparks, rings, or text inside the app icon. Do not rotate, distort, recolor individual forms, or use brand colors as interface status indicators.

## Typography

The supplied wordmark is outlined from Avenir Next Demi Bold, so SVG use does not depend on font availability. Product UI remains San Francisco through UIKit preferred text styles; the wordmark is brand art, not an interface typography change.

## Accessibility

The icon must never be the only accessible name for the product. In the app, retain the localized `AnimaSeek` accessibility label. Color carries the connection story, while the broad phoenix silhouette remains recognizable in the monochrome exports.
