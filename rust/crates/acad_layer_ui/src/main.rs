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
    window_open: bool,
}

impl LayerStandardizerApp {
    fn new() -> Self {
        let standard_layers = vec![
            "A-WALL-FULL".to_string(),
            "A-DOOR-SWNG".to_string(),
            "A-FL-EXT-WALL".to_string(),
            "A-ANNO-DIM".to_string(),
            "A-ANNO-TEXT".to_string(),
            "S-FL-BEAM".to_string(),
            "S-FL-COL".to_string(),
            "E-LGT-FIXT".to_string(),
            "M-HVAC-DUCT".to_string(),
            "P-SAN-PIPE".to_string(),
        ];

        let source_layers = vec![
            "WALL".to_string(),
            "A-DOOR".to_string(),
            "EXT-WALL".to_string(),
            "DIMENSIONS".to_string(),
            "TEXT".to_string(),
            "BEAM".to_string(),
            "COLUMNS".to_string(),
            "LIGHTING".to_string(),
            "DUCTWORK".to_string(),
            "PLUMBING-SAN".to_string(),
            "RANDOM_JUNK".to_string(),
        ];

        let matcher = HeuristicMatcher::new(standard_layers.clone(), 0.5);
        let matches: Vec<MatchResult> = source_layers
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

        Self {
            dark_theme: ThemePalette::dark(),
            search_query: String::new(),
            min_confidence: 0.6,
            source_layers,
            standard_layers,
            matches,
            window_open: true,
        }
    }
}

impl eframe::App for LayerStandardizerApp {
    fn ui(&mut self, ui: &mut egui::Ui, _frame: &mut eframe::Frame) {
        ui.heading("Spatial Layer Standardizer");
        ui.label("Powered by Rust, Spatial UI Kit & AutoCAD Dual Interop");

            ui.add_space(10.0);

            // Controls row
            ui.horizontal(|ui| {
                ui.label("Search:");
                ui.text_edit_singleline(&mut self.search_query);

                ui.add_space(20.0);
                ui.label("Min Confidence:");
                ui.add(egui::Slider::new(&mut self.min_confidence, 0.0..=1.0));
            });

            ui.separator();

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
