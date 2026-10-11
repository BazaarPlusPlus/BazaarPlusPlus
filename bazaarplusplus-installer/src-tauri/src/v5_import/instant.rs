//! V5 stored every instant as text; V6 stores Unix milliseconds. This is the one
//! conversion between them.
//!
//! It accepts what the V5 mod wrote and read back: `DateTimeOffset.ToString("o")`
//! (`2026-10-01T12:00:00.0000000+00:00`), `Z` text, and SQLite `datetime()` text
//! (`2026-10-05 14:18:00`). Text without an offset is UTC, as the mod's
//! `SqliteUtcInstant.Parse` read it (`DateTimeStyles.AssumeUniversal`).
//! Fractional seconds are truncated to the millisecond, as
//! `DateTimeOffset.ToUnixTimeMilliseconds` truncates ticks.

use chrono::{DateTime, NaiveDateTime};

const OFFSET_FREE_FORMATS: [&str; 4] = [
    "%Y-%m-%d %H:%M:%S%.f",
    "%Y-%m-%dT%H:%M:%S%.f",
    "%Y-%m-%d %H:%M",
    "%Y-%m-%dT%H:%M",
];

/// Unix milliseconds of a V5 instant, or `None` when the text is not one.
pub(crate) fn parse_v5_instant_ms(text: &str) -> Option<i64> {
    let text = text.trim();
    if text.is_empty() {
        return None;
    }
    if let Ok(value) = DateTime::parse_from_rfc3339(text) {
        return Some(value.timestamp_millis());
    }
    // RFC 3339 requires seconds; `o` text always has them, but a hand-edited or
    // `strftime` value may carry an offset without them.
    for format in ["%Y-%m-%dT%H:%M%:z", "%Y-%m-%d %H:%M%:z"] {
        if let Ok(value) = DateTime::parse_from_str(text, format) {
            return Some(value.timestamp_millis());
        }
    }
    let naive = text.strip_suffix(['Z', 'z']).unwrap_or(text);
    OFFSET_FREE_FORMATS.iter().find_map(|format| {
        NaiveDateTime::parse_from_str(naive, format)
            .ok()
            .map(|value| value.and_utc().timestamp_millis())
    })
}

/// The UTC offset in minutes a V5 local instant was written with (`captured_at_local`),
/// 0 for UTC or offset-free text, or `None` when the text is not an instant.
pub(crate) fn parse_v5_offset_minutes(text: &str) -> Option<i64> {
    let text = text.trim();
    parse_v5_instant_ms(text)?;
    Some(
        DateTime::parse_from_rfc3339(text)
            .ok()
            .or_else(|| {
                ["%Y-%m-%dT%H:%M%:z", "%Y-%m-%d %H:%M%:z"]
                    .iter()
                    .find_map(|format| DateTime::parse_from_str(text, format).ok())
            })
            .map_or(0, |value| i64::from(value.offset().local_minus_utc() / 60)),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    const ALPHA_START_MS: i64 = 1_790_856_000_000; // 2026-10-01T12:00:00Z

    #[test]
    fn round_trip_text_converts_to_utc_milliseconds() {
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T12:00:00.0000000+00:00"),
            Some(ALPHA_START_MS)
        );
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T12:00:00Z"),
            Some(ALPHA_START_MS)
        );
        // A local offset names the same instant.
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T20:00:00.0000000+08:00"),
            Some(ALPHA_START_MS)
        );
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T07:30:00-04:30"),
            Some(ALPHA_START_MS)
        );
    }

    #[test]
    fn fractional_seconds_truncate_to_the_millisecond() {
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T12:00:00.1239999+00:00"),
            Some(ALPHA_START_MS + 123)
        );
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T12:00:00.5Z"),
            Some(ALPHA_START_MS + 500)
        );
        // Before the epoch, truncating ticks floors the millisecond.
        assert_eq!(parse_v5_instant_ms("1969-12-31T23:59:59.9995Z"), Some(-1));
    }

    #[test]
    fn offset_free_text_is_utc() {
        assert_eq!(
            parse_v5_instant_ms("2026-10-01 12:00:00"),
            Some(ALPHA_START_MS)
        );
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T12:00:00.250"),
            Some(ALPHA_START_MS + 250)
        );
        assert_eq!(
            parse_v5_instant_ms("  2026-10-01 12:00  "),
            Some(ALPHA_START_MS)
        );
        assert_eq!(
            parse_v5_instant_ms("2026-10-01T20:00+08:00"),
            Some(ALPHA_START_MS)
        );
    }

    #[test]
    fn non_instants_are_refused() {
        for text in [
            "",
            "   ",
            "t",
            "2026-13-01T00:00:00Z",
            "yesterday",
            "2026-10-01",
        ] {
            assert_eq!(parse_v5_instant_ms(text), None, "{text:?}");
        }
    }

    #[test]
    fn local_offsets_convert_to_minutes() {
        assert_eq!(
            parse_v5_offset_minutes("2026-10-01T20:15:04.1200000+08:00"),
            Some(480)
        );
        assert_eq!(
            parse_v5_offset_minutes("2026-10-01T07:30:00-04:30"),
            Some(-270)
        );
        assert_eq!(parse_v5_offset_minutes("2026-10-01T12:00:00Z"), Some(0));
        assert_eq!(parse_v5_offset_minutes("2026-10-01 12:00:00"), Some(0));
        assert_eq!(parse_v5_offset_minutes("nope"), None);
    }
}
