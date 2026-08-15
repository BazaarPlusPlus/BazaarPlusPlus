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
    fn canonicalizes_the_dragons_aliases() {
        assert_eq!(canonical_hero_id("Hero8"), "TheDragons");
        assert_eq!(canonical_hero_id("TheDragons"), "TheDragons");
    }

    #[test]
    fn canonicalizes_the_dragons_case_insensitively() {
        assert_eq!(canonical_hero_id("hero8"), "TheDragons");
        assert_eq!(canonical_hero_id("THEDRAGONS"), "TheDragons");
    }

    #[test]
    fn trims_whitespace_before_canonicalizing() {
        assert_eq!(canonical_hero_id("  Hero8\n"), "TheDragons");
    }

    #[test]
    fn preserves_other_heroes_after_trimming() {
        assert_eq!(canonical_hero_id("  Vanessa  "), "Vanessa");
        assert_eq!(hero_display_name("Vanessa"), "Vanessa");
    }

    #[test]
    fn preserves_empty_input() {
        assert_eq!(canonical_hero_id("   "), "");
        assert_eq!(hero_display_name(""), "");
    }

    #[test]
    fn formats_the_dragons_display_name() {
        assert_eq!(hero_display_name("TheDragons"), "The Dragons");
    }
}
