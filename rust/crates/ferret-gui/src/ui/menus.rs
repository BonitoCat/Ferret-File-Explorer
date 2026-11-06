use eframe::egui;

pub struct Menus {}

impl Menus {
    pub fn new() -> Self {
        Self {}
    }
}

impl Menus {
    pub fn render_context_menu(_ui: &mut egui::Ui) {
        // Context menus are handled by explorer.rs directly
    }
}

impl Default for Menus {
    fn default() -> Self {
        Self::new()
    }
}