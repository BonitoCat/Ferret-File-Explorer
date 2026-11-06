use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use crate::entry::{Entry, EntryKind};
use crate::mime;
use crate::natural::compare_names;
use crate::paths::{is_remote, normalize_path, parent_of};

#[derive(Clone, Debug, Default)]
pub struct ListOptions {
    pub show_hidden: bool,
    pub search: Option<String>,
}

/// Read a directory into a sorted list. Directories are listed before files,
/// then names are compared naturally. A synthetic `..` entry is inserted when
/// the directory has a parent and no search filter is active.
pub fn list_directory(path: &Path, options: &ListOptions) -> io::Result<Vec<Entry>> {
    let path = normalize_path(path);
    let metadata = fs::metadata(&path)?;
    if !metadata.is_dir() {
        return Err(io::Error::new(
            io::ErrorKind::NotADirectory,
            format!("not a directory: {}", path.display()),
        ));
    }

    let mut entries = Vec::new();
    let search = options
        .search
        .as_deref()
        .filter(|value| !value.trim().is_empty());

    if search.is_none() {
        if let Some(parent) = parent_of(&path) {
            entries.push(Entry::parent(parent));
        }
    }

    for item in fs::read_dir(&path)? {
        let item = match item {
            Ok(item) => item,
            Err(_) => continue,
        };

        let name = item.file_name().to_string_lossy().into_owned();
        if name == "." || name == ".." {
            continue;
        }

        if let Some(pattern) = search {
            if !matches_pattern(pattern, &name) {
                continue;
            }
        }

        let is_hidden = name.starts_with('.');
        if is_hidden && !options.show_hidden {
            continue;
        }

        if let Some(entry) = build_entry(&item.path(), name, is_hidden) {
            entries.push(entry);
        }
    }

    entries.sort_by(compare_entries);
    Ok(entries)
}

fn build_entry(path: &Path, name: String, is_hidden: bool) -> Option<Entry> {
    let link_metadata = fs::symlink_metadata(path).ok();
    let is_symlink = link_metadata
        .as_ref()
        .map(|metadata| metadata.file_type().is_symlink())
        .unwrap_or(false);
    let symlink_target = if is_symlink {
        fs::read_link(path).ok()
    } else {
        None
    };

    let followed = fs::metadata(path);
    let is_broken_link = is_symlink && followed.is_err();
    let metadata = match followed {
        Ok(metadata) => metadata,
        Err(_) => link_metadata?,
    };

    let is_dir = metadata.is_dir();
    let is_executable = !is_dir && {
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            metadata.permissions().mode() & 0o111 != 0
        }
        #[cfg(not(unix))]
        {
            false
        }
    };

    let access_denied = !is_broken_link && access_denied(path, is_dir);
    let kind = if is_dir {
        EntryKind::Directory
    } else {
        EntryKind::File(mime::classify(path, is_executable))
    };

    let (mode, uid, gid) = {
        #[cfg(unix)]
        {
            use std::os::unix::fs::MetadataExt;
            (
                Some(metadata.mode()),
                Some(metadata.uid()),
                Some(metadata.gid()),
            )
        }
        #[cfg(not(unix))]
        {
            (None, None, None)
        }
    };

    Some(Entry {
        name,
        path: path.to_path_buf(),
        kind,
        is_hidden,
        is_symlink,
        is_broken_link,
        is_read_only: metadata.permissions().readonly(),
        is_executable,
        access_denied,
        size: if is_dir { 0 } else { metadata.len() },
        modified: metadata.modified().ok(),
        created: metadata.created().ok(),
        mode,
        uid,
        gid,
        symlink_target,
    })
}

fn compare_entries(a: &Entry, b: &Entry) -> std::cmp::Ordering {
    // `..` is always pinned to the top.
    match (a.is_parent(), b.is_parent()) {
        (true, false) => return std::cmp::Ordering::Less,
        (false, true) => return std::cmp::Ordering::Greater,
        (true, true) => return std::cmp::Ordering::Equal,
        (false, false) => {}
    }

    let dir_order = b.is_dir().cmp(&a.is_dir());
    if dir_order != std::cmp::Ordering::Equal {
        return dir_order;
    }

    match (a.is_hidden, b.is_hidden) {
        (false, true) => return std::cmp::Ordering::Less,
        (true, false) => return std::cmp::Ordering::Greater,
        _ => {}
    }

    compare_names(&a.name, &b.name)
}

fn access_denied(path: &Path, is_directory: bool) -> bool {
    #[cfg(unix)]
    {
        use std::ffi::CString;
        use std::os::unix::ffi::OsStrExt;

        let Ok(path) = CString::new(path.as_os_str().as_bytes()) else {
            return false;
        };
        let mode = libc::R_OK | if is_directory { libc::X_OK } else { 0 };
        unsafe { libc::access(path.as_ptr(), mode) != 0 }
    }
    #[cfg(not(unix))]
    {
        let _ = (path, is_directory);
        false
    }
}

pub fn matches_pattern(pattern: &str, name: &str) -> bool {
    let pattern = pattern.trim();
    if pattern.is_empty() {
        return true;
    }

    if pattern.contains('*') || pattern.contains('?') {
        wildcard_match(pattern, name)
    } else {
        name.to_lowercase().contains(&pattern.to_lowercase())
    }
}

fn wildcard_match(pattern: &str, name: &str) -> bool {
    let pattern: Vec<char> = pattern.to_lowercase().chars().collect();
    let name: Vec<char> = name.to_lowercase().chars().collect();
    let mut pattern_index = 0usize;
    let mut name_index = 0usize;
    let mut star = None;
    let mut star_name = 0usize;

    while name_index < name.len() {
        if pattern_index < pattern.len()
            && (pattern[pattern_index] == '?' || pattern[pattern_index] == name[name_index])
        {
            pattern_index += 1;
            name_index += 1;
        } else if pattern_index < pattern.len() && pattern[pattern_index] == '*' {
            star = Some(pattern_index);
            pattern_index += 1;
            star_name = name_index;
        } else if let Some(star_index) = star {
            pattern_index = star_index + 1;
            star_name += 1;
            name_index = star_name;
        } else {
            return false;
        }
    }

    while pattern_index < pattern.len() && pattern[pattern_index] == '*' {
        pattern_index += 1;
    }

    pattern_index == pattern.len()
}

/// Convenience used by the TUI for network-backed locations.
pub fn is_network_location(path: &Path) -> bool {
    is_remote(path)
}

pub fn parent_for_display(path: &Path) -> Option<PathBuf> {
    parent_of(path)
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
        let dir = std::env::temp_dir().join(format!("ferret-listing-{nanos}"));
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn lists_directories_first_and_hides_dotfiles() {
        let dir = temp_dir();
        fs::create_dir(dir.join("b-dir")).unwrap();
        fs::create_dir(dir.join("A-dir")).unwrap();
        fs::write(dir.join("z.txt"), "x").unwrap();
        fs::write(dir.join(".hidden"), "x").unwrap();
        fs::write(dir.join("file10"), "x").unwrap();
        fs::write(dir.join("file2"), "x").unwrap();

        let entries = list_directory(
            &dir,
            &ListOptions {
                show_hidden: false,
                search: None,
            },
        )
        .unwrap();

        let names: Vec<&str> = entries.iter().map(|entry| entry.name.as_str()).collect();
        assert_eq!(names, ["..", "A-dir", "b-dir", "file2", "file10", "z.txt"]);
        assert!(entries[0].is_parent());

        let entries = list_directory(
            &dir,
            &ListOptions {
                show_hidden: true,
                search: None,
            },
        )
        .unwrap();
        assert!(entries.iter().any(|entry| entry.name == ".hidden"));

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn search_filter_and_wildcards_work() {
        let dir = temp_dir();
        fs::write(dir.join("alpha.txt"), "x").unwrap();
        fs::write(dir.join("beta.log"), "x").unwrap();

        let entries = list_directory(
            &dir,
            &ListOptions {
                show_hidden: true,
                search: Some("*.txt".to_string()),
            },
        )
        .unwrap();

        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].name, "alpha.txt");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn wildcard_matching_is_case_insensitive() {
        assert!(matches_pattern("*.TXT", "readme.txt"));
        assert!(matches_pattern("file?1", "File21"));
        assert!(!matches_pattern("*.txt", "readme.md"));
    }
}
