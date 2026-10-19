//! The todo model: what the API accepts and returns, and its validation.

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use utoipa::{IntoParams, ToSchema};
use uuid::Uuid;

pub const MAX_TITLE_CHARS: usize = 200;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, ToSchema)]
pub struct Todo {
    pub id: Uuid,
    #[schema(example = "Buy milk")]
    pub title: String,
    pub completed: bool,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize, ToSchema)]
pub struct CreateTodo {
    /// 1 to 200 characters after trimming.
    #[schema(example = "Buy milk")]
    pub title: String,
}

/// Fields left out keep their current value.
#[derive(Debug, Clone, Default, Serialize, Deserialize, ToSchema)]
pub struct UpdateTodo {
    #[serde(default, skip_serializing_if = "Option::is_none")]
    #[schema(example = "Buy oat milk")]
    pub title: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub completed: Option<bool>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize, ToSchema)]
#[serde(rename_all = "lowercase")]
pub enum StatusFilter {
    #[default]
    All,
    Active,
    Completed,
}

#[derive(Debug, Clone, Copy, Default, Serialize, Deserialize, IntoParams)]
#[into_params(parameter_in = Query)]
pub struct ListTodos {
    /// Which todos to return.
    #[serde(default)]
    pub status: StatusFilter,
}

impl StatusFilter {
    /// The `completed` value this filter selects, if it restricts at all.
    pub fn completed(self) -> Option<bool> {
        match self {
            Self::All => None,
            Self::Active => Some(false),
            Self::Completed => Some(true),
        }
    }
}

/// Trims a title and checks its length. The error is a client-safe message.
pub fn validate_title(title: &str) -> Result<String, String> {
    let title = title.trim();
    if title.is_empty() {
        return Err("must not be empty".to_owned());
    }
    if title.chars().count() > MAX_TITLE_CHARS {
        return Err(format!("must be at most {MAX_TITLE_CHARS} characters"));
    }
    Ok(title.to_owned())
}

impl Todo {
    pub fn new(title: String) -> Self {
        let now = now();
        Self {
            id: Uuid::now_v7(),
            title,
            completed: false,
            created_at: now,
            updated_at: now,
        }
    }
}

/// The current time at the precision every backend stores (microseconds).
pub fn now() -> DateTime<Utc> {
    use chrono::SubsecRound;
    Utc::now().trunc_subsecs(6)
}
