use crate::{poll_events, FeedEvent, IpcResponse, PendingEntry};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    thread,
    time::Duration,
};

/// What the feed thread reports to the window.
#[derive(Debug, Clone)]
pub enum FeedMessage {
    Events(Vec<FeedEvent>),
    Resync,
    Down(String),
    Up,
}

#[derive(Debug, Clone)]
pub struct FeedState {
    pub since: Option<u64>,
    pub connected: bool,
}

impl Default for FeedState {
    fn default() -> Self {
        Self {
            since: None,
            connected: true,
        }
    }
}

/// Folds one poll outcome into the feed state and returns the messages to deliver.
pub fn step(state: &mut FeedState, outcome: Result<IpcResponse, String>) -> Vec<FeedMessage> {
    let mut out = Vec::new();
    match outcome {
        Ok(IpcResponse::Events {
            head,
            reset,
            events,
        }) => {
            if !state.connected {
                state.connected = true;
                out.push(FeedMessage::Up);
            }
            state.since = Some(head);
            if reset {
                out.push(FeedMessage::Resync);
            } else if !events.is_empty() {
                out.push(FeedMessage::Events(events));
            }
        }
        Ok(IpcResponse::Error(error)) | Err(error) => {
            if state.connected {
                state.connected = false;
                out.push(FeedMessage::Down(error));
            }
        }
        Ok(_) => {}
    }
    out
}

pub struct FeedHandle {
    stop: Arc<AtomicBool>,
    paused: Arc<AtomicBool>,
}

impl FeedHandle {
    pub fn stop(&self) {
        self.stop.store(true, Ordering::SeqCst);
    }

    pub fn set_paused(&self, paused: bool) {
        self.paused.store(paused, Ordering::SeqCst);
    }
}

/// Starts the background poll loop (about one poll per second).
pub fn spawn_feed<F: Fn(FeedMessage) + Send + 'static>(
    pending: Arc<Mutex<Vec<PendingEntry>>>,
    deliver: F,
) -> FeedHandle {
    let stop = Arc::new(AtomicBool::new(false));
    let paused = Arc::new(AtomicBool::new(false));
    let handle = FeedHandle {
        stop: stop.clone(),
        paused: paused.clone(),
    };
    thread::spawn(move || {
        let mut state = FeedState::default();
        while !stop.load(Ordering::SeqCst) {
            if !paused.load(Ordering::SeqCst) {
                let snapshot = pending
                    .lock()
                    .unwrap_or_else(|poisoned| poisoned.into_inner())
                    .clone();
                let outcome = poll_events(state.since, snapshot);
                for message in step(&mut state, outcome) {
                    deliver(message);
                }
            }
            // Sleep ~1 second in short slices so stop() takes effect promptly.
            for _ in 0..20 {
                if stop.load(Ordering::SeqCst) {
                    break;
                }
                thread::sleep(Duration::from_millis(50));
            }
        }
    });
    handle
}

#[cfg(test)]
mod tests {
    use super::*;

    fn events(head: u64, reset: bool, n: usize) -> Result<IpcResponse, String> {
        Ok(IpcResponse::Events {
            head,
            reset,
            events: (0..n)
                .map(|i| FeedEvent {
                    seq: head - (n - i) as u64 + 1,
                    kind: "DrawingActivated".into(),
                    payload: serde_json::json!({}),
                })
                .collect(),
        })
    }

    #[test]
    fn first_poll_reset_resyncs_and_records_head() {
        let mut state = FeedState::default();
        let out = step(&mut state, events(5, true, 0));
        assert_eq!(state.since, Some(5));
        assert!(matches!(out.as_slice(), [FeedMessage::Resync]));
    }

    #[test]
    fn new_events_are_delivered_and_since_advances() {
        let mut state = FeedState::default();
        let out = step(&mut state, events(7, false, 2));
        assert_eq!(state.since, Some(7));
        match out.as_slice() {
            [FeedMessage::Events(e)] => assert_eq!(e.len(), 2),
            _ => panic!("expected one Events message"),
        }
    }

    #[test]
    fn empty_events_deliver_nothing() {
        let mut state = FeedState::default();
        let out = step(&mut state, events(7, false, 0));
        assert!(out.is_empty());
        assert_eq!(state.since, Some(7));
    }

    #[test]
    fn a_failure_reports_down_once_and_recovery_reports_up_then_events() {
        let mut state = FeedState::default();
        let out = step(&mut state, Err("gone".into()));
        assert!(matches!(out.as_slice(), [FeedMessage::Down(e)] if e == "gone"));
        assert!(!state.connected);
        assert!(step(&mut state, Err("gone".into())).is_empty());
        assert!(step(&mut state, Ok(IpcResponse::Error("bad".into()))).is_empty());
        let out = step(&mut state, events(9, false, 1));
        assert!(state.connected);
        assert!(matches!(
            out.as_slice(),
            [FeedMessage::Up, FeedMessage::Events(e)] if e.len() == 1
        ));
    }
}
