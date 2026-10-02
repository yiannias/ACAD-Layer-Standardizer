use acad_layer_core::{LayerCategorizationResult, MatchResult};
use serde::{Deserialize, Serialize};

pub const DEFAULT_PIPE_NAME: &str = "acad_layer_standardizer";

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DrawingSnapshot {
    pub drawing_name: String,
    pub source_layers: Vec<String>,
    pub standard_layers: Vec<String>,
    pub empty_layers: Vec<String>,
    pub memory_mappings: std::collections::HashMap<String, String>,
    pub target_filters: Vec<TargetFilter>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TargetFilter {
    pub name: String,
    pub sort_group: String,
    pub layers: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "payload")]
pub enum IpcRequest {
    Ping,
    GetDrawingLayers,
    GetDrawingSnapshot,
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
    DrawingSnapshot(DrawingSnapshot),
    Classification(Vec<MatchResult>),
    Categorization(LayerCategorizationResult),
    Applied { count: usize },
    Error(String),
}

#[cfg(windows)]
pub fn request_drawing_snapshot() -> Result<IpcResponse, String> {
    use interprocess::os::windows::named_pipe::{pipe_mode, DuplexPipeStream};
    use std::{
        io::{BufRead, BufReader, Write},
        thread,
        time::Duration,
    };

    let pipe_path = format!(r"\\.\pipe\{DEFAULT_PIPE_NAME}");
    let mut last_error = String::from("AutoCAD IPC pipe is not available");

    for _ in 0..50 {
        match DuplexPipeStream::<pipe_mode::Bytes>::connect_by_path(pipe_path.as_str()) {
            Ok(stream) => {
                let mut connection = BufReader::new(stream);
                let request = serde_json::to_vec(&IpcRequest::GetDrawingSnapshot)
                    .map_err(|error| error.to_string())?;
                connection
                    .get_mut()
                    .write_all(&request)
                    .map_err(|error| error.to_string())?;
                connection
                    .get_mut()
                    .write_all(b"\n")
                    .map_err(|error| error.to_string())?;
                connection
                    .get_mut()
                    .flush()
                    .map_err(|error| error.to_string())?;

                let mut response = String::new();
                connection
                    .read_line(&mut response)
                    .map_err(|error| error.to_string())?;
                return serde_json::from_str(&response).map_err(|error| error.to_string());
            }
            Err(error) => {
                last_error = error.to_string();
                thread::sleep(Duration::from_millis(100));
            }
        }
    }

    Err(format!("Could not connect to AutoCAD: {last_error}"))
}

#[cfg(not(windows))]
pub fn request_drawing_snapshot() -> Result<IpcResponse, String> {
    Err("AutoCAD named-pipe IPC is available only on Windows".to_string())
}
