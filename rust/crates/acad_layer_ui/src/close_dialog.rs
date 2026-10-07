//! Pure decisions for the Apply / Discard / Cancel dialog shown when unapplied
//! connections would be lost: the window's own close, or a drawing close or quit
//! the connector blocked (no egui, no IPC calls).

use acad_layer_ipc::PendingEntry;
use std::collections::VecDeque;

/// Why the dialog is shown.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CloseReason {
    /// The window's own close button (or Alt+F4); no connector involvement.
    WindowClose,
    /// AutoCAD blocked closing this drawing (by id).
    DrawingClose(String),
    /// AutoCAD blocked quitting.
    Quit,
}

/// The user's answer.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CloseChoice {
    Apply,
    Discard,
    Cancel,
}

/// "1 unapplied connection" / "3 unapplied connections".
fn connections(count: usize) -> String {
    if count == 1 {
        "1 unapplied connection".to_string()
    } else {
        format!("{count} unapplied connections")
    }
}

/// The dialog's message: names the drawing and its count, asks what to do, and
/// lists the other drawings whose connections would also be lost.
pub fn dialog_text(
    reason: &CloseReason,
    drawing_name: &str,
    count: usize,
    others: &[(String, usize)],
) -> String {
    let action = match reason {
        CloseReason::WindowClose => "this window closes",
        CloseReason::DrawingClose(_) => "AutoCAD closes the drawing",
        CloseReason::Quit => "AutoCAD quits",
    };
    let mut text = if count > 0 {
        format!(
            "{drawing_name} has {}. Apply or discard {} before {action}?",
            connections(count),
            if count == 1 { "it" } else { "them" }
        )
    } else {
        format!("Unapplied connections will be lost when {action}.")
    };
    if !others.is_empty() {
        text.push_str("\n\nThese drawings would also lose their unapplied connections:");
        for (name, count) in others {
            text.push_str(&format!("\n• {name} ({})", connections(*count)));
        }
    }
    text
}

/// `dialog_text`, plus a note when Apply is not offered.
pub fn dialog_message(
    reason: &CloseReason,
    drawing_name: &str,
    count: usize,
    others: &[(String, usize)],
    apply_offered: bool,
) -> String {
    let mut text = dialog_text(reason, drawing_name, count, others);
    if !apply_offered && count > 0 {
        text.push_str(
            "\n\nApply is not available because this window is not showing that drawing.",
        );
    }
    text
}

/// Whether the window's own close is held for the dialog.
pub fn should_intercept_close(unapplied: usize) -> bool {
    unapplied > 0
}

/// Other drawings whose connections the close would lose: none for a drawing close
/// (only that drawing goes away), all of them for the window or a quit.
pub fn others_at_risk(reason: &CloseReason, others: Vec<(String, usize)>) -> Vec<(String, usize)> {
    match reason {
        CloseReason::DrawingClose(_) => Vec::new(),
        CloseReason::WindowClose | CloseReason::Quit => others,
    }
}

/// Apply is offered only for the displayed drawing, and only with something to apply.
/// `count` is the affected drawing's unapplied count.
pub fn can_apply(reason: &CloseReason, displayed_id: &str, count: usize) -> bool {
    if displayed_id.is_empty() || count == 0 {
        return false;
    }
    match reason {
        CloseReason::DrawingClose(id) => id == displayed_id,
        CloseReason::WindowClose | CloseReason::Quit => true,
    }
}

/// The reason for a `CloseBlocked` event's `kind` and `drawing_id`.
pub fn blocked_reason(kind: &str, drawing_id: &str) -> Option<CloseReason> {
    match kind {
        "drawing" if !drawing_id.is_empty() => {
            Some(CloseReason::DrawingClose(drawing_id.to_string()))
        }
        "quit" => Some(CloseReason::Quit),
        _ => None,
    }
}

/// The `replay_close` kind and drawing id for a blocked close (none for the window).
pub fn replay_target(reason: &CloseReason, displayed_id: &str) -> Option<(String, String)> {
    match reason {
        CloseReason::DrawingClose(id) => Some(("drawing".to_string(), id.clone())),
        CloseReason::Quit => Some(("quit".to_string(), displayed_id.to_string())),
        CloseReason::WindowClose => None,
    }
}

/// The pending report once the user applied or discarded: the closing drawing drops
/// out; a quit or the window closing leaves nothing pending.
pub fn pending_after_resolving(
    mut report: Vec<PendingEntry>,
    reason: &CloseReason,
) -> Vec<PendingEntry> {
    match reason {
        CloseReason::DrawingClose(id) => report.retain(|entry| entry.drawing_id != *id),
        CloseReason::WindowClose | CloseReason::Quit => report.clear(),
    }
    report
}

/// One dialog at a time. A repeat of the shown or a queued reason is ignored; a
/// blocked close or quit replaces an open window-close dialog (it is the user's
/// AutoCAD action that is waiting); a window close during a blocked-close dialog is
/// ignored; anything else waits its turn.
#[derive(Debug, Default)]
pub struct CloseQueue {
    current: Option<CloseReason>,
    queued: VecDeque<CloseReason>,
}

impl CloseQueue {
    /// Returns whether the reason was taken (shown now or queued).
    pub fn offer(&mut self, reason: CloseReason) -> bool {
        if self.current.as_ref() == Some(&reason) || self.queued.contains(&reason) {
            return false;
        }
        match &self.current {
            None => self.current = Some(reason),
            Some(_) if reason == CloseReason::WindowClose => return false,
            Some(CloseReason::WindowClose) => self.current = Some(reason),
            Some(_) => self.queued.push_back(reason),
        }
        true
    }

    pub fn current(&self) -> Option<&CloseReason> {
        self.current.as_ref()
    }

    /// The shown dialog was answered: show the next one, if any.
    pub fn resolve(&mut self) {
        self.current = self.queued.pop_front();
    }

    /// The drawing closed: its dialogs are moot.
    pub fn forget_drawing(&mut self, drawing_id: &str) {
        let is_it = |reason: &CloseReason| matches!(reason, CloseReason::DrawingClose(id) if id == drawing_id);
        self.queued.retain(|reason| !is_it(reason));
        if self.current.as_ref().is_some_and(is_it) {
            self.resolve();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn others(list: &[(&str, usize)]) -> Vec<(String, usize)> {
        list.iter().map(|(n, c)| (n.to_string(), *c)).collect()
    }

    fn entry(id: &str, count: usize) -> PendingEntry {
        PendingEntry {
            drawing_id: id.into(),
            count,
        }
    }

    #[test]
    fn dialog_text_names_the_drawing_and_count() {
        let text = dialog_text(&CloseReason::WindowClose, "Beds.dwg", 3, &[]);
        assert!(text.contains("Beds.dwg"), "{text}");
        assert!(text.contains("3 unapplied connections"), "{text}");
        let text = dialog_text(&CloseReason::DrawingClose("d1".into()), "Beds.dwg", 1, &[]);
        assert!(text.contains("1 unapplied connection."), "{text}");
        assert!(!text.contains("connections"), "{text}");
        let text = dialog_text(&CloseReason::Quit, "Beds.dwg", 2, &[]);
        assert!(text.contains("quit"), "{text}");
    }

    #[test]
    fn dialog_text_lists_other_drawings_that_would_lose_connections() {
        let text = dialog_text(
            &CloseReason::WindowClose,
            "Beds.dwg",
            2,
            &others(&[("Abe.dwg", 1), ("Zed.dwg", 4)]),
        );
        assert!(text.contains("would also lose"), "{text}");
        assert!(text.contains("Abe.dwg (1 unapplied connection)"), "{text}");
        assert!(text.contains("Zed.dwg (4 unapplied connections)"), "{text}");
        let text = dialog_text(&CloseReason::WindowClose, "Beds.dwg", 2, &[]);
        assert!(!text.contains("would also lose"), "{text}");
    }

    #[test]
    fn a_quit_with_nothing_pending_in_the_displayed_drawing_lists_only_the_others() {
        let text = dialog_text(
            &CloseReason::Quit,
            "Beds.dwg",
            0,
            &others(&[("Abe.dwg", 1)]),
        );
        assert!(!text.contains("Beds.dwg"), "{text}");
        assert!(text.contains("Abe.dwg (1 unapplied connection)"), "{text}");
    }

    #[test]
    fn a_window_close_with_nothing_pending_is_not_intercepted() {
        assert!(!should_intercept_close(0));
        assert!(should_intercept_close(1));
        assert!(should_intercept_close(12));
    }

    #[test]
    fn a_drawing_close_puts_no_other_drawing_at_risk() {
        let list = others(&[("Abe.dwg", 1)]);
        assert!(others_at_risk(&CloseReason::DrawingClose("d1".into()), list.clone()).is_empty());
        assert_eq!(
            others_at_risk(&CloseReason::WindowClose, list.clone()),
            list
        );
        assert_eq!(others_at_risk(&CloseReason::Quit, list.clone()), list);
    }

    #[test]
    fn the_message_says_when_apply_is_not_offered() {
        let reason = CloseReason::DrawingClose("d2".into());
        let with = dialog_message(&reason, "Baths.dwg", 2, &[], false);
        assert!(with.starts_with(&dialog_text(&reason, "Baths.dwg", 2, &[])));
        assert!(with.contains("Apply is not available"), "{with}");
        let without = dialog_message(&reason, "Baths.dwg", 2, &[], true);
        assert_eq!(without, dialog_text(&reason, "Baths.dwg", 2, &[]));
    }

    #[test]
    fn apply_is_offered_only_for_the_displayed_drawing_with_connections() {
        let close = |id: &str| CloseReason::DrawingClose(id.into());
        assert!(can_apply(&close("A"), "A", 2));
        assert!(!can_apply(&close("B"), "A", 2), "B is not displayed");
        assert!(!can_apply(&close("A"), "A", 0));
        assert!(can_apply(&CloseReason::WindowClose, "A", 1));
        assert!(can_apply(&CloseReason::Quit, "A", 1));
        assert!(!can_apply(&CloseReason::Quit, "A", 0));
        assert!(
            !can_apply(&CloseReason::WindowClose, "", 1),
            "no drawing shown"
        );
    }

    #[test]
    fn blocked_events_map_to_reasons() {
        assert_eq!(
            blocked_reason("drawing", "d1"),
            Some(CloseReason::DrawingClose("d1".into()))
        );
        assert_eq!(blocked_reason("drawing", ""), None);
        assert_eq!(blocked_reason("quit", ""), Some(CloseReason::Quit));
        assert_eq!(blocked_reason("quit", "d1"), Some(CloseReason::Quit));
        assert_eq!(blocked_reason("other", "d1"), None);
    }

    #[test]
    fn replay_targets_follow_the_reason() {
        assert_eq!(
            replay_target(&CloseReason::DrawingClose("d1".into()), "d2"),
            Some(("drawing".to_string(), "d1".to_string()))
        );
        assert_eq!(
            replay_target(&CloseReason::Quit, "d2"),
            Some(("quit".to_string(), "d2".to_string()))
        );
        assert_eq!(replay_target(&CloseReason::WindowClose, "d2"), None);
    }

    #[test]
    fn the_pending_report_drops_what_the_close_resolves() {
        let report = vec![entry("A", 2), entry("B", 1)];
        let left = pending_after_resolving(report.clone(), &CloseReason::DrawingClose("A".into()));
        assert_eq!(
            left.iter()
                .map(|e| (e.drawing_id.as_str(), e.count))
                .collect::<Vec<_>>(),
            vec![("B", 1)]
        );
        assert!(pending_after_resolving(report.clone(), &CloseReason::Quit).is_empty());
        assert!(pending_after_resolving(report, &CloseReason::WindowClose).is_empty());
    }

    #[test]
    fn a_repeated_block_for_the_shown_drawing_does_not_stack_or_reset() {
        let mut queue = CloseQueue::default();
        assert!(queue.offer(CloseReason::DrawingClose("A".into())));
        assert!(!queue.offer(CloseReason::DrawingClose("A".into())));
        assert_eq!(
            queue.current(),
            Some(&CloseReason::DrawingClose("A".into()))
        );
        queue.resolve();
        assert_eq!(queue.current(), None, "nothing was stacked behind it");
    }

    #[test]
    fn a_block_for_another_drawing_waits_its_turn() {
        let mut queue = CloseQueue::default();
        queue.offer(CloseReason::DrawingClose("A".into()));
        assert!(queue.offer(CloseReason::DrawingClose("B".into())));
        assert!(
            !queue.offer(CloseReason::DrawingClose("B".into())),
            "queued once"
        );
        assert_eq!(
            queue.current(),
            Some(&CloseReason::DrawingClose("A".into()))
        );
        queue.resolve();
        assert_eq!(
            queue.current(),
            Some(&CloseReason::DrawingClose("B".into()))
        );
        queue.resolve();
        assert_eq!(queue.current(), None);
    }

    #[test]
    fn a_blocked_close_replaces_an_open_window_close_dialog() {
        let mut queue = CloseQueue::default();
        queue.offer(CloseReason::WindowClose);
        assert!(queue.offer(CloseReason::DrawingClose("A".into())));
        assert_eq!(
            queue.current(),
            Some(&CloseReason::DrawingClose("A".into()))
        );
        queue.resolve();
        assert_eq!(
            queue.current(),
            None,
            "the window close is not brought back"
        );
    }

    #[test]
    fn a_window_close_during_a_blocked_close_dialog_is_ignored() {
        let mut queue = CloseQueue::default();
        queue.offer(CloseReason::DrawingClose("A".into()));
        assert!(!queue.offer(CloseReason::WindowClose));
        queue.resolve();
        assert_eq!(queue.current(), None);
    }

    #[test]
    fn a_closed_drawing_drops_its_dialogs() {
        let mut queue = CloseQueue::default();
        queue.offer(CloseReason::DrawingClose("A".into()));
        queue.offer(CloseReason::DrawingClose("B".into()));
        queue.forget_drawing("B");
        queue.forget_drawing("A");
        assert_eq!(queue.current(), None);
        queue.offer(CloseReason::DrawingClose("C".into()));
        queue.offer(CloseReason::DrawingClose("D".into()));
        queue.forget_drawing("C");
        assert_eq!(
            queue.current(),
            Some(&CloseReason::DrawingClose("D".into()))
        );
    }
}
