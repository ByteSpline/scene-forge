namespace SceneForge.Media.Rendering;

// How a segment whose source aspect ratio differs from RenderOutputSpec's
// target dimensions is reconciled - the brief's "explicit fit/fill/letterbox
// settings" (CLAUDE.md rule 10: never an opaque, unconfigurable default
// crop/stretch).
public enum AspectFitMode
{
    // Scale to fit entirely within the target box preserving aspect ratio,
    // padding the remaining area with RenderOutputSpec.PadColor
    // (letterbox/pillarbox). Never crops source content.
    Letterbox,

    // Scale to fully cover the target box preserving aspect ratio, cropping
    // whatever overflows. Never pads.
    Fill,

    // Scale to the target box exactly, ignoring the source aspect ratio.
    // Never crops or pads, but visibly distorts any source whose aspect
    // ratio differs from the target.
    Stretch,
}
