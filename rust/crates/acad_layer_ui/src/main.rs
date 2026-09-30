use acad_layer_core::{HeuristicMatcher, MatchResult, MatchSource};
use spatial_ui_kit::theme::ThemePalette;

#[allow(dead_code)]
struct LayerStandardizerApp {
    dark_theme: ThemePalette,
    search_query: String,
    min_confidence: f64,
    source_layers: Vec<String>,
    standard_layers: Vec<String>,
    matches: Vec<MatchResult>,
    status_message: String,
}

impl LayerStandardizerApp {
    fn new() -> Self {
        Self {
            dark_theme: ThemePalette::dark(),
            search_query: String::new(),
            min_confidence: 0.6,
            source_layers: Vec::new(),
            standard_layers: Vec::new(),
            matches: Vec::new(),
            status_message: "Ready. Connect AutoCAD or paste/import layers to standardize.".to_string(),
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
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        ui.heading("Spatial Layer Standardizer");
        ui.label("Powered by Rust, Spatial UI Kit & AutoCAD Interop");

        ui.add_space(8.0);
        ui.colored_label(egui::Color32::from_rgb(100, 180, 255), &self.status_message);

        ui.add_space(8.0);

        // Controls row
        ui.horizontal(|ui| {
            ui.label("Search:");
            ui.text_edit_singleline(&mut self.search_query);

            ui.add_space(20.0);
            ui.label("Min Confidence:");
            let prev_conf = self.min_confidence;
            ui.add(egui::Slider::new(&mut self.min_confidence, 0.0..=1.0));
            if (self.min_confidence - prev_conf).abs() > 0.01 {
                self.recalculate();
            }

            ui.add_space(20.0);
            if ui.button("Load Pasted Layers").clicked() && !self.search_query.is_empty() {
                let lines: Vec<String> = self.search_query.lines().map(|s| s.trim().to_string()).filter(|s| !s.is_empty()).collect();
                if !lines.is_empty() {
                    self.source_layers = lines;
                    self.status_message = format!("Loaded {} source layers.", self.source_layers.len());
                    self.recalculate();
                }
            }
        });

        ui.separator();

        if self.source_layers.is_empty() {
            ui.vertical_centered(|ui| {
                ui.add_space(40.0);
                ui.label("No active layers loaded.");
                ui.label("AutoCAD drawing layers will appear here automatically via IPC, or paste layer names above.");
            });
            return;
        }

        // Table of layer mappings
        egui::ScrollArea::vertical().show(ui, |ui| {
            egui::Grid::new("layer_grid")
                .striped(true)
                .min_col_width(150.0)
                .show(ui, |ui| {
                    ui.strong("Source Layer");
                    ui.strong("Target Standard");
                    ui.strong("Confidence");
                    ui.strong("Status");
                    ui.end_row();

                    for m in &self.matches {
                        if !self.search_query.is_empty()
                            && !m.source_layer.to_ascii_uppercase().contains(&self.search_query.to_ascii_uppercase())
                        {
                            continue;
                        }

                        ui.label(&m.source_layer);

                        let target_text = m.target_layer.as_deref().unwrap_or("<None>");
                        ui.label(target_text);

                        let pct = (m.confidence * 100.0) as u32;
                        let color = if m.confidence >= 0.85 {
                            egui::Color32::from_rgb(80, 200, 120)
                        } else if m.confidence >= 0.6 {
                            egui::Color32::from_rgb(230, 180, 50)
                        } else {
                            egui::Color32::from_rgb(220, 80, 80)
                        };

                        ui.colored_label(color, format!("{}%", pct));

                        match m.source {
                            MatchSource::Memory => ui.label("Learned Memory"),
                            MatchSource::Heuristic => ui.label("Heuristic Match"),
                            MatchSource::Unmatched => ui.label("Unmatched"),
                        };

                        ui.end_row();
                    }
                });
        });
    }
}

fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([720.0, 540.0])
            .with_title("AutoCAD Layer Standardizer - Spatial Edition"),
        ..Default::default()
    };

    eframe::run_native(
        "AutoCAD Layer Standardizer",
        options,
        Box::new(|_cc| Ok(Box::new(LayerStandardizerApp::new()))),
    )
}
