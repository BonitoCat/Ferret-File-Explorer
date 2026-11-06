use eframe::egui;

fn main() -> Result<(), eframe::Error> {
    let native_options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1200.0, 800.0])
            .with_min_inner_size([800.0, 600.0]),
        ..Default::default()
    };

    eframe::run_native(
        "Ferret File Explorer",
        native_options,
        Box::new(|_cc| Ok(Box::new(ferret_gui::FerretApp::new())))
    )
}