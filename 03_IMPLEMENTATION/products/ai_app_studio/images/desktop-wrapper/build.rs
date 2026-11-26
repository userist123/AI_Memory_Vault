fn main() {
    // No app commands: the generated app's webview gets no IPC surface of its own.
    tauri_build::try_build(
        tauri_build::Attributes::new().app_manifest(tauri_build::AppManifest::new().commands(&[])),
    )
    .expect("failed to run tauri-build");
}
