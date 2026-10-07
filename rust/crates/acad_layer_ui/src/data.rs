use acad_layer_core::{
    LayerCategorizer, LayerDictionaryDefinition, MemoryStore, PluginConfig, TranslationMemory,
};
use acad_layer_ipc::TargetFilter;
use std::collections::HashMap;
use std::path::{Path, PathBuf};

/// Target-side filter buttons derived from the layer dictionary, plus layers that
/// must never be shown. Mirrors what the C# `MappingsCommand` used to compute.
pub struct TargetView {
    pub filters: Vec<TargetFilter>,
    pub always_hidden: Vec<String>,
}

pub fn build_target_view(
    standard: &[String],
    dictionary: &LayerDictionaryDefinition,
) -> TargetView {
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

/// What happened to the translation memory after a confirmed Apply.
pub enum MemoryOutcome {
    NotRequested,
    Saved,
    Failed(String),
}

/// After a confirmed Apply the window normally closes. When the memory save failed it
/// stays up until the user has seen the warning (the drawing is already changed, so
/// the window must not be used for another Apply).
pub fn close_after_apply(memory: &MemoryOutcome) -> bool {
    !matches!(memory, MemoryOutcome::Failed(_))
}

pub fn applied_status_message(count: usize, memory: MemoryOutcome) -> String {
    match memory {
        MemoryOutcome::NotRequested => format!("Applied {count} mappings."),
        MemoryOutcome::Saved => format!("Applied {count} mappings and saved them to memory."),
        MemoryOutcome::Failed(error) => {
            format!("Applied {count} mappings, but could not save translation memory: {error}")
        }
    }
}

/// Same semantics as the C# `SaveRememberedMappings`: every drawing source layer that
/// is mapped gets its mapping stored, every one that is not is forgotten, and other
/// remembered sources are untouched. A corrupt memory file is an error, never
/// replaced by a fresh one.
pub fn remember_mappings(
    store: &MemoryStore,
    source_layers: &[String],
    mappings: &HashMap<String, String>,
) -> Result<(), String> {
    let mut memory = store.load_checked().map_err(|e| e.to_string())?;
    let by_lowercase: HashMap<String, &String> = mappings
        .iter()
        .map(|(source, target)| (source.to_lowercase(), target))
        .collect();
    for source in source_layers {
        match by_lowercase.get(&source.to_lowercase()) {
            Some(target) => memory.set_mapping(source, target),
            None => memory.remove_mapping(source),
        }
    }
    store.save(&memory).map_err(|e| e.to_string())
}

/// The user's dictionary (`layer_dictionary.json` in the config dir). Missing or
/// unreadable falls back to the empty default, exactly like the C# loader.
pub fn load_dictionary(dir: &Path) -> LayerDictionaryDefinition {
    std::fs::read_to_string(dir.join("layer_dictionary.json"))
        .ok()
        .and_then(|text| LayerDictionaryDefinition::from_json_str(&text).ok())
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
            notes.push(format!(
                "{error}. Using defaults and leaving the file untouched."
            ));
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

/// The result of pointing the app at a different memory file.
pub struct MemoryChange {
    pub config: PluginConfig,
    pub store: MemoryStore,
    pub memory: TranslationMemory,
}

/// Switches to another memory file. The target is read first (a file that does not
/// exist yet is fine: empty memory); if it cannot be read the config is left
/// untouched and the old memory stays in use.
pub fn switch_memory_file(
    config_path: &Path,
    config_dir: &Path,
    new_path: &str,
) -> Result<MemoryChange, String> {
    // Check the file the app will really use: relative paths are relative to the
    // config folder and a blank path means the default file there.
    let mut candidate = PluginConfig::load_from(config_path).map_err(|error| {
        format!("Could not read the settings file: {error}. The old memory stays in use.")
    })?;
    let previous = candidate.memory_file_path.clone();
    candidate.memory_file_path = new_path.to_string();
    let target = candidate.effective_memory_path(config_dir);
    if let Err(error) = MemoryStore::new(&target).load_checked() {
        return Err(format!(
            "Could not use the memory file {}: {error}. The old memory stays in use.",
            target.display()
        ));
    }
    let config = PluginConfig::set_memory_path(config_path, new_path).map_err(|error| {
        format!("Could not save the new memory location: {error}. The old memory stays in use.")
    })?;
    let store = MemoryStore::new(config.effective_memory_path(config_dir));
    let memory = match store.load_checked() {
        Ok(memory) => memory,
        Err(error) => {
            // The file changed after the check: put the old location back.
            let reason = format!(
                "Could not read the memory file {}: {error}",
                target.display()
            );
            return Err(
                match PluginConfig::set_memory_path(config_path, &previous) {
                    Ok(_) => format!("{reason}. The old memory stays in use."),
                    Err(rollback) => format!(
                        "{reason}. The old location could not be restored either ({rollback}); \
                     check the memory file location in settings."
                    ),
                },
            );
        }
    };
    Ok(MemoryChange {
        config,
        store,
        memory,
    })
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
        let text =
            fs::read_to_string(repo_root().join("installer/assets/layer_dictionary.json")).unwrap();
        serde_json::from_str(&text).unwrap()
    }

    fn golden() -> serde_json::Value {
        let text = fs::read_to_string(repo_root().join("tests/parity/categorization.golden.json"))
            .unwrap();
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
        assert_eq!(
            names, expected_names,
            "filters must keep the C# category order"
        );

        for filter in &view.filters {
            let members: BTreeSet<&str> = filter.layers.iter().map(String::as_str).collect();
            let expected: BTreeSet<&str> = golden["layerTags"]
                .as_object()
                .unwrap()
                .iter()
                .filter(|(_, tags)| {
                    tags.as_array()
                        .unwrap()
                        .iter()
                        .any(|t| t == filter.name.as_str())
                })
                .map(|(layer, _)| layer.as_str())
                .collect();
            assert_eq!(members, expected, "layers in filter {}", filter.name);
            assert_eq!(
                filter.sort_group,
                golden["sortGroupByTag"][filter.name.as_str()]
                    .as_str()
                    .unwrap_or("Specific")
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
    fn dictionary_is_read_like_csharp_with_a_bom_and_any_key_case() {
        let dir = std::env::temp_dir().join(format!("acad_ui_dict_bom_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let text = r#"{"Delimiter":"_","FIELDSSCANNED":2,
            "categories":[{"NAME":"Walls","Tokens":["WALL"],"sortgroup":"Discipline"}]}"#;
        fs::write(dir.join("layer_dictionary.json"), format!("\u{feff}{text}")).unwrap();
        let dictionary = load_dictionary(&dir);
        assert_eq!(dictionary.delimiter, "_");
        assert_eq!(dictionary.fields_scanned, 2);
        assert_eq!(dictionary.categories.len(), 1);
        assert_eq!(dictionary.categories[0].name, "Walls");
        assert_eq!(dictionary.categories[0].sort_group, "Discipline");
    }

    #[test]
    fn startup_with_nothing_on_disk_uses_defaults_and_no_notes() {
        let dir =
            std::env::temp_dir().join(format!("acad_ui_startup_empty_{}", std::process::id()));
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
        assert!(
            !startup.config_writable,
            "a corrupt config must not be overwritten with defaults"
        );
        assert!(startup.memory.mappings.is_empty());
        assert_eq!(
            fs::read_to_string(dir.join("standards_memory.json")).unwrap(),
            "{ nope"
        );
    }

    #[test]
    fn startup_reads_a_1_2_x_memory_file_from_the_configured_path() {
        let dir = std::env::temp_dir().join(format!("acad_ui_startup_mem_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        fs::copy(
            repo_root().join("tests/parity/memory_1_2_x.json"),
            dir.join("standards_memory.json"),
        )
        .unwrap();
        let startup = load_startup_from(Some(&dir));
        assert_eq!(startup.memory.lookup("a-ELEV-medm"), Some("A-DT-3"));
    }

    fn temp_store(name: &str) -> (std::path::PathBuf, MemoryStore) {
        let dir = std::env::temp_dir().join(format!("acad_ui_{name}_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let path = dir.join("standards_memory.json");
        (path.clone(), MemoryStore::new(path))
    }

    #[test]
    fn remember_mappings_sets_mapped_and_removes_unmapped_sources() {
        let (_, store) = temp_store("remember_sets");
        let mut seed = TranslationMemory::default();
        seed.mappings.insert("A-OLD".into(), "A-WALL".into());
        seed.mappings.insert("UNRELATED".into(), "KEEP".into());
        store.save(&seed).unwrap();

        let sources = vec!["A-NEW".to_string(), "A-OLD".to_string()];
        let mapped: std::collections::HashMap<String, String> =
            [("A-NEW".to_string(), "A-DOOR".to_string())]
                .into_iter()
                .collect();
        remember_mappings(&store, &sources, &mapped).unwrap();

        let memory = store.load_checked().unwrap();
        assert_eq!(memory.lookup("A-NEW"), Some("A-DOOR"));
        assert_eq!(
            memory.lookup("A-OLD"),
            None,
            "a source that is no longer mapped is forgotten"
        );
        assert_eq!(
            memory.lookup("UNRELATED"),
            Some("KEEP"),
            "other sources are untouched"
        );
    }

    #[test]
    fn remember_mappings_refuses_to_overwrite_a_corrupt_file() {
        let (path, store) = temp_store("remember_corrupt");
        fs::write(&path, "{ nope").unwrap();
        let mapped = [("A".to_string(), "B".to_string())].into_iter().collect();
        assert!(remember_mappings(&store, &["A".to_string()], &mapped).is_err());
        assert_eq!(fs::read_to_string(&path).unwrap(), "{ nope");
    }

    #[test]
    fn remember_mappings_matches_sources_case_insensitively() {
        let (_, store) = temp_store("remember_case");
        let mut seed = TranslationMemory::default();
        seed.mappings.insert("A-Wall".into(), "OLD".into());
        store.save(&seed).unwrap();
        let mapped = [("a-wall".to_string(), "NEW".to_string())]
            .into_iter()
            .collect();
        remember_mappings(&store, &["a-wall".to_string()], &mapped).unwrap();
        let memory = store.load_checked().unwrap();
        assert_eq!(memory.mappings.len(), 1);
        assert_eq!(memory.lookup("A-WALL"), Some("NEW"));
    }

    fn switch_dir(name: &str) -> std::path::PathBuf {
        let dir =
            std::env::temp_dir().join(format!("acad_ui_switch_{name}_{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn switching_to_a_valid_memory_file_updates_the_config_and_loads_it() {
        let dir = switch_dir("valid");
        let config_path = dir.join("config.json");
        let target = dir.join("other_memory.json");
        let store = MemoryStore::new(&target);
        let mut seed = TranslationMemory::default();
        seed.mappings.insert("A-OLD".into(), "A-WALL".into());
        store.save(&seed).unwrap();

        let change = switch_memory_file(&config_path, &dir, target.to_str().unwrap()).unwrap();
        assert_eq!(change.memory.lookup("A-OLD"), Some("A-WALL"));
        assert_eq!(change.config.memory_file_path, target.to_str().unwrap());
        assert_eq!(change.store.file_path(), target.as_path());
        let reloaded = PluginConfig::load_from(&config_path).unwrap();
        assert_eq!(reloaded.memory_file_path, target.to_str().unwrap());
    }

    #[test]
    fn switching_to_a_corrupt_file_changes_nothing() {
        let dir = switch_dir("corrupt");
        let config_path = dir.join("config.json");
        let original = r#"{"TemplateDwgPath":"keep.dwg"}"#;
        fs::write(&config_path, original).unwrap();
        let target = dir.join("bad_memory.json");
        fs::write(&target, "{ nope").unwrap();

        let error = match switch_memory_file(&config_path, &dir, target.to_str().unwrap()) {
            Ok(_) => panic!("a corrupt target must be refused"),
            Err(error) => error,
        };
        assert!(error.contains("bad_memory.json"), "{error}");
        assert!(error.contains("old memory"), "{error}");
        assert_eq!(fs::read_to_string(&config_path).unwrap(), original);
        assert_eq!(fs::read_to_string(&target).unwrap(), "{ nope");
    }

    #[test]
    fn a_corrupt_file_reached_through_a_relative_path_changes_nothing() {
        let dir = switch_dir("relative");
        let config_path = dir.join("config.json");
        let original = r#"{"TemplateDwgPath":"keep.dwg"}"#;
        fs::write(&config_path, original).unwrap();
        fs::write(dir.join("rel_memory.json"), "{ nope").unwrap();

        let error = match switch_memory_file(&config_path, &dir, "rel_memory.json") {
            Ok(_) => panic!("a corrupt target must be refused"),
            Err(error) => error,
        };
        assert!(error.contains("rel_memory.json"), "{error}");
        assert!(error.contains("old memory stays in use"), "{error}");
        assert_eq!(fs::read_to_string(&config_path).unwrap(), original);
    }

    #[test]
    fn a_blank_path_checks_the_default_memory_file() {
        let dir = switch_dir("blank");
        let config_path = dir.join("config.json");
        let original = r#"{"MemoryFilePath":"elsewhere.json"}"#;
        fs::write(&config_path, original).unwrap();
        fs::write(dir.join("standards_memory.json"), "{ nope").unwrap();

        let error = match switch_memory_file(&config_path, &dir, "  ") {
            Ok(_) => panic!("a corrupt default memory file must be refused"),
            Err(error) => error,
        };
        assert!(error.contains("standards_memory.json"), "{error}");
        assert!(error.contains("old memory stays in use"), "{error}");
        assert_eq!(fs::read_to_string(&config_path).unwrap(), original);
    }

    #[test]
    fn switching_to_the_current_path_works() {
        let dir = switch_dir("same");
        let config_path = dir.join("config.json");
        let target = dir.join("same_memory.json");
        let mut seed = TranslationMemory::default();
        seed.mappings.insert("A".into(), "B".into());
        MemoryStore::new(&target).save(&seed).unwrap();
        let path = target.to_str().unwrap();
        switch_memory_file(&config_path, &dir, path).unwrap();
        let again = switch_memory_file(&config_path, &dir, path).unwrap();
        assert_eq!(again.memory.lookup("A"), Some("B"));
        assert_eq!(again.config.memory_file_path, path);
    }

    #[test]
    fn switching_to_a_new_path_starts_with_empty_memory() {
        let dir = switch_dir("new");
        let config_path = dir.join("config.json");
        let target = dir.join("not_yet.json");
        let change = switch_memory_file(&config_path, &dir, target.to_str().unwrap()).unwrap();
        assert!(change.memory.mappings.is_empty());
        assert_eq!(change.store.file_path(), target.as_path());
        assert_eq!(
            PluginConfig::load_from(&config_path)
                .unwrap()
                .memory_file_path,
            target.to_str().unwrap()
        );
    }

    #[test]
    fn a_memory_failure_after_a_successful_apply_is_a_warning_not_a_failure() {
        let text = applied_status_message(3, MemoryOutcome::Failed("disk full".into()));
        assert!(text.starts_with("Applied 3 mappings"), "{text}");
        assert!(text.contains("could not save translation memory"), "{text}");
        assert!(text.contains("disk full"), "{text}");
        assert_eq!(
            applied_status_message(3, MemoryOutcome::Saved),
            "Applied 3 mappings and saved them to memory."
        );
        assert_eq!(
            applied_status_message(1, MemoryOutcome::NotRequested),
            "Applied 1 mappings."
        );
    }

    #[test]
    fn a_failed_memory_save_does_not_close_the_window_before_the_user_sees_it() {
        assert!(!close_after_apply(&MemoryOutcome::Failed(
            "disk full".into()
        )));
        assert!(close_after_apply(&MemoryOutcome::Saved));
        assert!(close_after_apply(&MemoryOutcome::NotRequested));
    }
}
