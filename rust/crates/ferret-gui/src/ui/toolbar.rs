use eframe::egui;

pub struct Toolbar {}

impl Toolbar {
    pub fn new() -> Self {
        Self {}
    }

    pub fn render(&mut self, ui: &mut egui::Ui) {
        ui.horizontal(|ui| {
            if ui.button("← Back").clicked() {
                // Navigate back
            }
            if ui.button("→ Forward").clicked() {
                // Navigate forward
            }
            if ui.button("↑ Up").clicked() {
                // Navigate up
            }
            if ui.button("Refresh").clicked() {
                // Refresh
            }
        });
    }
}

impl Default for Toolbar {
    fn default() -> Self {
        Self::new()
    }
}