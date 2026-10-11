use std::path::PathBuf;

use crate::problem::{SemanticProblem, SemanticProblemCode};
use crate::services::game_process::GameRunningRefusal;

/// Why an install-page action (install, reset, Legacy Root deletion) failed.
/// Refusals and partial failures stay typed until [`ActionError::into_problem`]
/// turns them into the problem the frontend renders; any other failure is a
/// diagnostic string.
#[derive(Debug, PartialEq, Eq)]
pub(crate) enum ActionError {
    /// The game was running, or its processes could not be inspected.
    Refused(GameRunningRefusal),
    /// The action removed what it could and left these paths behind.
    PartialFailure(Vec<PathBuf>),
    Failed(String),
}

impl ActionError {
    pub(crate) fn into_problem(self, operation: &str) -> SemanticProblem {
        match self {
            Self::Refused(refusal) => refusal.into_problem().with_param("operation", operation),
            Self::PartialFailure(paths) => {
                SemanticProblem::new(SemanticProblemCode::InstallPartialFailure)
                    .with_param("operation", operation)
                    .with_param("count", paths.len().to_string())
                    // The frontend splits on `\u{1f}`, which cannot collide with
                    // Windows drive letters or path separators.
                    .with_param(
                        "paths",
                        paths
                            .iter()
                            .map(|path| path.display().to_string())
                            .collect::<Vec<_>>()
                            .join("\u{1f}"),
                    )
            }
            Self::Failed(diagnostic) => {
                SemanticProblem::new(SemanticProblemCode::InstallActionFailed)
                    .with_param("operation", operation)
                    .with_diagnostic(diagnostic)
            }
        }
    }
}

impl From<String> for ActionError {
    fn from(diagnostic: String) -> Self {
        Self::Failed(diagnostic)
    }
}

impl From<GameRunningRefusal> for ActionError {
    fn from(refusal: GameRunningRefusal) -> Self {
        Self::Refused(refusal)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::services::game_process::GameStoppedOperation;

    #[test]
    fn each_kind_becomes_the_problem_the_install_page_renders() {
        let blocked = ActionError::from(GameRunningRefusal {
            operation: GameStoppedOperation::Install,
            inspection_error: None,
        })
        .into_problem("install");
        assert_eq!(blocked.code, SemanticProblemCode::InstallBlockedByGame);
        assert_eq!(
            blocked.params.get("operation").map(String::as_str),
            Some("install")
        );
        assert_eq!(blocked.diagnostic, None);

        let partial =
            ActionError::PartialFailure(vec![PathBuf::from("/tmp/a"), PathBuf::from("/tmp/b")])
                .into_problem("reset_bpp_data");
        assert_eq!(partial.code, SemanticProblemCode::InstallPartialFailure);
        assert_eq!(partial.params.get("count").map(String::as_str), Some("2"));
        assert_eq!(
            partial.params.get("paths").map(String::as_str),
            Some("/tmp/a\u{1f}/tmp/b")
        );
        assert_eq!(partial.diagnostic, None);

        let generic = ActionError::from("permission denied".to_string()).into_problem("install");
        assert_eq!(generic.code, SemanticProblemCode::InstallActionFailed);
        assert_eq!(generic.diagnostic.as_deref(), Some("permission denied"));
    }
}
