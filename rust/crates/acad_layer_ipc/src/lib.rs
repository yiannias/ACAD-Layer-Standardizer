use acad_layer_core::{LayerCategorizationResult, MatchResult};
use serde::{Deserialize, Serialize};

pub const DEFAULT_PIPE_NAME: &str = "acad_layer_standardizer";

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "payload")]
pub enum IpcRequest {
    Ping,
    GetDrawingLayers,
    ClassifyLayers {
        source_layers: Vec<String>,
        standard_layers: Vec<String>,
        min_confidence: f64,
    },
    CategorizeLayers {
        layers: Vec<String>,
    },
    ApplyPlan {
        mappings: Vec<(String, String)>,
    },
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "payload")]
pub enum IpcResponse {
    Pong,
    Layers(Vec<String>),
    Classification(Vec<MatchResult>),
    Categorization(LayerCategorizationResult),
    Applied { count: usize },
    Error(String),
}
