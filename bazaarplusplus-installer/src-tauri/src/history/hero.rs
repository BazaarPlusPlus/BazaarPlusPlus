pub(crate) fn canonical_hero_id(raw: &str) -> String {
    let trimmed = raw.trim();
    if trimmed.eq_ignore_ascii_case("Hero8") || trimmed.eq_ignore_ascii_case("TheDragons") {
        return "TheDragons".to_string();
    }

    trimmed.to_string()
}

pub(crate) fn hero_display_name(canonical: &str) -> &str {
    match canonical {
        "TheDragons" => "The Dragons",
        _ => canonical,
    }
}

#[cfg(test)]
mod tests {
    use super::{canonical_hero_id, hero_display_name};

    /// The analyzer owns the legacy-to-canonical mapping; the installer keeps its
    /// own matching rule (ASCII case-insensitive, trimmed).
    const HERO_ALIASES: &str =
        include_str!("../../../../bazaarplusplus-analyzer/contracts/v5/hero-aliases.json");

    #[test]
    fn hero_aliases_canonicalize_and_display() {
        let file: serde_json::Value = serde_json::from_str(HERO_ALIASES).unwrap();
        assert_eq!(file["schema_version"], 1);
        let aliases = file["aliases"].as_object().unwrap();
        assert!(!aliases.is_empty());
        for (alias, canonical) in aliases {
            let canonical = canonical.as_str().unwrap();
            for spelling in [
                alias.clone(),
                alias.to_ascii_lowercase(),
                alias.to_ascii_uppercase(),
                format!("  {alias}\n"),
                canonical.to_string(),
                canonical.to_ascii_uppercase(),
            ] {
                assert_eq!(canonical_hero_id(&spelling), canonical, "{spelling:?}");
            }
        }
        assert_eq!(hero_display_name("TheDragons"), "The Dragons");
        assert_eq!(canonical_hero_id("  Vanessa  "), "Vanessa");
        assert_eq!(hero_display_name("Vanessa"), "Vanessa");
        assert_eq!(canonical_hero_id("   "), "");
    }
}
