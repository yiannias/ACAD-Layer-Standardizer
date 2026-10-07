use serde::{Deserialize, Serialize};
use std::fmt;
use std::fs;
use std::path::{Path, PathBuf};

/// Plugin configuration shared with the C# connector
/// (`%APPDATA%\AcLayerStandardizer\config.json`). Keys this crate does not own
/// (for example ones added by a newer plugin) are preserved on save.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct PluginConfig {
    pub template_dwg_path: String,
    pub memory_file_path: String,
    pub heuristic_threshold: f64,
    pub install_ribbon: bool,
    pub install_menu: bool,
    #[serde(flatten)]
    pub extra: serde_json::Map<String, serde_json::Value>,
}

impl Default for PluginConfig {
    fn default() -> Self {
        Self {
            template_dwg_path: String::new(),
            memory_file_path: String::new(),
            heuristic_threshold: 0.6,
            install_ribbon: true,
            install_menu: true,
            extra: serde_json::Map::new(),
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub enum ConfigError {
    Io(String),
    Parse(String),
}

impl fmt::Display for ConfigError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ConfigError::Io(message) => write!(f, "could not read the plugin config: {message}"),
            ConfigError::Parse(message) => write!(f, "the plugin config is not valid JSON: {message}"),
        }
    }
}

impl std::error::Error for ConfigError {}

/// `%APPDATA%\AcLayerStandardizer`, where config, dictionary, and memory live.
pub fn config_dir() -> Option<PathBuf> {
    std::env::var_os("APPDATA").map(|appdata| PathBuf::from(appdata).join("AcLayerStandardizer"))
}

impl PluginConfig {
    /// A missing file is not an error (first run); an unreadable or corrupt file is,
    /// so callers never replace a damaged config with defaults by accident.
    pub fn load_from(path: &Path) -> Result<PluginConfig, ConfigError> {
        if !path.exists() {
            return Ok(PluginConfig::default());
        }
        let text = fs::read_to_string(path).map_err(|e| ConfigError::Io(e.to_string()))?;
        serde_json::from_str(&text).map_err(|e| ConfigError::Parse(e.to_string()))
    }

    pub fn save_to(&self, path: &Path) -> std::io::Result<()> {
        if let Some(parent) = path.parent() {
            if !parent.as_os_str().is_empty() {
                fs::create_dir_all(parent)?;
            }
        }
        let json = serde_json::to_string_pretty(self)
            .map_err(|e| std::io::Error::new(std::io::ErrorKind::Other, e))?;
        let temp = path.with_extension(format!("{}.tmp", std::process::id()));
        fs::write(&temp, json)?;
        let result = fs::rename(&temp, path);
        if result.is_err() {
            let _ = fs::remove_file(&temp);
        }
        result
    }

    /// A blank setting means the default file next to the config; a relative one is
    /// relative to the config folder (the same rule the C# connector applies).
    pub fn effective_memory_path(&self, dir: &Path) -> PathBuf {
        if self.memory_file_path.trim().is_empty() {
            dir.join("standards_memory.json")
        } else {
            let configured = PathBuf::from(&self.memory_file_path);
            if configured.is_absolute() {
                configured
            } else {
                dir.join(configured)
            }
        }
    }

    /// Records the chosen standard. Re-reads the file first so settings changed while
    /// the window was open are kept, and a corrupt file is an error, never replaced.
    pub fn set_template_path(path: &Path, template: &str) -> Result<PluginConfig, ConfigError> {
        let mut config = PluginConfig::load_from(path)?;
        config.template_dwg_path = template.to_string();
        config
            .save_to(path)
            .map_err(|e| ConfigError::Io(e.to_string()))?;
        Ok(config)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::path::PathBuf;

    fn temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("acad_layer_core_{name}_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn config_reads_shipped_config_json() {
        let mut path = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
        path.pop();
        path.pop();
        path.pop();
        path.push("installer/assets/config.json");
        let config = PluginConfig::load_from(&path).unwrap();
        assert_eq!(config.template_dwg_path, "");
        assert_eq!(config.memory_file_path, "");
        assert_eq!(config.heuristic_threshold, 0.6);
        assert!(config.install_ribbon && config.install_menu);
    }

    #[test]
    fn config_round_trip_preserves_unknown_keys() {
        let dir = temp_dir("config_round_trip");
        let path = dir.join("config.json");
        fs::write(&path, r#"{"TemplateDwgPath":"T.dwg","InstallRibbon":false,"Extra":1}"#).unwrap();
        let mut config = PluginConfig::load_from(&path).unwrap();
        config.template_dwg_path = "U.dwg".into();
        config.save_to(&path).unwrap();
        let text = fs::read_to_string(&path).unwrap();
        assert!(text.contains("\"InstallRibbon\": false"), "{text}");
        assert!(text.contains("\"Extra\": 1"), "{text}");
        assert!(text.contains("U.dwg"));
    }

    #[test]
    fn config_missing_file_gives_defaults() {
        let dir = temp_dir("config_missing");
        let config = PluginConfig::load_from(&dir.join("nope.json")).unwrap();
        assert_eq!(config, PluginConfig::default());
    }

    #[test]
    fn config_corrupt_file_is_an_error_not_defaults() {
        let dir = temp_dir("config_corrupt");
        let path = dir.join("config.json");
        fs::write(&path, "{ not json").unwrap();
        assert!(PluginConfig::load_from(&path).is_err());
    }

    #[test]
    fn effective_memory_path_defaults_next_to_config() {
        let dir = PathBuf::from("C:/cfg");
        assert_eq!(
            PluginConfig::default().effective_memory_path(&dir),
            dir.join("standards_memory.json")
        );
        let custom = PluginConfig {
            memory_file_path: "D:/shared/mem.json".into(),
            ..PluginConfig::default()
        };
        assert_eq!(custom.effective_memory_path(&dir), PathBuf::from("D:/shared/mem.json"));
    }

    #[test]
    fn a_blank_memory_path_means_the_default() {
        let dir = PathBuf::from("C:/cfg");
        let blank = PluginConfig {
            memory_file_path: "   ".into(),
            ..PluginConfig::default()
        };
        assert_eq!(blank.effective_memory_path(&dir), dir.join("standards_memory.json"));
    }

    #[test]
    fn a_relative_memory_path_is_relative_to_the_config_folder() {
        let dir = PathBuf::from("C:/cfg");
        let relative = PluginConfig {
            memory_file_path: "shared/mem.json".into(),
            ..PluginConfig::default()
        };
        assert_eq!(relative.effective_memory_path(&dir), dir.join("shared/mem.json"));
    }

    #[test]
    fn remembering_the_template_keeps_settings_changed_while_the_window_was_open() {
        let dir = temp_dir("config_update");
        let path = dir.join("config.json");
        fs::write(&path, r#"{"TemplateDwgPath":"","HeuristicThreshold":0.6}"#).unwrap();
        // Another session changes the file after this window loaded its copy.
        fs::write(
            &path,
            r#"{"TemplateDwgPath":"","HeuristicThreshold":0.9,"InstallRibbon":false}"#,
        )
        .unwrap();
        let saved = PluginConfig::set_template_path(&path, "S.dws").unwrap();
        assert_eq!(saved.template_dwg_path, "S.dws");
        let reloaded = PluginConfig::load_from(&path).unwrap();
        assert_eq!(reloaded.template_dwg_path, "S.dws");
        assert_eq!(reloaded.heuristic_threshold, 0.9);
        assert!(!reloaded.install_ribbon);
    }

    #[test]
    fn remembering_the_template_never_overwrites_a_corrupt_config() {
        let dir = temp_dir("config_update_corrupt");
        let path = dir.join("config.json");
        fs::write(&path, "{ not json").unwrap();
        assert!(PluginConfig::set_template_path(&path, "S.dws").is_err());
        assert_eq!(fs::read_to_string(&path).unwrap(), "{ not json");
    }
}
