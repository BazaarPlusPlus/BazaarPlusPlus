pub(crate) const THE_BAZAAR_APP_ID: &str = "1617400";
pub(crate) const LAUNCH_OPTIONS_KEY: &str = "LaunchOptions";
pub(crate) fn launch_options_empty_in_content(vdf_content: &str) -> Result<Option<bool>, String> {
    let lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    let mut nested_depth = 0usize;
    for line in &lines[app_open + 1..app_close] {
        match line.trim() {
            "{" => {
                nested_depth += 1;
                continue;
            }
            "}" => {
                nested_depth = nested_depth.saturating_sub(1);
                continue;
            }
            _ => {}
        }
        if nested_depth == 0
            && parse_line_pair(line)
                .is_some_and(|(_indent, key, value)| key == LAUNCH_OPTIONS_KEY && !value.is_empty())
        {
            return Ok(Some(false));
        }
    }
    Ok(Some(true))
}

fn parse_line_pair(line: &str) -> Option<(&str, &str, &str)> {
    let indent_len = line.find('"')?;
    let indent = &line[..indent_len];
    let trimmed = &line[indent_len..];

    fn parse_quoted(input: &str) -> Option<(&str, &str)> {
        let mut escaped = false;
        let mut end = None;
        let chars = input.char_indices();

        for (idx, ch) in chars.skip(1) {
            if escaped {
                escaped = false;
                continue;
            }

            if ch == '\\' {
                escaped = true;
                continue;
            }

            if ch == '"' {
                end = Some(idx);
                break;
            }
        }

        let end = end?;
        Some((&input[1..end], &input[end + 1..]))
    }

    let (key, rest) = parse_quoted(trimmed)?;
    let rest = rest.trim_start();
    let (value, remainder) = parse_quoted(rest)?;
    if !remainder.trim().is_empty() {
        return None;
    }

    Some((indent, key, value))
}

fn join_lines(lines: &[String]) -> String {
    lines.join("\n")
}

fn find_apps_block(lines: &[String]) -> Option<(usize, usize)> {
    let start = lines
        .iter()
        .position(|line| line.trim().eq_ignore_ascii_case("\"apps\""))?;
    let open = (start + 1..lines.len()).find(|&idx| lines[idx].trim() == "{")?;
    let mut depth = 0usize;

    for (idx, line) in lines.iter().enumerate().skip(open) {
        match line.trim() {
            "{" => depth += 1,
            "}" => {
                depth = depth.saturating_sub(1);
                if depth == 0 {
                    return Some((open, idx));
                }
            }
            _ => {}
        }
    }

    None
}

fn find_named_block(
    lines: &[String],
    range: std::ops::RangeInclusive<usize>,
    key: &str,
) -> Option<(usize, usize)> {
    let mut idx = *range.start();
    while idx <= *range.end() {
        if lines[idx].trim() == format!("\"{key}\"") {
            let open = (idx + 1..=*range.end()).find(|&line_idx| lines[line_idx].trim() == "{")?;
            let mut depth = 0usize;
            for (offset, line) in lines[open..=*range.end()].iter().enumerate() {
                let line_idx = open + offset;
                match line.trim() {
                    "{" => depth += 1,
                    "}" => {
                        depth = depth.saturating_sub(1);
                        if depth == 0 {
                            return Some((open, line_idx));
                        }
                    }
                    _ => {}
                }
            }
        }
        idx += 1;
    }
    None
}

fn remove_launch_options_text(vdf_content: &str) -> Result<Option<String>, String> {
    let mut lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    let mut nested_depth = 0usize;
    let mut launch_option_lines = Vec::new();
    for (idx, line) in lines.iter().enumerate().take(app_close).skip(app_open + 1) {
        match line.trim() {
            "{" => {
                nested_depth += 1;
                continue;
            }
            "}" => {
                nested_depth = nested_depth.saturating_sub(1);
                continue;
            }
            _ => {}
        }
        if nested_depth == 0
            && parse_line_pair(line).is_some_and(|(_indent, key, _value)| key == LAUNCH_OPTIONS_KEY)
        {
            launch_option_lines.push(idx);
        }
    }
    if launch_option_lines.is_empty() {
        return Ok(None);
    }
    for idx in launch_option_lines.into_iter().rev() {
        lines.remove(idx);
    }

    Ok(Some(join_lines(&lines)))
}

pub fn clear_launch_options(vdf_content: &str) -> Result<Option<String>, String> {
    remove_launch_options_text(vdf_content)
}
