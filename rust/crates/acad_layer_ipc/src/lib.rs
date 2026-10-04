use acad_layer_core::{LayerCategorizationResult, MatchResult};
use serde::{Deserialize, Serialize};

pub const DEFAULT_PIPE_NAME: &str = "acad_layer_standardizer";
pub const IPC_PROTOCOL_VERSION: u32 = 2;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DrawingSnapshot {
    pub drawing_name: String,
    #[serde(default = "default_heuristic_threshold")]
    pub heuristic_threshold: f64,
    #[serde(default)]
    pub template_name: String,
    #[serde(default)]
    pub template_path: String,
    pub source_layers: Vec<String>,
    pub standard_layers: Vec<String>,
    pub empty_layers: Vec<String>,
    pub memory_mappings: std::collections::HashMap<String, String>,
    pub target_filters: Vec<TargetFilter>,
    #[serde(default)]
    pub always_hidden_targets: Vec<String>,
}

fn default_heuristic_threshold() -> f64 {
    0.6
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
        protocol_version: u32,
        drawing_name: String,
        mappings: Vec<LayerMapping>,
        remember: bool,
        properties: PropertyMatchSettings,
    },
    PurgeEmptyLayers {
        protocol_version: u32,
        drawing_name: String,
        layers: Vec<String>,
    },
    LoadStandard {
        protocol_version: u32,
        path: String,
    },
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub struct LayerMapping {
    pub source_layer: String,
    pub target_layer: String,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub struct PropertyMatchSettings {
    pub match_color: bool,
    pub match_linetype: bool,
    pub match_lineweight: bool,
    pub make_by_layer: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "payload")]
pub enum IpcResponse {
    Pong,
    Layers(Vec<String>),
    DrawingSnapshot(DrawingSnapshot),
    Classification(Vec<MatchResult>),
    Categorization(LayerCategorizationResult),
    Applied {
        protocol_version: u32,
        count: usize,
        remembered: bool,
        warning: Option<String>,
    },
    Purged {
        protocol_version: u32,
        layers: Vec<String>,
    },
    TemplateLoaded(DrawingSnapshot),
    Error(String),
}

#[cfg(windows)]
pub fn request_drawing_snapshot() -> Result<IpcResponse, String> {
    request(IpcRequest::GetDrawingSnapshot)
}

#[cfg(windows)]
pub fn apply_plan(
    drawing_name: String,
    mappings: Vec<LayerMapping>,
    remember: bool,
    properties: PropertyMatchSettings,
) -> Result<IpcResponse, String> {
    request(IpcRequest::ApplyPlan {
        protocol_version: IPC_PROTOCOL_VERSION,
        drawing_name,
        mappings,
        remember,
        properties,
    })
}

#[cfg(windows)]
pub fn purge_empty_layers(
    drawing_name: String,
    layers: Vec<String>,
) -> Result<IpcResponse, String> {
    request(IpcRequest::PurgeEmptyLayers {
        protocol_version: IPC_PROTOCOL_VERSION,
        drawing_name,
        layers,
    })
}

#[cfg(windows)]
pub fn load_standard(path: String) -> Result<IpcResponse, String> {
    request(IpcRequest::LoadStandard {
        protocol_version: IPC_PROTOCOL_VERSION,
        path,
    })
}

#[cfg(windows)]
fn request(request: IpcRequest) -> Result<IpcResponse, String> {
    use std::{
        fs::OpenOptions,
        io::{BufRead, BufReader, Write},
        thread,
        time::{Duration, SystemTime, UNIX_EPOCH},
    };

    fn trace(message: &str) {
        use std::{fs::OpenOptions, io::Write};
        let Ok(mut file) = OpenOptions::new()
            .create(true)
            .append(true)
            .open(std::env::temp_dir().join("AcLayerStandardizer-rust-ipc.log"))
        else {
            return;
        };
        let millis = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|duration| duration.as_millis())
            .unwrap_or_default();
        let _ = writeln!(file, "{millis} pid={} {message}", std::process::id());
    }

    let pipe_path = format!(r"\\.\pipe\{DEFAULT_PIPE_NAME}");
    let mut last_error = String::from("AutoCAD IPC pipe is not available");
    trace("Snapshot request started.");

    for attempt in 0..50 {
        if attempt == 0 {
            trace("Opening named pipe.");
        }
        match OpenOptions::new()
            .read(true)
            .write(true)
            .open(pipe_path.as_str())
        {
            Ok(stream) => {
                trace("Named pipe opened.");
                let mut connection = BufReader::new(stream);
                let request = serde_json::to_vec(&request).map_err(|error| error.to_string())?;
                trace(&format!("Writing {} request bytes.", request.len()));
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
                trace("Request flushed; waiting for response line.");

                let mut response = String::new();
                connection
                    .read_line(&mut response)
                    .map_err(|error| error.to_string())?;
                trace(&format!("Read {} response bytes.", response.len()));
                return serde_json::from_str(&response).map_err(|error| error.to_string());
            }
            Err(error) => {
                last_error = error.to_string();
                if attempt == 0 || attempt == 49 {
                    trace(&format!(
                        "Pipe open attempt {} failed: {last_error}",
                        attempt + 1
                    ));
                }
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

#[cfg(not(windows))]
pub fn apply_plan(
    _drawing_name: String,
    _mappings: Vec<LayerMapping>,
    _remember: bool,
    _properties: PropertyMatchSettings,
) -> Result<IpcResponse, String> {
    Err("AutoCAD named-pipe IPC is available only on Windows".to_string())
}

#[cfg(not(windows))]
pub fn purge_empty_layers(
    _drawing_name: String,
    _layers: Vec<String>,
) -> Result<IpcResponse, String> {
    Err("AutoCAD named-pipe IPC is available only on Windows".to_string())
}

#[cfg(not(windows))]
pub fn load_standard(_path: String) -> Result<IpcResponse, String> {
    Err("AutoCAD named-pipe IPC is available only on Windows".to_string())
}
