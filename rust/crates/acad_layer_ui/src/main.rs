#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

use acad_layer_core::{HeuristicMatcher, MatchResult, MatchSource};
use acad_layer_ipc::{DrawingSnapshot, IpcResponse, TargetFilter};
use raw_window_handle::{HasWindowHandle, RawWindowHandle};
use spatial_ui_kit::theme::ThemePalette;
use std::{
    collections::{HashMap, HashSet},
    sync::mpsc::Receiver,
};

mod mapping_editor;

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
    ipc_results: Receiver<Result<IpcResponse, String>>,
    drawing_name: String,
    empty_layers: HashSet<String>,
    memory_mappings: HashMap<String, String>,
    target_filters: Vec<TargetFilter>,
    mapping_editor: mapping_editor::MappingEditor,
}

impl LayerStandardizerApp {
    fn new(owner_hwnd: Option<isize>, ipc_results: Receiver<Result<IpcResponse, String>>) -> Self {
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
            ipc_results,
            drawing_name: String::new(),
            empty_layers: HashSet::new(),
            memory_mappings: HashMap::new(),
            target_filters: Vec::new(),
            mapping_editor: mapping_editor::MappingEditor::default(),
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
        self.matches = self
            .source_layers
            .iter()
            .map(|name| {
                if let Some(target) = self
                    .standard_layers
                    .iter()
                    .find(|target| target.eq_ignore_ascii_case(name))
                {
                    return MatchResult {
                        source_layer: name.clone(),
                        target_layer: Some(target.clone()),
                        confidence: 1.0,
                        source: MatchSource::Exact,
                    };
                }
                if let Some((_, target)) = self
                    .memory_mappings
                    .iter()
                    .find(|(source, _)| source.eq_ignore_ascii_case(name))
                {
                    if self
                        .standard_layers
                        .iter()
                        .any(|standard| standard.eq_ignore_ascii_case(target))
                    {
                        return MatchResult {
                            source_layer: name.clone(),
                            target_layer: Some(target.clone()),
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
                Ok(IpcResponse::Error(message)) | Err(message) => {
                    self.status_message = message;
                }
                Ok(_) => {
                    self.status_message = "AutoCAD returned an unexpected IPC response.".to_string()
                }
            }
        }

        let previous_threshold = self.mapping_editor.confidence;
        if let Some(new_threshold) = self.mapping_editor.show(
            ui,
            &ui.ctx().clone(),
            &self.drawing_name,
            &self.source_layers,
            &self.standard_layers,
            &self.matches,
            &self.empty_layers,
            &self.target_filters,
            &self.status_message,
        ) {
            self.mapping_editor.confidence = new_threshold;
        }
        if (self.mapping_editor.confidence - previous_threshold).abs() > f64::EPSILON {
            self.min_confidence = self.mapping_editor.confidence;
            self.recalculate();
        }
    }
}

impl LayerStandardizerApp {
    fn set_snapshot(&mut self, snapshot: DrawingSnapshot) {
        self.drawing_name = snapshot.drawing_name;
        self.empty_layers = snapshot.empty_layers.into_iter().collect();
        self.memory_mappings = snapshot.memory_mappings;
        self.target_filters = snapshot.target_filters;
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

    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1200.0, 650.0])
            .with_title("AutoCAD Layer Standardizer - Spatial Edition")
            .with_visible(owner_hwnd.is_none()),
        ..Default::default()
    };

    let (ipc_tx, ipc_results) = std::sync::mpsc::channel();
    if owner_hwnd.is_some() {
        std::thread::spawn(move || {
            let _ = ipc_tx.send(acad_layer_ipc::request_drawing_snapshot());
        });
    }

    eframe::run_native(
        "AutoCAD Layer Standardizer",
        options,
        Box::new(move |_cc| Ok(Box::new(LayerStandardizerApp::new(owner_hwnd, ipc_results)))),
    )
}
