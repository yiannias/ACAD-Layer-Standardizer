use acad_layer_core::{MatchResult, MatchSource};
use acad_layer_ipc::TargetFilter;
use egui::{Align2, Color32, FontId, Id, Painter, Pos2, Rect, RichText, Sense, Stroke, Vec2};
use std::collections::{HashMap, HashSet};

const CANVAS: Color32 = Color32::from_rgb(14, 14, 14);
const NODE: Color32 = Color32::from_rgb(66, 66, 66);
const BLUE: Color32 = Color32::from_rgb(70, 130, 180);
const GREEN: Color32 = Color32::from_rgb(139, 195, 74);
const YELLOW: Color32 = Color32::from_rgb(255, 215, 64);
const PURPLE: Color32 = Color32::from_rgb(144, 133, 233);
const RED: Color32 = Color32::from_rgb(198, 40, 40);
const GREY: Color32 = Color32::from_rgb(136, 136, 136);
const ROW_STEP: f32 = 44.0;
const NODE_WIDTH: f32 = 280.0;
const NODE_HEIGHT: f32 = 30.0;
const SOURCE_X: f32 = 250.0;
const TARGET_X: f32 = 580.0;
const TARGET_STEP: f32 = 320.0;

pub struct MappingEditor {
    pub confidence: f64,
    pub overrides: HashMap<String, Option<String>>,
    source_query: String,
    target_query: String,
    show_exact: bool,
    show_memory: bool,
    show_heuristic: bool,
    show_manual: bool,
    show_unmatched: bool,
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
    pointer_is_panning: bool,
}

impl Default for MappingEditor {
    fn default() -> Self {
        Self {
            confidence: 0.6,
            overrides: HashMap::new(),
            source_query: String::new(),
            target_query: String::new(),
            show_exact: true,
            show_memory: true,
            show_heuristic: true,
            show_manual: true,
            show_unmatched: true,
            highlight_empty: false,
            match_color: true,
            match_linetype: true,
            match_lineweight: true,
            make_by_layer: false,
            animations: false,
            category_visibility: HashMap::new(),
            zoom: 0.68,
            pan: Vec2::new(180.0, 40.0),
            dragging_source: None,
            pointer_is_panning: false,
        }
    }
}

impl MappingEditor {
    pub fn show(
        &mut self,
        ui: &mut egui::Ui,
        ctx: &egui::Context,
        drawing_name: &str,
        source_layers: &[String],
        standard_layers: &[String],
        matches: &[MatchResult],
        empty_layers: &HashSet<String>,
        target_filters: &[TargetFilter],
        status: &str,
    ) -> Option<f64> {
        let previous_confidence = self.confidence;
        egui::Area::new(Id::new("mapping_status_bar"))
            .anchor(Align2::CENTER_BOTTOM, egui::vec2(0.0, -4.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                egui::Frame::new().fill(Color32::from_rgb(45, 45, 45)).inner_margin(egui::Margin::symmetric(12, 6)).show(ui, |ui| {
                ui.vertical(|ui| {
                    ui.label(RichText::new("Drag from a source → standard layer to map. Click a connection to remove it. Scroll to zoom. Middle-drag to pan.").size(12.0).color(Color32::from_rgb(170, 170, 170)));
                    ui.horizontal(|ui| {
                        ui.label(RichText::new(status).size(12.0).color(Color32::from_rgb(170, 170, 170)));
                        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                            ui.add_enabled(false, egui::Button::new("Apply & Remember").fill(BLUE));
                            ui.add_space(6.0);
                            ui.add_enabled(false, egui::Button::new("Apply"));
                        });
                    });
                });
                });
            });

        let mut canvas_rect = ui.available_rect_before_wrap();
        canvas_rect.max.y -= 48.0;
        let canvas_response = ui.allocate_rect(canvas_rect, Sense::click_and_drag());
        let painter = ui.painter_at(canvas_rect);
        painter.rect_filled(canvas_rect, 0.0, CANVAS);
        self.handle_canvas_navigation(ctx, &canvas_response, canvas_rect);
        self.draw_grid(&painter, canvas_rect);

        let visible_sources = self.visible_sources(source_layers, matches);
        let visible_targets = self.visible_targets(standard_layers, target_filters);
        let source_positions = self.source_positions(&visible_sources, canvas_rect);
        let target_positions = self.target_positions(&visible_targets, canvas_rect);

        self.draw_group_headers(
            &painter,
            canvas_rect,
            drawing_name,
            &visible_sources,
            &visible_targets,
        );
        self.draw_connections(
            &painter,
            matches,
            source_layers,
            &source_positions,
            &target_positions,
            standard_layers,
        );
        self.handle_connection_click(
            &canvas_response,
            matches,
            source_layers,
            standard_layers,
            &source_positions,
            &target_positions,
        );

        let mut drop_target_rects = Vec::with_capacity(target_positions.len());
        for (index, rect) in &target_positions {
            let name = &standard_layers[*index];
            drop_target_rects.push((name.as_str(), *rect));
            self.draw_layer_node(
                ui,
                &painter,
                rect,
                name,
                false,
                None,
                self.target_color(name, matches, source_layers),
                false,
            );
        }

        for (index, rect) in &source_positions {
            let name = &source_layers[*index];
            let result = matches.get(*index);
            let empty = empty_layers
                .iter()
                .any(|layer| layer.eq_ignore_ascii_case(name));
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
                empty && self.highlight_empty,
            );

            let response = ui.interact(
                *rect,
                Id::new(("source-layer", name)),
                Sense::click_and_drag(),
            );
            if response.drag_started() {
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
                            self.overrides
                                .insert(name.clone(), Some((*target).to_string()));
                        }
                    }
                    self.dragging_source = None;
                }
            }
        }

        self.draw_left_panel(ctx, source_layers.len(), empty_layers.len());
        self.draw_right_panel(ctx, target_filters);
        self.draw_canvas_help(ctx, canvas_rect);
        ((self.confidence - previous_confidence).abs() > f64::EPSILON).then_some(self.confidence)
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
            self.zoom = (self.zoom * (scroll * 0.001).exp()).clamp(0.08, 1.6);
            self.pan = pointer - rect.min - before * self.zoom;
        }
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
        visible
            .iter()
            .enumerate()
            .filter_map(|(row, index)| {
                let rect = self.world_rect(
                    canvas,
                    SOURCE_X,
                    150.0 + row as f32 * ROW_STEP,
                    NODE_WIDTH,
                    NODE_HEIGHT,
                );
                rect.intersects(canvas).then_some((*index, rect))
            })
            .collect()
    }

    fn target_positions(&self, visible: &[usize], canvas: Rect) -> HashMap<usize, Rect> {
        let columns = 5usize.min(visible.len().max(1));
        let rows = visible.len().div_ceil(columns).max(1);
        visible
            .iter()
            .enumerate()
            .filter_map(|(position, index)| {
                let column = position / rows;
                let row = position % rows;
                let rect = self.world_rect(
                    canvas,
                    TARGET_X + column as f32 * TARGET_STEP,
                    150.0 + row as f32 * ROW_STEP,
                    NODE_WIDTH,
                    NODE_HEIGHT,
                );
                rect.intersects(canvas).then_some((*index, rect))
            })
            .collect()
    }

    fn world_rect(&self, canvas: Rect, x: f32, y: f32, width: f32, height: f32) -> Rect {
        let min = canvas.min + self.pan + Vec2::new(x, y) * self.zoom;
        Rect::from_min_size(min, Vec2::new(width, height) * self.zoom)
    }

    fn draw_group_headers(
        &self,
        painter: &Painter,
        canvas: Rect,
        drawing: &str,
        sources: &[usize],
        targets: &[usize],
    ) {
        let source_right = SOURCE_X + NODE_WIDTH + 24.0;
        let source_bottom =
            150.0 + sources.len().saturating_sub(1) as f32 * ROW_STEP + NODE_HEIGHT + 24.0;
        let target_columns = targets
            .len()
            .div_ceil(targets.len().div_ceil(5).max(1))
            .max(1)
            .min(5);
        let target_right =
            TARGET_X + target_columns.saturating_sub(1) as f32 * TARGET_STEP + NODE_WIDTH + 24.0;
        let target_rows = targets.len().div_ceil(target_columns.max(1));
        let target_bottom =
            150.0 + target_rows.saturating_sub(1) as f32 * ROW_STEP + NODE_HEIGHT + 24.0;
        for (x, right, bottom, title, subtitle) in [
            (
                SOURCE_X - 24.0,
                source_right,
                source_bottom,
                "Source",
                drawing,
            ),
            (
                TARGET_X - 24.0,
                target_right,
                target_bottom,
                "Target",
                "Standard template",
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
                FontId::proportional(18.0 * self.zoom),
                Color32::from_rgb(225, 225, 225),
            );
            let sub_pos = self.world_rect(canvas, x + 18.0, 77.0, 220.0, 20.0).min;
            painter.text(
                sub_pos,
                Align2::LEFT_TOP,
                subtitle,
                FontId::proportional(11.0 * self.zoom),
                Color32::from_rgb(145, 145, 145),
            );
        }
    }

    fn draw_connections(
        &self,
        painter: &Painter,
        matches: &[MatchResult],
        source_names: &[String],
        sources: &HashMap<usize, Rect>,
        targets: &HashMap<usize, Rect>,
        target_names: &[String],
    ) {
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
                    Some(MatchSource::Memory) => BLUE,
                    Some(MatchSource::Heuristic) => YELLOW,
                    Some(MatchSource::Manual) => PURPLE,
                    _ => GREY,
                }
            };
            let start = Pos2::new(source_rect.right(), source_rect.center().y);
            if let Some(target_rect) = targets.iter().find_map(|(index, rect)| {
                target_names
                    .get(*index)
                    .filter(|name| name.eq_ignore_ascii_case(target_name))
                    .map(|_| rect)
            }) {
                let end = Pos2::new(target_rect.left(), target_rect.center().y);
                let c1 = start + Vec2::new((end.x - start.x) * 0.45, 0.0);
                let c2 = end - Vec2::new((end.x - start.x) * 0.45, 0.0);
                let points = [start, c1, c2, end];
                painter.add(egui::epaint::CubicBezierShape::from_points_stroke(
                    points,
                    false,
                    Color32::TRANSPARENT,
                    Stroke::new(1.5, color),
                ));
            }
        }
    }

    fn handle_connection_click(
        &mut self,
        canvas: &egui::Response,
        matches: &[MatchResult],
        source_names: &[String],
        target_names: &[String],
        sources: &HashMap<usize, Rect>,
        targets: &HashMap<usize, Rect>,
    ) {
        if !canvas.clicked() {
            return;
        }
        let Some(pointer) = canvas.interact_pointer_pos() else {
            return;
        };
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
            let Some((_, target_rect)) = targets.iter().find(|(index, _)| {
                target_names
                    .get(**index)
                    .is_some_and(|name| name.eq_ignore_ascii_case(target_name))
            }) else {
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
        if let Some((_, source)) = nearest.filter(|(distance, _)| *distance <= 7.0) {
            self.overrides.insert(source, None);
        }
    }

    fn draw_layer_node(
        &mut self,
        ui: &mut egui::Ui,
        painter: &Painter,
        rect: &Rect,
        name: &str,
        source: bool,
        result: Option<&MatchResult>,
        color: Color32,
        empty: bool,
    ) {
        let mut fill = if source && result.is_some_and(|m| m.target_layer.is_some()) {
            blend(NODE, color, 0.27)
        } else {
            NODE
        };
        if !source {
            fill = NODE;
        }
        if empty {
            fill = RED;
        }
        painter.rect_filled(*rect, 3.0, fill);
        painter.rect_stroke(
            *rect,
            3.0,
            Stroke::new(
                1.0,
                if empty {
                    Color32::from_rgb(255, 80, 80)
                } else {
                    Color32::from_rgb(55, 55, 55)
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
        painter.text(
            rect.center(),
            Align2::CENTER_CENTER,
            name,
            FontId::proportional((12.0 * self.zoom).max(7.0)),
            Color32::from_rgb(220, 220, 220),
        );
        let _ = ui;
    }

    fn source_color(&self, _name: &str, result: Option<&MatchResult>) -> Color32 {
        match result.map(|m| m.source) {
            Some(MatchSource::Exact) => GREEN,
            Some(MatchSource::Memory) => BLUE,
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

    fn target_color(&self, _name: &str, _matches: &[MatchResult], _sources: &[String]) -> Color32 {
        Color32::from_rgb(140, 140, 140)
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

    fn visible_targets(&mut self, targets: &[String], filters: &[TargetFilter]) -> Vec<usize> {
        targets
            .iter()
            .enumerate()
            .filter_map(|(index, name)| {
                if !contains_folded(name, &self.target_query) {
                    return None;
                }
                if filters.is_empty() {
                    return Some(index);
                }
                let matched: Vec<&TargetFilter> = filters
                    .iter()
                    .filter(|filter| {
                        filter
                            .layers
                            .iter()
                            .any(|layer| layer.eq_ignore_ascii_case(name))
                    })
                    .collect();
                if matched.is_empty() {
                    return Some(index);
                }
                let checked = matched.iter().any(|filter| {
                    *self
                        .category_visibility
                        .entry(filter.name.clone())
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
                    .fill(Color32::from_rgba_unmultiplied(42, 42, 42, 226))
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
                        ui.add_space(8.0);
                        filter_button(ui, "Exact Match", GREEN, &mut self.show_exact);
                        filter_button(ui, "Memory Match", BLUE, &mut self.show_memory);
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
                                        .selected(self.highlight_empty),
                                )
                                .clicked()
                                .then(|| self.highlight_empty = !self.highlight_empty);
                            ui.add_enabled(
                                false,
                                egui::Button::new("Purge")
                                    .fill(RED)
                                    .min_size(Vec2::new(74.0, 28.0)),
                            )
                            .on_hover_text(
                                "Purge becomes available after the apply bridge is implemented.",
                            );
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

    fn draw_right_panel(&mut self, ctx: &egui::Context, filters: &[TargetFilter]) {
        if filters.is_empty() {
            return;
        }
        egui::Area::new(Id::new("mapping_target_filters"))
            .anchor(Align2::RIGHT_TOP, egui::vec2(-20.0, 20.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                egui::Frame::new()
                    .fill(Color32::from_rgba_unmultiplied(42, 42, 42, 226))
                    .stroke(Stroke::new(1.0, Color32::from_rgb(95, 95, 95)))
                    .corner_radius(8)
                    .inner_margin(egui::Margin::symmetric(14, 12))
                    .show(ui, |ui| {
                        ui.set_width(172.0);
                        ui.label(
                            RichText::new("Target Filter")
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
                        ui.label(
                            RichText::new("Target filter")
                                .size(9.0)
                                .color(Color32::from_gray(130)),
                        );
                        ui.add(
                            egui::TextEdit::singleline(&mut self.target_query)
                                .desired_width(150.0)
                                .hint_text("Search layers"),
                        );
                        ui.add_space(7.0);
                        if ui
                            .add_sized([150.0, 28.0], egui::Button::new("All On/Off").fill(BLUE))
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
                        egui::ScrollArea::vertical()
                            .max_height(ctx.content_rect().height() - 110.0)
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
                                    if ui
                                        .add_sized(
                                            [150.0, 28.0],
                                            egui::Button::new(&filter.name).fill(if *checked {
                                                color
                                            } else {
                                                GREY
                                            }),
                                        )
                                        .clicked()
                                    {
                                        *checked = !*checked;
                                    }
                                    ui.add_space(4.0);
                                }
                            });
                    });
            });
    }

    fn draw_canvas_help(&self, _ctx: &egui::Context, _rect: Rect) {}
}

fn filter_button(ui: &mut egui::Ui, label: &str, color: Color32, enabled: &mut bool) {
    let fill = if *enabled {
        color
    } else {
        Color32::from_rgb(78, 78, 78)
    };
    if ui
        .add_sized(
            [150.0, 28.0],
            egui::Button::new(RichText::new(label).strong()).fill(fill),
        )
        .clicked()
    {
        *enabled = !*enabled;
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

fn blend(base: Color32, tint: Color32, amount: f32) -> Color32 {
    let lerp = |a: u8, b: u8| (a as f32 + (b as f32 - a as f32) * amount).round() as u8;
    Color32::from_rgb(
        lerp(base.r(), tint.r()),
        lerp(base.g(), tint.g()),
        lerp(base.b(), tint.b()),
    )
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
