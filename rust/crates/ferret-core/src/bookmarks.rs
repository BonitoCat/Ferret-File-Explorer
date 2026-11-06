use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use crate::natural::compare_names;
use crate::paths::{file_name, is_remote, lexical_absolute, normalize_path, path_exists};

#[derive(Clone, Debug)]
pub struct Bookmark {
    pub link: PathBuf,
    pub target: PathBuf,
    pub name: String,
}

#[derive(Clone, Debug)]
pub struct BookmarkStore {
    dir: PathBuf,
}

impl BookmarkStore {
    pub fn new(dir: PathBuf) -> Self {
        Self { dir }
    }

    pub fn dir(&self) -> &Path {
        &self.dir
    }

    pub fn ensure_dir(&self) -> io::Result<()> {
        fs::create_dir_all(&self.dir)
    }

    pub fn is_bookmark_dir(&self, path: &Path) -> bool {
        normalize_path(&self.dir) == normalize_path(path)
    }

    pub fn entries(&self) -> Vec<Bookmark> {
        let mut result = Vec::new();
        let Ok(entries) = fs::read_dir(&self.dir) else {
            return result;
        };

        for entry in entries.flatten() {
            let link = entry.path();
            let target = resolve_target(&link);
            if !is_remote(&target) && !target.is_dir() {
                continue;
            }

            result.push(Bookmark {
                name: file_name(&target),
                link,
                target,
            });
        }

        result.sort_by(|a, b| compare_names(&a.name, &b.name));
        result
    }

    /// Add the target, or remove it when it is already bookmarked.
    pub fn toggle(&self, target: &Path) -> io::Result<bool> {
        if let Some(link) = self.find_link(target) {
            remove_link(&link)?;
            return Ok(false);
        }

        self.ensure_dir()?;
        let link = self.unique_link_path(target);
        make_symlink(&normalize_target(target), &link)?;
        Ok(true)
    }

    pub fn remove(&self, target: &Path) -> io::Result<bool> {
        let Some(link) = self.find_link(target) else {
            return Ok(false);
        };
        remove_link(&link)?;
        Ok(true)
    }

    pub fn find_link(&self, target: &Path) -> Option<PathBuf> {
        let wanted = normalize_target(target);
        let Ok(entries) = fs::read_dir(&self.dir) else {
            return None;
        };

        for entry in entries.flatten() {
            let link = entry.path();
            let raw = resolve_target(&link);
            if normalize_target(&raw) == wanted {
                return Some(link);
            }
        }

        None
    }

    fn unique_link_path(&self, target: &Path) -> PathBuf {
        let mut name = if target == Path::new("/") {
            "root".to_string()
        } else {
            file_name(target)
        };
        if name.is_empty() || name == "/" {
            name = "root".to_string();
        }

        let mut candidate = self.dir.join(&name);
        for index in 2u64.. {
            if !path_exists(&candidate) {
                break;
            }
            candidate = self.dir.join(format!("{name} ({index})"));
        }
        candidate
    }
}

pub fn resolve_target(link: &Path) -> PathBuf {
    let Ok(raw) = fs::read_link(link) else {
        return link.to_path_buf();
    };

    let absolute = if raw.is_absolute() {
        raw
    } else {
        link.parent().map(|parent| parent.join(&raw)).unwrap_or(raw)
    };

    if is_remote(&absolute) {
        return lexical_absolute(&absolute);
    }

    normalize_path(&absolute)
}

fn normalize_target(target: &Path) -> PathBuf {
    if is_remote(target) {
        lexical_absolute(target)
    } else {
        normalize_path(target)
    }
}

fn remove_link(link: &Path) -> io::Result<()> {
    let metadata = fs::symlink_metadata(link)?;
    if metadata.file_type().is_symlink() || metadata.is_file() {
        fs::remove_file(link)
    } else {
        fs::remove_dir(link)
    }
}

fn make_symlink(target: &Path, link: &Path) -> io::Result<()> {
    #[cfg(unix)]
    {
        std::os::unix::fs::symlink(target, link)
    }
    #[cfg(not(unix))]
    {
        let _ = (target, link);
        Err(io::Error::new(
            io::ErrorKind::Unsupported,
            "symlink bookmarks are not supported on this platform",
        ))
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
        let dir = std::env::temp_dir().join(format!("ferret-bookmarks-{nanos}"));
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn add_and_remove_bookmark_roundtrip() {
        let root = temp_dir();
        let target = root.join("target");
        fs::create_dir(&target).unwrap();
        let store = BookmarkStore::new(root.join("bookmarks"));

        assert!(store.toggle(&target).unwrap());
        let bookmarks = store.entries();
        assert_eq!(bookmarks.len(), 1);
        assert_eq!(bookmarks[0].target, normalize_path(&target));

        assert!(!store.toggle(&target).unwrap());
        assert!(store.entries().is_empty());

        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn duplicate_names_get_suffixes() {
        let root = temp_dir();
        let one = root.join("one");
        let two = root.join("two");
        fs::create_dir(&one).unwrap();
        fs::create_dir(&two).unwrap();
        fs::create_dir(one.join("nested")).unwrap();
        fs::create_dir(two.join("nested")).unwrap();
        let store = BookmarkStore::new(root.join("bookmarks"));

        store.toggle(&one.join("nested")).unwrap();
        store.toggle(&two.join("nested")).unwrap();
        assert_eq!(store.entries().len(), 2);

        fs::remove_dir_all(root).unwrap();
    }
}
