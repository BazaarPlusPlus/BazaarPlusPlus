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

    #[test]
    fn hero_aliases_canonicalize_and_display() {
        for alias in ["Hero8", "TheDragons", "hero8", "THEDRAGONS", "  Hero8\n"] {
            assert_eq!(canonical_hero_id(alias), "TheDragons", "{alias:?}");
        }
        assert_eq!(hero_display_name("TheDragons"), "The Dragons");
        assert_eq!(canonical_hero_id("  Vanessa  "), "Vanessa");
        assert_eq!(hero_display_name("Vanessa"), "Vanessa");
        assert_eq!(canonical_hero_id("   "), "");
    }
}
