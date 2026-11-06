use eframe::egui;
use std::path::PathBuf;
use crate::{bookmarks, places, PlaceKind};

pub struct Sidebar {
    visible: bool,
    pub places: Vec<places::Place>,
    pub bookmarks: Vec<places::Place>,
    pub devices: Vec<places::Place>,
    pub network: Vec<places::Place>,
    expanded_places: bool,
    expanded_bookmarks: bool,
    expanded_devices: bool,
    expanded_network: bool,
    pub navigate_request: Option<PathBuf>,
}

impl Sidebar {
    pub fn new() -> Self {
        Self {
            visible: true,
            places: Vec::new(),
            bookmarks: Vec::new(),
            devices: Vec::new(),
            network: Vec::new(),
            expanded_places: true,
            expanded_bookmarks: true,
            expanded_devices: true,
            expanded_network: true,
            navigate_request: None,
        }
    }

    pub fn load_places(&mut self, paths: &crate::paths::AppPaths) {
        let store = bookmarks::BookmarkStore::new(paths.data_dir().join("fe").join("bookmarks"));
        self.places = places::standard_places(paths);
        self.bookmarks = places::bookmark_places(&store);
        self.devices = places::device_places();
        self.network = places::network_places();
    }

    pub fn add_bookmark(&mut self, path: PathBuf) {
        if !self.bookmarks.iter().any(|b| b.path == path) {
            self.bookmarks.push(places::Place {
                kind: PlaceKind::Bookmarks,
                name: path
                    .file_name()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_string(),
                path,
                mounted: true,
                device: None,
                bookmark: true,
                remote_uri: None,
            });
        }
    }

    pub fn remove_bookmark(&mut self, path: &PathBuf) {
        self.bookmarks.retain(|b| b.path != *path);
    }

    fn place_buttons(ui: &mut egui::Ui, items: &[places::Place]) -> Option<PathBuf> {
        let mut requested = None;
        for place in items {
            let icon = match place.kind {
                PlaceKind::Places => "🏠",
                PlaceKind::Bookmarks => "★",
                PlaceKind::Devices => "💾",
                PlaceKind::Network => "🌐",
            };
            let label = format!("{}  {}", icon, place.name);
            if ui.selectable_label(false, label).clicked() {
                requested = Some(place.path.clone());
            }
        }
        requested
    }

    pub fn render(&mut self, ui: &mut egui::Ui) {
        if !self.visible {
            return;
        }

        ui.group(|ui| {
            ui.horizontal(|ui| {
                let label = if self.expanded_places { "▼" } else { "▶" };
                if ui.button(label).clicked() {
                    self.expanded_places = !self.expanded_places;
                }
                ui.heading("PLACES");
            });
            if self.expanded_places {
                if let Some(p) = Self::place_buttons(ui, &self.places) { self.navigate_request = Some(p); }
            }

            ui.separator();

            ui.horizontal(|ui| {
                let label = if self.expanded_bookmarks { "▼" } else { "▶" };
                if ui.button(label).clicked() {
                    self.expanded_bookmarks = !self.expanded_bookmarks;
                }
                ui.heading("BOOKMARKS");
            });
            if self.expanded_bookmarks {
                if self.bookmarks.is_empty() {
                    ui.label("No bookmarks");
                } else {
                    let names: Vec<String> = self.bookmarks.iter().map(|p| p.name.clone()).collect();
                    let paths: Vec<PathBuf> = self.bookmarks.iter().map(|p| p.path.clone()).collect();
                    for i in 0..names.len() {
                        ui.horizontal(|ui| {
                            if ui.selectable_label(false, &names[i]).clicked() {
                                self.navigate_request = Some(paths[i].clone());
                            }
                            if ui.small_button("✕").clicked() {
                                self.remove_bookmark(&paths[i]);
                            }
                        });
                    }
                }
            }

            ui.separator();

            ui.horizontal(|ui| {
                let label = if self.expanded_devices { "▼" } else { "▶" };
                if ui.button(label).clicked() {
                    self.expanded_devices = !self.expanded_devices;
                }
                ui.heading("DEVICES");
            });
            if self.expanded_devices {
                if self.devices.is_empty() {
                    ui.label("No devices");
                } else {
                    if let Some(p) = Self::place_buttons(ui, &self.devices) { self.navigate_request = Some(p); }
                }
            }

            ui.separator();

            ui.horizontal(|ui| {
                let label = if self.expanded_network { "▼" } else { "▶" };
                if ui.button(label).clicked() {
                    self.expanded_network = !self.expanded_network;
                }
                ui.heading("NETWORK");
            });
            if self.expanded_network {
                if self.network.is_empty() {
                    ui.label("No network shares");
                } else {
                    if let Some(p) = Self::place_buttons(ui, &self.network) { self.navigate_request = Some(p); }
                }
            }
        });
    }
}

impl Default for Sidebar {
    fn default() -> Self {
        Self::new()
    }
}
