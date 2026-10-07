//! Pure logic behind the Settings panel. No egui in this file.

// Task 5 wires these items into the UI and removes this allow.
#![allow(dead_code)]

use acad_layer_core::ImportReport;
use std::path::Path;

pub const BETA_NOTICE: &str = "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes.";

#[derive(Default)]
pub struct SettingsPanel {
    pub open: bool,
    pub status: String,
}

pub enum SettingsAction {
    ChangeStandard,
    ChangeMemoryFile,
    ImportMemory,
    ExportMemory,
}

pub struct StandardsFileLabel {
    pub text: String,
    pub missing: bool,
}

/// Last path component, accepting both `\` and `/` so Windows paths work anywhere.
fn file_name(path: &str) -> &str {
    path.rsplit(['\\', '/']).next().unwrap_or(path)
}

pub fn standards_file_label(path: &str, exists: bool) -> StandardsFileLabel {
    if path.trim().is_empty() {
        return StandardsFileLabel {
            text: "No Standards File chosen".to_string(),
            missing: false,
        };
    }
    let name = file_name(path);
    if exists {
        StandardsFileLabel {
            text: name.to_string(),
            missing: false,
        }
    } else {
        StandardsFileLabel {
            text: format!("Unavailable: {name}"),
            missing: true,
        }
    }
}

pub fn import_status(report: &ImportReport) -> String {
    let mappings = if report.imported == 1 {
        "1 mapping".to_string()
    } else {
        format!("{} mappings", report.imported)
    };
    let new = if report.added == 1 {
        "1 was new".to_string()
    } else {
        format!("{} were new", report.added)
    };
    format!("Imported {mappings}. {new}.")
}

pub fn export_status(path: &Path) -> String {
    format!("Exported your memory to {}.", path.display())
}

pub fn about_line(version: &str, build: &str) -> String {
    format!("Layer Standardizer Beta {version} - Build {build}")
}

/// False while an Apply/Purge/Load Standard is in flight.
pub fn settings_actions_enabled(busy: bool) -> bool {
    !busy
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn standards_file_label_covers_empty_existing_and_missing() {
        let l = standards_file_label("", false);
        assert_eq!(l.text, "No Standards File chosen");
        assert!(!l.missing);
        let l = standards_file_label("", true);
        assert_eq!(l.text, "No Standards File chosen");
        assert!(!l.missing);

        let l = standards_file_label(r"C:\Standards\office.json", true);
        assert_eq!(l.text, "office.json");
        assert!(!l.missing);
        let l = standards_file_label("/srv/standards/office.json", true);
        assert_eq!(l.text, "office.json");

        let l = standards_file_label(r"C:\Standards\office.json", false);
        assert_eq!(l.text, "Unavailable: office.json");
        assert!(l.missing);
    }

    #[test]
    fn import_status_uses_singular_and_plural_wording() {
        let r = |imported, added| ImportReport { imported, added };
        assert_eq!(import_status(&r(5, 3)), "Imported 5 mappings. 3 were new.");
        assert_eq!(import_status(&r(1, 1)), "Imported 1 mapping. 1 was new.");
        assert_eq!(import_status(&r(0, 0)), "Imported 0 mappings. 0 were new.");
    }

    #[test]
    fn export_status_names_the_file() {
        assert_eq!(
            export_status(Path::new("C:/tmp/memory.json")),
            "Exported your memory to C:/tmp/memory.json."
        );
    }

    #[test]
    fn about_line_has_the_version_and_build() {
        assert_eq!(
            about_line("1.4.0", "abc123"),
            "Layer Standardizer Beta 1.4.0 - Build abc123"
        );
    }

    #[test]
    fn beta_notice_is_the_agreed_wording() {
        assert_eq!(
            BETA_NOTICE,
            "Beta: please try this on a copy of your drawing first, and keep a backup before you apply changes."
        );
    }

    #[test]
    fn settings_actions_are_disabled_while_busy() {
        assert!(settings_actions_enabled(false));
        assert!(!settings_actions_enabled(true));
    }
}
