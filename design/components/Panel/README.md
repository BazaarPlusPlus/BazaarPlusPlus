Panels and what goes in them: rows of label, value and control, and key-value grids.

Source: `.bpp-panel`, `.bpp-panel-pad`, `.bpp-row*`, `.bpp-kv*` and `.bpp-disclosure*` in `bundle.css`. The site's `panel` utility is the same frame.

## Use

- A panel is `bg-panel` with a 1px `line` border and `r-panel`, and **no shadow**. `bpp-panel-pad` adds 16px padding with 14px gaps.
- Every row is `row-height` (52px), padded 16px at the sides, with a `line` between them and its content centred. Settings rows and key-value rows are the same height, so neighbouring panels line up. Each row is a `body-medium` label, an optional value in `text-3` and a trailing badge or `sm` button. The button's verb acts on the label (本地数据 ··· 删除), so it doesn't repeat it. Add a `caption` description only when the label can't stand alone.
- Key-value grids have a 148px `text-3` key column, an ellipsized value (`.is-empty` in `text-3`), an actions column, and the same 52px rows. A trailing icon-only button hangs 8px into the padding so its glyph lines up with the edge.
- `bpp-disclosure` folds a list of rows under a summary row whose chevron turns 180° when it opens.

## The consumer provides

The rows or key-value cells as children.
