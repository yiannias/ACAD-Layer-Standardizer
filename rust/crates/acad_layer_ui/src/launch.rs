//! The window's command-line arguments. Unknown or malformed arguments are ignored:
//! a bad launch argument must never stop the window from opening.

#[derive(Debug, Default, PartialEq, Eq)]
pub struct LaunchArgs {
    /// `--owner-hwnd=<decimal>`: AutoCAD's main window, which owns this one.
    pub owner_hwnd: Option<isize>,
    /// `--first-run-notice`: show the one-time "memory moved" notice.
    pub first_run_notice: bool,
}

pub fn parse_launch_args(args: impl Iterator<Item = String>) -> LaunchArgs {
    let mut parsed = LaunchArgs::default();
    for arg in args {
        if arg == "--first-run-notice" {
            parsed.first_run_notice = true;
        } else if let Some(value) = arg.strip_prefix("--owner-hwnd=") {
            if parsed.owner_hwnd.is_none() {
                parsed.owner_hwnd = value.parse::<isize>().ok();
            }
        }
    }
    parsed
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parse(args: &[&str]) -> LaunchArgs {
        parse_launch_args(args.iter().map(|a| a.to_string()))
    }

    #[test]
    fn parses_owner_and_notice() {
        let args = parse(&["--owner-hwnd=12345", "--first-run-notice"]);
        assert_eq!(args.owner_hwnd, Some(12345));
        assert!(args.first_run_notice);
        let none = parse(&[]);
        assert_eq!(none.owner_hwnd, None);
        assert!(!none.first_run_notice);
    }

    #[test]
    fn ignores_unknown_arguments() {
        let args = parse(&["--bogus", "--owner-hwnd=7", "positional", "--x=1"]);
        assert_eq!(args.owner_hwnd, Some(7));
        assert!(!args.first_run_notice);
    }

    #[test]
    fn the_first_valid_owner_wins() {
        let args = parse(&["--owner-hwnd=abc", "--owner-hwnd=5", "--owner-hwnd=9"]);
        assert_eq!(args.owner_hwnd, Some(5));
    }

    #[test]
    fn an_invalid_owner_is_ignored() {
        assert_eq!(parse(&["--owner-hwnd=abc"]).owner_hwnd, None);
        assert_eq!(parse(&["--owner-hwnd="]).owner_hwnd, None);
        assert_eq!(
            parse(&["--owner-hwnd=abc", "--first-run-notice"]).owner_hwnd,
            None
        );
    }
}
