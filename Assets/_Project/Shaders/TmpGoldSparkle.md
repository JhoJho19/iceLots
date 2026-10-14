# Gold TextMeshPro UI

The scene's balances, last win, bet, jackpot amount and daily reward labels use
`Baloo2-Bold Gold.mat`. The shader works with the project's URP Canvas and uses
TMP's Mobile Distance Field SDF path, including outline, softness, dilate,
underlay, stencil masks, RectMask2D clipping and CanvasGroup opacity.

## Setup

1. Duplicate your font asset's material (or duplicate `Baloo2-Bold Gold.mat`
   when using Baloo 2 Bold). Choose **IceP/TextMeshPro/Gold Sparkle** as its shader.
   Keep the font atlas, gradient scale, texture dimensions and weight settings
   from the original font material. A preset must match its text's font atlas.
2. Assign the preset to the TextMeshProUGUI **Material Preset** field. Set the
   text's vertex color to white; face color and vertex tint multiply the gold.
3. Add **TmpGoldSparkleBinder** and assign the same preset to **Material Preset**.
4. Enter Play mode to preview animation and the star. The binder creates
   temporary per-label material instances and one child procedural quad. It
   restores the assigned preset and destroys those resources when disabled.
   The saved scene contains the preset, not generated materials or children.

The component follows the visible glyph bounds after text changes, alignment,
autosizing, wrapping and layout changes. It anchors one flare to the whole
string's upper-right corner. Child transforms inherit Canvas scaling and the
text's transform. No sprite, texture for the star, or post-processing is used.

## Tuning

All requested properties are visible below the standard TMP panels in the
material Inspector:

| Property | Meaning / starting value |
| --- | --- |
| `_GoldColorA`, `_GoldColorB` | Light and deep gold; keep enough contrast with the background. |
| `_ShineColor` | Warm ivory highlight. |
| `_ShineIntensity` | 0.65; lower for a quieter effect. |
| `_ShineSpeed` | 0.22 cycles/second, one sweep about every 4.5 seconds. |
| `_ShineWidth` | 0.1 of the whole string's width. |
| `_SparkleColor` | Warm ivory/gold flare. |
| `_SparkleIntensity` | 0.8; zero hides the flare. |
| `_SparkleSize` | 0.32 radius in em, relative to the current autosized font size. |
| `_SparklePulseSpeed` | 0.55 pulses/second. |
| `_SparkleOffsetX`, `_SparkleOffsetY` | 0.18 / 0.08 em beyond the visible top-right corner. |

The preset uses a thin dark outline and soft underlay for readability. Standard
TMP outline and underlay toggles remain available. Animation uses `_Time`, so it
follows Unity's scaled game time. A zero speed freezes the corresponding effect.

## Practical limits

This adds one small UI draw for the flare and two transient materials per active
label. No per-frame glyph mesh rewrite is needed for animation. Bounds update
when TMP rebuilds its text; preset changes and the flare's tint/geometry are
synchronized by the component. Parent masks intentionally clip the flare, so
leave enough space around the text inside a masked viewport.

Bevel, bump mapping, environment reflections and glow from the desktop TMP
shader are not included. This preset targets the supplied font atlas; a font
fallback or additional atlas material needs its own compatible gold preset.
