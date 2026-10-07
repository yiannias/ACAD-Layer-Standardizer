//! Pure decisions for following the active AutoCAD drawing (no egui, no IPC calls).

use acad_layer_ipc::{FeedEvent, FeedMessage};
use std::collections::HashSet;
use std::time::{Duration, Instant};

/// How long to wait before retrying a layer read that failed (AutoCAD busy, pipe down).
pub const READ_RETRY_DELAY: Duration = Duration::from_secs(1);

/// What one batch of feed events asks the window to do.
#[derive(Debug, Default, PartialEq)]
pub struct FeedPlan {
    /// Show this drawing instead of the current one.
    pub switch_to: Option<String>,
    /// Re-read the current drawing's layers.
    pub refresh_current: bool,
    /// Drawings that closed: drop their sessions.
    pub forget: Vec<String>,
    /// Closes AutoCAD blocked for unapplied connections, as (kind, drawing_id), each
    /// once, in order (the id is empty for a quit without one).
    pub blocked: Vec<(String, String)>,
}

fn event_drawing_id(event: &FeedEvent) -> Option<&str> {
    event.payload.get("drawing_id").and_then(|id| id.as_str())
}

/// Folds a batch of feed events, in order, into one plan. `current_id` is the drawing
/// the window shows (or is already switching to); unknown event kinds are ignored.
/// `CloseBlocked` events are collected for the close dialog.
pub fn plan_feed_events(current_id: &str, events: &[FeedEvent]) -> FeedPlan {
    let mut plan = FeedPlan::default();
    // Whether the current drawing's layers changed / it closed anywhere in the batch.
    let mut current_changed = false;
    let mut current_closed = false;
    for event in events {
        if event.kind == "CloseBlocked" {
            if let Some(kind) = event.payload.get("kind").and_then(|kind| kind.as_str()) {
                let blocked = (
                    kind.to_string(),
                    event_drawing_id(event).unwrap_or_default().to_string(),
                );
                if !plan.blocked.contains(&blocked) {
                    plan.blocked.push(blocked);
                }
            }
            continue;
        }
        let Some(id) = event_drawing_id(event) else {
            continue;
        };
        match event.kind.as_str() {
            "DrawingActivated" => {
                // Coming back to the displayed drawing cancels an earlier switch.
                plan.switch_to = (id != current_id).then(|| id.to_string());
            }
            "LayersChanged" => {
                if id == current_id && !current_id.is_empty() {
                    current_changed = true;
                }
            }
            "DrawingClosed" => {
                if plan.switch_to.as_deref() == Some(id) {
                    plan.switch_to = None;
                }
                if id == current_id {
                    current_closed = true;
                }
                if !plan.forget.iter().any(|known| known == id) {
                    plan.forget.push(id.to_string());
                }
            }
            _ => {}
        }
    }
    // A switch reads the new drawing's layers, so it needs no refresh; a change seen
    // before switching away and back within the batch still refreshes.
    plan.refresh_current = plan.switch_to.is_none() && current_changed && !current_closed;
    plan
}

/// A layer read the window wants from the connector.
#[derive(Debug, Clone, PartialEq)]
pub enum LayerRead {
    /// Ask which drawing is active, then read its layers.
    Active,
    /// Read this drawing's layers.
    Drawing(String),
}

/// Keeps at most one layer read in flight, defers reads while the window is busy,
/// and retries failed reads after a short delay.
#[derive(Debug, Default)]
pub struct ReadSchedule {
    wanted: Option<LayerRead>,
    issued: Option<LayerRead>,
    wanted_again: bool,
    retry_at: Option<Instant>,
}

impl ReadSchedule {
    /// A read that replaces whatever was wanted before (a switch or a resync).
    pub fn want(&mut self, read: LayerRead) {
        if self.issued.as_ref() == Some(&read) {
            self.wanted_again = true;
        }
        if self.wanted.as_ref() != Some(&read) {
            // A different read is due now, not after an earlier read's retry delay.
            self.retry_at = None;
        }
        self.wanted = Some(read);
    }

    /// A refresh of the displayed drawing: does not override a switch or resync
    /// that is already wanted (those read the layers too).
    pub fn want_refresh(&mut self, current_id: &str) {
        let read = LayerRead::Drawing(current_id.to_string());
        match &self.wanted {
            None => self.want(read),
            Some(wanted) if *wanted == read => self.want(read),
            Some(_) => {}
        }
    }

    /// Drops a wanted read of a drawing that has closed.
    pub fn cancel_drawing(&mut self, drawing_id: &str) {
        if matches!(&self.wanted, Some(LayerRead::Drawing(id)) if id == drawing_id) {
            self.wanted = None;
            self.wanted_again = false;
            self.retry_at = None;
        }
    }

    /// True while a read is wanted and has not completed (the window itself only
    /// needs `next`, so this is for tests).
    #[cfg(test)]
    pub fn refresh_wanted(&self) -> bool {
        self.wanted.is_some()
    }

    /// True when the window is waiting to show a drawing other than `displayed_id`.
    pub fn switch_pending(&self, displayed_id: &str) -> bool {
        matches!(&self.wanted, Some(LayerRead::Drawing(id)) if id != displayed_id)
    }

    /// The drawing the window will show once wanted reads finish: a wanted switch
    /// target, else the displayed drawing. Feed events are planned against this id.
    pub fn showing_next<'a>(&'a self, displayed_id: &'a str) -> &'a str {
        match &self.wanted {
            Some(LayerRead::Drawing(id)) => id,
            _ => displayed_id,
        }
    }

    /// The read to start now, if any; it is then in flight until `finished`.
    pub fn next(&mut self, busy: bool, now: Instant) -> Option<LayerRead> {
        if busy || self.issued.is_some() {
            return None;
        }
        if self.retry_at.is_some_and(|at| now < at) {
            return None;
        }
        let read = self.wanted.clone()?;
        self.retry_at = None;
        self.wanted_again = false;
        self.issued = Some(read.clone());
        Some(read)
    }

    /// How long until a deferred retry is due, so the window can schedule a repaint.
    pub fn retry_in(&self, now: Instant) -> Option<Duration> {
        if self.wanted.is_none() || self.issued.is_some() {
            return None;
        }
        self.retry_at.map(|at| at.saturating_duration_since(now))
    }

    /// Records the end of the in-flight read. Returns whether its result should be
    /// shown: false when a different read has been wanted since (the result is stale).
    pub fn finished(&mut self, ok: bool, now: Instant) -> bool {
        let issued = self.issued.take();
        let current = issued.is_some() && issued == self.wanted;
        if !current {
            return false;
        }
        if !ok {
            self.retry_at = Some(now + READ_RETRY_DELAY);
            return false;
        }
        if !self.wanted_again {
            self.wanted = None;
        }
        self.wanted_again = false;
        true
    }
}

/// Whether layers read for `new_id` belong to a different drawing than the one shown.
/// A drawing shown from a snapshot without an id (an older connector) is adopted, so
/// its connections are kept rather than dropped.
pub fn is_switch(displayed_id: &str, no_drawing: bool, new_id: &str) -> bool {
    new_id != displayed_id && (no_drawing || !displayed_id.is_empty())
}

/// Whether a refresh changed the layer lists (order does not matter).
pub fn layers_differ(
    old_sources: &[String],
    old_empty: &HashSet<String>,
    new_sources: &[String],
    new_empty: &[String],
) -> bool {
    let set = |names: &[String]| names.iter().cloned().collect::<HashSet<_>>();
    set(old_sources) != set(new_sources) || *old_empty != set(new_empty)
}

/// The feed polls whenever the window is open, focused or not; it pauses only while
/// the window is minimized AND no drawing (displayed or stashed) has unapplied
/// connections. Each poll is the check-in that lets AutoCAD refuse a close, so it
/// must never stop while something could be lost. (Occluded windows keep polling.)
pub fn feed_should_pause(minimized: Option<bool>, anything_pending: bool) -> bool {
    minimized.unwrap_or(false) && !anything_pending
}

/// The layer read a feed status message asks for. Only a reset (first poll, or a
/// restarted connector) re-reads the active drawing; reconnecting after a busy spell
/// does not, because the events missed meanwhile arrive through `since`.
pub fn feed_status_read(message: &FeedMessage) -> Option<LayerRead> {
    match message {
        FeedMessage::Resync => Some(LayerRead::Active),
        FeedMessage::Up | FeedMessage::Down(_) | FeedMessage::Events(_) => None,
    }
}

/// The message shown instead of sending an Apply or Purge, if it must not be sent.
pub fn blocked_action_message(no_drawing: bool, switch_pending: bool) -> Option<&'static str> {
    if no_drawing {
        Some("No drawing is open.")
    } else if switch_pending {
        Some(
            "AutoCAD has switched to another drawing. Wait for this window to show it, then try again.",
        )
    } else {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn event(kind: &str, id: &str) -> FeedEvent {
        FeedEvent {
            seq: 1,
            kind: kind.into(),
            payload: serde_json::json!({ "drawing_id": id, "display_name": "x.dwg" }),
        }
    }

    #[test]
    fn activation_of_another_drawing_switches() {
        let plan = plan_feed_events(
            "A",
            &[event("DrawingActivated", "B"), event("LayersChanged", "B")],
        );
        assert_eq!(plan.switch_to.as_deref(), Some("B"));
        assert!(!plan.refresh_current, "the switch reads B's layers anyway");
        assert!(plan.forget.is_empty());
    }

    #[test]
    fn activation_of_the_displayed_drawing_does_not_switch_or_refresh() {
        let plan = plan_feed_events("A", &[event("DrawingActivated", "A")]);
        assert_eq!(plan, FeedPlan::default());
        // Away and back within one batch ends on the displayed drawing.
        let plan = plan_feed_events(
            "A",
            &[
                event("DrawingActivated", "B"),
                event("DrawingActivated", "A"),
            ],
        );
        assert_eq!(plan, FeedPlan::default());
    }

    #[test]
    fn layers_changed_for_the_displayed_drawing_refreshes() {
        let plan = plan_feed_events("A", &[event("LayersChanged", "A")]);
        assert_eq!(
            plan,
            FeedPlan {
                refresh_current: true,
                ..FeedPlan::default()
            }
        );
    }

    #[test]
    fn a_change_survives_switching_away_and_back_in_one_batch() {
        let plan = plan_feed_events(
            "A",
            &[
                event("LayersChanged", "A"),
                event("DrawingActivated", "B"),
                event("DrawingActivated", "A"),
            ],
        );
        assert_eq!(
            plan,
            FeedPlan {
                refresh_current: true,
                ..FeedPlan::default()
            }
        );
        // A change made while B was planned still counts once A is back.
        let plan = plan_feed_events(
            "A",
            &[
                event("DrawingActivated", "B"),
                event("LayersChanged", "A"),
                event("DrawingActivated", "A"),
            ],
        );
        assert!(plan.refresh_current && plan.switch_to.is_none());
    }

    #[test]
    fn layers_changed_for_another_drawing_is_ignored() {
        let plan = plan_feed_events("A", &[event("LayersChanged", "B")]);
        assert_eq!(plan, FeedPlan::default());
    }

    #[test]
    fn closed_drawings_are_forgotten() {
        let plan = plan_feed_events(
            "A",
            &[event("DrawingClosed", "B"), event("DrawingClosed", "A")],
        );
        assert_eq!(plan.forget, vec!["B".to_string(), "A".to_string()]);
        assert_eq!(plan.switch_to, None);
        assert!(!plan.refresh_current);
    }

    #[test]
    fn closing_the_drawing_being_switched_to_cancels_the_switch() {
        let plan = plan_feed_events(
            "A",
            &[event("DrawingActivated", "B"), event("DrawingClosed", "B")],
        );
        assert_eq!(plan.switch_to, None);
        assert_eq!(plan.forget, vec!["B".to_string()]);
    }

    #[test]
    fn a_refresh_is_dropped_when_the_displayed_drawing_closes_later_in_the_batch() {
        let plan = plan_feed_events(
            "A",
            &[event("LayersChanged", "A"), event("DrawingClosed", "A")],
        );
        assert!(!plan.refresh_current);
        assert_eq!(plan.forget, vec!["A".to_string()]);
    }

    #[test]
    fn with_no_drawing_shown_an_activation_switches() {
        let plan = plan_feed_events("", &[event("DrawingActivated", "B")]);
        assert_eq!(plan.switch_to.as_deref(), Some("B"));
    }

    #[test]
    fn unknown_kinds_and_payloads_without_an_id_are_ignored() {
        let mut no_id = event("LayersChanged", "A");
        no_id.payload = serde_json::json!({});
        let plan = plan_feed_events("A", &[event("SomethingNew", "B"), no_id]);
        assert_eq!(plan, FeedPlan::default());
    }

    fn blocked(payload: serde_json::Value) -> FeedEvent {
        FeedEvent {
            seq: 1,
            kind: "CloseBlocked".into(),
            payload,
        }
    }

    #[test]
    fn blocked_closes_are_collected_once_each_in_order() {
        let plan = plan_feed_events(
            "A",
            &[
                blocked(serde_json::json!({ "kind": "drawing", "drawing_id": "B" })),
                blocked(serde_json::json!({ "kind": "drawing", "drawing_id": "A" })),
                blocked(serde_json::json!({ "kind": "drawing", "drawing_id": "B" })),
                blocked(serde_json::json!({ "kind": "quit" })),
            ],
        );
        assert_eq!(
            plan.blocked,
            vec![
                ("drawing".to_string(), "B".to_string()),
                ("drawing".to_string(), "A".to_string()),
                ("quit".to_string(), String::new()),
            ]
        );
        assert_eq!(plan.switch_to, None);
        assert!(plan.forget.is_empty() && !plan.refresh_current);
    }

    #[test]
    fn a_blocked_close_without_a_kind_is_ignored() {
        let plan = plan_feed_events("A", &[blocked(serde_json::json!({ "drawing_id": "A" }))]);
        assert_eq!(plan, FeedPlan::default());
    }

    #[test]
    fn a_wanted_read_is_issued_once_and_cleared_on_success() {
        let now = Instant::now();
        let mut reads = ReadSchedule::default();
        assert_eq!(reads.next(false, now), None);
        reads.want(LayerRead::Drawing("B".into()));
        assert!(reads.refresh_wanted());
        assert_eq!(reads.next(false, now), Some(LayerRead::Drawing("B".into())));
        assert_eq!(reads.next(false, now), None, "one read in flight at a time");
        assert!(reads.finished(true, now));
        assert!(!reads.refresh_wanted());
        assert_eq!(reads.next(false, now), None);
    }

    #[test]
    fn reads_wait_while_busy() {
        let now = Instant::now();
        let mut reads = ReadSchedule::default();
        reads.want_refresh("A");
        assert_eq!(reads.next(true, now), None);
        assert!(reads.refresh_wanted());
        assert_eq!(reads.next(false, now), Some(LayerRead::Drawing("A".into())));
    }

    #[test]
    fn a_failed_read_is_retried_after_the_delay() {
        let now = Instant::now();
        let mut reads = ReadSchedule::default();
        reads.want(LayerRead::Active);
        reads.next(false, now);
        assert!(!reads.finished(false, now));
        assert!(reads.refresh_wanted());
        assert_eq!(reads.next(false, now), None);
        assert_eq!(reads.retry_in(now), Some(READ_RETRY_DELAY));
        let later = now + READ_RETRY_DELAY;
        assert_eq!(reads.next(false, later), Some(LayerRead::Active));
    }

    #[test]
    fn a_result_superseded_by_a_newer_read_is_stale() {
        let now = Instant::now();
        let mut reads = ReadSchedule::default();
        reads.want(LayerRead::Drawing("B".into()));
        reads.next(false, now);
        reads.want(LayerRead::Drawing("C".into()));
        assert!(!reads.finished(true, now), "B's layers must not be shown");
        assert_eq!(reads.next(false, now), Some(LayerRead::Drawing("C".into())));
    }

    #[test]
    fn the_same_read_wanted_again_while_in_flight_is_shown_and_repeated() {
        let now = Instant::now();
        let mut reads = ReadSchedule::default();
        reads.want_refresh("A");
        reads.next(false, now);
        reads.want_refresh("A");
        assert!(reads.finished(true, now));
        assert_eq!(reads.next(false, now), Some(LayerRead::Drawing("A".into())));
        assert!(reads.finished(true, now));
        assert!(!reads.refresh_wanted());
    }

    #[test]
    fn a_refresh_does_not_override_a_wanted_switch() {
        let mut reads = ReadSchedule::default();
        reads.want(LayerRead::Drawing("B".into()));
        reads.want_refresh("A");
        assert!(reads.switch_pending("A"));
        assert!(!reads.switch_pending("B"));
        let now = Instant::now();
        assert_eq!(reads.next(false, now), Some(LayerRead::Drawing("B".into())));
    }

    #[test]
    fn a_closed_drawing_is_no_longer_wanted() {
        let now = Instant::now();
        let mut reads = ReadSchedule::default();
        reads.want(LayerRead::Drawing("B".into()));
        reads.cancel_drawing("C");
        assert!(reads.refresh_wanted());
        reads.cancel_drawing("B");
        assert!(!reads.refresh_wanted());
        assert!(!reads.switch_pending("A"));
        assert_eq!(reads.next(false, now), None);
    }

    #[test]
    fn a_resync_is_not_a_known_switch() {
        let mut reads = ReadSchedule::default();
        reads.want(LayerRead::Active);
        assert!(!reads.switch_pending("A"));
        assert_eq!(reads.showing_next("A"), "A");
    }

    #[test]
    fn events_are_planned_against_the_drawing_being_switched_to() {
        let mut reads = ReadSchedule::default();
        assert_eq!(reads.showing_next("A"), "A");
        reads.want(LayerRead::Drawing("B".into()));
        assert_eq!(reads.showing_next("A"), "B");
        // B's layers change while its read is pending: refresh B, not a switch.
        let plan = plan_feed_events(reads.showing_next("A"), &[event("LayersChanged", "B")]);
        assert!(plan.refresh_current && plan.switch_to.is_none());
        // Going back to A while B is pending is a switch to A.
        let plan = plan_feed_events(reads.showing_next("A"), &[event("DrawingActivated", "A")]);
        assert_eq!(plan.switch_to.as_deref(), Some("A"));
    }

    #[test]
    fn a_different_id_is_a_switch_but_an_unidentified_snapshot_is_adopted() {
        assert!(is_switch("A", false, "B"));
        assert!(!is_switch("A", false, "A"));
        assert!(is_switch("", true, "B"), "from No drawing is open");
        assert!(
            !is_switch("", false, "B"),
            "snapshot had no id: keep its edits"
        );
    }

    #[test]
    fn layer_lists_compare_as_sets() {
        let names = |list: &[&str]| list.iter().map(|s| s.to_string()).collect::<Vec<_>>();
        let empty: HashSet<String> = ["E".to_string()].into_iter().collect();
        let sources = names(&["A", "B", "E"]);
        assert!(!layers_differ(
            &sources,
            &empty,
            &names(&["B", "E", "A"]),
            &names(&["E"])
        ));
        assert!(layers_differ(
            &sources,
            &empty,
            &names(&["A", "B"]),
            &names(&["E"])
        ));
        assert!(layers_differ(&sources, &empty, &sources, &[]));
    }

    #[test]
    fn the_feed_pauses_only_when_minimized_with_nothing_pending() {
        assert!(feed_should_pause(Some(true), false));
        assert!(!feed_should_pause(Some(false), false));
        assert!(!feed_should_pause(None, false));
    }

    #[test]
    fn the_feed_never_pauses_while_connections_are_pending() {
        // Pausing would stop the check-ins, so AutoCAD would let a close through.
        assert!(!feed_should_pause(Some(true), true));
        assert!(!feed_should_pause(Some(false), true));
        assert!(!feed_should_pause(None, true));
    }

    #[test]
    fn only_a_resync_rereads_the_active_drawing() {
        assert_eq!(
            feed_status_read(&FeedMessage::Resync),
            Some(LayerRead::Active)
        );
        // A reconnect after a busy spell re-reads nothing: missed events arrive
        // through `since`, and a restarted connector answers with a reset.
        assert_eq!(feed_status_read(&FeedMessage::Up), None);
        assert_eq!(feed_status_read(&FeedMessage::Down("gone".into())), None);
        assert_eq!(feed_status_read(&FeedMessage::Events(Vec::new())), None);
    }

    #[test]
    fn apply_is_blocked_with_no_drawing_or_during_a_switch() {
        assert_eq!(blocked_action_message(false, false), None);
        assert_eq!(
            blocked_action_message(true, false),
            Some("No drawing is open.")
        );
        let message = blocked_action_message(false, true).unwrap();
        assert!(message.contains("switched to another drawing"), "{message}");
    }
}
