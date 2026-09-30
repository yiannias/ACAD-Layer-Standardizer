use acad_layer_core::{LayerCategorizer, LayerDictionaryDefinition};
use std::fs;
use std::path::PathBuf;

fn get_shipped_dictionary() -> LayerDictionaryDefinition {
    let mut path = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    path.pop(); // crates
    path.pop(); // rust
    path.pop(); // ACAD-Layer-Standardizer
    path.push("installer");
    path.push("assets");
    path.push("layer_dictionary.json");

    assert!(path.exists(), "layer_dictionary.json must exist at {:?}", path);
    let content = fs::read_to_string(&path).expect("failed to read layer_dictionary.json");
    serde_json::from_str(&content).expect("failed to parse layer_dictionary.json")
}

fn get_fixture_layers() -> Vec<String> {
    let mut path = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    path.push("tests");
    path.push("fixtures");
    path.push("real_template_layers.json");

    let content = fs::read_to_string(&path).expect("failed to read test fixtures");
    serde_json::from_str(&content).expect("failed to parse fixture JSON")
}

#[test]
fn test_categorizer_on_real_layers() {
    let dict = get_shipped_dictionary();
    let layers = get_fixture_layers();
    let res = LayerCategorizer::classify(&layers, &dict);

    // System layers should be hidden
    assert!(res.always_hidden.contains("DEFPOINTS"));
    assert!(res.always_hidden.contains("0___1"));
    assert!(res.always_hidden.contains("ADSK_ASSOC_ENTITY_BACKUPS"));

    // Annotation should be classified
    assert!(res.layer_tags.get("A-ANNO-DIM").unwrap().contains("Annotative"));
    // Architectural discipline
    assert!(res.layer_tags.get("A-FL-WALL").unwrap().contains("Architectural"));
    // Structural discipline
    assert!(res.layer_tags.get("S-FL-BEAM").unwrap().contains("Structural"));

    // Visible categories should have items
    assert!(!res.visible_categories.is_empty());
}
