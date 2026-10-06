use acad_layer_core::{
    LayerCategorizer, LayerDictionaryDefinition, MemoryStore, PluginConfig, TranslationMemory,
};
use acad_layer_ipc::TargetFilter;
use std::path::{Path, PathBuf};

/// Target-side filter buttons derived from the layer dictionary, plus layers that
/// must never be shown. Mirrors what the C# `MappingsCommand` used to compute.
pub struct TargetView {
    pub filters: Vec<TargetFilter>,
    pub always_hidden: Vec<String>,
}

pub fn build_target_view(standard: &[String], dictionary: &LayerDictionaryDefinition) -> TargetView {
    let result = LayerCategorizer::classify(standard.iter(), dictionary);
    let filters = result
        .visible_categories
        .iter()
        .map(|category| TargetFilter {
            name: category.clone(),
            sort_group: result
                .sort_group_by_tag
                .get(category)
                .cloned()
                .unwrap_or_else(|| "Specific".to_string()),
            layers: standard
                .iter()
                .filter(|layer| {
                    result
                        .layer_tags
                        .get(*layer)
                        .is_some_and(|tags| tags.contains(category))
                })
                .cloned()
                .collect(),
        })
        .collect();
    let mut always_hidden: Vec<String> = result.always_hidden.into_iter().collect();
    always_hidden.sort();
    TargetView {
        filters,
        always_hidden,
    }
}

/// The user's dictionary (`layer_dictionary.json` in the config dir). Missing or
/// unreadable falls back to the empty default, exactly like the C# loader.
pub fn load_dictionary(dir: &Path) -> LayerDictionaryDefinition {
    std::fs::read_to_string(dir.join("layer_dictionary.json"))
        .ok()
        .and_then(|text| serde_json::from_str(&text).ok())
        .unwrap_or_default()
}

/// Everything the window reads from disk at startup.
pub struct StartupData {
    pub config: PluginConfig,
    pub config_path: Option<PathBuf>,
    /// False when the config file was unreadable: saving would replace it with defaults.
    pub config_writable: bool,
    pub dictionary: LayerDictionaryDefinition,
    pub memory_store: Option<MemoryStore>,
    pub memory: TranslationMemory,
    /// Problems the user should see (corrupt config or memory file).
    pub notes: Vec<String>,
}

pub fn load_startup() -> StartupData {
    load_startup_from(acad_layer_core::config_dir().as_deref())
}

pub fn load_startup_from(dir: Option<&Path>) -> StartupData {
    let mut notes = Vec::new();
    let config_path = dir.map(|d| d.join("config.json"));

    let (config, config_writable) = match config_path.as_deref().map(PluginConfig::load_from) {
        Some(Ok(config)) => (config, true),
        Some(Err(error)) => {
            notes.push(format!("{error}. Using defaults and leaving the file untouched."));
            (PluginConfig::default(), false)
        }
        None => (PluginConfig::default(), false),
    };

    let memory_store = dir.map(|d| MemoryStore::new(config.effective_memory_path(d)));
    let memory = match memory_store.as_ref().map(MemoryStore::load_checked) {
        Some(Ok(memory)) => memory,
        Some(Err(error)) => {
            notes.push(format!(
                "{error}. Starting with empty memory; the file will not be overwritten."
            ));
            TranslationMemory::default()
        }
        None => TranslationMemory::default(),
    };

    StartupData {
        dictionary: dir.map(load_dictionary).unwrap_or_default(),
        config,
        config_path,
        config_writable,
        memory_store,
        memory,
        notes,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::BTreeSet;
    use std::fs;
    use std::path::PathBuf;

    fn repo_root() -> PathBuf {
        let mut path = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
        path.pop();
        path.pop();
        path.pop();
        path
    }

    fn real_layers() -> Vec<String> {
        let text = fs::read_to_string(repo_root().join("tests/parity/real_layers.json")).unwrap();
        serde_json::from_str(&text).unwrap()
    }

    fn shipped_dictionary() -> LayerDictionaryDefinition {
        let text = fs::read_to_string(repo_root().join("installer/assets/layer_dictionary.json")).unwrap();
        serde_json::from_str(&text).unwrap()
    }

    fn golden() -> serde_json::Value {
        let text = fs::read_to_string(repo_root().join("tests/parity/categorization.golden.json")).unwrap();
        serde_json::from_str(&text).unwrap()
    }

    #[test]
    fn target_view_matches_the_csharp_filters_for_real_layers() {
        let golden = golden();
        let view = build_target_view(&real_layers(), &shipped_dictionary());

        let names: Vec<&str> = view.filters.iter().map(|f| f.name.as_str()).collect();
        let expected_names: Vec<&str> = golden["visibleCategories"]
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_str().unwrap())
            .collect();
        assert_eq!(names, expected_names, "filters must keep the C# category order");

        for filter in &view.filters {
            let members: BTreeSet<&str> = filter.layers.iter().map(String::as_str).collect();
            let expected: BTreeSet<&str> = golden["layerTags"]
                .as_object()
                .unwrap()
                .iter()
                .filter(|(_, tags)| tags.as_array().unwrap().iter().any(|t| t == filter.name.as_str()))
                .map(|(layer, _)| layer.as_str())
                .collect();
            assert_eq!(members, expected, "layers in filter {}", filter.name);
            assert_eq!(
                filter.sort_group,
                golden["sortGroupByTag"][filter.name.as_str()].as_str().unwrap_or("Specific")
            );
        }

        let hidden: BTreeSet<&str> = view.always_hidden.iter().map(String::as_str).collect();
        let expected_hidden: BTreeSet<&str> = golden["alwaysHidden"]
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_str().unwrap())
            .collect();
        assert_eq!(hidden, expected_hidden);
    }

    #[test]
    fn target_view_with_no_dictionary_has_one_misc_filter_like_csharp() {
        // C# puts every untagged layer into a catch-all "Misc" bucket (Pass 4), so an
        // empty dictionary still produces a single Misc filter holding all layers.
        let layers = real_layers();
        let view = build_target_view(&layers, &LayerDictionaryDefinition::default());
        assert_eq!(view.filters.len(), 1);
        assert_eq!(view.filters[0].name, "Misc");
        assert_eq!(view.filters[0].layers.len(), layers.len());
        assert!(view.always_hidden.is_empty());
    }

    #[test]
    fn dictionary_missing_or_corrupt_falls_back_to_defaults() {
        let dir = std::env::temp_dir().join(format!("acad_ui_dict_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        assert!(load_dictionary(&dir).categories.is_empty());
        fs::write(dir.join("layer_dictionary.json"), "{ nope").unwrap();
        assert!(load_dictionary(&dir).categories.is_empty());
        fs::write(
            dir.join("layer_dictionary.json"),
            fs::read_to_string(repo_root().join("installer/assets/layer_dictionary.json")).unwrap(),
        )
        .unwrap();
        assert!(!load_dictionary(&dir).categories.is_empty());
    }

    #[test]
    fn startup_with_nothing_on_disk_uses_defaults_and_no_notes() {
        let dir = std::env::temp_dir().join(format!("acad_ui_startup_empty_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let startup = load_startup_from(Some(&dir));
        assert!(startup.notes.is_empty(), "{:?}", startup.notes);
        assert!(startup.config_writable);
        assert_eq!(startup.config.template_dwg_path, "");
        assert!(startup.memory.mappings.is_empty());
    }

    #[test]
    fn startup_with_corrupt_files_reports_notes_and_protects_the_config() {
        let dir = std::env::temp_dir().join(format!("acad_ui_startup_bad_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        fs::write(dir.join("config.json"), "{ nope").unwrap();
        fs::write(dir.join("standards_memory.json"), "{ nope").unwrap();
        let startup = load_startup_from(Some(&dir));
        assert_eq!(startup.notes.len(), 2, "{:?}", startup.notes);
        assert!(!startup.config_writable, "a corrupt config must not be overwritten with defaults");
        assert!(startup.memory.mappings.is_empty());
        assert_eq!(fs::read_to_string(dir.join("standards_memory.json")).unwrap(), "{ nope");
    }

    #[test]
    fn startup_reads_a_1_2_x_memory_file_from_the_configured_path() {
        let dir = std::env::temp_dir().join(format!("acad_ui_startup_mem_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        fs::copy(repo_root().join("tests/parity/memory_1_2_x.json"), dir.join("standards_memory.json")).unwrap();
        let startup = load_startup_from(Some(&dir));
        assert_eq!(startup.memory.lookup("a-ELEV-medm"), Some("A-DT-3"));
    }
}
