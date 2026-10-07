use crate::{poll_events, FeedEvent, IpcResponse, PendingEntry};
use std::{
    sync::{
        atomic::{AtomicBool, Ordering},
        mpsc, Arc, Mutex,
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
    /// Closed (sender dropped) when the feed thread has finished, last poll included.
    done: Option<mpsc::Receiver<()>>,
}

impl FeedHandle {
    pub fn stop(&self) {
        self.stop.store(true, Ordering::SeqCst);
    }

    pub fn set_paused(&self, paused: bool) {
        self.paused.store(paused, Ordering::SeqCst);
    }

    /// Stops the feed and waits at most `cap` for its last (empty) report to go
    /// out. Returns whether the thread finished in time.
    pub fn stop_and_wait(&mut self, cap: Duration) -> bool {
        self.stop();
        match self.done.take() {
            Some(done) => matches!(
                done.recv_timeout(cap),
                Ok(()) | Err(mpsc::RecvTimeoutError::Disconnected)
            ),
            None => true,
        }
    }
}

/// The poll loop (about one poll per second) until `stop` is set. Then the window
/// is going away and nothing it holds can be applied any more, so the shared
/// report is cleared and, unless AutoCAD is already unreachable, one last poll
/// tells the connector that nothing is pending: a drawing close right after the
/// window is gone is then not refused on the strength of a stale report.
fn run_feed<P, F>(
    pending: &Mutex<Vec<PendingEntry>>,
    stop: &AtomicBool,
    paused: &AtomicBool,
    mut poll: P,
    deliver: F,
) where
    P: FnMut(Option<u64>, Vec<PendingEntry>) -> Result<IpcResponse, String>,
    F: Fn(FeedMessage),
{
    let mut state = FeedState::default();
    while !stop.load(Ordering::SeqCst) {
        if !paused.load(Ordering::SeqCst) {
            let snapshot = pending
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner())
                .clone();
            let outcome = poll(state.since, snapshot);
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
    pending
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
        .clear();
    if state.connected {
        let _ = poll(state.since, Vec::new());
    }
}

/// `spawn_feed` with the poll function injected (tests).
fn spawn_feed_with<P, F>(pending: Arc<Mutex<Vec<PendingEntry>>>, poll: P, deliver: F) -> FeedHandle
where
    P: FnMut(Option<u64>, Vec<PendingEntry>) -> Result<IpcResponse, String> + Send + 'static,
    F: Fn(FeedMessage) + Send + 'static,
{
    let stop = Arc::new(AtomicBool::new(false));
    let paused = Arc::new(AtomicBool::new(false));
    let (done_tx, done) = mpsc::channel();
    let handle = FeedHandle {
        stop: stop.clone(),
        paused: paused.clone(),
        done: Some(done),
    };
    thread::spawn(move || {
        run_feed(&pending, &stop, &paused, poll, deliver);
        let _ = done_tx.send(());
    });
    handle
}

/// Starts the background poll loop (about one poll per second).
pub fn spawn_feed<F: Fn(FeedMessage) + Send + 'static>(
    pending: Arc<Mutex<Vec<PendingEntry>>>,
    deliver: F,
) -> FeedHandle {
    spawn_feed_with(pending, poll_events, deliver)
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

    fn entry(id: &str, count: usize) -> PendingEntry {
        PendingEntry {
            drawing_id: id.into(),
            count,
        }
    }

    #[test]
    fn stopping_clears_the_report_and_sends_one_last_empty_poll() {
        let pending = Mutex::new(vec![entry("A", 2)]);
        let stop = Arc::new(AtomicBool::new(false));
        let paused = AtomicBool::new(false);
        let mut calls: Vec<(Option<u64>, Vec<(String, usize)>)> = Vec::new();
        let stopper = stop.clone();
        run_feed(
            &pending,
            &stop,
            &paused,
            |since, report| {
                calls.push((
                    since,
                    report
                        .into_iter()
                        .map(|e| (e.drawing_id, e.count))
                        .collect(),
                ));
                stopper.store(true, Ordering::SeqCst);
                events(4, false, 0)
            },
            |_| {},
        );
        assert_eq!(
            calls,
            vec![(None, vec![("A".to_string(), 2)]), (Some(4), Vec::new()),]
        );
        assert!(pending.lock().unwrap().is_empty());
    }

    #[test]
    fn a_disconnected_feed_stops_without_a_last_poll() {
        let pending = Mutex::new(vec![entry("A", 2)]);
        let stop = Arc::new(AtomicBool::new(false));
        let paused = AtomicBool::new(false);
        let mut calls = 0;
        let stopper = stop.clone();
        run_feed(
            &pending,
            &stop,
            &paused,
            |_, _| {
                calls += 1;
                stopper.store(true, Ordering::SeqCst);
                Err("gone".into())
            },
            |_| {},
        );
        assert_eq!(calls, 1, "AutoCAD is gone: no last poll");
        assert!(pending.lock().unwrap().is_empty());
    }

    #[test]
    fn stop_and_wait_gives_up_after_the_cap_when_the_last_poll_hangs() {
        let pending = Arc::new(Mutex::new(Vec::new()));
        let polls = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let counter = polls.clone();
        let mut handle = spawn_feed_with(
            pending,
            move |_, _| {
                if counter.fetch_add(1, Ordering::SeqCst) > 0 {
                    thread::sleep(Duration::from_secs(5));
                }
                events(1, false, 0)
            },
            |_| {},
        );
        while polls.load(Ordering::SeqCst) == 0 {
            thread::sleep(Duration::from_millis(5));
        }
        let started = std::time::Instant::now();
        assert!(!handle.stop_and_wait(Duration::from_millis(100)));
        assert!(started.elapsed() < Duration::from_secs(2));
    }

    #[test]
    fn stop_and_wait_reports_a_finished_last_poll() {
        let pending = Arc::new(Mutex::new(vec![entry("A", 1)]));
        let mut handle = spawn_feed_with(pending.clone(), |_, _| events(1, false, 0), |_| {});
        assert!(handle.stop_and_wait(Duration::from_secs(5)));
        assert!(pending.lock().unwrap().is_empty());
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
