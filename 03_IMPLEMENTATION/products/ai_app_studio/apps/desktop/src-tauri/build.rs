fn main() {
    // Only our own command is exposed to the webview, via the generated `allow-get-session` permission.
    tauri_build::try_build(
        tauri_build::Attributes::new()
            .app_manifest(tauri_build::AppManifest::new().commands(&["get_session"])),
    )
    .expect("failed to run tauri-build");
}
