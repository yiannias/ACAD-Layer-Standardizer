use std::time::{SystemTime, UNIX_EPOCH};

fn main() {
    println!("cargo:rerun-if-env-changed=ACAD_LAYER_UI_BUILD_ID");
    let build_id = std::env::var("ACAD_LAYER_UI_BUILD_ID").unwrap_or_else(|_| {
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system time must be after the Unix epoch")
            .as_millis()
            .to_string()
    });

    println!("cargo:rustc-env=ACAD_LAYER_UI_BUILD_ID={build_id}");
}
