#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use acad_layer_core::{HeuristicMatcher, MatchResult, MatchSource};
use acad_layer_ipc::{DrawingSnapshot, IpcResponse, TargetFilter};
use raw_window_handle::{HasWindowHandle, RawWindowHandle};
use serde::{Deserialize, Serialize};
use spatial_ui_kit::theme::ThemePalette;
use std::{
    collections::{HashMap, HashSet},
    fs,
    path::PathBuf,
    sync::mpsc::{Receiver, Sender},
};

mod mapping_editor;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default, rename_all = "PascalCase")]
struct UserPreferences {
    mapping_editor_width: f32,
    mapping_editor_height: f32,
    mapping_editor_maximized: bool,
    mapping_editor_zoom: f32,
    mapping_editor_viewport_x: f32,
    mapping_editor_viewport_y: f32,
    animations_enabled: bool,
}

impl Default for UserPreferences {
    fn default() -> Self {
        Self {
            mapping_editor_width: 1200.0,
            mapping_editor_height: 650.0,
            mapping_editor_maximized: false,
            mapping_editor_zoom: 1.0,
            mapping_editor_viewport_x: 0.0,
            mapping_editor_viewport_y: 0.0,
            animations_enabled: true,
        }
    }
}

fn preferences_path() -> Option<PathBuf> {
    std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .map(|path| path.join("AcLayerStandardizer").join("ui_preferences.json"))
}

fn load_preferences() -> UserPreferences {
    preferences_path()
        .and_then(|path| fs::read(path).ok())
        .and_then(|bytes| serde_json::from_slice(&bytes).ok())
        .unwrap_or_default()
}

#[allow(dead_code)]
struct LayerStandardizerApp {
    dark_theme: ThemePalette,
    search_query: String,
    min_confidence: f64,
    source_layers: Vec<String>,
    standard_layers: Vec<String>,
    matches: Vec<MatchResult>,
    status_message: String,
    owner_hwnd: Option<isize>,
    owner_attached: bool,
    apply_pending: bool,
    ipc_sender: Sender<Result<IpcResponse, String>>,
    ipc_results: Receiver<Result<IpcResponse, String>>,
    drawing_name: String,
    template_name: String,
    empty_layers: HashSet<String>,
    memory_mappings: HashMap<String, String>,
    target_filters: Vec<TargetFilter>,
    always_hidden_targets: HashSet<String>,
    mapping_editor: mapping_editor::MappingEditor,
    user_preferences: UserPreferences,
}

impl LayerStandardizerApp {
    fn new(
        owner_hwnd: Option<isize>,
        ipc_sender: Sender<Result<IpcResponse, String>>,
        ipc_results: Receiver<Result<IpcResponse, String>>,
        user_preferences: UserPreferences,
    ) -> Self {
        let mut mapping_editor = mapping_editor::MappingEditor::default();
        mapping_editor.restore_view_preferences(
            user_preferences.mapping_editor_zoom,
            user_preferences.mapping_editor_viewport_x,
            user_preferences.mapping_editor_viewport_y,
            user_preferences.animations_enabled,
        );
        Self {
            dark_theme: ThemePalette::dark(),
            search_query: String::new(),
            min_confidence: 0.6,
            source_layers: Vec::new(),
            standard_layers: Vec::new(),
            matches: Vec::new(),
            status_message: if owner_hwnd.is_some() {
                "Connecting to AutoCAD…".to_string()
            } else {
                "Ready. Connect AutoCAD or paste/import layers to standardize.".to_string()
            },
            owner_hwnd,
            owner_attached: false,
            apply_pending: false,
            ipc_sender,
            ipc_results,
            drawing_name: String::new(),
            template_name: String::new(),
            empty_layers: HashSet::new(),
            memory_mappings: HashMap::new(),
            target_filters: Vec::new(),
            always_hidden_targets: HashSet::new(),
            mapping_editor,
            user_preferences,
        }
    }

    #[allow(dead_code)]
    pub fn set_layers(&mut self, source: Vec<String>, standard: Vec<String>) {
        self.source_layers = source;
        self.standard_layers = standard;
        self.recalculate();
    }

    pub fn recalculate(&mut self) {
        if self.standard_layers.is_empty() || self.source_layers.is_empty() {
            self.matches.clear();
            return;
        }

        let matcher = HeuristicMatcher::new(self.standard_layers.clone(), self.min_confidence);
        let standards_by_name = self
            .standard_layers
            .iter()
            .map(|name| (name.to_ascii_lowercase(), name.as_str()))
            .collect::<HashMap<_, _>>();
        let memory_by_source = self
            .memory_mappings
            .iter()
            .map(|(source, target)| (source.to_ascii_lowercase(), target.as_str()))
            .collect::<HashMap<_, _>>();
        self.matches = self
            .source_layers
            .iter()
            .map(|name| {
                if let Some(target) = standards_by_name.get(&name.to_ascii_lowercase()) {
                    return MatchResult {
                        source_layer: name.clone(),
                        target_layer: Some((*target).to_string()),
                        confidence: 1.0,
                        source: MatchSource::Exact,
                    };
                }
                if let Some(target) = memory_by_source.get(&name.to_ascii_lowercase()) {
                    if let Some(target) = standards_by_name.get(&target.to_ascii_lowercase()) {
                        return MatchResult {
                            source_layer: name.clone(),
                            target_layer: Some((*target).to_string()),
                            confidence: 1.0,
                            source: MatchSource::Memory,
                        };
                    }
                }
                matcher.try_match(name).unwrap_or(MatchResult {
                    source_layer: name.clone(),
                    target_layer: None,
                    confidence: 0.0,
                    source: MatchSource::Unmatched,
                })
            })
            .collect();
    }
}

impl eframe::App for LayerStandardizerApp {
    fn ui(&mut self, ui: &mut egui::Ui, frame: &mut eframe::Frame) {
        ui.ctx().set_visuals(self.dark_theme.base.visuals());
        self.attach_to_owner_if_requested(frame, ui.ctx());
        while let Ok(result) = self.ipc_results.try_recv() {
            match result {
                Ok(IpcResponse::DrawingSnapshot(snapshot)) => {
                    self.set_snapshot(snapshot);
                    self.status_message = format!(
                        "Connected to {} • {} drawing layers • {} standard layers",
                        self.drawing_name,
                        self.source_layers.len(),
                        self.standard_layers.len()
                    );
                }
                Ok(IpcResponse::TemplateLoaded(snapshot)) => {
                    self.apply_pending = false;
                    self.set_snapshot(snapshot);
                    self.status_message = format!(
                        "Loaded {}. Existing assignments were kept where target names matched.",
                        self.template_name
                    );
                }
                Ok(IpcResponse::Applied {
                    protocol_version,
                    count,
                    remembered,
                    warning,
                }) => {
                    self.apply_pending = false;
                    if protocol_version != acad_layer_ipc::IPC_PROTOCOL_VERSION {
                        self.status_message = format!(
                            "AutoCAD returned unsupported apply response version {protocol_version}."
                        );
                    } else if let Some(warning) = warning {
                        self.status_message = format!(
                            "Applied {count} mappings, but could not save translation memory: {warning}"
                        );
                    } else {
                        self.status_message = if remembered {
                            format!("Applied {count} mappings and saved them to memory.")
                        } else {
                            format!("Applied {count} mappings.")
                        };
                    }
                    ui.ctx().send_viewport_cmd(egui::ViewportCommand::Close);
                }
                Ok(IpcResponse::Purged {
                    protocol_version,
                    layers,
                }) => {
                    self.apply_pending = false;
                    if protocol_version != acad_layer_ipc::IPC_PROTOCOL_VERSION {
                        self.status_message = format!(
                            "AutoCAD returned unsupported purge response version {protocol_version}."
                        );
                    } else {
                        self.source_layers.retain(|name| {
                            !layers
                                .iter()
                                .any(|removed| removed.eq_ignore_ascii_case(name))
                        });
                        self.empty_layers.retain(|name| {
                            !layers
                                .iter()
                                .any(|removed| removed.eq_ignore_ascii_case(name))
                        });
                        self.mapping_editor.overrides.retain(|source, _| {
                            !layers
                                .iter()
                                .any(|removed| removed.eq_ignore_ascii_case(source))
                        });
                        self.recalculate();
                        self.status_message = format!("Purged {} empty layer(s).", layers.len());
                    }
                }
                Ok(IpcResponse::Error(message)) | Err(message) => {
                    self.apply_pending = false;
                    self.status_message = message;
                }
                Ok(_) => {
                    self.status_message = "AutoCAD returned an unexpected IPC response.".to_string()
                }
            }
        }

        let previous_threshold = self.mapping_editor.confidence;
        let editor_event = self.mapping_editor.show(
            ui,
            &ui.ctx().clone(),
            &self.drawing_name,
            &self.template_name,
            &self.source_layers,
            &self.standard_layers,
            &self.matches,
            &self.empty_layers,
            &self.target_filters,
            &self.always_hidden_targets,
            &self.status_message,
            self.apply_pending,
        );
        if let Some(new_threshold) = editor_event.confidence {
            self.mapping_editor.confidence = new_threshold;
        }
        if (self.mapping_editor.confidence - previous_threshold).abs() > f64::EPSILON {
            self.min_confidence = self.mapping_editor.confidence;
            self.recalculate();
        }

        if let Some(remember) = editor_event.remember {
            let request = (
                self.drawing_name.clone(),
                self.mapping_editor.current_mappings(&self.matches),
                remember,
                self.mapping_editor.property_settings(),
            );
            let sender = self.ipc_sender.clone();
            self.apply_pending = true;
            self.status_message = "Applying mappings in AutoCAD…".to_string();
            std::thread::spawn(move || {
                let result = acad_layer_ipc::apply_plan(request.0, request.1, request.2, request.3);
                let _ = sender.send(result);
            });
        }

        if editor_event.purge {
            let request = (
                self.drawing_name.clone(),
                self.empty_layers.iter().cloned().collect::<Vec<_>>(),
            );
            let sender = self.ipc_sender.clone();
            self.apply_pending = true;
            self.status_message = "Removing empty layers in AutoCAD…".to_string();
            std::thread::spawn(move || {
                let result = acad_layer_ipc::purge_empty_layers(request.0, request.1);
                let _ = sender.send(result);
            });
        }

        if editor_event.choose_standard && !self.apply_pending {
            let selected = frame.winit_window().and_then(|window| {
                rfd::FileDialog::new()
                    .set_title("Choose Standard Drawing")
                    .add_filter("AutoCAD drawings", &["dwg", "dxf"])
                    .set_parent(window)
                    .pick_file()
            });
            if let Some(path) = selected {
                let sender = self.ipc_sender.clone();
                self.apply_pending = true;
                self.status_message = "Loading standard drawing in AutoCAD…".to_string();
                std::thread::spawn(move || {
                    let result = acad_layer_ipc::load_standard(path.to_string_lossy().into_owned());
                    let _ = sender.send(result);
                });
            }
        }

        self.capture_user_preferences(ui.ctx());
    }

    fn persist_egui_memory(&self) -> bool {
        false
    }

    fn save(&mut self, _storage: &mut dyn eframe::Storage) {
        let Some(path) = preferences_path() else {
            return;
        };
        let Some(directory) = path.parent() else {
            return;
        };
        if fs::create_dir_all(directory).is_ok() {
            if let Ok(json) = serde_json::to_vec_pretty(&self.user_preferences) {
                let _ = fs::write(path, json);
            }
        }
    }
}

impl LayerStandardizerApp {
    fn capture_user_preferences(&mut self, ctx: &egui::Context) {
        let viewport = ctx.input(|input| input.viewport().clone());
        let maximized = viewport
            .maximized
            .unwrap_or(self.user_preferences.mapping_editor_maximized);
        if !maximized {
            if let Some(size) = viewport.inner_rect.map(|rect| rect.size()) {
                if size.x > 0.0 && size.y > 0.0 {
                    self.user_preferences.mapping_editor_width = size.x;
                    self.user_preferences.mapping_editor_height = size.y;
                }
            }
        }
        self.user_preferences.mapping_editor_maximized = maximized;

        let (zoom, pan, animations) = self.mapping_editor.view_preferences();
        self.user_preferences.mapping_editor_zoom = zoom;
        self.user_preferences.mapping_editor_viewport_x = pan.x;
        self.user_preferences.mapping_editor_viewport_y = pan.y;
        self.user_preferences.animations_enabled = animations;
    }

    fn set_snapshot(&mut self, snapshot: DrawingSnapshot) {
        if self.drawing_name.is_empty() {
            self.min_confidence = snapshot.heuristic_threshold.clamp(0.6, 1.0);
            self.mapping_editor.confidence = self.min_confidence;
        }
        self.drawing_name = snapshot.drawing_name;
        self.template_name = snapshot.template_name;
        self.empty_layers = snapshot.empty_layers.into_iter().collect();
        self.memory_mappings = snapshot.memory_mappings;
        self.target_filters = snapshot.target_filters;
        self.always_hidden_targets = snapshot.always_hidden_targets.into_iter().collect();
        self.mapping_editor
            .retain_valid_targets(&snapshot.standard_layers);
        self.set_layers(snapshot.source_layers, snapshot.standard_layers);
    }
}

impl LayerStandardizerApp {
    fn attach_to_owner_if_requested(&mut self, frame: &eframe::Frame, ctx: &egui::Context) {
        if self.owner_attached || self.owner_hwnd.is_none() {
            return;
        }

        let result = attach_native_window_to_owner(frame, self.owner_hwnd.unwrap());
        self.owner_attached = true;
        if let Err(message) = result {
            self.status_message = format!("Could not attach window to AutoCAD: {message}");
        }

        // The root window starts hidden when an owner was requested, so it
        // cannot flash as an unrelated top-level window before attachment.
        ctx.send_viewport_cmd(egui::ViewportCommand::Visible(true));
    }
}

#[cfg(target_os = "windows")]
fn attach_native_window_to_owner(frame: &eframe::Frame, owner_hwnd: isize) -> Result<(), String> {
    use windows_sys::Win32::Foundation::{GetLastError, SetLastError, HWND};
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        IsWindow, SetWindowLongPtrW, GWLP_HWNDPARENT,
    };

    if owner_hwnd == 0 {
        return Err("the owner HWND was zero".to_string());
    }

    if unsafe { IsWindow(owner_hwnd as HWND) } == 0 {
        return Err("the supplied AutoCAD HWND is not a valid window".to_string());
    }

    let window = frame
        .winit_window()
        .ok_or_else(|| "the native window handle is unavailable".to_string())?;
    let handle = window.window_handle().map_err(|error| error.to_string())?;
    let RawWindowHandle::Win32(handle) = handle.as_raw() else {
        return Err("the UI window does not have a Win32 handle".to_string());
    };

    // Win32 returns the previous owner value, which may be zero on success;
    // clear/read last-error to distinguish that from an API failure.
    unsafe {
        SetLastError(0);
        let previous = SetWindowLongPtrW(handle.hwnd.get() as HWND, GWLP_HWNDPARENT, owner_hwnd);
        let error = GetLastError();
        if previous == 0 && error != 0 {
            return Err(format!("SetWindowLongPtrW failed with Win32 error {error}"));
        }
    }

    Ok(())
}

#[cfg(not(target_os = "windows"))]
fn attach_native_window_to_owner(_frame: &eframe::Frame, _owner_hwnd: isize) -> Result<(), String> {
    Err("AutoCAD window ownership is supported only on Windows".to_string())
}

fn main() -> eframe::Result<()> {
    let owner_hwnd = std::env::args().skip(1).find_map(|arg| {
        arg.strip_prefix("--owner-hwnd=").map(|value| {
            value
                .parse::<isize>()
                .expect("--owner-hwnd must be a decimal HWND")
        })
    });

    let user_preferences = load_preferences();
    let storage_path = preferences_path().map(|path| path.with_file_name("rust_ui_state.ron"));
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([
                user_preferences.mapping_editor_width,
                user_preferences.mapping_editor_height,
            ])
            .with_maximized(user_preferences.mapping_editor_maximized)
            .with_title("AutoCAD Layer Standardizer - Spatial Edition")
            .with_visible(owner_hwnd.is_none()),
        persistence_path: storage_path,
        persist_window: false,
        ..Default::default()
    };

    let (ipc_tx, ipc_results) = std::sync::mpsc::channel();
    if owner_hwnd.is_some() {
        let snapshot_sender = ipc_tx.clone();
        std::thread::spawn(move || {
            let _ = snapshot_sender.send(acad_layer_ipc::request_drawing_snapshot());
        });
    }

    eframe::run_native(
        "AutoCAD Layer Standardizer",
        options,
        Box::new(move |_cc| {
            Ok(Box::new(LayerStandardizerApp::new(
                owner_hwnd,
                ipc_tx,
                ipc_results,
                user_preferences,
            )))
        }),
    )
}
