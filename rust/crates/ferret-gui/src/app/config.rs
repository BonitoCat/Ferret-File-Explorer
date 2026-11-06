use crate::settings;
use crate::paths::AppPaths;
use std::path::PathBuf;

#[derive(Debug, Clone)]
pub struct AppSettings {
    pub sidebar_visible: bool,
    pub hidden_files: bool,
    pub show_file_sizes: bool,
    pub start_directory: Option<String>,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            sidebar_visible: true,
            hidden_files: false,
            show_file_sizes: true,
            start_directory: None,
        }
    }
}

pub struct SettingsManager {
    path: PathBuf,
}

impl SettingsManager {
    pub fn new(paths: &AppPaths) -> Self {
        let path = paths.data_dir().join("fe").join("cli_settings");
        Self { path }
    }

    pub fn load(&self) -> Result<AppSettings, Box<dyn std::error::Error>> {
        let core_settings = settings::Settings::load(self.path.clone());
        Ok(AppSettings {
            sidebar_visible: core_settings.sidebar_visible(),
            hidden_files: core_settings.show_hidden_files(),
            show_file_sizes: true,
            start_directory: core_settings.get("start_directory").map(|s| s.to_string()),
        })
    }

    pub fn save(&self, settings: &AppSettings) -> Result<(), Box<dyn std::error::Error>> {
        let mut core = settings::Settings::load(self.path.clone());
        core.set_sidebar_visible(settings.sidebar_visible);
        core.set_show_hidden_files(settings.hidden_files);
        core.save()?;
        Ok(())
    }
}