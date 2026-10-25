use std::collections::BTreeMap;

#[derive(Clone, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize, specta::Type)]
pub struct SemanticProblem {
    pub code: SemanticProblemCode,
    pub params: BTreeMap<String, String>,
    pub diagnostic: Option<String>,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize, specta::Type)]
#[serde(rename_all = "snake_case")]
pub enum SemanticProblemCode {
    HistoryUnavailable,
    HistoryReadFailed,
    HistoryReadBlockedByGame,
    HistoryDatabaseUnsupportedSchema,
    HistoryActionFailed,
    HistoryThumbnailsUnavailable,
    InstallDetectionFailed,
    InstallActionFailed,
    InstallPartialFailure,
    InstallBlockedByGame,
    ResetBlockedByGame,
    BepinexResetBlockedByGame,
    ImportBlockedByGame,
    // The V5 import stopped; its `reason` parameter is `ImportError::code`.
    ImportFailed,
    LegacyDeleteBlockedByGame,
    StreamServiceFailed,
    StreamWindowFailed,
    StreamCropFailed,
}

impl SemanticProblem {
    pub fn new(code: SemanticProblemCode) -> Self {
        Self {
            code,
            params: BTreeMap::new(),
            diagnostic: None,
        }
    }

    pub fn with_param(mut self, name: impl Into<String>, value: impl Into<String>) -> Self {
        self.params.insert(name.into(), value.into());
        self
    }

    pub fn with_diagnostic(mut self, diagnostic: impl Into<String>) -> Self {
        self.diagnostic = Some(diagnostic.into());
        self
    }

    /// Attaches `error` and its causes as the diagnostic.
    pub fn with_error(self, error: &anyhow::Error) -> Self {
        self.with_diagnostic(error_diagnostic(error))
    }
}

/// Renders an error and its causes as one diagnostic line, outermost first and
/// joined by `": "`. A cause whose text the message before it already
/// contains is skipped, because many library errors embed their source in
/// their own message.
pub(crate) fn error_diagnostic(error: &anyhow::Error) -> String {
    let mut diagnostic = String::new();
    for cause in error.chain() {
        let message = cause.to_string();
        if diagnostic.contains(&message) {
            continue;
        }
        if !diagnostic.is_empty() {
            diagnostic.push_str(": ");
        }
        diagnostic.push_str(&message);
    }
    diagnostic
}

#[cfg(test)]
mod tests {
    use super::{error_diagnostic, SemanticProblem, SemanticProblemCode};
    use anyhow::Context as _;

    #[test]
    fn error_diagnostic_joins_causes_and_skips_embedded_ones() {
        let io = std::io::Error::other("disk full");
        let error = Err::<(), _>(io)
            .context("Cannot write manifest")
            .unwrap_err();
        assert_eq!(error_diagnostic(&error), "Cannot write manifest: disk full");

        let embedded = anyhow::anyhow!("zip error: disk full").context("Cannot read archive");
        assert_eq!(
            error_diagnostic(&embedded),
            "Cannot read archive: zip error: disk full"
        );
    }

    #[test]
    fn semantic_problem_serializes_stable_code_params_and_optional_diagnostic() {
        let problem = SemanticProblem::new(SemanticProblemCode::HistoryReadFailed)
            .with_param("operation", "list_runs")
            .with_diagnostic("database is locked");

        assert_eq!(
            serde_json::to_value(problem).unwrap(),
            serde_json::json!({
                "code": "history_read_failed",
                "params": { "operation": "list_runs" },
                "diagnostic": "database is locked"
            })
        );

        assert_eq!(
            serde_json::to_value(SemanticProblem::new(
                SemanticProblemCode::HistoryUnavailable
            ))
            .unwrap(),
            serde_json::json!({
                "code": "history_unavailable",
                "params": {},
                "diagnostic": null
            })
        );

        assert_eq!(
            serde_json::to_value(SemanticProblem::new(
                SemanticProblemCode::HistoryDatabaseUnsupportedSchema
            ))
            .unwrap(),
            serde_json::json!({
                "code": "history_database_unsupported_schema",
                "params": {},
                "diagnostic": null
            })
        );

        assert_eq!(
            serde_json::to_value(SemanticProblem::new(
                SemanticProblemCode::HistoryThumbnailsUnavailable
            ))
            .unwrap(),
            serde_json::json!({
                "code": "history_thumbnails_unavailable",
                "params": {},
                "diagnostic": null
            })
        );

        assert_eq!(
            serde_json::to_value(SemanticProblem::new(
                SemanticProblemCode::InstallDetectionFailed
            ))
            .unwrap(),
            serde_json::json!({
                "code": "install_detection_failed",
                "params": {},
                "diagnostic": null
            })
        );

        assert_eq!(
            serde_json::to_value(SemanticProblem::new(
                SemanticProblemCode::StreamServiceFailed
            ))
            .unwrap(),
            serde_json::json!({
                "code": "stream_service_failed",
                "params": {},
                "diagnostic": null
            })
        );
    }
}
