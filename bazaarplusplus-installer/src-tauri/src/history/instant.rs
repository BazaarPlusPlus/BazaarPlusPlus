//! The mod stores every instant as Unix-epoch milliseconds in `*_at_ms` columns;
//! History DTOs and stream records keep RFC 3339 text, so these convert at the
//! column boundary.

use chrono::{DateTime, FixedOffset, SecondsFormat, Utc};
use rusqlite::types::Type;

/// RFC 3339 UTC text for a stored instant: `Z`, with milliseconds only when present.
pub(crate) fn utc_text(millis: i64) -> Option<String> {
    DateTime::<Utc>::from_timestamp_millis(millis)
        .map(|value| value.to_rfc3339_opts(SecondsFormat::AutoSi, true))
}

/// RFC 3339 text of a stored instant at the UTC offset recorded beside it.
pub(crate) fn local_text(millis: i64, utc_offset_minutes: i64) -> Option<String> {
    let offset = FixedOffset::east_opt(i32::try_from(utc_offset_minutes.checked_mul(60)?).ok()?)?;
    DateTime::<Utc>::from_timestamp_millis(millis).map(|value| {
        value
            .with_timezone(&offset)
            .to_rfc3339_opts(SecondsFormat::AutoSi, false)
    })
}

/// Reads an `*_at_ms` column as RFC 3339 UTC text.
pub(crate) fn utc_column(row: &rusqlite::Row<'_>, index: usize) -> rusqlite::Result<String> {
    let millis: i64 = row.get(index)?;
    utc_text(millis).ok_or_else(|| out_of_range(index, millis))
}

/// Reads a nullable `*_at_ms` column as RFC 3339 UTC text.
pub(crate) fn optional_utc_column(
    row: &rusqlite::Row<'_>,
    index: usize,
) -> rusqlite::Result<Option<String>> {
    row.get::<_, Option<i64>>(index)?
        .map(|millis| utc_text(millis).ok_or_else(|| out_of_range(index, millis)))
        .transpose()
}

fn out_of_range(index: usize, millis: i64) -> rusqlite::Error {
    rusqlite::Error::FromSqlConversionFailure(
        index,
        Type::Integer,
        format!("{millis} is not a representable Unix-millisecond instant").into(),
    )
}

#[cfg(test)]
mod tests {
    use super::{local_text, utc_text};

    #[test]
    fn instants_format_as_rfc3339_text() {
        assert_eq!(
            utc_text(1_767_258_600_000).as_deref(),
            Some("2026-01-01T09:10:00Z")
        );
        assert_eq!(
            utc_text(1_767_258_600_123).as_deref(),
            Some("2026-01-01T09:10:00.123Z")
        );
        assert_eq!(
            local_text(1_767_258_600_000, 480).as_deref(),
            Some("2026-01-01T17:10:00+08:00")
        );
        assert_eq!(utc_text(i64::MAX), None);
    }
}
