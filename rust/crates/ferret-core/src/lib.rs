//! Core library for the Rust Ferret port.
//!
//! This crate intentionally contains no terminal/UI code. It mirrors the
//! reusable parts of `FileExplorer.Core`: path discovery, directory listing,
//! sorting, navigation history, bookmarks, places, settings and file
//! operations.

pub mod bookmarks;
pub mod entry;
pub mod format;
pub mod listing;
pub mod mime;
pub mod natural;
pub mod navigation;
pub mod operations;
pub mod paths;
pub mod places;
pub mod settings;

pub use entry::{Entry, EntryKind, FileKind};
pub use listing::{list_directory, ListOptions};
pub use operations::{OpReport, PasteMode};
pub use places::{Place, PlaceKind};
