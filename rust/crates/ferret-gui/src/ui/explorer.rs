use eframe::egui;
use std::path::PathBuf;
use crate::Entry;

pub struct Explorer {
    pub current_path: PathBuf,
    pub entries: Vec<Entry>,
    pub selected: Vec<PathBuf>,
    pub show_hidden_files: bool,
    pub show_file_sizes: bool,
    pub path_filter: String,
    pub navigate_request: Option<PathBuf>,
    last_single_click: Option<PathBuf>,
    last_click_time: f64,
}

impl Explorer {
    pub fn new() -> Self {
        Self {
            current_path: PathBuf::from("/"),
            entries: Vec::new(),
            selected: Vec::new(),
            show_hidden_files: false,
            show_file_sizes: true,
            path_filter: String::new(),
            navigate_request: None,
            last_single_click: None,
            last_click_time: 0.0,
        }
    }

    pub fn set_entries(&mut self, entries: Vec<Entry>) {
        self.entries = entries;
    }

    pub fn entries(&self) -> &[Entry] {
        &self.entries
    }

    pub fn hidden_files(&self) -> bool {
        self.show_hidden_files
    }

    pub fn set_hidden_files(&mut self, visible: bool) {
        self.show_hidden_files = visible;
    }

    pub fn show_file_sizes(&self) -> bool {
        self.show_file_sizes
    }

    pub fn set_show_file_sizes(&mut self, visible: bool) {
        self.show_file_sizes = visible;
    }

    pub fn selected_paths(&self) -> Vec<PathBuf> {
        self.selected.clone()
    }

    pub fn set_current_path(&mut self, path: PathBuf) {
        self.current_path = path;
    }

    pub fn is_selected(&self, path: &PathBuf) -> bool {
        self.selected.iter().any(|p| p == path)
    }

    pub fn toggle_selection(&mut self, path: &PathBuf, ctrl: bool, shift: bool) {
        if shift && !self.selected.is_empty() {
            let start_idx = self.entries.iter().position(|e| e.path == self.selected[0]).unwrap_or(0);
            let end_idx = self.entries.iter().position(|e| e.path == *path).unwrap_or(0);
            if start_idx <= end_idx {
                self.selected.clear();
                for entry in &self.entries[start_idx..=end_idx] {
                    self.selected.push(entry.path.clone());
                }
            } else {
                self.selected.clear();
                self.selected.push(path.clone());
            }
        } else if ctrl {
            if let Some(idx) = self.selected.iter().position(|p| p == path) {
                self.selected.remove(idx);
            } else {
                self.selected.push(path.clone());
            }
        } else {
            self.selected.clear();
            self.selected.push(path.clone());
        }
    }

    pub fn render(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        ui.horizontal(|ui| {
            ui.label("Filter:");
            ui.text_edit_singleline(&mut self.path_filter);
        });
        ui.separator();

        let filter = self.path_filter.to_lowercase();
        let mut opened: Option<PathBuf> = None;

        egui::ScrollArea::vertical().show(ui, |ui| {
            for idx in 0..self.entries.len() {
                let (name, path, _is_dir, icon) = {
                    let entry = &self.entries[idx];
                    (
                        entry.name.clone(),
                        entry.path.clone(),
                        entry.is_dir(),
                        entry.icon(),
                    )
                };

                if !filter.is_empty() && !name.to_lowercase().contains(&filter) {
                    continue;
                }

                let selected = self.is_selected(&path);
                let response = ui.selectable_label(selected, format!("{}  {}", icon, name));

                if response.clicked() {
                    let now = ctx.input(|i| i.time);
                    let ctrl = ctx.input(|i| i.modifiers.ctrl);
                    let shift = ctx.input(|i| i.modifiers.shift);
                    let double = !ctrl
                        && !shift
                        && self.last_single_click.as_ref() == Some(&path)
                        && now - self.last_click_time < 0.3;

                    if double {
                        opened = Some(path.clone());
                    } else {
                        self.toggle_selection(&path, ctrl, shift);
                    }
                    self.last_single_click = Some(path.clone());
                    self.last_click_time = now;
                }

                response.context_menu(|ui| {
                    if ui.button("Open").clicked() {
                        opened = Some(path.clone());
                        ui.close_menu();
                    }
                    if ui.button("Copy Path").clicked() {
                        ui.output_mut(|o| o.copied_text = path.display().to_string());
                        ui.close_menu();
                    }
                });
            }
        });

        if let Some(path) = opened {
            if path.is_dir() {
                self.navigate_request = Some(path);
            } else {
                self.open_file(&path);
            }
        }
    }

    fn open_file(&self, path: &PathBuf) {
        let _ = std::process::Command::new("xdg-open").arg(path).spawn();
    }
}

impl Default for Explorer {
    fn default() -> Self {
        Self::new()
    }
}