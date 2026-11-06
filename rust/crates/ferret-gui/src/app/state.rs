use eframe::egui;
use crate::app::navigation::Navigation;
use crate::app::operations::Operations;
use crate::app::file_operations::FileOperations;
use crate::ui::sidebar::Sidebar;
use crate::ui::status::StatusBar;
use crate::ui::explorer::Explorer;
use crate::ui::dialogs::Dialogs;
use crate::ui::toolbar::Toolbar;
use crate::ui::menus::Menus;
use crate::{listing, ListOptions, PasteMode, AppPaths, core_operations, paths, settings, bookmarks};

pub struct FerretApp {
    pub current_path: std::path::PathBuf,
    pub sidebar_visible: bool,
    pub status_message: String,
    pub error_message: Option<String>,
    pub paths: AppPaths,

    pub navigation: Navigation,
    pub file_ops: FileOperations,
    pub operations: Operations,
    pub explorer: Explorer,
    pub sidebar: Sidebar,
    pub dialogs: Dialogs,
    pub status_bar: StatusBar,
    pub toolbar: Toolbar,
    pub menus: Menus,
}

impl FerretApp {
    pub fn new() -> Self {
        let paths = AppPaths::discover();
        let _ = paths.ensure_data_dir();

        let cfg = settings::Settings::load_for(&paths);
        let sidebar_visible = cfg.sidebar_visible();
        let hidden_files = cfg.show_hidden_files();
        let start_dir: Option<String> = cfg.get("start_directory").map(|dir| dir.to_string());

        let current_path = start_dir
            .map(std::path::PathBuf::from)
            .or_else(|| dirs::home_dir())
            .unwrap_or_else(|| std::path::PathBuf::from("/"));

        let navigation = Navigation::new(&current_path);

        let mut app = Self {
            current_path,
            sidebar_visible,
            status_message: String::new(),
            error_message: None,
            paths,
            navigation,
            file_ops: FileOperations::new(),
            operations: Operations::new(),
            explorer: Explorer::new(),
            sidebar: Sidebar::new(),
            dialogs: Dialogs::new(),
            status_bar: StatusBar::new(),
            toolbar: Toolbar::new(),
            menus: Menus::new(),
        };

        app.explorer.set_hidden_files(hidden_files);
        app.sidebar.load_places(&app.paths);
        app.load_command_line_args();
        app.load_directory();

        app
    }

    fn load_command_line_args(&mut self) {
        let mut args = std::env::args();
        while let Some(arg) = args.next() {
            if arg == "-d" || arg == "--directory" {
                if let Some(dir) = args.next() {
                    self.current_path = std::path::PathBuf::from(dir);
                    self.navigation.navigate_to(&self.current_path);
                }
            }
        }
    }

    pub fn navigate_to(&mut self, path: &std::path::Path) {
        self.current_path = path.to_path_buf();
        self.navigation.navigate_to(&self.current_path);
        self.load_directory();
    }

    pub fn navigate_back(&mut self) -> bool {
        if self.navigation.go_back() {
            let current = self.navigation.current().to_path_buf();
            self.navigate_to(&current);
            true
        } else {
            false
        }
    }

    pub fn navigate_forward(&mut self) -> bool {
        if self.navigation.go_forward() {
            let current = self.navigation.current().to_path_buf();
            self.navigate_to(&current);
            true
        } else {
            false
        }
    }

    pub fn navigate_up(&mut self) {
        if let Some(parent) = self.current_path.parent().map(|p| p.to_path_buf()) {
            self.navigate_to(&parent);
        }
    }

    pub fn navigate_to_home(&mut self) {
        if let Some(home) = dirs::home_dir() {
            self.navigate_to(&home);
        }
    }

    pub fn load_directory(&mut self) {
        let options = ListOptions {
            show_hidden: self.explorer.hidden_files(),
            search: None,
        };
        match listing::list_directory(&self.current_path, &options) {
            Ok(entries) => {
                self.explorer.set_entries(entries);
                self.status_message = format!("{} items", self.explorer.entries().len());
            }
            Err(e) => {
                self.status_message = format!("Error: {}", e);
                self.show_error(format!("Cannot read directory {}: {}", self.current_path.display(), e));
            }
        }
    }

    pub fn refresh_current_dir(&mut self) {
        self.status_bar.set_message("Refreshing...", crate::ui::status::Status::Processing);
        self.load_directory();
        self.status_bar.set_message("Ready", crate::ui::status::Status::Ready);
    }

    pub fn copy_selected(&mut self) {
        let paths: Vec<std::path::PathBuf> = self.explorer.selected_paths();
        if paths.is_empty() { return; }
        self.file_ops.copy(paths);
        self.status_message = "Copied to clipboard".to_string();
        self.status_bar.set_message("Copied to clipboard", crate::ui::status::Status::Success);
    }

    pub fn cut_selected(&mut self) {
        let paths: Vec<std::path::PathBuf> = self.explorer.selected_paths();
        if paths.is_empty() { return; }
        self.file_ops.cut(paths);
        self.status_message = "Cut to clipboard".to_string();
        self.status_bar.set_message("Cut to clipboard", crate::ui::status::Status::Success);
    }

    pub fn paste_selected(&mut self) {
        let mode = self.file_ops.operation_type();
        let sources = self.file_ops.clipboard();
        if sources.is_empty() { return; }
        let paste_mode = match mode {
            crate::app::file_operations::OperationType::Copy => PasteMode::Copy,
            crate::app::file_operations::OperationType::Cut => PasteMode::Cut,
        };
        let report = core_operations::paste(&self.current_path, paste_mode, &sources);
        if report.errors.is_empty() {
            let count = report.targets.len();
            self.status_message = format!("Pasted {} item{}", count, if count == 1 { "" } else { "s" });
            self.status_bar.set_message(&self.status_message, crate::ui::status::Status::Success);
            if mode == crate::app::file_operations::OperationType::Cut {
                self.file_ops.clear_clipboard();
            }
        } else {
            for err in &report.errors {
                self.show_error(err.clone());
            }
            self.status_message = if report.errors.is_empty() { "Done." } else { "Some failures." }.to_string();
            self.status_bar.set_message(&self.status_message, crate::ui::status::Status::Error);
        }
        self.load_directory();
    }

    pub fn delete_selected(&mut self) {
        let paths: Vec<std::path::PathBuf> = self.explorer.selected_paths();
        if paths.is_empty() { return; }
        let report = core_operations::trash(&paths);
        if report.errors.is_empty() {
            let count = report.targets.len();
            self.status_message = format!("Moved {} item{} to trash", count, if count == 1 { "" } else { "s" });
            self.status_bar.set_message(&self.status_message, crate::ui::status::Status::Success);
        } else {
            for err in &report.errors {
                self.show_error(err.clone());
            }
            self.status_message = if report.errors.is_empty() { "Done." } else { "Some failures." }.to_string();
            self.status_bar.set_message(&self.status_message, crate::ui::status::Status::Error);
        }
        self.load_directory();
    }

    pub fn rename_selected(&mut self, new_name: &str) {
        let paths: Vec<std::path::PathBuf> = self.explorer.selected_paths();
        if paths.is_empty() { return; }
        for path in &paths {
            if let Err(e) = core_operations::rename(path, new_name) {
                self.show_error(format!("Rename failed for {}: {}", path.display(), e));
            }
        }
        self.load_directory();
        self.status_message = "Renamed".to_string();
        self.status_bar.set_message("Renamed", crate::ui::status::Status::Success);
    }

    pub fn duplicate_selected(&mut self) {
        let paths: Vec<std::path::PathBuf> = self.explorer.selected_paths();
        if paths.is_empty() { return; }
        for path in &paths {
            let dest = paths::unique_path(path);
            if let Err(e) = core_operations::copy_entry(path, &dest) {
                self.show_error(format!("Failed to duplicate {}: {}", path.display(), e));
            }
        }
        self.load_directory();
        self.status_message = "Duplicated".to_string();
        self.status_bar.set_message("Duplicated", crate::ui::status::Status::Success);
    }

    pub fn create_file(&mut self, name: &str) {
        let path = self.current_path.join(name);
        if let Err(e) = core_operations::create_file(&path) {
            self.show_error(format!("Create file failed: {}", e));
        } else {
            self.load_directory();
            self.status_message = "Created file".to_string();
            self.status_bar.set_message("Created file", crate::ui::status::Status::Success);
        }
    }

    pub fn create_directory(&mut self, name: &str) {
        let path = self.current_path.join(name);
        if let Err(e) = core_operations::create_dir(&path) {
            self.show_error(format!("Create directory failed: {}", e));
        } else {
            self.load_directory();
            self.status_message = "Created directory".to_string();
            self.status_bar.set_message("Created directory", crate::ui::status::Status::Success);
        }
    }

    pub fn toggle_hidden_files(&mut self) {
        let new_val = !self.explorer.hidden_files();
        self.explorer.set_hidden_files(new_val);
        self.load_directory();
        self.status_message = if new_val { "Showing hidden files" } else { "Hiding hidden files" }.to_string();
        self.status_bar.set_message(&self.status_message, crate::ui::status::Status::Success);
    }

    pub fn toggle_sidebar(&mut self) {
        self.sidebar_visible = !self.sidebar_visible;
    }

    pub fn add_bookmark(&mut self) {
        let bookmark_dir = self.paths.data_dir().join("fe").join("bookmarks");
        let store = bookmarks::BookmarkStore::new(bookmark_dir);
        match store.toggle(&self.current_path) {
            Ok(added) => {
                if added {
                    self.sidebar.add_bookmark(self.current_path.clone());
                    self.status_message = "Bookmark added".to_string();
                    self.status_bar.set_message("Bookmark added", crate::ui::status::Status::Success);
                } else {
                    self.sidebar.remove_bookmark(&self.current_path);
                    self.status_message = "Bookmark removed".to_string();
                    self.status_bar.set_message("Bookmark removed", crate::ui::status::Status::Success);
                }
            }
            Err(e) => self.show_error(format!("Bookmark failed: {}", e)),
        }
    }

    pub fn save_settings(&mut self) {
        let path = self.paths.data_dir().join("fe").join("cli_settings");
        let mut core_settings = settings::Settings::load(path.clone());
        core_settings.set_sidebar_visible(self.sidebar_visible);
        core_settings.set_show_hidden_files(self.explorer.hidden_files());
        let _ = core_settings.save();
    }

    pub fn show_error(&mut self, error: String) {
        self.error_message = Some(error);
    }

    pub fn clear_error(&mut self) {
        self.error_message = None;
    }

    pub fn status_color(&self) -> egui::Color32 {
        match self.status_bar.status() {
            crate::ui::status::Status::Ready => egui::Color32::GRAY,
            crate::ui::status::Status::Processing => egui::Color32::YELLOW,
            crate::ui::status::Status::Error => egui::Color32::RED,
            crate::ui::status::Status::Success => egui::Color32::GREEN,
        }
    }
}

impl eframe::App for FerretApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.operations.update(ctx);
        self.ui(ctx);
    }
}

impl FerretApp {
    pub fn ui(&mut self, ctx: &egui::Context) {
        self.top_menu_bar(ctx);
        self.main_layout(ctx);
        self.status_bar_render(ctx);
        self.render_dialogs(ctx);
        if let Some(ref err) = self.error_message {
            self.show_error_popup(ctx, err.clone());
        }
    }

    fn render_dialogs(&mut self, ctx: &egui::Context) {
        let mut dialogs = std::mem::take(&mut self.dialogs);
        let mut rename: Option<String> = None;
        let mut new_dir: Option<String> = None;
        let mut new_file: Option<String> = None;
        let mut do_delete = false;

        dialogs.render_rename(ctx, &mut |name| rename = Some(name.to_string()));
        dialogs.render_new_directory(ctx, &mut |name| new_dir = Some(name.to_string()));
        dialogs.render_new_file(ctx, &mut |name| new_file = Some(name.to_string()));
        dialogs.render_delete_confirmation(ctx, &mut || do_delete = true);
        dialogs.render_properties(ctx);

        self.dialogs = dialogs;

        if let Some(name) = rename {
            self.rename_selected(&name);
        }
        if let Some(name) = new_dir {
            self.create_directory(&name);
        }
        if let Some(name) = new_file {
            self.create_file(&name);
        }
        if do_delete {
            self.delete_selected();
        }
    }

    fn top_menu_bar(&mut self, ctx: &egui::Context) {
        egui::TopBottomPanel::top("top_menu_bar").show(ctx, |ui| {
            egui::menu::bar(ui, |ui| {
                ui.menu_button("File", |ui| {
                    if ui.button("New Directory...").clicked() {
                        self.dialogs.show_new_directory = true;
                    }
                    if ui.button("New File...").clicked() {
                        self.dialogs.show_new_file = true;
                    }
                    ui.separator();
                    if ui.button("Refresh").clicked() { self.refresh_current_dir(); }
                    ui.separator();
                    if ui.button("Exit").clicked() { std::process::exit(0); }
                });
                ui.menu_button("Edit", |ui| {
                    if ui.button("Copy").clicked() { self.copy_selected(); }
                    if ui.button("Cut").clicked() { self.cut_selected(); }
                    if ui.button("Paste").clicked() { self.paste_selected(); }
                    ui.separator();
                    if ui.button("Duplicate").clicked() { self.duplicate_selected(); }
                    if ui.button("Rename...").clicked() {
                        if let Some(first) = self.explorer.selected_paths().first() {
                            self.dialogs.rename_input = first
                                .file_name()
                                .unwrap_or_default()
                                .to_string_lossy()
                                .to_string();
                            self.dialogs.show_rename = true;
                        }
                    }
                    ui.separator();
                    if ui.button("Delete").clicked() {
                        let count = self.explorer.selected_paths().len();
                        if count > 0 {
                            self.dialogs.delete_count = count;
                            self.dialogs.show_delete = true;
                        }
                    }
                    ui.separator();
                    if ui.button("Properties").clicked() {
                        if let Some(first) = self.explorer.selected_paths().first() {
                            self.dialogs.properties_path = Some(first.clone());
                            self.dialogs.show_properties = true;
                        }
                    }
                });
                ui.menu_button("View", |ui| {
                    ui.checkbox(&mut self.sidebar_visible, "Sidebar");
                    if ui.button("Toggle Hidden Files").clicked() { self.toggle_hidden_files(); }
                    ui.separator();
                    if ui.button("Refresh").clicked() { self.refresh_current_dir(); }
                });
                ui.menu_button("Navigate", |ui| {
                    if ui.button("Back").clicked() { self.navigate_back(); }
                    if ui.button("Forward").clicked() { self.navigate_forward(); }
                    if ui.button("Up").clicked() { self.navigate_up(); }
                    if ui.button("Home").clicked() { self.navigate_to_home(); }
                });
                ui.menu_button("Bookmarks", |ui| {
                    if ui.button("Add Bookmark").clicked() { self.add_bookmark(); }
                });
            });
        });
    }

    fn main_layout(&mut self, ctx: &egui::Context) {
        if self.sidebar_visible {
            egui::SidePanel::left("sidebar_panel")
                .resizable(true)
                .default_width(220.0)
                .show(ctx, |ui| {
                    self.left_panel(ui);
                });
        }
        egui::CentralPanel::default().show(ctx, |ui| {
            self.explorer_panel(ui, ctx);
        });
    }

    fn left_panel(&mut self, ui: &mut egui::Ui) {
        egui::Frame::group(ui.style())
            .show(ui, |ui| {
                ui.vertical(|ui| {
                    ui.heading("Location");
                    ui.horizontal(|ui| {
                        if ui.button("←").clicked() { self.navigate_back(); }
                        if ui.button("↑").clicked() { self.navigate_up(); }
                        ui.add(egui::Label::new(format!("{}", self.current_path.display())));
                    });
                    ui.separator();

                    ui.horizontal(|ui| {
                        if ui.button("Home").clicked() { self.navigate_to_home(); }
                        if ui.button("Refresh").clicked() { self.refresh_current_dir(); }
                    });
                    ui.separator();

                    self.sidebar.render(ui);
                });
            });

        if let Some(path) = self.sidebar.navigate_request.take() {
            self.navigate_to(&path);
        }
    }

    fn explorer_panel(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        egui::Frame::group(ui.style())
            .show(ui, |ui| {
                ui.heading("Files");
                ui.separator();
                self.explorer.render(ui, ctx);
            });
        if let Some(path) = self.explorer.navigate_request.take() {
            self.navigate_to(&path);
        }
    }

    fn status_bar_render(&mut self, ctx: &egui::Context) {
        egui::TopBottomPanel::bottom("status_bar").show(ctx, |ui| {
            ui.horizontal(|ui| {
                let color = self.status_color();
                ui.colored_label(color, &format!("Status: {}", self.status_bar.message()));
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    ui.label("Ready");
                });
            });
        });
    }

    fn show_error_popup(&mut self, ctx: &egui::Context, error: String) {
        egui::Window::new("Error")
            .collapsible(false)
            .resizable(false)
            .show(ctx, |ui| {
                ui.label("An error occurred:");
                ui.separator();
                ui.label(&error);
                ui.separator();
                if ui.button("OK").clicked() { self.clear_error(); }
            });
    }
}