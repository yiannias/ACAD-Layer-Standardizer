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

    embed_exe_icon();
}

/// Embeds the Layer Herder icon in acad_layer_ui.exe so Explorer and Task
/// Manager show it. Needs the Windows SDK resource compiler; when that is
/// missing the build carries on with a warning and the exe has no icon.
fn embed_exe_icon() {
    println!("cargo:rerun-if-changed=assets/app_icon.ico");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() != Ok("windows") {
        return;
    }
    let mut resource = winresource::WindowsResource::new();
    resource.set_icon("assets/app_icon.ico");
    if let Err(error) = resource.compile() {
        println!("cargo:warning=exe icon not embedded: {error}");
    }
}
