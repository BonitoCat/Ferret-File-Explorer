use eframe::egui;
use std::os::unix::fs::PermissionsExt;

pub struct Dialogs {
    pub show_rename: bool,
    pub show_new_directory: bool,
    pub show_new_file: bool,
    pub show_delete: bool,
    pub show_properties: bool,
    pub rename_input: String,
    pub new_directory_input: String,
    pub new_file_input: String,
    pub delete_count: usize,
    pub properties_path: Option<std::path::PathBuf>,
}

impl Dialogs {
    pub fn new() -> Self {
        Self {
            show_rename: false,
            show_new_directory: false,
            show_new_file: false,
            show_delete: false,
            show_properties: false,
            rename_input: String::new(),
            new_directory_input: String::new(),
            new_file_input: String::new(),
            delete_count: 0,
            properties_path: None,
        }
    }

    pub fn render_rename(&mut self, ctx: &egui::Context, on_ok: &mut dyn FnMut(&str)) {
        if !self.show_rename { return; }
        egui::Window::new("Rename")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label("Enter new name:");
                ui.text_edit_singleline(&mut self.rename_input);
                ui.separator();
                ui.horizontal(|ui| {
                    if ui.button("Cancel").clicked() {
                        self.show_rename = false;
                        self.rename_input.clear();
                    }
                    if ui.button("OK").clicked() {
                        on_ok(&self.rename_input);
                        self.show_rename = false;
                        self.rename_input.clear();
                    }
                });
            });
    }

    pub fn render_new_directory(&mut self, ctx: &egui::Context, on_ok: &mut dyn FnMut(&str)) {
        if !self.show_new_directory { return; }
        egui::Window::new("New Directory")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label("Enter directory name:");
                ui.text_edit_singleline(&mut self.new_directory_input);
                ui.separator();
                ui.horizontal(|ui| {
                    if ui.button("Cancel").clicked() {
                        self.show_new_directory = false;
                        self.new_directory_input.clear();
                    }
                    if ui.button("OK").clicked() {
                        on_ok(&self.new_directory_input);
                        self.show_new_directory = false;
                        self.new_directory_input.clear();
                    }
                });
            });
    }

    pub fn render_new_file(&mut self, ctx: &egui::Context, on_ok: &mut dyn FnMut(&str)) {
        if !self.show_new_file { return; }
        egui::Window::new("New File")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label("Enter file name:");
                ui.text_edit_singleline(&mut self.new_file_input);
                ui.separator();
                ui.horizontal(|ui| {
                    if ui.button("Cancel").clicked() {
                        self.show_new_file = false;
                        self.new_file_input.clear();
                    }
                    if ui.button("OK").clicked() {
                        on_ok(&self.new_file_input);
                        self.show_new_file = false;
                        self.new_file_input.clear();
                    }
                });
            });
    }

    pub fn render_delete_confirmation(&mut self, ctx: &egui::Context, on_ok: &mut dyn FnMut()) {
        if !self.show_delete { return; }
        egui::Window::new("Confirm Delete")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label(format!("Are you sure you want to delete {} item(s)?", self.delete_count));
                ui.separator();
                ui.horizontal(|ui| {
                    if ui.button("Cancel").clicked() {
                        self.show_delete = false;
                    }
                    if ui.button("Delete").clicked() {
                        on_ok();
                        self.show_delete = false;
                    }
                });
            });
    }

    pub fn render_properties(&mut self, ctx: &egui::Context) {
        if !self.show_properties { return; }
        let Some(path) = &self.properties_path else { return; };
        egui::Window::new("Properties")
            .collapsible(true)
            .resizable(true)
            .show(ctx, |ui| {
                ui.heading(&path.display().to_string());
                ui.separator();
                if let Ok(meta) = std::fs::symlink_metadata(path) {
                    ui.label(format!("Name: {}", path.file_name().unwrap_or_default().to_string_lossy()));
                    ui.label(format!("Path: {}", path.display()));
                    if meta.file_type().is_symlink() {
                        if let Ok(target) = std::fs::read_link(path) {
                            ui.label(format!("Link target: {}", target.display()));
                        }
                    }
                    ui.label(format!("Size: {}", crate::format::format_size(meta.len())));
                    let mode = meta.permissions().mode();
                    ui.label(format!("Permissions: {}", crate::format::format_permissions(mode)));
                    if let Ok(modified) = meta.modified() {
                        ui.label(format!("Modified: {}", crate::format::format_time(modified)));
                    }
                }
            });
    }
}

impl Default for Dialogs {
    fn default() -> Self {
        Self::new()
    }
}