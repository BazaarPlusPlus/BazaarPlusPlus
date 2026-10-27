Inputs and selects with their label, laid out as a field.

Source: `.bpp-input`, `.bpp-select`, `.bpp-select-wrap`, `.bpp-field*` in `bundle.css` (no React wrapper).

## Use

- Controls are `control-md` (36px) tall with 12px padding, the same as a button, on `bg-canvas` with a `line-control` border (3:1, so the field is visible without its label) and `r-control`. Text is `body` (15px) `text-1` and placeholders are `text-3`.
- On focus the border turns `accent` with a 3px `accent-subtle` halo (no outline). Disabled controls drop to 0.45 opacity, like buttons.
- Labels are `small` (14px, 500) in `text-2`. There is no hint line under a field: the label and the control's own placeholder or value carry it. `bpp-field-row` lines up a control with its button, whose verb acts on the field (游戏目录 ··· 选择).
- Use `.mono` for paths, ports and addresses. `.is-readonly` shows a value that can't be edited in `text-2`, with an ellipsis.
- The select draws its own chevron (ChevronDown) at the right, in `text-3`.
- Always give the field a visible label as well.

## The consumer provides

A `<label for>` and the control.
