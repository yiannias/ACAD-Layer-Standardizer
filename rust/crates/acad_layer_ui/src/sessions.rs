use acad_layer_ipc::PendingEntry;
use std::collections::HashMap;

type Overrides = HashMap<String, Option<String>>;

/// One drawing's unapplied manual connections plus its undo/redo history.
#[derive(Default, Clone)]
pub struct EditState {
    pub overrides: Overrides,
    pub undo: Vec<Overrides>,
    pub redo: Vec<Overrides>,
}

impl EditState {
    pub fn pending_count(&self) -> usize {
        self.overrides.len()
    }
}

/// Edit states of drawings that are open but not currently shown.
#[derive(Default)]
pub struct DrawingSessions {
    entries: HashMap<String, (String, EditState)>,
}

impl DrawingSessions {
    pub fn stash(&mut self, id: &str, name: &str, state: EditState) {
        if state.pending_count() == 0 {
            self.entries.remove(id);
        } else {
            self.entries
                .insert(id.to_string(), (name.to_string(), state));
        }
    }

    pub fn take(&mut self, id: &str) -> EditState {
        self.entries
            .remove(id)
            .map(|(_, state)| state)
            .unwrap_or_default()
    }

    pub fn forget(&mut self, id: &str) {
        self.entries.remove(id);
    }

    pub fn forget_all(&mut self) {
        self.entries.clear();
    }

    /// (name, count) of a stashed drawing with unapplied connections.
    pub fn pending_of(&self, id: &str) -> Option<(String, usize)> {
        self.entries
            .get(id)
            .filter(|(_, state)| state.pending_count() > 0)
            .map(|(name, state)| (name.clone(), state.pending_count()))
    }

    /// Applies `retain_valid_targets` to every stashed drawing (a new standard was
    /// loaded); drawings left with no connections are dropped.
    pub fn retain_valid_targets(&mut self, targets: &[String]) {
        for (_, state) in self.entries.values_mut() {
            retain_valid_targets(state, targets);
        }
        self.entries
            .retain(|_, (_, state)| state.pending_count() > 0);
    }

    /// Pending counts for every drawing: stashed ones plus the current one.
    pub fn pending_report(&self, current: Option<(&str, usize)>) -> Vec<PendingEntry> {
        let mut report: Vec<PendingEntry> = self
            .entries
            .iter()
            .filter(|(id, _)| current.is_none_or(|(cur, _)| cur != id.as_str()))
            .filter(|(_, (_, state))| state.pending_count() > 0)
            .map(|(id, (_, state))| PendingEntry {
                drawing_id: id.clone(),
                count: state.pending_count(),
            })
            .collect();
        if let Some((id, count)) = current {
            if count > 0 {
                report.push(PendingEntry {
                    drawing_id: id.to_string(),
                    count,
                });
            }
        }
        report
    }

    /// (name, count) of stashed drawings other than the current one, sorted by name.
    pub fn others_pending(&self, current_id: Option<&str>) -> Vec<(String, usize)> {
        let mut others: Vec<(String, usize)> = self
            .entries
            .iter()
            .filter(|(id, _)| current_id != Some(id.as_str()))
            .filter(|(_, (_, state))| state.pending_count() > 0)
            .map(|(_, (name, state))| (name.clone(), state.pending_count()))
            .collect();
        others.sort();
        others
    }

    pub fn footer_note(&self, current_id: Option<&str>) -> Option<String> {
        let others = self.others_pending(current_id);
        match others.as_slice() {
            [] => None,
            [(name, 1)] => Some(format!("{name} has 1 unapplied connection")),
            [(name, count)] => Some(format!("{name} has {count} unapplied connections")),
            many => Some(format!(
                "{} other drawings have unapplied connections",
                many.len()
            )),
        }
    }
}

/// Removes overrides whose source layer no longer exists; returns how many were dropped.
pub fn drop_missing_sources(overrides: &mut Overrides, source_layers: &[String]) -> usize {
    let present: std::collections::HashSet<String> = source_layers
        .iter()
        .map(|name| name.to_lowercase())
        .collect();
    let before = overrides.len();
    overrides.retain(|source, _| present.contains(&source.to_lowercase()));
    before - overrides.len()
}

/// Removes connections to targets the loaded standard no longer has, from the
/// connections and from every undo/redo snapshot; explicit disconnects (`None`) stay.
/// Names compare ignoring ASCII case, like `MappingEditor::retain_valid_targets`.
pub fn retain_valid_targets(state: &mut EditState, targets: &[String]) {
    let names: std::collections::HashSet<String> = targets
        .iter()
        .map(|name| name.to_ascii_lowercase())
        .collect();
    let keep = |overrides: &mut Overrides| {
        overrides.retain(|_, target| {
            target
                .as_ref()
                .is_none_or(|name| names.contains(&name.to_ascii_lowercase()))
        });
    };
    keep(&mut state.overrides);
    state.undo.iter_mut().for_each(keep);
    state.redo.iter_mut().for_each(keep);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn invalid_targets_are_removed_from_connections_and_history() {
        let mut edit = state(&[("A", Some("t-wall")), ("B", Some("GONE")), ("C", None)]);
        edit.undo
            .push(state(&[("B", Some("Gone")), ("A", Some("T-WALL"))]).overrides);
        edit.redo
            .push(state(&[("C", Some("gone")), ("D", None)]).overrides);
        retain_valid_targets(&mut edit, &["T-WALL".to_string()]);
        assert_eq!(
            edit.overrides,
            state(&[("A", Some("t-wall")), ("C", None)]).overrides
        );
        assert_eq!(edit.undo, vec![state(&[("A", Some("T-WALL"))]).overrides]);
        assert_eq!(edit.redo, vec![state(&[("D", None)]).overrides]);
    }

    #[test]
    fn a_new_standard_filters_stashed_drawings_and_drops_emptied_ones() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", state(&[("A", Some("GONE"))]));
        sessions.stash(
            "id2",
            "Baths.dwg",
            state(&[("A", Some("T-WALL")), ("B", Some("GONE"))]),
        );
        sessions.retain_valid_targets(&["T-WALL".to_string()]);
        assert_eq!(
            sessions.others_pending(None),
            vec![("Baths.dwg".to_string(), 1)]
        );
        assert_eq!(sessions.take("id1").pending_count(), 0);
    }

    fn state(pairs: &[(&str, Option<&str>)]) -> EditState {
        EditState {
            overrides: pairs
                .iter()
                .map(|(k, v)| (k.to_string(), v.map(str::to_string)))
                .collect(),
            ..EditState::default()
        }
    }

    #[test]
    fn stash_then_take_restores_the_same_state() {
        let mut sessions = DrawingSessions::default();
        let mut original = state(&[("A", Some("B")), ("C", None)]);
        original.undo.push(HashMap::new());
        original.redo.push(state(&[("A", None)]).overrides);
        sessions.stash("id1", "Beds.dwg", original.clone());
        let taken = sessions.take("id1");
        assert_eq!(taken.overrides, original.overrides);
        assert_eq!(taken.undo, original.undo);
        assert_eq!(taken.redo, original.redo);
        assert_eq!(sessions.take("id1").pending_count(), 0);
    }

    #[test]
    fn an_unseen_drawing_gets_a_fresh_state() {
        let mut sessions = DrawingSessions::default();
        let taken = sessions.take("nope");
        assert!(taken.overrides.is_empty() && taken.undo.is_empty() && taken.redo.is_empty());
    }

    #[test]
    fn empty_states_are_not_stashed() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", EditState::default());
        assert!(sessions.others_pending(None).is_empty());
        assert!(sessions.pending_report(None).is_empty());
    }

    #[test]
    fn pending_report_includes_the_current_drawing_and_stashed_ones() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", state(&[("A", None), ("B", None)]));
        let mut report = sessions.pending_report(Some(("id2", 3)));
        report.sort_by(|a, b| a.drawing_id.cmp(&b.drawing_id));
        assert_eq!(report.len(), 2);
        assert_eq!((report[0].drawing_id.as_str(), report[0].count), ("id1", 2));
        assert_eq!((report[1].drawing_id.as_str(), report[1].count), ("id2", 3));
        assert_eq!(sessions.pending_report(Some(("id2", 0))).len(), 1);
    }

    #[test]
    fn pending_of_names_a_stashed_drawing_and_its_count() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", state(&[("A", None), ("B", None)]));
        assert_eq!(
            sessions.pending_of("id1"),
            Some(("Beds.dwg".to_string(), 2))
        );
        assert_eq!(sessions.pending_of("nope"), None);
    }

    #[test]
    fn forget_all_drops_every_stashed_drawing() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", state(&[("A", None)]));
        sessions.stash("id2", "Baths.dwg", state(&[("A", None)]));
        sessions.forget_all();
        assert!(sessions.pending_report(None).is_empty());
    }

    #[test]
    fn forget_removes_a_stashed_drawing() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", state(&[("A", None)]));
        sessions.forget("id1");
        assert!(sessions.pending_report(None).is_empty());
    }

    #[test]
    fn footer_note_for_one_other_drawing() {
        let mut sessions = DrawingSessions::default();
        sessions.stash(
            "id1",
            "Beds.dwg",
            state(&[("A", None), ("B", None), ("C", None)]),
        );
        assert_eq!(
            sessions.footer_note(Some("cur")).as_deref(),
            Some("Beds.dwg has 3 unapplied connections")
        );
        sessions.stash("id1", "Beds.dwg", state(&[("A", None)]));
        assert_eq!(
            sessions.footer_note(Some("cur")).as_deref(),
            Some("Beds.dwg has 1 unapplied connection")
        );
    }

    #[test]
    fn footer_note_for_several_drawings() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Beds.dwg", state(&[("A", None)]));
        sessions.stash("id2", "Baths.dwg", state(&[("A", None)]));
        assert_eq!(
            sessions.footer_note(Some("cur")).as_deref(),
            Some("2 other drawings have unapplied connections")
        );
    }

    #[test]
    fn footer_note_is_none_when_only_the_current_drawing_is_pending() {
        let mut sessions = DrawingSessions::default();
        assert_eq!(sessions.footer_note(Some("cur")), None);
        sessions.stash("cur", "Beds.dwg", state(&[("A", None)]));
        assert_eq!(sessions.footer_note(Some("cur")), None);
    }

    #[test]
    fn others_pending_is_sorted_by_name() {
        let mut sessions = DrawingSessions::default();
        sessions.stash("id1", "Zed.dwg", state(&[("A", None)]));
        sessions.stash("id2", "Abe.dwg", state(&[("A", None), ("B", None)]));
        assert_eq!(
            sessions.others_pending(None),
            vec![("Abe.dwg".to_string(), 2), ("Zed.dwg".to_string(), 1)]
        );
    }

    #[test]
    fn switching_away_and_back_restores_connections_and_drops_deleted_layers() {
        let mut sessions = DrawingSessions::default();
        let mut a = state(&[("WALL", Some("A-WALL")), ("OLD", Some("A-DOOR"))]);
        a.undo.push(HashMap::new());
        // Switch A -> B: A is stashed, B starts fresh.
        sessions.stash("A", "Beds.dwg", a);
        let b = sessions.take("B");
        assert_eq!(b.pending_count(), 0);
        assert_eq!(
            sessions.footer_note(Some("B")).as_deref(),
            Some("Beds.dwg has 2 unapplied connections")
        );
        // Switch back B -> A; "OLD" was deleted in A meanwhile.
        sessions.stash("B", "Baths.dwg", b);
        let mut back = sessions.take("A");
        let dropped = drop_missing_sources(&mut back.overrides, &["wall".to_string()]);
        assert_eq!(dropped, 1);
        assert_eq!(
            back.overrides.get("WALL"),
            Some(&Some("A-WALL".to_string()))
        );
        assert_eq!(back.undo.len(), 1);
        assert_eq!(sessions.footer_note(Some("A")), None);
        assert!(sessions.pending_report(None).is_empty());
    }

    #[test]
    fn drop_missing_sources_is_case_insensitive_and_counts() {
        let mut overrides: HashMap<String, Option<String>> = HashMap::new();
        overrides.insert("LAYER1".into(), Some("T".into()));
        overrides.insert("Gone".into(), None);
        let dropped = drop_missing_sources(&mut overrides, &["layer1".to_string()]);
        assert_eq!(dropped, 1);
        assert!(overrides.contains_key("LAYER1"));
        assert!(!overrides.contains_key("Gone"));
    }
}
