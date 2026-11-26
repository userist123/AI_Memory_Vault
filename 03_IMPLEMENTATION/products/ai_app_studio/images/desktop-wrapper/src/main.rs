// Studio-owned (S9c). One window that shows the embedded web build; no app commands, no extensions.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    tauri::Builder::default()
        .run(tauri::generate_context!())
        .expect("error while running the app");
}
