#[cfg(test)]
use keyvalues_parser::{Obj, Value};

pub(crate) const THE_BAZAAR_APP_ID: &str = "1617400";
pub(crate) const LAUNCH_OPTIONS_KEY: &str = "LaunchOptions";

#[cfg(test)]
fn first_obj<'a, 'text>(values: &'a [Value<'text>]) -> Option<&'a Obj<'text>>
where
    'a: 'text,
{
    values.first()?.get_obj()
}

#[cfg(test)]
pub(crate) fn get_app_obj<'a, 'text>(root: &'a Obj<'text>, app_id: &str) -> Option<&'a Obj<'text>>
where
    'a: 'text,
{
    root.get("Software")
        .and_then(|values| first_obj(values))
        .and_then(|software| software.get("Valve").and_then(|values| first_obj(values)))
        .and_then(|valve| valve.get("Steam").and_then(|values| first_obj(values)))
        .and_then(|steam| steam.get("apps").and_then(|values| first_obj(values)))
        .and_then(|apps| apps.get(app_id).and_then(|values| first_obj(values)))
}

fn escape_vdf_string(value: &str) -> String {
    value.replace('\\', "\\\\").replace('"', "\\\"")
}

pub(crate) fn verify_launch_options_in_content(
    vdf_content: &str,
    expected: &str,
) -> Result<Option<bool>, String> {
    let lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    for idx in app_open + 1..app_close {
        if let Some((_indent, key, value)) = parse_line_pair(&lines[idx]) {
            if key == LAUNCH_OPTIONS_KEY {
                return Ok(Some(value == escape_vdf_string(expected)));
            }
        }
    }

    Ok(None)
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

    for idx in open..lines.len() {
        match lines[idx].trim() {
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
            for line_idx in open..=*range.end() {
                match lines[line_idx].trim() {
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

fn malformed_launch_option_fragment_count(lines: &[String], start: usize) -> usize {
    let mut consumed = 0usize;
    let mut saw_command = false;

    for line in lines.iter().skip(start) {
        let Some((_indent, key, value)) = parse_line_pair(line) else {
            break;
        };

        if consumed == 0 && !key.contains('/') && !value.contains("%command%") {
            break;
        }

        consumed += 1;
        if key.contains("%command%") || value.contains("%command%") {
            saw_command = true;
            break;
        }
    }

    if saw_command {
        consumed
    } else {
        0
    }
}

fn collect_fragment_text(lines: &[String], start: usize, count: usize) -> String {
    let mut parts = Vec::new();
    for line in lines.iter().skip(start).take(count) {
        if let Some((_indent, key, value)) = parse_line_pair(line) {
            parts.push(key.trim().to_string());
            if !value.trim().is_empty() {
                parts.push(value.trim().to_string());
            }
        }
    }
    parts.join(" ")
}

fn cleanup_malformed_bpp_launch_options(lines: &mut Vec<String>) {
    let mut idx = 0usize;
    while idx < lines.len() {
        let Some((_indent, key, value)) = parse_line_pair(&lines[idx]) else {
            idx += 1;
            continue;
        };

        if key != LAUNCH_OPTIONS_KEY || !value.is_empty() {
            idx += 1;
            continue;
        }

        let fragment_count = malformed_launch_option_fragment_count(lines, idx + 1);
        if fragment_count == 0 {
            idx += 1;
            continue;
        }

        let fragment_text = collect_fragment_text(lines, idx + 1, fragment_count);
        if !fragment_text.contains("run_bepinex.sh") {
            idx += 1;
            continue;
        }

        lines.drain(idx..idx + 1 + fragment_count);
    }
}

fn upsert_launch_options_text(vdf_content: &str, args: &str) -> Result<Option<String>, String> {
    let mut lines = vdf_content.lines().map(str::to_string).collect::<Vec<_>>();
    let Some((apps_open, apps_close)) = find_apps_block(&lines) else {
        return Err("Malformed VDF: could not locate Steam/apps object".to_string());
    };
    let Some((app_open, app_close)) =
        find_named_block(&lines, apps_open..=apps_close, THE_BAZAAR_APP_ID)
    else {
        return Ok(None);
    };

    let mut launch_line_idx = None;
    for idx in app_open + 1..app_close {
        if let Some((_indent, key, _value)) = parse_line_pair(&lines[idx]) {
            if key == LAUNCH_OPTIONS_KEY {
                launch_line_idx = Some(idx);
                break;
            }
        }
    }

    let escape_args = escape_vdf_string(args);
    let property_indent = (app_open + 1..app_close)
        .find_map(|idx| parse_line_pair(&lines[idx]).map(|(indent, _, _)| indent.to_string()))
        .unwrap_or_else(|| format!("{}\t", lines[app_close].split('"').next().unwrap_or("")));
    let launch_line = format!("{property_indent}\"{LAUNCH_OPTIONS_KEY}\"\t\t\"{escape_args}\"");

    if let Some(idx) = launch_line_idx {
        lines[idx] = launch_line;
        let malformed_count = malformed_launch_option_fragment_count(&lines, idx + 1);
        if malformed_count > 0 {
            lines.drain(idx + 1..idx + 1 + malformed_count);
        }
    } else {
        lines.insert(app_close, launch_line);
    }

    cleanup_malformed_bpp_launch_options(&mut lines);

    Ok(Some(join_lines(&lines)))
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

    let mut launch_line_idx = None;
    for idx in app_open + 1..app_close {
        if let Some((_indent, key, _value)) = parse_line_pair(&lines[idx]) {
            if key == LAUNCH_OPTIONS_KEY {
                launch_line_idx = Some(idx);
                break;
            }
        }
    }

    let Some(idx) = launch_line_idx else {
        return Ok(None);
    };

    let malformed_count = malformed_launch_option_fragment_count(&lines, idx + 1);
    lines.remove(idx);
    if malformed_count > 0 {
        lines.drain(idx..idx + malformed_count);
    }

    cleanup_malformed_bpp_launch_options(&mut lines);

    Ok(Some(join_lines(&lines)))
}

pub fn inject_launch_options(vdf_content: &str, args: &str) -> Result<Option<String>, String> {
    upsert_launch_options_text(vdf_content, args)
}

pub fn clear_launch_options(vdf_content: &str) -> Result<Option<String>, String> {
    remove_launch_options_text(vdf_content)
}
