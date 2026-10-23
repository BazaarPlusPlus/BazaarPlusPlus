A radio group drawn as joined segments, for switching between a few views of the same content.

Source: `bazaarplusplus-installer/src/components/ui/SegmentedControl.tsx` with `.bpp-segmented*`.

## Use

- Use it for two to four options that filter or re-scope what is already on screen. Use a select for more options.
- The control is exactly `control-md` (36px): a 1px border, a 2px inset and `control-sm` (30px) segments, so it lines up with buttons and inputs beside it. The track is `bg-canvas` with a `line` border and `r-control`. The checked segment is `bg-selected` on `r-inner` with `text-1`, and the others are `text-2` (hover `text-1`).
- It is built on native radios, so arrow keys work. The focus ring is a 2px `accent` outline on the segment.

## The consumer provides

`label` (the accessible group name), `name`, `value`, `options` (value, label, optional disabled) and `onChange`.
