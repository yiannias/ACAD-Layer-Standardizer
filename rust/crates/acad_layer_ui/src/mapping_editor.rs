use acad_layer_core::{MatchResult, MatchSource};
use acad_layer_ipc::{LayerMapping, PropertyMatchSettings, TargetFilter};
use egui::{
    Align2, Color32, CursorIcon, FontId, Id, Painter, Pos2, Rect, RichText, Sense, Stroke, Vec2,
};
use std::collections::{HashMap, HashSet};
use std::time::{Duration, Instant};

const CANVAS: Color32 = Color32::from_rgb(14, 14, 14);
const NODE: Color32 = Color32::from_rgb(38, 40, 44);
const BLUE: Color32 = Color32::from_rgb(70, 130, 180);
const GREEN: Color32 = Color32::from_rgb(56, 184, 79);
const MEMORY_BLUE: Color32 = Color32::from_rgb(28, 164, 229);
const YELLOW: Color32 = Color32::from_rgb(235, 176, 20);
const PURPLE: Color32 = Color32::from_rgb(164, 78, 232);
const RED: Color32 = Color32::from_rgb(198, 40, 40);
const GREY: Color32 = Color32::from_rgb(136, 136, 136);
const PAIR_HIGHLIGHT: Color32 = Color32::from_rgb(217, 89, 38);
const ROW_STEP: f32 = 44.0;
const NODE_WIDTH: f32 = 280.0;
const NODE_HEIGHT: f32 = 30.0;
const COLUMN_ROW_STEP: f32 = 38.0;
const COLUMN_NODE_HEIGHT: f32 = 30.0;
const SOURCE_X: f32 = 250.0;
const TARGET_X: f32 = 620.0;
const TARGET_STEP: f32 = 320.0;
const SOURCE_STEP: f32 = 320.0;
/// Node mode never stacks more than this many nodes in one column, on either side.
const MAX_NODE_COLUMN_HEIGHT: usize = 25;
/// Lines that pass under other columns of nodes are drawn this much of their color.
const PASS_UNDER_DARKEN: f32 = 0.7;
/// The node-mode filter boxes are a fixed size in drawing units, so they scale with zoom.
const FILTER_FIELD_WIDTH: f32 = 260.0;
const FILTER_FIELD_HEIGHT: f32 = 28.0;
const MIN_ZOOM: f32 = 0.03;
const MIN_LABEL_PX: f32 = 1.0;

#[derive(Debug, Default)]
pub struct MappingEditorEvent {
    pub confidence: Option<f64>,
    pub remember: Option<bool>,
    pub purge: bool,
    pub choose_standard: bool,
}

#[derive(Clone, Copy)]
struct PositionTween {
    from: Rect,
    to: Rect,
    started: Instant,
}

#[derive(Default)]
struct TargetPairSummary {
    source_names: Vec<String>,
    match_counts: [usize; 4],
}

pub struct MappingEditor {
    pub confidence: f64,
    pub overrides: HashMap<String, Option<String>>,
    undo_stack: Vec<HashMap<String, Option<String>>>,
    redo_stack: Vec<HashMap<String, Option<String>>>,
    source_query: String,
    target_query: String,
    show_exact: bool,
    show_memory: bool,
    show_heuristic: bool,
    show_manual: bool,
    show_unmatched: bool,
    column_mode: bool,
    /// Node columns per side chosen with the footer slider; `None` is Auto.
    node_columns: Option<usize>,
    highlight_empty: bool,
    match_color: bool,
    match_linetype: bool,
    match_lineweight: bool,
    make_by_layer: bool,
    animations: bool,
    category_visibility: HashMap<String, bool>,
    zoom: f32,
    pan: Vec2,
    dragging_source: Option<String>,
    selected_sources: HashSet<String>,
    selected_target: Option<String>,
    source_scroll: f32,
    target_scroll: f32,
    target_filter_width: f32,
    target_filter_height: Option<f32>,
    drawn_rects: HashMap<(bool, usize), Rect>,
    position_tweens: HashMap<(bool, usize), PositionTween>,
    last_view_transform: Option<(f32, Vec2)>,
    pointer_is_panning: bool,
    fit_requested: bool,
    purge_confirmation_open: bool,
}

impl Default for MappingEditor {
    fn default() -> Self {
        Self {
            confidence: 0.6,
            overrides: HashMap::new(),
            undo_stack: Vec::new(),
            redo_stack: Vec::new(),
            source_query: String::new(),
            target_query: String::new(),
            show_exact: true,
            show_memory: true,
            show_heuristic: true,
            show_manual: true,
            show_unmatched: true,
            column_mode: false,
            node_columns: None,
            highlight_empty: false,
            match_color: true,
            match_linetype: true,
            match_lineweight: true,
            make_by_layer: false,
            animations: true,
            category_visibility: HashMap::new(),
            zoom: 0.68,
            pan: Vec2::new(180.0, 40.0),
            dragging_source: None,
            selected_sources: HashSet::new(),
            selected_target: None,
            source_scroll: 0.0,
            target_scroll: 0.0,
            target_filter_width: 260.0,
            target_filter_height: None,
            drawn_rects: HashMap::new(),
            position_tweens: HashMap::new(),
            last_view_transform: None,
            pointer_is_panning: false,
            fit_requested: true,
            purge_confirmation_open: false,
        }
    }
}

impl MappingEditor {
    pub fn restore_view_preferences(
        &mut self,
        zoom: f32,
        pan_x: f32,
        pan_y: f32,
        animations: bool,
    ) {
        self.zoom = zoom.clamp(0.08, 1.6);
        self.pan = Vec2::new(pan_x, pan_y);
        self.animations = animations;
        self.last_view_transform = None;
        self.drawn_rects.clear();
        self.position_tweens.clear();
    }

    pub fn view_preferences(&self) -> (f32, Vec2, bool) {
        (self.zoom, self.pan, self.animations)
    }

    pub fn show(
        &mut self,
        ui: &mut egui::Ui,
        ctx: &egui::Context,
        drawing_name: &str,
        template_name: &str,
        source_layers: &[String],
        standard_layers: &[String],
        matches: &[MatchResult],
        empty_layers: &HashSet<String>,
        target_filters: &[TargetFilter],
        always_hidden_targets: &HashSet<String>,
        status: &str,
        apply_pending: bool,
    ) -> MappingEditorEvent {
        let previous_confidence = self.confidence;
        let mappings = self.current_mappings(matches);
        let can_apply = !apply_pending && !mappings.is_empty();
        let mut remember = None;
        let mut purge = false;
        let footer_width = (ctx.content_rect().width() - 48.0).max(320.0);
        let footer_area = egui::Area::new(Id::new("mapping_status_bar"))
            .anchor(Align2::CENTER_BOTTOM, egui::vec2(0.0, -18.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                ui.set_width(footer_width);
                egui::Frame::new()
                    .fill(Color32::from_rgba_unmultiplied(31, 33, 37, 236))
                    .stroke(Stroke::new(1.0, Color32::from_rgb(95, 95, 95)))
                    .corner_radius(8)
                    .inner_margin(egui::Margin::symmetric(16, 9))
                    .show(ui, |ui| {
                        ui.set_width(footer_width - 32.0);
                        ui.horizontal_centered(|ui| {
                            ui.vertical(|ui| {
                                ui.label(
                                    RichText::new(if self.column_mode {
                                        "Drag a source layer onto a target layer to pair them. Click a target to highlight its source layers."
                                    } else {
                                        "Drag from a source → standard layer to map. Click a connection to remove it. Scroll to zoom. Middle-drag to pan."
                                    })
                                    .size(12.0)
                                    .color(Color32::from_rgb(170, 170, 170)),
                                );
                                ui.horizontal(|ui| {
                                ui.label(
                                    RichText::new(status)
                                        .size(12.0)
                                        .color(Color32::from_rgb(170, 170, 170)),
                                );
                                ui.label(
                                    RichText::new(format!(
                                        "v{} · Build {}",
                                        env!("CARGO_PKG_VERSION"),
                                        env!("ACAD_LAYER_UI_BUILD_ID")
                                    ))
                                    .size(10.0)
                                    .color(Color32::from_rgb(135, 135, 135)),
                                );
                                });
                            });
                            ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                                        if ui
                                            .add_enabled(
                                                can_apply,
                                                egui::Button::new("Apply & Remember")
                                                    .min_size(Vec2::new(132.0, 34.0))
                                                    .fill(BLUE)
                                                    .corner_radius(8)
                                                    .stroke(Stroke::new(1.0, Color32::from_rgb(104, 166, 218))),
                                            )
                                            .clicked()
                                        {
                                            remember = Some(true);
                                        }
                                        ui.add_space(6.0);
                                        if ui
                                            .add_enabled(
                                                can_apply,
                                                egui::Button::new("Apply")
                                                    .min_size(Vec2::new(76.0, 34.0))
                                                    .fill(Color32::from_rgb(48, 50, 54))
                                                    .corner_radius(8)
                                                    .stroke(Stroke::new(1.0, Color32::from_rgb(112, 112, 112))),
                                            )
                                            .clicked()
                                        {
                                            remember = Some(false);
                                        }
                                        ui.separator();
                                        self.column_mode = draw_mode_switch(ui, self.column_mode);
                                        if !self.column_mode {
                                            ui.add_space(8.0);
                                            let stop = draw_columns_slider(
                                                ui,
                                                stop_for_node_columns(self.node_columns),
                                            );
                                            self.node_columns = node_columns_for_stop(stop);
                                        }
                                    });
                        });
                    });
            });
        let footer_rect = footer_area.response.rect;

        let canvas_rect = ui.available_rect_before_wrap();
        self.handle_mapping_history(ctx);

        let visible_sources = self.visible_sources(source_layers, matches);
        let mut visible_targets =
            self.visible_targets(standard_layers, target_filters, always_hidden_targets);
        let visible_target_names = visible_targets
            .iter()
            .filter_map(|index| standard_layers.get(*index))
            .map(|name| name.to_ascii_lowercase())
            .collect::<HashSet<_>>();
        if self
            .selected_target
            .as_ref()
            .is_some_and(|target| !visible_target_names.contains(&target.to_ascii_lowercase()))
        {
            self.selected_target = None;
        }
        let mut connected_targets = HashSet::new();
        for index in &visible_sources {
            let Some(source) = source_layers.get(*index) else {
                continue;
            };
            let target = match self.overrides.get(source) {
                Some(Some(target)) => Some(target.as_str()),
                Some(None) => None,
                None => matches
                    .get(*index)
                    .and_then(|matched| matched.target_layer.as_deref()),
            };
            if let Some(target) = target {
                let target = target.to_ascii_lowercase();
                if visible_target_names.contains(&target) {
                    connected_targets.insert(target);
                }
            }
        }
        visible_targets.sort_by_key(|index| {
            standard_layers
                .get(*index)
                .map(|name| {
                    let lower = name.to_ascii_lowercase();
                    (!connected_targets.contains(&lower), *index)
                })
                .unwrap_or((true, usize::MAX))
        });

        let target_pairs = self.target_pair_summaries(source_layers, matches);
        if self.column_mode {
            let _canvas_response = ui.allocate_rect(canvas_rect, Sense::hover());
            let max_list_bottom = (footer_rect.top() - 12.0).min(canvas_rect.bottom());
            let visible_row_slots = ((max_list_bottom - canvas_rect.top() - 130.0)
                / COLUMN_ROW_STEP)
                .floor()
                .max(1.0);
            let list_bottom = (canvas_rect.top() + 130.0 + visible_row_slots * COLUMN_ROW_STEP)
                .min(canvas_rect.bottom());
            let filter_left =
                ctx.content_rect().right() - 20.0 - self.target_filter_width.max(210.0);
            let filter_clear_right = (filter_left - 18.0).min(canvas_rect.right());
            let initial_side_space = (canvas_rect.width() * 0.15).clamp(168.0, 220.0);
            let natural_target_right = canvas_rect.right() - initial_side_space;
            let mut list_right = canvas_rect.right();
            if natural_target_right > filter_clear_right {
                list_right = (filter_clear_right + initial_side_space).min(canvas_rect.right());
                for _ in 0..4 {
                    let side_space = ((list_right - canvas_rect.left()) * 0.15).clamp(168.0, 220.0);
                    list_right = (filter_clear_right + side_space).min(canvas_rect.right());
                }
            }
            let list_area = Rect::from_min_max(canvas_rect.min, Pos2::new(list_right, list_bottom));
            ui.painter_at(canvas_rect)
                .rect_filled(canvas_rect, 0.0, CANVAS);
            let target_name_clicked = self.draw_column_lists(
                ui,
                ctx,
                list_area,
                drawing_name,
                template_name,
                source_layers,
                standard_layers,
                matches,
                empty_layers,
                &visible_sources,
                &visible_targets,
                &target_pairs,
            );
            self.draw_left_panel(ctx, source_layers.len(), empty_layers.len());
            let choose_standard =
                self.draw_right_panel(ctx, target_filters, template_name) || target_name_clicked;
            if self.purge_confirmation_open {
                egui::Window::new("Purge Empty Layers")
                    .collapsible(false)
                    .resizable(false)
                    .anchor(Align2::CENTER_CENTER, Vec2::ZERO)
                    .show(ctx, |ui| {
                        ui.label(format!(
                            "Remove the {} empty layer(s) from this drawing?",
                            empty_layers.len()
                        ));
                        ui.horizontal(|ui| {
                            if ui.button("Cancel").clicked() {
                                self.purge_confirmation_open = false;
                            }
                            if ui
                                .add_enabled(
                                    !apply_pending && !empty_layers.is_empty(),
                                    egui::Button::new("Purge").fill(RED),
                                )
                                .clicked()
                            {
                                self.purge_confirmation_open = false;
                                purge = true;
                            }
                        });
                    });
            }
            return MappingEditorEvent {
                confidence: ((self.confidence - previous_confidence).abs() > f64::EPSILON)
                    .then_some(self.confidence),
                remember,
                purge,
                choose_standard,
            };
        }

        let canvas_response = ui.allocate_rect(canvas_rect, Sense::click_and_drag());
        let painter = ui.painter_at(canvas_rect);
        painter.rect_filled(canvas_rect, 0.0, CANVAS);
        self.handle_canvas_navigation(ctx, &canvas_response, canvas_rect);
        self.draw_grid(&painter, canvas_rect);

        let wants_fit_key = ctx
            .input(|i| i.key_pressed(egui::Key::F) || i.key_pressed(egui::Key::Home))
            && !ctx.egui_wants_keyboard_input();
        if wants_fit_key {
            self.fit_requested = true;
        }
        if self.fit_requested && (!visible_sources.is_empty() || !visible_targets.is_empty()) {
            self.fit_to_content(
                canvas_rect,
                visible_sources.len(),
                visible_targets.len(),
                !target_filters.is_empty(),
            );
            self.fit_requested = false;
        }
        let transform = (self.zoom, self.pan);
        let allow_layout_animation = self.last_view_transform == Some(transform);
        self.last_view_transform = Some(transform);
        let mut source_positions = self.source_positions(&visible_sources, canvas_rect);
        let mut target_positions = self.target_positions(&visible_targets, canvas_rect);
        self.animate_positions(&mut source_positions, true, allow_layout_animation, ctx);
        self.animate_positions(&mut target_positions, false, allow_layout_animation, ctx);
        let target_rects_by_name = target_positions
            .iter()
            .filter_map(|(index, rect)| {
                standard_layers
                    .get(*index)
                    .map(|name| (name.to_ascii_lowercase(), *rect))
            })
            .collect::<HashMap<_, _>>();

        let target_name_clicked = self.draw_group_headers(
            ui,
            &painter,
            canvas_rect,
            drawing_name,
            if template_name.is_empty() {
                "No standard selected"
            } else {
                template_name
            },
            &visible_sources,
            &visible_targets,
        );
        self.draw_connections(
            &painter,
            matches,
            source_layers,
            &source_positions,
            &target_rects_by_name,
        );
        self.handle_connection_click(
            &canvas_response,
            matches,
            source_layers,
            &source_positions,
            &target_positions,
            &target_rects_by_name,
        );

        let mut drop_target_rects = Vec::with_capacity(target_positions.len());
        for (index, rect) in &target_positions {
            if !rect.intersects(canvas_rect) {
                continue;
            }
            let name = &standard_layers[*index];
            drop_target_rects.push((name.as_str(), *rect));
            let target_selected = self.selected_target.as_deref() == Some(name.as_str());
            self.draw_layer_node(
                ui,
                &painter,
                rect,
                name,
                false,
                None,
                target_pairs
                    .get(&name.to_ascii_lowercase())
                    .map(|summary| Self::summary_color(summary))
                    .unwrap_or(GREY),
                target_pairs
                    .get(&name.to_ascii_lowercase())
                    .is_some_and(|summary| !summary.source_names.is_empty()),
                false,
                false,
                target_selected,
            );
            let response = ui.interact(*rect, Id::new(("target-layer", name)), Sense::click());
            if response.clicked() {
                self.selected_target = if target_selected {
                    None
                } else {
                    Some(name.clone())
                };
                self.selected_sources.clear();
            }
            if let Some(summary) = target_pairs.get(&name.to_ascii_lowercase()) {
                if !summary.source_names.is_empty() {
                    response
                        .clone()
                        .on_hover_text(summary.source_names.join("\n"));
                }
            }
            response.context_menu(|menu| {
                if !target_filters.is_empty() && menu.button("All On/Off").clicked() {
                    let all_on = target_filters.iter().all(|filter| {
                        *self
                            .category_visibility
                            .entry(filter.name.clone())
                            .or_insert(true)
                    });
                    for filter in target_filters {
                        self.category_visibility
                            .insert(filter.name.clone(), !all_on);
                    }
                    menu.close();
                }
                if !target_filters.is_empty() {
                    menu.separator();
                }
                for filter in target_filters {
                    let checked = self
                        .category_visibility
                        .entry(filter.name.clone())
                        .or_insert(true);
                    menu.checkbox(checked, &filter.name);
                }
            });
        }

        for (index, rect) in &source_positions {
            if !rect.intersects(canvas_rect) {
                continue;
            }
            let name = &source_layers[*index];
            let result = matches.get(*index);
            let empty = empty_layers.contains(name);
            let paired_to_selected_target = self
                .selected_target
                .as_ref()
                .and_then(|selected| {
                    self.effective_target(name, *index, matches)
                        .map(|target| target.eq_ignore_ascii_case(selected))
                })
                .unwrap_or(false);
            let color = if self.overrides.get(name).is_some_and(Option::is_some) {
                PURPLE
            } else {
                self.source_color(name, result)
            };
            self.draw_layer_node(
                ui,
                &painter,
                rect,
                name,
                true,
                result,
                color,
                self.effective_target(name, *index, matches).is_some(),
                empty && self.highlight_empty,
                self.selected_sources.contains(name),
                paired_to_selected_target,
            );

            let response = ui.interact(
                *rect,
                Id::new(("source-layer", name)),
                Sense::click_and_drag(),
            );
            let context_sources =
                if self.selected_sources.contains(name) && self.selected_sources.len() > 1 {
                    self.selected_sources.iter().cloned().collect::<Vec<_>>()
                } else {
                    vec![name.clone()]
                };
            let can_unmatch = context_sources.iter().any(|source| {
                self.overrides
                    .get(source)
                    .map(Option::is_some)
                    .unwrap_or_else(|| {
                        source_layers
                            .iter()
                            .position(|candidate| candidate == source)
                            .and_then(|index| matches.get(index))
                            .is_some_and(|result| result.target_layer.is_some())
                    })
            });
            response.context_menu(|menu| {
                if menu
                    .add_enabled(
                        can_unmatch,
                        egui::Button::new(if context_sources.len() > 1 {
                            format!("Un-match ({})", context_sources.len())
                        } else {
                            "Un-match".to_string()
                        }),
                    )
                    .clicked()
                {
                    self.push_undo_snapshot();
                    for source in &context_sources {
                        self.overrides.insert(source.clone(), None);
                    }
                    menu.close();
                }
                menu.separator();
                menu.checkbox(&mut self.show_exact, "Exact Match");
                menu.checkbox(&mut self.show_memory, "Memory Match");
                menu.checkbox(&mut self.show_heuristic, "Heuristic Match");
                menu.checkbox(&mut self.show_manual, "Manual Match");
                menu.checkbox(&mut self.show_unmatched, "Unmatched");
            });
            if response.clicked() {
                let extend = ctx.input(|input| input.modifiers.ctrl || input.modifiers.shift);
                if extend {
                    if !self.selected_sources.remove(name) {
                        self.selected_sources.insert(name.clone());
                    }
                } else if !self.selected_sources.contains(name) {
                    self.selected_sources.clear();
                    self.selected_sources.insert(name.clone());
                }
            }
            if response.drag_started() {
                if !self.selected_sources.contains(name) {
                    self.selected_sources.clear();
                    self.selected_sources.insert(name.clone());
                }
                self.dragging_source = Some(name.clone());
            }
            if self.dragging_source.as_deref() == Some(name.as_str()) {
                if let Some(pointer) = ctx.input(|input| input.pointer.hover_pos()) {
                    let start = Pos2::new(rect.right(), rect.center().y);
                    painter.line_segment([start, pointer], Stroke::new(2.0, PURPLE));
                }
                if response.drag_stopped() {
                    if let Some(pointer) = ctx.input(|input| input.pointer.interact_pos()) {
                        if let Some((target, _)) = drop_target_rects
                            .iter()
                            .find(|(_, target_rect)| target_rect.contains(pointer))
                        {
                            let mapped_sources = visible_sources
                                .iter()
                                .filter_map(|index| source_layers.get(*index))
                                .filter(|source| self.selected_sources.contains(*source))
                                .cloned()
                                .collect::<Vec<_>>();
                            self.push_undo_snapshot();
                            for source in mapped_sources {
                                self.overrides.insert(source, Some((*target).to_string()));
                            }
                            self.selected_sources.clear();
                        }
                    }
                    self.dragging_source = None;
                }
            }
        }

        self.draw_left_panel(ctx, source_layers.len(), empty_layers.len());
        let choose_standard =
            self.draw_right_panel(ctx, target_filters, template_name) || target_name_clicked;
        if self.purge_confirmation_open {
            egui::Window::new("Purge Empty Layers")
                .collapsible(false)
                .resizable(false)
                .anchor(Align2::CENTER_CENTER, Vec2::ZERO)
                .show(ctx, |ui| {
                    ui.label(format!(
                        "Remove the {} empty layer(s) from this drawing?",
                        empty_layers.len()
                    ));
                    ui.horizontal(|ui| {
                        if ui.button("Cancel").clicked() {
                            self.purge_confirmation_open = false;
                        }
                        if ui
                            .add_enabled(
                                !apply_pending && !empty_layers.is_empty(),
                                egui::Button::new("Purge").fill(RED),
                            )
                            .clicked()
                        {
                            self.purge_confirmation_open = false;
                            purge = true;
                        }
                    });
                });
        }
        self.draw_canvas_help(ctx, canvas_rect);
        MappingEditorEvent {
            confidence: ((self.confidence - previous_confidence).abs() > f64::EPSILON)
                .then_some(self.confidence),
            remember,
            purge,
            choose_standard,
        }
    }

    pub fn current_mappings(&self, matches: &[MatchResult]) -> Vec<LayerMapping> {
        matches
            .iter()
            .filter_map(|result| {
                let target = match self.overrides.get(&result.source_layer) {
                    Some(target) => target.as_ref(),
                    None => result.target_layer.as_ref(),
                }?;
                Some(LayerMapping {
                    source_layer: result.source_layer.clone(),
                    target_layer: target.clone(),
                })
            })
            .collect()
    }

    pub fn property_settings(&self) -> PropertyMatchSettings {
        PropertyMatchSettings {
            match_color: self.match_color,
            match_linetype: self.match_linetype,
            match_lineweight: self.match_lineweight,
            make_by_layer: self.make_by_layer,
        }
    }

    fn push_undo_snapshot(&mut self) {
        self.undo_stack.push(self.overrides.clone());
        if self.undo_stack.len() > 100 {
            self.undo_stack.remove(0);
        }
        self.redo_stack.clear();
    }

    fn handle_mapping_history(&mut self, ctx: &egui::Context) {
        if ctx.egui_wants_keyboard_input() {
            return;
        }

        let undo = ctx.input(|input| input.modifiers.ctrl && input.key_pressed(egui::Key::Z));
        let redo = ctx.input(|input| input.modifiers.ctrl && input.key_pressed(egui::Key::Y));
        if undo {
            if let Some(previous) = self.undo_stack.pop() {
                self.redo_stack
                    .push(std::mem::replace(&mut self.overrides, previous));
            }
        } else if redo {
            if let Some(next) = self.redo_stack.pop() {
                self.undo_stack
                    .push(std::mem::replace(&mut self.overrides, next));
            }
        }
    }

    fn handle_canvas_navigation(
        &mut self,
        ctx: &egui::Context,
        response: &egui::Response,
        rect: Rect,
    ) {
        if response.drag_started_by(egui::PointerButton::Middle) {
            self.pointer_is_panning = true;
        }
        if self.pointer_is_panning && response.dragged_by(egui::PointerButton::Middle) {
            self.pan += response.drag_delta();
        }
        if response.drag_stopped_by(egui::PointerButton::Middle) {
            self.pointer_is_panning = false;
        }
        let scroll = ctx.input(|input| input.smooth_scroll_delta.y);
        if scroll != 0.0
            && ctx.input(|input| {
                input
                    .pointer
                    .hover_pos()
                    .is_some_and(|pos| rect.contains(pos))
            })
        {
            let pointer = ctx
                .input(|input| input.pointer.hover_pos())
                .unwrap_or(rect.center());
            let before = (pointer - rect.min - self.pan) / self.zoom;
            self.zoom = (self.zoom * (scroll * 0.001).exp()).clamp(MIN_ZOOM, 1.6);
            self.pan = pointer - rect.min - before * self.zoom;
        }
    }

    /// World-space bounds of the source and target groups, matching `draw_group_headers`.
    fn content_bounds(sources: usize, targets: usize, columns: Option<usize>) -> Rect {
        let (source_columns, source_rows) = column_layout(sources, columns);
        let (target_columns, target_rows) = column_layout(targets, columns);
        let right = if targets > 0 {
            TARGET_X + (target_columns - 1) as f32 * TARGET_STEP + NODE_WIDTH + 24.0
        } else {
            SOURCE_X + NODE_WIDTH + 24.0
        };
        let rows = source_rows.max(target_rows);
        let bottom = 150.0 + (rows - 1) as f32 * ROW_STEP + NODE_HEIGHT + 24.0;
        Rect::from_min_max(
            Pos2::new(source_group_left(source_columns), 40.0),
            Pos2::new(right, bottom.max(140.0)),
        )
    }

    /// Centers and scales the view so all content is visible, clear of the floating panels.
    fn fit_to_content(&mut self, canvas: Rect, sources: usize, targets: usize, has_filters: bool) {
        let bounds = Self::content_bounds(sources, targets, self.node_columns);
        let mut usable = canvas.shrink(16.0);
        usable.min.x += 190.0;
        if has_filters {
            usable.max.x -= 210.0;
        }
        if usable.width() < 100.0 || usable.height() < 100.0 {
            usable = canvas;
        }
        let zoom = (usable.width() / bounds.width())
            .min(usable.height() / bounds.height())
            .clamp(MIN_ZOOM, 1.0);
        self.zoom = zoom;
        self.pan = usable.min - canvas.min + (usable.size() - bounds.size() * zoom) / 2.0
            - bounds.min.to_vec2() * zoom;
    }

    fn draw_grid(&self, painter: &Painter, rect: Rect) {
        let spacing = 60.0 * self.zoom;
        if spacing < 8.0 {
            return;
        }
        let grid = Stroke::new(1.0, Color32::from_rgba_unmultiplied(255, 255, 255, 13));
        let x_offset = self.pan.x.rem_euclid(spacing);
        let y_offset = self.pan.y.rem_euclid(spacing);
        let mut x = rect.left() + x_offset;
        while x < rect.right() {
            painter.line_segment(
                [Pos2::new(x, rect.top()), Pos2::new(x, rect.bottom())],
                grid,
            );
            x += spacing;
        }
        let mut y = rect.top() + y_offset;
        while y < rect.bottom() {
            painter.line_segment(
                [Pos2::new(rect.left(), y), Pos2::new(rect.right(), y)],
                grid,
            );
            y += spacing;
        }
    }

    fn source_positions(&self, visible: &[usize], canvas: Rect) -> HashMap<usize, Rect> {
        let (columns, rows) = column_layout(visible.len(), self.node_columns);
        visible
            .iter()
            .enumerate()
            .map(|(position, index)| {
                let (column, row) = (position / rows, position % rows);
                let x = SOURCE_X - (columns - 1 - column) as f32 * SOURCE_STEP;
                let rect = self.world_rect(
                    canvas,
                    x,
                    150.0 + row as f32 * ROW_STEP,
                    NODE_WIDTH,
                    NODE_HEIGHT,
                );
                (*index, rect)
            })
            .collect()
    }

    fn draw_column_lists(
        &mut self,
        ui: &mut egui::Ui,
        ctx: &egui::Context,
        area: Rect,
        drawing_name: &str,
        template_name: &str,
        source_layers: &[String],
        standard_layers: &[String],
        matches: &[MatchResult],
        empty_layers: &HashSet<String>,
        visible_sources: &[usize],
        visible_targets: &[usize],
        target_pairs: &HashMap<String, TargetPairSummary>,
    ) -> bool {
        let painter = ui.painter_at(area);
        painter.rect_filled(area, 0.0, CANVAS);

        let side_space = (area.width() * 0.15).clamp(168.0, 220.0);
        let gap = 96.0;
        let column_width = ((area.width() - side_space * 2.0 - gap) * 0.5).max(120.0);
        let source_rect = Rect::from_min_size(
            Pos2::new(area.left() + side_space, area.top() + 16.0),
            Vec2::new(column_width, area.height() - 32.0),
        );
        let target_rect = source_rect.translate(Vec2::new(column_width + gap, 0.0));
        let (source_rows_rect, _) = Self::draw_column_panel_header(
            ui,
            &painter,
            source_rect,
            "Source",
            drawing_name,
            false,
        );
        let (target_rows_rect, target_name_clicked) = Self::draw_column_panel_header(
            ui,
            &painter,
            target_rect,
            "Target",
            if template_name.is_empty() {
                "No standard selected"
            } else {
                template_name
            },
            true,
        );

        for (header_rect, query, hint) in [
            (source_rect, &mut self.source_query, "Filter source layers"),
            (target_rect, &mut self.target_query, "Filter target layers"),
        ] {
            let field = Rect::from_min_size(
                Pos2::new(header_rect.left() + 14.0, header_rect.top() + 58.0),
                Vec2::new((header_rect.width() - 28.0).min(300.0), 24.0),
            );
            filter_field(ui, field, query, hint);
        }

        self.update_column_scroll(ctx, source_rows_rect, visible_sources.len(), true);
        self.update_column_scroll(ctx, target_rows_rect, visible_targets.len(), false);

        let first_target = (self.target_scroll / COLUMN_ROW_STEP).floor() as usize;
        let last_target =
            ((self.target_scroll + target_rows_rect.height()) / COLUMN_ROW_STEP).ceil() as usize;
        let target_painter = painter.with_clip_rect(target_rows_rect);
        let mut drop_target_rects = Vec::new();
        let mut target_row_rects = HashMap::new();
        for position in first_target..last_target.min(visible_targets.len()) {
            let index = visible_targets[position];
            let Some(name) = standard_layers.get(index) else {
                continue;
            };
            let row = Rect::from_min_size(
                Pos2::new(
                    target_rows_rect.left() + 6.0,
                    target_rows_rect.top() + position as f32 * COLUMN_ROW_STEP - self.target_scroll,
                ),
                Vec2::new(target_rows_rect.width() - 12.0, COLUMN_NODE_HEIGHT),
            );
            if !row.intersects(target_rows_rect) {
                continue;
            }
            drop_target_rects.push((name.as_str(), row));
            let key = name.to_ascii_lowercase();
            target_row_rects.insert(key.clone(), row);
            let summary = target_pairs.get(&key);
            let paired_count = summary.map_or(0, |summary| summary.source_names.len());
            let selected = self
                .selected_target
                .as_deref()
                .is_some_and(|selected| selected.eq_ignore_ascii_case(name));
            let pointer_over = ctx
                .input(|input| input.pointer.hover_pos())
                .is_some_and(|pointer| row.contains(pointer));
            let drop_highlight = self.dragging_source.is_some() && pointer_over;
            let color = summary.map_or(GREY, Self::summary_color);
            let fill = NODE;
            target_painter.rect_filled(row, 5.0, fill);
            target_painter.rect_stroke(
                row,
                5.0,
                Stroke::new(
                    if selected || drop_highlight { 2.0 } else { 1.5 },
                    if drop_highlight {
                        Color32::from_rgb(131, 184, 228)
                    } else if selected {
                        highlight_color(color)
                    } else if paired_count > 0 {
                        color
                    } else {
                        GREY
                    },
                ),
                egui::StrokeKind::Inside,
            );
            let annotation = summary.and_then(|summary| {
                (!summary.source_names.is_empty()).then(|| {
                    if summary.source_names.len() == 1 {
                        summary.source_names[0].clone()
                    } else {
                        format!("{} layers", summary.source_names.len())
                    }
                })
            });
            Self::draw_column_row_text(&target_painter, row, name, annotation.as_deref());

            let response = ui.interact(
                row.intersect(target_rows_rect),
                Id::new(("column-target", name)),
                Sense::click(),
            );
            if response.clicked() {
                self.selected_target = if selected { None } else { Some(name.clone()) };
                self.selected_sources.clear();
            }
            if let Some(summary) = summary {
                if !summary.source_names.is_empty() {
                    response.on_hover_text(summary.source_names.join("\n"));
                }
            }
        }

        let first_source = (self.source_scroll / COLUMN_ROW_STEP).floor() as usize;
        let last_source =
            ((self.source_scroll + source_rows_rect.height()) / COLUMN_ROW_STEP).ceil() as usize;
        let source_painter = painter.with_clip_rect(source_rows_rect);
        let mut dropped_on = None;
        let mut visible_source_rows = HashMap::new();
        let mut column_connections = Vec::new();
        for position in first_source..last_source.min(visible_sources.len()) {
            let index = visible_sources[position];
            let Some(name) = source_layers.get(index) else {
                continue;
            };
            let row = Rect::from_min_size(
                Pos2::new(
                    source_rows_rect.left() + 6.0,
                    source_rows_rect.top() + position as f32 * COLUMN_ROW_STEP - self.source_scroll,
                ),
                Vec2::new(source_rows_rect.width() - 12.0, COLUMN_NODE_HEIGHT),
            );
            if !row.intersects(source_rows_rect) {
                continue;
            }
            visible_source_rows.insert(name.to_ascii_lowercase(), row);
            let target = self
                .effective_target(name, index, matches)
                .map(str::to_owned);
            let color = if self.overrides.get(name).is_some_and(Option::is_some) {
                PURPLE
            } else {
                self.source_color(name, matches.get(index))
            };
            let empty = empty_layers.contains(name) && self.highlight_empty;
            let paired_to_selected = target.as_ref().is_some_and(|target| {
                self.selected_target
                    .as_deref()
                    .is_some_and(|selected| selected.eq_ignore_ascii_case(target))
            });
            let fill = if empty { RED } else { NODE };
            source_painter.rect_filled(row, 5.0, fill);
            source_painter.rect_stroke(
                row,
                5.0,
                Stroke::new(
                    if paired_to_selected || self.selected_sources.contains(name) {
                        2.0
                    } else {
                        1.5
                    },
                    if self.selected_sources.contains(name) {
                        Color32::from_rgb(190, 170, 255)
                    } else if paired_to_selected {
                        highlight_color(color)
                    } else if empty {
                        Color32::from_rgb(255, 80, 80)
                    } else if target.is_some() {
                        color
                    } else {
                        GREY
                    },
                ),
                egui::StrokeKind::Inside,
            );
            Self::draw_column_row_text(
                &source_painter,
                row,
                name,
                target
                    .as_deref()
                    .map(|target| format!("to {target}"))
                    .as_deref(),
            );

            let response = ui.interact(
                row.intersect(source_rows_rect),
                Id::new(("column-source", name)),
                Sense::click_and_drag(),
            );
            if response.clicked() {
                let extend = ctx.input(|input| input.modifiers.ctrl || input.modifiers.shift);
                if extend {
                    if !self.selected_sources.remove(name) {
                        self.selected_sources.insert(name.clone());
                    }
                } else if !self.selected_sources.contains(name) {
                    self.selected_sources.clear();
                    self.selected_sources.insert(name.clone());
                }
            }
            if response.drag_started() {
                if !self.selected_sources.contains(name) {
                    self.selected_sources.clear();
                    self.selected_sources.insert(name.clone());
                }
                self.dragging_source = Some(name.clone());
            }
            if self.dragging_source.as_deref() == Some(name.as_str()) {
                if let Some(pointer) = ctx.input(|input| input.pointer.hover_pos()) {
                    paint_connection_curve(
                        &source_painter,
                        Pos2::new(row.right(), row.center().y),
                        pointer,
                        Stroke::new(2.0, PURPLE),
                    );
                }
                if response.drag_stopped() {
                    if let Some(pointer) = ctx.input(|input| input.pointer.interact_pos()) {
                        dropped_on = drop_target_rects
                            .iter()
                            .find(|(_, target_row)| target_row.contains(pointer))
                            .map(|(target, _)| (*target).to_string());
                    }
                }
            }
            if let Some(target) = target {
                column_connections.push((
                    name.to_ascii_lowercase(),
                    target.to_ascii_lowercase(),
                    color,
                ));
            }
        }

        for (source, target, color) in column_connections {
            if self
                .dragging_source
                .as_deref()
                .is_some_and(|dragging| dragging.eq_ignore_ascii_case(&source))
            {
                continue;
            }
            let (Some(source_row), Some(target_row)) = (
                visible_source_rows.get(&source),
                target_row_rects.get(&target),
            ) else {
                continue;
            };
            let start = Pos2::new(source_row.right(), source_row.center().y);
            let end = Pos2::new(target_row.left(), target_row.center().y);
            paint_connection_curve(&painter, start, end, Stroke::new(1.5, color));
            painter.circle_filled(start, 3.0, color);
            painter.circle_filled(end, 3.0, color);
        }

        if let Some(target) = dropped_on {
            let mapped_sources = visible_sources
                .iter()
                .filter_map(|index| source_layers.get(*index))
                .filter(|source| self.selected_sources.contains(*source))
                .cloned()
                .collect::<Vec<_>>();
            if !mapped_sources.is_empty() {
                self.push_undo_snapshot();
                for source in mapped_sources {
                    self.overrides.insert(source, Some(target.clone()));
                }
                self.selected_sources.clear();
                self.selected_target = Some(target);
                self.dragging_source = None;
                ctx.request_repaint();
            }
        }

        Self::draw_column_scrollbar(
            &painter,
            source_rows_rect,
            visible_sources.len(),
            self.source_scroll,
        );
        Self::draw_column_scrollbar(
            &painter,
            target_rows_rect,
            visible_targets.len(),
            self.target_scroll,
        );
        target_name_clicked
    }

    fn draw_column_panel_header(
        ui: &mut egui::Ui,
        painter: &Painter,
        rect: Rect,
        title: &str,
        subtitle: &str,
        clickable_subtitle: bool,
    ) -> (Rect, bool) {
        painter.rect_filled(rect, 8.0, Color32::from_rgba_unmultiplied(31, 33, 37, 236));
        painter.rect_stroke(
            rect,
            8.0,
            Stroke::new(1.0, Color32::from_rgb(95, 95, 95)),
            egui::StrokeKind::Inside,
        );
        painter.text(
            Pos2::new(rect.left() + 14.0, rect.top() + 9.0),
            Align2::LEFT_TOP,
            title,
            FontId::proportional(21.0),
            Color32::from_rgb(230, 230, 230),
        );
        let subtitle_rect = Rect::from_min_size(
            Pos2::new(rect.left() + 14.0, rect.top() + 34.0),
            Vec2::new((rect.width() - 28.0).max(80.0), 18.0),
        );
        let mut choose_standard = false;
        let mut subtitle_color = Color32::from_rgb(150, 150, 150);
        if clickable_subtitle {
            let response = ui.interact(
                subtitle_rect,
                Id::new("column_target_standard_name"),
                Sense::click(),
            );
            if response.hovered() {
                subtitle_color = Color32::from_rgb(205, 205, 205);
                ui.ctx().set_cursor_icon(CursorIcon::PointingHand);
            }
            choose_standard = response.clicked();
        }
        painter.text(
            subtitle_rect.min,
            Align2::LEFT_TOP,
            subtitle,
            FontId::proportional(11.0),
            subtitle_color,
        );
        let rows_rect = Rect::from_min_max(
            Pos2::new(rect.left() + 10.0, rect.top() + 90.0),
            Pos2::new(rect.right() - 10.0, rect.bottom() - 8.0),
        );
        (rows_rect, choose_standard)
    }

    fn draw_column_row_text(painter: &Painter, row: Rect, name: &str, annotation: Option<&str>) {
        let font = FontId::proportional(14.0);
        let annotation_font = FontId::proportional(12.0);
        let annotation = annotation.map(|text| {
            let maximum = row.width() * 0.42;
            Self::truncate_to_width(painter, text, annotation_font.clone(), maximum)
        });
        let annotation_width = annotation.as_ref().map_or(0.0, |text| {
            painter
                .layout_no_wrap(text.clone(), annotation_font.clone(), GREY)
                .size()
                .x
        });
        let name_width = (row.width() - annotation_width - 38.0).max(30.0);
        let name = Self::truncate_to_width(painter, name, font.clone(), name_width);
        painter.text(
            Pos2::new(row.left() + 10.0, row.center().y),
            Align2::LEFT_CENTER,
            name,
            font,
            Color32::from_rgb(224, 224, 224),
        );
        if let Some(annotation) = annotation {
            painter.text(
                Pos2::new(row.right() - 10.0, row.center().y),
                Align2::RIGHT_CENTER,
                annotation,
                annotation_font,
                Color32::from_rgb(184, 184, 184),
            );
        }
    }

    fn truncate_to_width(painter: &Painter, text: &str, font: FontId, max_width: f32) -> String {
        if painter
            .layout_no_wrap(text.to_string(), font.clone(), Color32::WHITE)
            .size()
            .x
            <= max_width
        {
            return text.to_string();
        }
        let mut chars = text.chars().collect::<Vec<_>>();
        while !chars.is_empty() {
            chars.pop();
            let candidate = format!("{}…", chars.iter().collect::<String>());
            if painter
                .layout_no_wrap(candidate.clone(), font.clone(), Color32::WHITE)
                .size()
                .x
                <= max_width
            {
                return candidate;
            }
        }
        String::new()
    }

    fn update_column_scroll(
        &mut self,
        ctx: &egui::Context,
        viewport: Rect,
        item_count: usize,
        source: bool,
    ) {
        let max_scroll = (item_count as f32 * COLUMN_ROW_STEP - viewport.height()).max(0.0);
        let scroll = if source {
            &mut self.source_scroll
        } else {
            &mut self.target_scroll
        };
        if ctx
            .input(|input| input.pointer.hover_pos())
            .is_some_and(|pointer| viewport.contains(pointer))
        {
            *scroll =
                (*scroll - ctx.input(|input| input.smooth_scroll_delta.y)).clamp(0.0, max_scroll);
        } else {
            *scroll = (*scroll).clamp(0.0, max_scroll);
        }
    }

    fn draw_column_scrollbar(painter: &Painter, viewport: Rect, item_count: usize, scroll: f32) {
        let content_height = item_count as f32 * COLUMN_ROW_STEP;
        if content_height <= viewport.height() {
            return;
        }
        let track = Rect::from_min_max(
            Pos2::new(viewport.right() - 3.0, viewport.top()),
            Pos2::new(viewport.right(), viewport.bottom()),
        );
        let thumb_height = (viewport.height() * viewport.height() / content_height).max(22.0);
        let max_scroll = content_height - viewport.height();
        let thumb_top = track.top() + (track.height() - thumb_height) * (scroll / max_scroll);
        painter.rect_filled(track, 2.0, Color32::from_rgb(48, 48, 48));
        painter.rect_filled(
            Rect::from_min_size(
                Pos2::new(track.left(), thumb_top),
                Vec2::new(3.0, thumb_height),
            ),
            2.0,
            Color32::from_rgb(112, 112, 112),
        );
    }

    fn target_pair_summaries(
        &self,
        source_layers: &[String],
        matches: &[MatchResult],
    ) -> HashMap<String, TargetPairSummary> {
        let mut summaries = HashMap::<String, TargetPairSummary>::new();
        for (index, source) in source_layers.iter().enumerate() {
            let Some(target) = self.effective_target(source, index, matches) else {
                continue;
            };
            let source_kind = if self.overrides.get(source).is_some_and(Option::is_some) {
                MatchSource::Manual
            } else if let Some(result) = matches.get(index) {
                result.source
            } else {
                continue;
            };
            let kind_index = match source_kind {
                MatchSource::Exact => 0,
                MatchSource::Memory => 1,
                MatchSource::Heuristic => 2,
                MatchSource::Manual => 3,
                MatchSource::Unmatched => continue,
            };
            let summary = summaries.entry(target.to_ascii_lowercase()).or_default();
            summary.source_names.push(source.clone());
            summary.match_counts[kind_index] += 1;
        }
        summaries
    }

    fn summary_color(summary: &TargetPairSummary) -> Color32 {
        let mut best_index = None;
        let mut best_count = 0;
        for (index, count) in summary.match_counts.iter().copied().enumerate() {
            if count > best_count {
                best_index = Some(index);
                best_count = count;
            }
        }
        match best_index {
            Some(0) => GREEN,
            Some(1) => MEMORY_BLUE,
            Some(2) => YELLOW,
            Some(3) => PURPLE,
            _ => GREY,
        }
    }

    fn effective_target<'a>(
        &'a self,
        source: &str,
        index: usize,
        matches: &'a [MatchResult],
    ) -> Option<&'a str> {
        match self.overrides.get(source) {
            Some(target) => target.as_deref(),
            None => matches.get(index)?.target_layer.as_deref(),
        }
    }

    fn target_positions(&self, visible: &[usize], canvas: Rect) -> HashMap<usize, Rect> {
        let (_, rows) = column_layout(visible.len(), self.node_columns);
        visible
            .iter()
            .enumerate()
            .map(|(position, index)| {
                let (column, row) = (position / rows, position % rows);
                let rect = self.world_rect(
                    canvas,
                    TARGET_X + column as f32 * TARGET_STEP,
                    150.0 + row as f32 * ROW_STEP,
                    NODE_WIDTH,
                    NODE_HEIGHT,
                );
                (*index, rect)
            })
            .collect()
    }

    fn animate_positions(
        &mut self,
        positions: &mut HashMap<usize, Rect>,
        is_source: bool,
        allow_animation: bool,
        ctx: &egui::Context,
    ) {
        const DURATION: Duration = Duration::from_millis(180);
        let now = Instant::now();
        let mut repaint = false;

        for (index, target) in positions.iter_mut() {
            let key = (is_source, *index);
            if !self.animations || !allow_animation {
                self.position_tweens.remove(&key);
                self.drawn_rects.insert(key, *target);
                continue;
            }

            let previous = self.drawn_rects.get(&key).copied();
            let tween = self.position_tweens.get(&key).copied();
            if tween.is_some_and(|active| active.to != *target) {
                self.position_tweens.remove(&key);
            }
            if !self.position_tweens.contains_key(&key) {
                if let Some(from) = previous.filter(|from| *from != *target) {
                    self.position_tweens.insert(
                        key,
                        PositionTween {
                            from,
                            to: *target,
                            started: now,
                        },
                    );
                }
            }

            if let Some(tween) = self.position_tweens.get(&key).copied() {
                let progress = (now - tween.started).as_secs_f32() / DURATION.as_secs_f32();
                if progress >= 1.0 {
                    self.position_tweens.remove(&key);
                    self.drawn_rects.insert(key, *target);
                } else {
                    let eased = progress * progress * (3.0 - 2.0 * progress);
                    *target = interpolate_rect(tween.from, tween.to, eased);
                    self.drawn_rects.insert(key, *target);
                    repaint = true;
                }
            } else {
                self.drawn_rects.insert(key, *target);
            }
        }

        if repaint {
            ctx.request_repaint_after(Duration::from_millis(16));
        }
    }

    fn world_rect(&self, canvas: Rect, x: f32, y: f32, width: f32, height: f32) -> Rect {
        let min = canvas.min + self.pan + Vec2::new(x, y) * self.zoom;
        Rect::from_min_size(min, Vec2::new(width, height) * self.zoom)
    }

    fn draw_group_headers(
        &mut self,
        ui: &mut egui::Ui,
        painter: &Painter,
        canvas: Rect,
        drawing: &str,
        template_name: &str,
        sources: &[usize],
        targets: &[usize],
    ) -> bool {
        let (source_columns, source_rows) = column_layout(sources.len(), self.node_columns);
        let (target_columns, target_rows) = column_layout(targets.len(), self.node_columns);
        let source_left = source_group_left(source_columns);
        let source_right = SOURCE_X + NODE_WIDTH + 24.0;
        let source_bottom =
            150.0 + source_rows.saturating_sub(1) as f32 * ROW_STEP + NODE_HEIGHT + 24.0;
        let target_right =
            TARGET_X + target_columns.saturating_sub(1) as f32 * TARGET_STEP + NODE_WIDTH + 24.0;
        let target_bottom =
            150.0 + target_rows.saturating_sub(1) as f32 * ROW_STEP + NODE_HEIGHT + 24.0;
        let mut target_name_clicked = false;
        for (x, right, bottom, title, subtitle, is_target) in [
            (
                source_left,
                source_right,
                source_bottom,
                "Source",
                drawing,
                false,
            ),
            (
                TARGET_X - 24.0,
                target_right,
                target_bottom,
                "Target",
                template_name,
                true,
            ),
        ] {
            let rect = self.world_rect(canvas, x, 40.0, right - x, (bottom - 40.0).max(100.0));
            painter.rect_filled(rect, 8.0, Color32::from_rgba_unmultiplied(255, 255, 255, 5));
            painter.rect_stroke(
                rect,
                8.0,
                Stroke::new(1.0, Color32::from_rgb(80, 80, 80)),
                egui::StrokeKind::Inside,
            );
            let title_pos = self.world_rect(canvas, x + 18.0, 50.0, 160.0, 25.0).min;
            painter.text(
                title_pos,
                Align2::LEFT_TOP,
                title,
                FontId::proportional((18.0 * self.zoom).max(12.0)),
                Color32::from_rgb(225, 225, 225),
            );
            let subtitle_rect = self.world_rect(
                canvas,
                x + 18.0,
                77.0,
                (right - x - 36.0).clamp(80.0, 420.0),
                20.0,
            );
            let mut subtitle_color = Color32::from_rgb(145, 145, 145);
            if is_target {
                let response = ui.interact(
                    subtitle_rect,
                    Id::new("node_target_standard_name"),
                    Sense::click(),
                );
                if response.hovered() {
                    subtitle_color = Color32::from_rgb(205, 205, 205);
                    ui.ctx().set_cursor_icon(CursorIcon::PointingHand);
                }
                target_name_clicked = response.clicked();
            }
            if 11.0 * self.zoom >= MIN_LABEL_PX {
                painter.text(
                    subtitle_rect.min,
                    Align2::LEFT_TOP,
                    subtitle,
                    FontId::proportional(11.0 * self.zoom),
                    subtitle_color,
                );
            }
            let field = filter_field_rect(
                self.world_rect(canvas, x + 18.0, 104.0, 0.0, 0.0).min,
                self.zoom,
            );
            if canvas.contains_rect(field) {
                let (query, hint) = if is_target {
                    (&mut self.target_query, "Filter target layers")
                } else {
                    (&mut self.source_query, "Filter source layers")
                };
                filter_field(ui, field, query, hint);
            }
        }
        target_name_clicked
    }

    fn draw_connections(
        &self,
        painter: &Painter,
        matches: &[MatchResult],
        source_names: &[String],
        sources: &HashMap<usize, Rect>,
        target_rects_by_name: &HashMap<String, Rect>,
    ) {
        let max_source_right = sources.values().map(|r| r.right()).fold(f32::MIN, f32::max);
        let min_target_left = target_rects_by_name
            .values()
            .map(|r| r.left())
            .fold(f32::MAX, f32::min);
        for (source_index, source_rect) in sources {
            let Some(source_name) = source_names.get(*source_index).map(String::as_str) else {
                continue;
            };
            let target_name = if let Some(override_target) = self.overrides.get(source_name) {
                override_target.as_deref()
            } else {
                matches
                    .get(*source_index)
                    .and_then(|m| m.target_layer.as_deref())
            };
            let Some(target_name) = target_name else {
                continue;
            };
            let color = if self.overrides.contains_key(source_name) {
                PURPLE
            } else {
                match matches.get(*source_index).map(|m| m.source) {
                    Some(MatchSource::Exact) => GREEN,
                    Some(MatchSource::Memory) => MEMORY_BLUE,
                    Some(MatchSource::Heuristic) => YELLOW,
                    Some(MatchSource::Manual) => PURPLE,
                    _ => GREY,
                }
            };
            let start = Pos2::new(source_rect.right(), source_rect.center().y);
            if let Some(target_rect) = target_rects_by_name.get(&target_name.to_ascii_lowercase()) {
                let end = Pos2::new(target_rect.left(), target_rect.center().y);
                let c1 = start + Vec2::new((end.x - start.x) * 0.45, 0.0);
                let c2 = end - Vec2::new((end.x - start.x) * 0.45, 0.0);
                let points = [start, c1, c2, end];
                // Lines between the adjacent columns keep the node outline color; lines
                // that run under other nodes are darker so they do not blend into them.
                let line_color = if line_passes_under(
                    source_rect.right(),
                    max_source_right,
                    target_rect.left(),
                    min_target_left,
                ) {
                    darken(color, PASS_UNDER_DARKEN)
                } else {
                    color
                };
                painter.add(egui::epaint::CubicBezierShape::from_points_stroke(
                    points,
                    false,
                    Color32::TRANSPARENT,
                    Stroke::new(1.5, line_color),
                ));
            }
        }
    }

    fn handle_connection_click(
        &mut self,
        canvas: &egui::Response,
        matches: &[MatchResult],
        source_names: &[String],
        sources: &HashMap<usize, Rect>,
        targets: &HashMap<usize, Rect>,
        target_rects_by_name: &HashMap<String, Rect>,
    ) {
        let Some(pointer) = canvas.interact_pointer_pos() else {
            return;
        };
        if sources
            .values()
            .chain(targets.values())
            .any(|rect| rect.contains(pointer))
        {
            return;
        }
        let nearest = self.nearest_connection_at(
            pointer,
            matches,
            source_names,
            sources,
            target_rects_by_name,
        );

        let mut delete_connection = None;
        canvas.context_menu(|menu| {
            if let Some((distance, source)) = &nearest {
                if *distance <= 7.0 && menu.button("Delete Connection").clicked() {
                    delete_connection = Some(source.clone());
                    menu.close();
                } else if *distance > 7.0 {
                    menu.close();
                }
            } else {
                menu.close();
            }
        });
        if let Some(source) = delete_connection {
            self.push_undo_snapshot();
            self.overrides.insert(source, None);
            return;
        }
        if !canvas.clicked() {
            return;
        }
        if let Some((_, source)) = nearest.filter(|(distance, _)| *distance <= 7.0) {
            self.push_undo_snapshot();
            self.overrides.insert(source, None);
        }
    }

    fn nearest_connection_at(
        &self,
        pointer: Pos2,
        matches: &[MatchResult],
        source_names: &[String],
        sources: &HashMap<usize, Rect>,
        target_rects_by_name: &HashMap<String, Rect>,
    ) -> Option<(f32, String)> {
        let mut nearest: Option<(f32, String)> = None;
        for (source_index, source_rect) in sources {
            let Some(source_name) = source_names.get(*source_index) else {
                continue;
            };
            let target_name = if let Some(override_target) = self.overrides.get(source_name) {
                override_target.as_deref()
            } else {
                matches
                    .get(*source_index)
                    .and_then(|m| m.target_layer.as_deref())
            };
            let Some(target_name) = target_name else {
                continue;
            };
            let Some(target_rect) = target_rects_by_name.get(&target_name.to_ascii_lowercase())
            else {
                continue;
            };
            let start = Pos2::new(source_rect.right(), source_rect.center().y);
            let end = Pos2::new(target_rect.left(), target_rect.center().y);
            let span = end.x - start.x;
            let control1 = start + Vec2::new(span * 0.45, 0.0);
            let control2 = end - Vec2::new(span * 0.45, 0.0);
            let mut previous = start;
            let mut distance = f32::INFINITY;
            for step in 1..=24 {
                let point = cubic(start, control1, control2, end, step as f32 / 24.0);
                distance = distance.min(distance_to_segment(pointer, previous, point));
                previous = point;
            }
            if nearest.as_ref().is_none_or(|(best, _)| distance < *best) {
                nearest = Some((distance, source_name.clone()));
            }
        }
        nearest
    }

    fn draw_layer_node(
        &mut self,
        ui: &mut egui::Ui,
        painter: &Painter,
        rect: &Rect,
        name: &str,
        source: bool,
        _result: Option<&MatchResult>,
        color: Color32,
        connected: bool,
        empty: bool,
        selected: bool,
        pair_highlighted: bool,
    ) {
        let mut fill = NODE;
        if empty {
            fill = RED;
        }
        painter.rect_filled(*rect, 3.0, fill);
        painter.rect_stroke(
            *rect,
            3.0,
            Stroke::new(
                if selected || pair_highlighted {
                    2.0
                } else if connected {
                    1.5
                } else {
                    1.0
                },
                if selected {
                    Color32::from_rgb(190, 170, 255)
                } else if pair_highlighted {
                    highlight_color(color)
                } else if empty {
                    Color32::from_rgb(255, 80, 80)
                } else if connected {
                    color
                } else {
                    GREY
                },
            ),
            egui::StrokeKind::Inside,
        );
        painter.circle_filled(
            Pos2::new(
                if source { rect.right() } else { rect.left() },
                rect.center().y,
            ),
            (5.0 * self.zoom).max(2.0),
            color,
        );
        let font_size = 12.0 * self.zoom;
        if font_size >= MIN_LABEL_PX {
            painter.with_clip_rect(rect.shrink(2.0)).text(
                rect.center(),
                Align2::CENTER_CENTER,
                name,
                FontId::proportional(font_size),
                Color32::from_rgb(220, 220, 220),
            );
        }
        let _ = ui;
    }

    fn source_color(&self, _name: &str, result: Option<&MatchResult>) -> Color32 {
        match result.map(|m| m.source) {
            Some(MatchSource::Exact) => GREEN,
            Some(MatchSource::Memory) => MEMORY_BLUE,
            Some(MatchSource::Heuristic) => YELLOW,
            Some(MatchSource::Manual) => PURPLE,
            _ => {
                if result.is_some_and(|m| m.target_layer.is_some()) {
                    PURPLE
                } else {
                    GREY
                }
            }
        }
    }

    fn visible_sources(&self, sources: &[String], matches: &[MatchResult]) -> Vec<usize> {
        sources
            .iter()
            .enumerate()
            .filter_map(|(index, name)| {
                if !contains_folded(name, &self.source_query) {
                    return None;
                }
                if self.overrides.contains_key(name) {
                    return self.show_manual.then_some(index);
                }
                let kind = matches.get(index).map(|m| m.source);
                let shown = match kind {
                    Some(MatchSource::Exact) => self.show_exact,
                    Some(MatchSource::Memory) => self.show_memory,
                    Some(MatchSource::Heuristic) => {
                        self.show_heuristic
                            && matches
                                .get(index)
                                .is_some_and(|m| m.confidence >= self.confidence)
                    }
                    Some(MatchSource::Manual) => self.show_manual,
                    _ => self.show_unmatched,
                };
                shown.then_some(index)
            })
            .collect()
    }

    fn visible_targets(
        &mut self,
        targets: &[String],
        filters: &[TargetFilter],
        always_hidden: &HashSet<String>,
    ) -> Vec<usize> {
        let mut filters_by_layer = HashMap::<String, Vec<(&str, &str)>>::new();
        let mut specific_counts = HashMap::<String, usize>::new();
        let hidden_names = always_hidden
            .iter()
            .map(|name| name.to_ascii_lowercase())
            .collect::<HashSet<_>>();
        for filter in filters {
            for layer in &filter.layers {
                filters_by_layer
                    .entry(layer.to_ascii_lowercase())
                    .or_default()
                    .push((filter.name.as_str(), filter.sort_group.as_str()));
                if filter.sort_group == "Specific" {
                    *specific_counts
                        .entry(filter.name.to_ascii_lowercase())
                        .or_default() += 1;
                }
            }
        }

        targets
            .iter()
            .enumerate()
            .filter_map(|(index, name)| {
                if hidden_names.contains(&name.to_ascii_lowercase()) {
                    return None;
                }
                if !contains_folded(name, &self.target_query) {
                    return None;
                }
                if filters.is_empty() {
                    return Some(index);
                }
                let Some(matched) = filters_by_layer.get(&name.to_ascii_lowercase()) else {
                    return Some(index);
                };
                let primary_specific = matched
                    .iter()
                    .filter(|(_, group)| *group == "Specific")
                    .min_by_key(|(filter_name, _)| {
                        (
                            specific_counts
                                .get(&filter_name.to_ascii_lowercase())
                                .copied()
                                .unwrap_or(usize::MAX),
                            filter_name.to_ascii_lowercase(),
                        )
                    })
                    .map(|(name, _)| *name);
                let checked = matched.iter().any(|(filter_name, group)| {
                    (*group != "Specific" || Some(*filter_name) == primary_specific)
                        && *self
                            .category_visibility
                            .entry((*filter_name).to_string())
                            .or_insert(true)
                });
                checked.then_some(index)
            })
            .collect()
    }

    fn draw_left_panel(&mut self, ctx: &egui::Context, source_count: usize, empty_count: usize) {
        egui::Area::new(Id::new("mapping_legend"))
            .fixed_pos(egui::pos2(24.0, 20.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                egui::Frame::new()
                    .fill(Color32::from_rgba_unmultiplied(31, 33, 37, 236))
                    .stroke(Stroke::new(1.0, Color32::from_rgb(95, 95, 95)))
                    .corner_radius(8)
                    .inner_margin(egui::Margin::symmetric(14, 12))
                    .show(ui, |ui| {
                        ui.set_width(154.0);
                        ui.label(
                            RichText::new("Source Drawing")
                                .size(16.0)
                                .strong()
                                .color(Color32::from_rgb(230, 230, 230)),
                        );
                        ui.label(
                            RichText::new("Visibility Toggle")
                                .size(11.0)
                                .color(Color32::from_rgb(120, 120, 120)),
                        );
                        ui.add_space(4.0);
                        if ui
                            .add_sized([150.0, 24.0], egui::Button::new("Fit to View (F)"))
                            .clicked()
                        {
                            self.fit_requested = true;
                        }
                        ui.add_space(8.0);
                        filter_button(ui, "Exact Match", GREEN, &mut self.show_exact);
                        filter_button(ui, "Memory Match", MEMORY_BLUE, &mut self.show_memory);
                        filter_button(ui, "Heuristic Match", YELLOW, &mut self.show_heuristic);
                        ui.horizontal(|ui| {
                            ui.add(
                                egui::Slider::new(&mut self.confidence, 0.6..=1.0)
                                    .show_value(false),
                            );
                            ui.label(
                                RichText::new(format!("{:.0}%", self.confidence * 100.0))
                                    .size(10.0)
                                    .color(Color32::LIGHT_GRAY),
                            );
                        });
                        filter_button(ui, "Manual Match", PURPLE, &mut self.show_manual);
                        filter_button(ui, "Unmatched", GREY, &mut self.show_unmatched);
                        ui.add_space(8.0);
                        separator(ui);
                        ui.label(
                            RichText::new(format!("Empty Layers ({empty_count})"))
                                .size(11.0)
                                .color(Color32::from_rgb(125, 125, 125)),
                        );
                        ui.horizontal(|ui| {
                            let _ = ui
                                .add_sized(
                                    [74.0, 28.0],
                                    egui::Button::new("Highlight")
                                        .fill(RED)
                                        .corner_radius(7)
                                        .selected(self.highlight_empty),
                                )
                                .clicked()
                                .then(|| self.highlight_empty = !self.highlight_empty);
                            if ui
                                .add_enabled(
                                    empty_count > 0,
                                    egui::Button::new("Purge")
                                        .fill(RED)
                                        .corner_radius(7)
                                        .min_size(Vec2::new(74.0, 28.0)),
                                )
                                .on_hover_text(
                                    "Remove unused empty layers from the active drawing.",
                                )
                                .clicked()
                            {
                                self.purge_confirmation_open = true;
                            }
                        });
                        ui.add_space(6.0);
                        separator(ui);
                        ui.label(
                            RichText::new("Layer Property Matching")
                                .size(11.0)
                                .color(Color32::from_rgb(125, 125, 125)),
                        );
                        property_toggle(ui, "Match Color", &mut self.match_color);
                        property_toggle(ui, "Match Linetype", &mut self.match_linetype);
                        property_toggle(ui, "Match Lineweight", &mut self.match_lineweight);
                        property_toggle(ui, "Make Elements ByLayer", &mut self.make_by_layer);
                        ui.add_space(4.0);
                        separator(ui);
                        ui.label(
                            RichText::new("Display")
                                .size(11.0)
                                .color(Color32::from_rgb(125, 125, 125)),
                        );
                        property_toggle(ui, "Animations", &mut self.animations);
                        ui.label(
                            RichText::new(format!("{source_count} source layers"))
                                .size(9.0)
                                .color(Color32::from_rgb(100, 100, 100)),
                        );
                    });
            });
    }

    fn draw_right_panel(
        &mut self,
        ctx: &egui::Context,
        filters: &[TargetFilter],
        template_name: &str,
    ) -> bool {
        let mut choose_standard = false;
        egui::Area::new(Id::new("mapping_target_filters"))
            .anchor(Align2::RIGHT_TOP, egui::vec2(-20.0, 20.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                let min_height = 260.0;
                let max_height = (ctx.content_rect().height() - 40.0).max(300.0);
                let default_height = (ctx.content_rect().height() - 180.0).max(280.0);
                let panel_height = self
                    .target_filter_height
                    .unwrap_or(default_height)
                    .clamp(min_height, max_height);
                self.target_filter_height = Some(panel_height);
                let panel_width = self.target_filter_width;
                ui.set_width(panel_width);
                {
                    {
                        let panel = egui::Frame::new()
                            .fill(Color32::from_rgba_unmultiplied(31, 33, 37, 236))
                            .stroke(Stroke::new(1.0, Color32::from_rgb(95, 95, 95)))
                            .corner_radius(8)
                            .inner_margin(egui::Margin::symmetric(14, 12))
                            .show(ui, |ui| {
                                ui.set_height(panel_height - 24.0);
                                let control_width = ui.available_width();
                                ui.label(
                                    RichText::new("Target Filter")
                                        .size(16.0)
                                        .strong()
                                        .color(Color32::from_rgb(230, 230, 230)),
                                );
                                ui.label(
                                    RichText::new(if template_name.is_empty() {
                                        "No standard selected"
                                    } else {
                                        template_name
                                    })
                                    .size(10.0)
                                    .color(Color32::from_rgb(160, 160, 160)),
                                );
                                if ui
                                    .add_sized(
                                        [control_width, 28.0],
                                        egui::Button::new("Choose Standard")
                                            .fill(BLUE)
                                            .corner_radius(7),
                                    )
                                    .clicked()
                                {
                                    choose_standard = true;
                                }
                                ui.label(
                                    RichText::new("Visibility Toggle")
                                        .size(11.0)
                                        .color(Color32::from_rgb(120, 120, 120)),
                                );
                                ui.add_space(4.0);
                                ui.label(
                                    RichText::new("Target filter")
                                        .size(9.0)
                                        .color(Color32::from_gray(130)),
                                );
                                ui.add_space(7.0);
                                if !filters.is_empty()
                                    && ui
                                        .add_sized(
                                            [control_width, 28.0],
                                            egui::Button::new("All On/Off")
                                                .fill(BLUE)
                                                .corner_radius(7),
                                        )
                                        .clicked()
                                {
                                    let all_on = filters.iter().all(|f| {
                                        *self
                                            .category_visibility
                                            .entry(f.name.clone())
                                            .or_insert(true)
                                    });
                                    for filter in filters {
                                        self.category_visibility
                                            .insert(filter.name.clone(), !all_on);
                                    }
                                }
                                if !filters.is_empty() {
                                    egui::ScrollArea::vertical()
                                        .max_height((ui.available_height() - 4.0).max(80.0))
                                        .show(ui, |ui| {
                                            for filter in filters {
                                                let checked = self
                                                    .category_visibility
                                                    .entry(filter.name.clone())
                                                    .or_insert(true);
                                                let color = match filter.sort_group.as_str() {
                                                    "Discipline" => Color32::from_rgb(74, 125, 196),
                                                    "General" => Color32::from_rgb(74, 133, 119),
                                                    _ => Color32::from_rgb(102, 102, 102),
                                                };
                                                let label = RichText::new(&filter.name).color(
                                                    if *checked {
                                                        Color32::from_rgb(232, 232, 232)
                                                    } else {
                                                        Color32::from_rgb(158, 158, 158)
                                                    },
                                                );
                                                if ui
                                                    .add_sized(
                                                        [ui.available_width(), 28.0],
                                                        egui::Button::new(label)
                                                            .fill(Color32::from_rgb(38, 40, 44))
                                                            .stroke(Stroke::new(
                                                                if *checked { 1.5 } else { 1.0 },
                                                                if *checked { color } else { GREY },
                                                            ))
                                                            .corner_radius(7),
                                                    )
                                                    .clicked()
                                                {
                                                    *checked = !*checked;
                                                }
                                                ui.add_space(4.0);
                                            }
                                        });
                                }
                            });
                        // Height-only grip, inset so it never covers the rounded corner.
                        let corner = panel.response.rect.right_bottom();
                        let grip_rect = Rect::from_min_max(corner - Vec2::splat(22.0), corner);
                        let grip = ui.interact(
                            grip_rect,
                            Id::new("target_filter_height_grip"),
                            Sense::drag(),
                        );
                        if grip.hovered() || grip.dragged() {
                            ctx.set_cursor_icon(egui::CursorIcon::ResizeVertical);
                        }
                        if grip.dragged() {
                            self.target_filter_height = Some(
                                (panel_height + grip.drag_delta().y).clamp(min_height, max_height),
                            );
                        }
                        let grip_color = if grip.hovered() || grip.dragged() {
                            Color32::from_gray(190)
                        } else {
                            Color32::from_gray(105)
                        };
                        let origin = corner - Vec2::splat(10.0);
                        for step in [3.0_f32, 6.0, 9.0] {
                            ui.painter().line_segment(
                                [origin + Vec2::new(0.0, step), origin + Vec2::new(step, 0.0)],
                                Stroke::new(1.2, grip_color),
                            );
                        }
                    }
                }
            });
        choose_standard
    }

    pub fn retain_valid_targets(&mut self, targets: &[String]) {
        let names = targets
            .iter()
            .map(|name| name.to_ascii_lowercase())
            .collect::<HashSet<_>>();
        self.overrides.retain(|_, target| {
            target
                .as_ref()
                .is_none_or(|name| names.contains(&name.to_ascii_lowercase()))
        });
        self.undo_stack.clear();
        self.redo_stack.clear();
    }

    fn draw_canvas_help(&self, _ctx: &egui::Context, _rect: Rect) {}
}

/// Outline color for a node whose pair is highlighted (selected target, or the
/// sources paired to it). Manual matches keep their purple; everything else uses
/// the orange pair highlight.
fn highlight_color(match_color: Color32) -> Color32 {
    if match_color == PURPLE {
        PURPLE
    } else {
        PAIR_HIGHLIGHT
    }
}

/// Splits `count` nodes into the fewest columns of at most
/// `MAX_NODE_COLUMN_HEIGHT`, balanced so no column is much shorter than the rest.
/// Returns `(columns, rows_per_column)`.
///
/// With `requested` columns (the footer slider) the nodes are split evenly across at
/// most that many columns instead; a side with few layers uses fewer, never leaving a
/// column empty.
fn column_layout(count: usize, requested: Option<usize>) -> (usize, usize) {
    match requested {
        None => {
            let columns = count.div_ceil(MAX_NODE_COLUMN_HEIGHT).max(1);
            (columns, count.div_ceil(columns).max(1))
        }
        Some(chosen) => {
            let rows = count.div_ceil(chosen.max(1)).max(1);
            (count.div_ceil(rows).max(1), rows)
        }
    }
}

/// The footer slider's stops: 0 is "Auto", 1..=8 are that many node columns.
const MAX_COLUMN_STOP: usize = 8;

fn node_columns_for_stop(stop: usize) -> Option<usize> {
    (stop > 0).then(|| stop.min(MAX_COLUMN_STOP))
}

fn stop_for_node_columns(columns: Option<usize>) -> usize {
    columns.map_or(0, |columns| columns.min(MAX_COLUMN_STOP))
}

fn column_stop_label(stop: usize) -> String {
    match stop {
        0 => "Auto".to_string(),
        1 => "1 column".to_string(),
        n => format!("{n} columns"),
    }
}

/// The slider stop nearest to pointer x, for a track running from `left` to `right`.
fn stop_at_position(x: f32, left: f32, right: f32) -> usize {
    let fraction = ((x - left) / (right - left)).clamp(0.0, 1.0);
    (fraction * MAX_COLUMN_STOP as f32).round() as usize
}

/// World x of the left edge of the Source group: its last column sits beside the
/// Target group, so extra columns grow to the left.
fn source_group_left(columns: usize) -> f32 {
    SOURCE_X - 24.0 - columns.saturating_sub(1) as f32 * SOURCE_STEP
}

fn darken(color: Color32, factor: f32) -> Color32 {
    let scale = |channel: u8| (channel as f32 * factor).round() as u8;
    Color32::from_rgb(scale(color.r()), scale(color.g()), scale(color.b()))
}

/// A line has to pass under other nodes when its source is not in the column beside
/// the targets or its target is not in the column beside the sources.
fn line_passes_under(
    source_right: f32,
    max_source_right: f32,
    target_left: f32,
    min_target_left: f32,
) -> bool {
    source_right < max_source_right - 1.0 || target_left > min_target_left + 1.0
}

/// A filter box anchored at a panel's header corner; a fixed size in drawing units,
/// so it scales with zoom exactly like the nodes.
fn filter_field_rect(top_left: Pos2, zoom: f32) -> Rect {
    Rect::from_min_size(
        top_left,
        Vec2::new(FILTER_FIELD_WIDTH, FILTER_FIELD_HEIGHT) * zoom,
    )
}

/// A text filter box with a clearly visible light-grey frame.
fn filter_field(ui: &mut egui::Ui, rect: Rect, query: &mut String, hint: &str) {
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect));
    let visuals = child.visuals_mut();
    let frame = Color32::from_gray(150);
    visuals.widgets.inactive.bg_stroke = Stroke::new(1.0, frame);
    visuals.widgets.hovered.bg_stroke = Stroke::new(1.0, Color32::from_gray(190));
    child.add_sized(
        rect.size(),
        egui::TextEdit::singleline(query)
            .hint_text(hint)
            .font(FontId::proportional(13.0)),
    );
}

fn filter_button(ui: &mut egui::Ui, label: &str, color: Color32, enabled: &mut bool) {
    let text_color = if *enabled {
        Color32::from_rgb(232, 232, 232)
    } else {
        Color32::from_rgb(158, 158, 158)
    };
    if ui
        .add_sized(
            [150.0, 28.0],
            egui::Button::new(RichText::new(label).strong().color(text_color))
                .fill(Color32::from_rgb(38, 40, 44))
                .stroke(Stroke::new(
                    if *enabled { 1.5 } else { 1.0 },
                    if *enabled { color } else { GREY },
                ))
                .corner_radius(7),
        )
        .clicked()
    {
        *enabled = !*enabled;
    }
}

/// Slider in the footer's mode-switch style: a dark pill with a blue thumb that snaps
/// between "Auto" and 1..=8 node columns. Returns the (possibly changed) stop.
fn draw_columns_slider(ui: &mut egui::Ui, stop: usize) -> usize {
    let (rect, response) = ui.allocate_exact_size(Vec2::new(262.0, 34.0), Sense::click_and_drag());
    let painter = ui.painter();
    painter.rect_filled(rect, 17.0, Color32::from_rgb(37, 37, 37));
    painter.rect_stroke(
        rect,
        17.0,
        Stroke::new(1.0, Color32::from_rgb(102, 102, 102)),
        egui::StrokeKind::Inside,
    );

    let track_left = rect.left() + 96.0;
    let track_right = rect.right() - 18.0;
    let stop_x = |stop: usize| {
        track_left + (track_right - track_left) * stop as f32 / MAX_COLUMN_STOP as f32
    };
    let stop = if response.dragged() || response.is_pointer_button_down_on() {
        response
            .interact_pointer_pos()
            .map_or(stop, |pos| stop_at_position(pos.x, track_left, track_right))
    } else {
        stop
    };

    painter.text(
        Pos2::new(rect.left() + 16.0, rect.center().y),
        Align2::LEFT_CENTER,
        column_stop_label(stop),
        FontId::proportional(13.0),
        Color32::WHITE,
    );
    painter.line_segment(
        [
            Pos2::new(track_left, rect.center().y),
            Pos2::new(track_right, rect.center().y),
        ],
        Stroke::new(2.0, Color32::from_rgb(102, 102, 102)),
    );
    for tick in 0..=MAX_COLUMN_STOP {
        painter.circle_filled(
            Pos2::new(stop_x(tick), rect.center().y),
            2.5,
            Color32::from_rgb(130, 130, 130),
        );
    }
    painter.circle_filled(Pos2::new(stop_x(stop), rect.center().y), 11.0, BLUE);
    response.on_hover_text("Node columns per side (Auto wraps at 25 nodes per column)");
    stop
}

fn draw_mode_switch(ui: &mut egui::Ui, column_mode: bool) -> bool {
    let (rect, _) = ui.allocate_exact_size(Vec2::new(136.0, 34.0), Sense::hover());
    let painter = ui.painter();
    painter.rect_filled(rect, 17.0, Color32::from_rgb(37, 37, 37));
    painter.rect_stroke(
        rect,
        17.0,
        Stroke::new(1.0, Color32::from_rgb(102, 102, 102)),
        egui::StrokeKind::Inside,
    );

    let half = rect.width() * 0.5;
    let node_rect = Rect::from_min_max(rect.min, Pos2::new(rect.left() + half, rect.bottom()));
    let column_rect = Rect::from_min_max(Pos2::new(rect.left() + half, rect.top()), rect.max);
    let node_response = ui.interact(node_rect, Id::new("mapping-mode-node"), Sense::click());
    let column_response = ui.interact(column_rect, Id::new("mapping-mode-column"), Sense::click());

    let thumb_rect = if column_mode {
        Rect::from_min_max(
            Pos2::new(rect.left() + half + 3.0, rect.top() + 3.0),
            Pos2::new(rect.right() - 3.0, rect.bottom() - 3.0),
        )
    } else {
        Rect::from_min_max(
            Pos2::new(rect.left() + 3.0, rect.top() + 3.0),
            Pos2::new(rect.left() + half - 3.0, rect.bottom() - 3.0),
        )
    };
    painter.rect_filled(thumb_rect, 14.0, BLUE);
    painter.text(
        Pos2::new(node_rect.center().x, rect.center().y),
        Align2::CENTER_CENTER,
        "Node",
        FontId::proportional(13.0),
        if column_mode {
            Color32::from_rgb(175, 175, 175)
        } else {
            Color32::WHITE
        },
    );
    painter.text(
        Pos2::new(column_rect.center().x, rect.center().y),
        Align2::CENTER_CENTER,
        "Column",
        FontId::proportional(13.0),
        if column_mode {
            Color32::WHITE
        } else {
            Color32::from_rgb(175, 175, 175)
        },
    );

    if node_response.clicked() {
        false
    } else if column_response.clicked() {
        true
    } else {
        column_mode
    }
}

fn property_toggle(ui: &mut egui::Ui, label: &str, value: &mut bool) {
    ui.horizontal(|ui| {
        ui.label(
            RichText::new(label)
                .size(10.0)
                .color(Color32::from_rgb(180, 180, 180)),
        );
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            ui.checkbox(value, "");
        });
    });
}

fn separator(ui: &mut egui::Ui) {
    ui.add_space(4.0);
    ui.separator();
    ui.add_space(4.0);
}

fn contains_folded(text: &str, query: &str) -> bool {
    query.is_empty() || text.to_lowercase().contains(&query.to_lowercase())
}

fn interpolate_rect(from: Rect, to: Rect, amount: f32) -> Rect {
    Rect::from_min_max(from.min.lerp(to.min, amount), from.max.lerp(to.max, amount))
}

/// Draws the same horizontal-tangent S-curve that node mode uses for connections.
fn paint_connection_curve(painter: &Painter, start: Pos2, end: Pos2, stroke: Stroke) {
    let handle = Vec2::new((end.x - start.x) * 0.45, 0.0);
    painter.add(egui::epaint::CubicBezierShape::from_points_stroke(
        [start, start + handle, end - handle, end],
        false,
        Color32::TRANSPARENT,
        stroke,
    ));
}

fn cubic(a: Pos2, b: Pos2, c: Pos2, d: Pos2, t: f32) -> Pos2 {
    let u = 1.0 - t;
    Pos2::new(
        u.powi(3) * a.x + 3.0 * u.powi(2) * t * b.x + 3.0 * u * t.powi(2) * c.x + t.powi(3) * d.x,
        u.powi(3) * a.y + 3.0 * u.powi(2) * t * b.y + 3.0 * u * t.powi(2) * c.y + t.powi(3) * d.y,
    )
}

fn distance_to_segment(point: Pos2, start: Pos2, end: Pos2) -> f32 {
    let delta = end - start;
    let length_squared = delta.length_sq();
    if length_squared <= f32::EPSILON {
        return point.distance(start);
    }
    let t = ((point - start).dot(delta) / length_squared).clamp(0.0, 1.0);
    point.distance(start + delta * t)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn unit_editor() -> MappingEditor {
        let mut editor = MappingEditor::default();
        editor.zoom = 1.0;
        editor.pan = Vec2::ZERO;
        editor
    }

    fn canvas() -> Rect {
        Rect::from_min_size(Pos2::ZERO, Vec2::new(10_000.0, 10_000.0))
    }

    fn indices(count: usize) -> Vec<usize> {
        (0..count).collect()
    }

    fn tallest_column(rects: &HashMap<usize, Rect>) -> usize {
        let mut per_column: HashMap<i32, usize> = HashMap::new();
        for rect in rects.values() {
            *per_column.entry(rect.left().round() as i32).or_default() += 1;
        }
        per_column.values().copied().max().unwrap_or(0)
    }

    fn column_count(rects: &HashMap<usize, Rect>) -> usize {
        rects
            .values()
            .map(|r| r.left().round() as i32)
            .collect::<HashSet<_>>()
            .len()
    }

    #[test]
    fn column_layout_never_exceeds_the_maximum_height_and_stays_balanced() {
        assert_eq!(column_layout(0, None), (1, 1));
        assert_eq!(column_layout(25, None), (1, 25));
        assert_eq!(column_layout(26, None), (2, 13));
        assert_eq!(column_layout(129, None), (6, 22));
        assert_eq!(column_layout(321, None), (13, 25));
        for count in 1..=2000 {
            let (columns, rows) = column_layout(count, None);
            assert!(rows <= MAX_NODE_COLUMN_HEIGHT, "{count}: {rows} rows");
            assert!(
                columns * rows >= count,
                "{count}: layout cannot hold every node"
            );
        }
    }

    #[test]
    fn a_chosen_column_count_splits_the_nodes_evenly() {
        assert_eq!(column_layout(100, Some(1)), (1, 100));
        assert_eq!(column_layout(100, Some(4)), (4, 25));
        assert_eq!(column_layout(100, Some(8)), (8, 13));
        assert_eq!(column_layout(0, Some(3)), (1, 1));
        for count in 1..=300 {
            for chosen in 1..=8 {
                let (columns, rows) = column_layout(count, Some(chosen));
                assert!(columns <= chosen, "{count}/{chosen}: {columns} columns");
                assert!(
                    columns * rows >= count,
                    "{count}/{chosen}: cannot hold every node"
                );
                assert!(
                    (columns - 1) * rows < count,
                    "{count}/{chosen}: the last column would be empty"
                );
            }
        }
    }

    #[test]
    fn few_layers_never_get_empty_columns() {
        assert_eq!(column_layout(3, Some(8)), (3, 1));
        assert_eq!(column_layout(10, Some(8)), (5, 2));
        assert_eq!(column_layout(1, Some(5)), (1, 1));
    }

    #[test]
    fn slider_stops_map_to_auto_then_one_to_eight_columns() {
        assert_eq!(node_columns_for_stop(0), None);
        assert_eq!(node_columns_for_stop(1), Some(1));
        assert_eq!(node_columns_for_stop(8), Some(8));
        assert_eq!(node_columns_for_stop(99), Some(8));
        for stop in 0..=MAX_COLUMN_STOP {
            assert_eq!(stop_for_node_columns(node_columns_for_stop(stop)), stop);
        }
        assert_eq!(column_stop_label(0), "Auto");
        assert_eq!(column_stop_label(1), "1 column");
        assert_eq!(column_stop_label(3), "3 columns");
    }

    #[test]
    fn a_pointer_position_picks_the_nearest_slider_stop() {
        assert_eq!(stop_at_position(100.0, 100.0, 180.0), 0);
        assert_eq!(stop_at_position(180.0, 100.0, 180.0), MAX_COLUMN_STOP);
        assert_eq!(stop_at_position(140.0, 100.0, 180.0), 4);
        assert_eq!(
            stop_at_position(-50.0, 100.0, 180.0),
            0,
            "clamped on the left"
        );
        assert_eq!(
            stop_at_position(900.0, 100.0, 180.0),
            MAX_COLUMN_STOP,
            "clamped on the right"
        );
    }

    #[test]
    fn the_chosen_column_count_drives_node_placement_and_bounds() {
        let mut editor = unit_editor();
        editor.node_columns = Some(3);
        let sources = editor.source_positions(&indices(100), canvas());
        let targets = editor.target_positions(&indices(100), canvas());
        assert_eq!(column_count(&sources), 3);
        assert_eq!(column_count(&targets), 3);
        let bounds = MappingEditor::content_bounds(100, 100, Some(3));
        for rect in sources.values().chain(targets.values()) {
            assert!(bounds.contains_rect(*rect), "{rect:?} outside {bounds:?}");
        }
    }

    #[test]
    fn source_nodes_wrap_into_short_columns_with_the_last_column_next_to_the_target() {
        let editor = unit_editor();
        let rects = editor.source_positions(&indices(129), canvas());
        assert_eq!(rects.len(), 129);
        assert_eq!(column_count(&rects), 6);
        assert!(tallest_column(&rects) <= MAX_NODE_COLUMN_HEIGHT);
        let rightmost = rects.values().map(|r| r.right()).fold(f32::MIN, f32::max);
        assert_eq!(
            rightmost,
            SOURCE_X + NODE_WIDTH,
            "the column beside the target does not move"
        );
    }

    #[test]
    fn target_nodes_wrap_into_short_columns() {
        let editor = unit_editor();
        let rects = editor.target_positions(&indices(321), canvas());
        assert_eq!(rects.len(), 321);
        assert_eq!(column_count(&rects), 13);
        assert!(tallest_column(&rects) <= MAX_NODE_COLUMN_HEIGHT);
        let leftmost = rects.values().map(|r| r.left()).fold(f32::MAX, f32::min);
        assert_eq!(leftmost, TARGET_X);
    }

    #[test]
    fn content_bounds_cover_every_node_in_both_groups() {
        let editor = unit_editor();
        let sources = editor.source_positions(&indices(129), canvas());
        let targets = editor.target_positions(&indices(321), canvas());
        let bounds = MappingEditor::content_bounds(129, 321, None);
        for rect in sources.values().chain(targets.values()) {
            assert!(bounds.contains_rect(*rect), "{rect:?} outside {bounds:?}");
        }
    }

    #[test]
    fn a_line_between_the_adjacent_columns_keeps_its_color_and_others_are_darker() {
        assert!(!line_passes_under(530.0, 530.0, 620.0, 620.0));
        assert!(
            line_passes_under(210.0, 530.0, 620.0, 620.0),
            "source behind another column"
        );
        assert!(
            line_passes_under(530.0, 530.0, 940.0, 620.0),
            "target behind another column"
        );
        let darker = darken(Color32::from_rgb(100, 200, 50), 0.7);
        assert_eq!((darker.r(), darker.g(), darker.b()), (70, 140, 35));
    }

    #[test]
    fn a_filter_box_scales_with_zoom_like_the_nodes() {
        let corner = Pos2::new(123.0, 456.0);
        for zoom in [0.25, 1.0, 2.0] {
            let field = filter_field_rect(corner, zoom);
            assert_eq!(field.min, corner);
            let expected = Vec2::new(FILTER_FIELD_WIDTH, FILTER_FIELD_HEIGHT) * zoom;
            assert!((field.size() - expected).length() < 0.01);
        }
    }

    #[test]
    fn a_highlighted_manual_match_stays_purple_and_other_matches_use_the_pair_highlight() {
        assert_eq!(highlight_color(PURPLE), PURPLE);
        for color in [GREEN, MEMORY_BLUE, YELLOW, GREY] {
            assert_eq!(highlight_color(color), PAIR_HIGHLIGHT);
        }
    }
}
