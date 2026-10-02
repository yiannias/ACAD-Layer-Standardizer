use acad_layer_core::{
    HeuristicMatcher, LayerCategorizer, LayerDictionaryDefinition, MatchResult, MatchingEngine,
    MemoryMatcher,
};
use serde::Deserialize;
use std::collections::HashMap;
use std::ffi::{CStr, CString};
use std::os::raw::c_char;

#[no_mangle]
pub extern "C" fn acad_version() -> *const c_char {
    static VERSION: &[u8] = b"0.1.0\0";
    VERSION.as_ptr() as *const c_char
}

#[no_mangle]
pub unsafe extern "C" fn acad_calculate_similarity(a: *const c_char, b: *const c_char) -> f64 {
    if a.is_null() || b.is_null() {
        return 0.0;
    }

    let a_str = match CStr::from_ptr(a).to_str() {
        Ok(s) => s,
        Err(_) => return 0.0,
    };
    let b_str = match CStr::from_ptr(b).to_str() {
        Ok(s) => s,
        Err(_) => return 0.0,
    };

    HeuristicMatcher::calculate_similarity(a_str, b_str)
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ClassifyRequest {
    source_layers: Vec<String>,
    standard_layers: Vec<String>,
    #[serde(default = "default_min_confidence")]
    min_confidence: f64,
    #[serde(default)]
    memory_mappings: HashMap<String, String>,
}

fn default_min_confidence() -> f64 {
    0.6
}

#[no_mangle]
pub unsafe extern "C" fn acad_classify_layers_json(request_json: *const c_char) -> *mut c_char {
    if request_json.is_null() {
        return std::ptr::null_mut();
    }

    let raw_str = match CStr::from_ptr(request_json).to_str() {
        Ok(s) => s,
        Err(_) => return std::ptr::null_mut(),
    };

    let req: ClassifyRequest = match serde_json::from_str(raw_str) {
        Ok(r) => r,
        Err(_) => return std::ptr::null_mut(),
    };

    let mem_matcher = MemoryMatcher::new(&req.memory_mappings);
    let heur_matcher = HeuristicMatcher::new(req.standard_layers, req.min_confidence);
    let engine = MatchingEngine::new(Some(mem_matcher), Some(heur_matcher));

    let results: Vec<MatchResult> = engine.classify_all(&req.source_layers);

    let output_json = match serde_json::to_string(&results) {
        Ok(j) => j,
        Err(_) => return std::ptr::null_mut(),
    };

    match CString::new(output_json) {
        Ok(c_str) => c_str.into_raw(),
        Err(_) => std::ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn acad_categorize_layers_json(
    layers_json: *const c_char,
    dict_json: *const c_char,
) -> *mut c_char {
    if layers_json.is_null() {
        return std::ptr::null_mut();
    }

    let layers_str = match CStr::from_ptr(layers_json).to_str() {
        Ok(s) => s,
        Err(_) => return std::ptr::null_mut(),
    };

    let layers: Vec<String> = match serde_json::from_str(layers_str) {
        Ok(l) => l,
        Err(_) => return std::ptr::null_mut(),
    };

    let dict: LayerDictionaryDefinition = if dict_json.is_null() {
        LayerDictionaryDefinition::default()
    } else {
        match CStr::from_ptr(dict_json).to_str() {
            Ok(s) if !s.trim().is_empty() => serde_json::from_str(s).unwrap_or_default(),
            _ => LayerDictionaryDefinition::default(),
        }
    };

    let result = LayerCategorizer::classify(layers.iter().map(|s| s.as_str()), &dict);

    let output_json = match serde_json::to_string(&result) {
        Ok(j) => j,
        Err(_) => return std::ptr::null_mut(),
    };

    match CString::new(output_json) {
        Ok(c_str) => c_str.into_raw(),
        Err(_) => std::ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn acad_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        let _ = CString::from_raw(ptr);
    }
}
