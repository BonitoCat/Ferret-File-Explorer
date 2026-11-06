//! Ferret GUI - A desktop file explorer written in Rust using egui/eframe.

pub mod app;
pub mod ui;

// Re-export ferret-core modules for convenient access in UI
pub use ferret_core::{
    bookmarks,
    entry::{Entry, EntryKind, FileKind},
    format,
    listing::{self, ListOptions},
    mime,
    natural,
    navigation as core_navigation,
    operations as core_operations,
    paths::{self, AppPaths},
    settings,
    PasteMode,
    places,
    PlaceKind,
};

pub use app::FerretApp;