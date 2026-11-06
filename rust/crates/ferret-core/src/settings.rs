use std::collections::BTreeMap;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use crate::paths::AppPaths;

/// Small `key=value` settings file compatible with the existing CLI settings.
#[derive(Clone, Debug)]
pub struct Settings {
    path: PathBuf,
    values: BTreeMap<String, String>,
}

impl Settings {
    pub fn load_for(paths: &AppPaths) -> Self {
        let path = paths.data_dir().join("cli_settings");
        let legacy = paths.data_dir().join("cli_sidebar");
        Self::load_with_legacy(path, Some(legacy))
    }

    pub fn load(path: PathBuf) -> Self {
        Self::load_with_legacy(path, None)
    }

    pub fn load_with_legacy(path: PathBuf, legacy_sidebar: Option<PathBuf>) -> Self {
        let mut values = BTreeMap::new();

        if let Some(legacy) = legacy_sidebar {
            if let Ok(text) = fs::read_to_string(legacy) {
                values.insert(
                    "sidebar".to_string(),
                    if text.trim() == "hidden" {
                        "false"
                    } else {
                        "true"
                    }
                    .to_string(),
                );
            }
        }

        if let Ok(text) = fs::read_to_string(&path) {
            for line in text.lines() {
                if let Some((key, value)) = line.split_once('=') {
                    let key = key.trim();
                    if !key.is_empty() {
                        values.insert(key.to_string(), value.trim().to_string());
                    }
                }
            }
        }

        Self { path, values }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn sidebar_visible(&self) -> bool {
        self.get_bool("sidebar", true)
    }

    pub fn set_sidebar_visible(&mut self, visible: bool) {
        self.set_bool("sidebar", visible);
    }

    pub fn show_hidden_files(&self) -> bool {
        self.get_bool("hidden_files", true)
    }

    pub fn set_show_hidden_files(&mut self, visible: bool) {
        self.set_bool("hidden_files", visible);
    }

    pub fn get(&self, key: &str) -> Option<&str> {
        self.values.get(key).map(String::as_str)
    }

    pub fn get_bool(&self, key: &str, fallback: bool) -> bool {
        self.values
            .get(key)
            .map(|value| value == "true")
            .unwrap_or(fallback)
    }

    pub fn set_bool(&mut self, key: &str, value: bool) {
        self.values.insert(
            key.to_string(),
            if value { "true" } else { "false" }.to_string(),
        );
    }

    pub fn save(&self) -> io::Result<()> {
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }

        let mut text = String::new();
        for (key, value) in &self.values {
            text.push_str(key);
            text.push('=');
            text.push_str(value);
            text.push('\n');
        }

        let temporary = self.path.with_file_name(format!(
            ".{}.tmp",
            self.path
                .file_name()
                .map(|name| name.to_string_lossy().into_owned())
                .unwrap_or_else(|| "cli_settings".to_string())
        ));
        fs::write(&temporary, text)?;
        fs::rename(temporary, &self.path)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn temp_dir() -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let dir = std::env::temp_dir().join(format!("ferret-settings-{nanos}"));
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn settings_roundtrip() {
        let dir = temp_dir();
        let path = dir.join("cli_settings");
        let mut settings = Settings::load(path.clone());
        assert!(settings.sidebar_visible());
        assert!(settings.show_hidden_files());

        settings.set_sidebar_visible(false);
        settings.set_show_hidden_files(false);
        settings.save().unwrap();

        let loaded = Settings::load(path);
        assert!(!loaded.sidebar_visible());
        assert!(!loaded.show_hidden_files());

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn legacy_sidebar_value_is_read() {
        let dir = temp_dir();
        let legacy = dir.join("cli_sidebar");
        fs::write(&legacy, "hidden\n").unwrap();
        let settings = Settings::load_with_legacy(dir.join("cli_settings"), Some(legacy));
        assert!(!settings.sidebar_visible());
        fs::remove_dir_all(dir).unwrap();
    }
}
