//! Asserts the Rust core against the golden files in `tests/parity`, which were
//! generated from the shipped C# implementation (see
//! `tests/AcLayerStandardizer.Tests/ParityTests.cs`). A failure here is a parity
//! bug in Rust: fix the Rust code, never edit the golden.

use acad_layer_core::{
    HeuristicMatcher, LayerCategorizer, LayerDictionaryDefinition, MemoryStore, TranslationMemory,
};
use serde_json::Value;
use std::collections::BTreeMap;
use std::fs;
use std::path::PathBuf;

fn repo_root() -> PathBuf {
    let mut path = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    path.pop(); // crates
    path.pop(); // rust
    path.pop(); // repo root
    path
}

fn parity(name: &str) -> PathBuf {
    repo_root().join("tests").join("parity").join(name)
}

fn read_json(path: PathBuf) -> Value {
    let text = fs::read_to_string(&path).unwrap_or_else(|e| panic!("{path:?}: {e}"));
    serde_json::from_str(&text).unwrap_or_else(|e| panic!("{path:?}: {e}"))
}

fn real_layers() -> Vec<String> {
    serde_json::from_value(read_json(parity("real_layers.json"))).unwrap()
}

fn shipped_dictionary() -> LayerDictionaryDefinition {
    let path = repo_root().join("installer/assets/layer_dictionary.json");
    serde_json::from_str(&fs::read_to_string(path).unwrap()).unwrap()
}

fn strings(value: &Value) -> Vec<String> {
    value
        .as_array()
        .unwrap()
        .iter()
        .map(|v| v.as_str().unwrap().to_string())
        .collect()
}

#[test]
fn categorization_matches_golden() {
    let golden = read_json(parity("categorization.golden.json"));
    let result = LayerCategorizer::classify(real_layers(), &shipped_dictionary());

    let mut hidden: Vec<String> = result.always_hidden.iter().cloned().collect();
    hidden.sort();
    assert_eq!(hidden, strings(&golden["alwaysHidden"]), "alwaysHidden");

    assert_eq!(
        result.visible_categories,
        strings(&golden["visibleCategories"]),
        "visibleCategories (order matters)"
    );

    let groups: BTreeMap<String, String> = result.sort_group_by_tag.clone().into_iter().collect();
    let golden_groups: BTreeMap<String, String> =
        serde_json::from_value(golden["sortGroupByTag"].clone()).unwrap();
    assert_eq!(groups, golden_groups, "sortGroupByTag");

    let tags: BTreeMap<String, Vec<String>> = result
        .layer_tags
        .iter()
        .map(|(layer, set)| {
            let mut tags: Vec<String> = set.iter().cloned().collect();
            tags.sort();
            (layer.clone(), tags)
        })
        .collect();
    let golden_tags: BTreeMap<String, Vec<String>> =
        serde_json::from_value(golden["layerTags"].clone()).unwrap();
    for (layer, expected) in &golden_tags {
        assert_eq!(tags.get(layer), Some(expected), "layerTags[{layer}]");
    }
    assert_eq!(tags.len(), golden_tags.len(), "layerTags layer count");
}

#[test]
fn heuristic_scores_match_golden() {
    let golden = read_json(parity("heuristic.golden.json"));
    let min = golden["minConfidence"].as_f64().unwrap();
    let matcher = HeuristicMatcher::new(real_layers(), min);

    for case in golden["matches"].as_array().unwrap() {
        let source = case["source"].as_str().unwrap();
        let actual = matcher.try_match(source);
        match case["target"].as_str() {
            None => assert!(actual.is_none(), "{source}: expected no match, got {actual:?}"),
            Some(target) => {
                let actual = actual.unwrap_or_else(|| panic!("{source}: expected a match"));
                assert_eq!(actual.target_layer.as_deref(), Some(target), "{source}");
                let expected = case["confidence"].as_f64().unwrap();
                assert!(
                    (actual.confidence - expected).abs() < 1e-9,
                    "{source}: confidence {} vs {expected}",
                    actual.confidence
                );
                assert_eq!(format!("{:?}", actual.source), case["kind"].as_str().unwrap(), "{source}");
            }
        }
    }

    for pair in golden["similarities"].as_array().unwrap() {
        let (a, b) = (pair["a"].as_str().unwrap(), pair["b"].as_str().unwrap());
        let expected = pair["score"].as_f64().unwrap();
        let actual = HeuristicMatcher::calculate_similarity(a, b);
        assert!((actual - expected).abs() < 1e-9, "similarity({a:?},{b:?}) = {actual}, expected {expected}");
    }
}

#[test]
fn memory_file_matches_golden() {
    let golden = read_json(parity("memory_lookup.golden.json"));
    let memory = MemoryStore::new(parity("memory_1_2_x.json")).load_checked().unwrap();
    assert_eq!(memory.mappings.len() as u64, golden["count"].as_u64().unwrap());
    for lookup in golden["lookups"].as_array().unwrap() {
        let source = lookup["source"].as_str().unwrap();
        assert_eq!(memory.lookup(source), lookup["target"].as_str(), "lookup({source})");
    }
}

/// Writes `tests/parity/memory_saved_by_rust.json`, which the C# parity test loads
/// to prove a Rust-saved file is readable by the C# `MemoryStore`.
/// Regenerate with: `cargo test -p acad_layer_core --test parity_tests -- --ignored`
#[test]
#[ignore]
fn generate_memory_saved_by_rust() {
    let mut memory = TranslationMemory::default();
    memory.mappings.insert("A-Wall".into(), "A-WALL".into());
    memory.mappings.insert("X-SCRATCH".into(), "X-SCRATCH".into());
    let path = parity("memory_saved_by_rust.json");
    let _ = fs::remove_file(&path);
    MemoryStore::new(&path).save(&memory).unwrap();
}

#[test]
fn memory_sample_saved_by_rust_is_readable() {
    let memory = MemoryStore::new(parity("memory_saved_by_rust.json")).load_checked().unwrap();
    assert_eq!(memory.lookup("a-wall"), Some("A-WALL"));
    assert_eq!(memory.mappings.len(), 2);
}
