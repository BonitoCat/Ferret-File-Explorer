use std::env;
use std::ffi::OsStr;
use std::fs;
use std::io;
use std::path::{Component, Path, PathBuf};

/// Application directories. XDG variables are honoured verbatim, matching the
/// existing C# implementation (including relative values).
#[derive(Clone, Debug)]
pub struct AppPaths {
    home: PathBuf,
    data_root: PathBuf,
    config_root: PathBuf,
    cache_root: PathBuf,
}

impl AppPaths {
    pub fn discover() -> Self {
        let home = env::var_os("HOME")
            .filter(|value| !value.is_empty())
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from("/"));

        let data_root =
            env_dir("XDG_DATA_HOME").unwrap_or_else(|| home.join(".local").join("share"));
        let config_root = env_dir("XDG_CONFIG_HOME").unwrap_or_else(|| home.join(".config"));
        let cache_root = env_dir("XDG_CACHE_HOME").unwrap_or_else(|| home.join(".cache"));

        Self {
            home,
            data_root,
            config_root,
            cache_root,
        }
    }

    /// Construct paths from explicit roots. Used by tests and embedders.
    pub fn with_roots(
        home: PathBuf,
        data_root: PathBuf,
        config_root: PathBuf,
        cache_root: PathBuf,
    ) -> Self {
        Self {
            home,
            data_root,
            config_root,
            cache_root,
        }
    }

    pub fn home(&self) -> &Path {
        &self.home
    }

    pub fn data_root(&self) -> &Path {
        &self.data_root
    }

    pub fn config_root(&self) -> &Path {
        &self.config_root
    }

    pub fn cache_root(&self) -> &Path {
        &self.cache_root
    }

    pub fn data_dir(&self) -> PathBuf {
        self.data_root.join("fe")
    }

    pub fn config_dir(&self) -> PathBuf {
        self.config_root.join("fe")
    }

    pub fn cache_dir(&self) -> PathBuf {
        self.cache_root.join("fe")
    }

    pub fn ensure_data_dir(&self) -> io::Result<PathBuf> {
        let dir = self.data_dir();
        fs::create_dir_all(&dir)?;
        Ok(dir)
    }

    pub fn data_path(&self, name: impl AsRef<Path>) -> io::Result<PathBuf> {
        let dir = self.ensure_data_dir()?;
        Ok(dir.join(name))
    }

    pub fn data_subdir(&self, name: impl AsRef<Path>) -> io::Result<PathBuf> {
        let dir = self.data_dir().join(name);
        fs::create_dir_all(&dir)?;
        Ok(dir)
    }

    pub fn cache_subdir(&self, name: impl AsRef<Path>) -> io::Result<PathBuf> {
        let dir = self.cache_dir().join(name);
        fs::create_dir_all(&dir)?;
        Ok(dir)
    }

    /// Move old `$XDG_CONFIG_HOME/fe` contents into the data directory. This
    /// mirrors `AppPaths.MigrateLegacyConfig` closely enough for settings,
    /// bookmarks and addon state. Failures are deliberately ignored.
    pub fn migrate_legacy_config(&self) {
        let legacy = self.config_dir();
        let data = self.data_dir();
        if legacy == data || !legacy.is_dir() {
            return;
        }

        let _ = fs::create_dir_all(&data);
        if let Ok(entries) = fs::read_dir(&legacy) {
            for entry in entries.flatten() {
                let target = data.join(entry.file_name());
                if !target.exists() && fs::symlink_metadata(&target).is_err() {
                    let _ = fs::rename(entry.path(), target);
                }
            }
        }

        if fs::read_dir(&legacy)
            .map(|mut entries| entries.next().is_none())
            .unwrap_or(false)
        {
            let _ = fs::remove_dir(&legacy);
        }
    }
}

fn env_dir(name: &str) -> Option<PathBuf> {
    env::var_os(name)
        .filter(|value| !value.is_empty())
        .map(PathBuf::from)
}

/// Expand `~` and `~/...` using the supplied home directory.
pub fn expand_home(path: &str, home: &Path) -> PathBuf {
    if path == "~" {
        return home.to_path_buf();
    }
    if let Some(rest) = path.strip_prefix("~/") {
        return home.join(rest);
    }
    PathBuf::from(path)
}

/// Resolve a path to an absolute, symlink-free path when the target exists.
/// Non-existent paths are normalised lexically so `..` still behaves.
pub fn normalize_path(path: &Path) -> PathBuf {
    fs::canonicalize(path).unwrap_or_else(|_| lexical_absolute(path))
}

pub fn lexical_absolute(path: &Path) -> PathBuf {
    let base = if path.is_absolute() {
        PathBuf::new()
    } else {
        env::current_dir().unwrap_or_else(|_| PathBuf::from("/"))
    };
    let joined = base.join(path);
    let mut out = PathBuf::new();

    for component in joined.components() {
        match component {
            Component::Prefix(prefix) => out.push(prefix.as_os_str()),
            Component::RootDir => out.push(OsStr::new("/")),
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            Component::Normal(part) => out.push(part),
        }
    }

    if out.as_os_str().is_empty() {
        PathBuf::from("/")
    } else {
        out
    }
}

pub fn is_root(path: &Path) -> bool {
    path.parent().is_none() || path == Path::new("/")
}

pub fn parent_of(path: &Path) -> Option<PathBuf> {
    if is_root(path) {
        return None;
    }
    path.parent().map(|parent| {
        if parent.as_os_str().is_empty() {
            PathBuf::from("/")
        } else {
            parent.to_path_buf()
        }
    })
}

pub fn file_name(path: &Path) -> String {
    path.file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_else(|| {
            if is_root(path) {
                "/".to_string()
            } else {
                path.to_string_lossy().into_owned()
            }
        })
}

pub fn display_name(path: &Path) -> String {
    if is_root(path) {
        "File System".to_string()
    } else {
        file_name(path)
    }
}

/// True when `path` is `ancestor` or lives below it. `Path::starts_with` is
/// component-aware, so `/foo` does not match `/foobar`.
pub fn is_same_or_ancestor(ancestor: &Path, path: &Path) -> bool {
    path.starts_with(ancestor)
}

pub fn is_inside(container: &Path, path: &Path) -> bool {
    path != container && path.starts_with(container)
}

pub fn is_remote(path: &Path) -> bool {
    let text = path.to_string_lossy();
    text.starts_with("/run/user/") && text.contains("/gvfs/")
}

pub fn gvfs_root() -> PathBuf {
    #[cfg(unix)]
    let uid = unsafe { libc::getuid() };
    #[cfg(not(unix))]
    let uid = 0;

    PathBuf::from(format!("/run/user/{uid}/gvfs"))
}

pub fn is_privileged() -> bool {
    #[cfg(unix)]
    {
        unsafe { libc::geteuid() == 0 }
    }
    #[cfg(not(unix))]
    {
        false
    }
}

/// Find a free `name (n).ext` path next to `path`.
pub fn unique_path(path: &Path) -> PathBuf {
    if !path_exists(path) {
        return path.to_path_buf();
    }

    let is_file = fs::symlink_metadata(path)
        .map(|metadata| metadata.file_type().is_file())
        .unwrap_or(false);
    let parent = path.parent().unwrap_or_else(|| Path::new("/"));
    let name = path
        .file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_else(|| "item".to_string());

    let (stem, extension) = if is_file {
        split_extension(&name)
    } else {
        (name.as_str(), "")
    };

    for index in 1u64.. {
        let candidate = parent.join(format!("{stem} ({index}){extension}"));
        if !path_exists(&candidate) {
            return candidate;
        }
    }

    unreachable!()
}

pub fn split_extension(name: &str) -> (&str, &str) {
    match name.rfind('.') {
        Some(index) if index > 0 => (&name[..index], &name[index..]),
        _ => (name, ""),
    }
}

pub fn path_exists(path: &Path) -> bool {
    fs::symlink_metadata(path).is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lexical_normalisation_handles_parents() {
        assert_eq!(lexical_absolute(Path::new("/a/b/../c")), Path::new("/a/c"));
        assert_eq!(lexical_absolute(Path::new("/")), Path::new("/"));
    }

    #[test]
    fn split_extension_keeps_dotfiles_intact() {
        assert_eq!(split_extension(".bashrc"), (".bashrc", ""));
        assert_eq!(split_extension("archive.tar.gz"), ("archive.tar", ".gz"));
        assert_eq!(split_extension("README"), ("README", ""));
    }
}
