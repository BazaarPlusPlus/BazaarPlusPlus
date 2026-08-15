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
    InstallDetectionFailed,
    InstallActionFailed,
    InstallGameRunning,
    InstallPartialFailure,
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
}

#[cfg(test)]
mod tests {
    use super::{SemanticProblem, SemanticProblemCode};

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
