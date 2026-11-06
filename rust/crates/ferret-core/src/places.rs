use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

use crate::bookmarks::BookmarkStore;
use crate::natural::compare_names;
use crate::paths::{display_name, file_name, gvfs_root, is_remote, path_exists, AppPaths};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum PlaceKind {
    Places,
    Bookmarks,
    Devices,
    Network,
}

impl PlaceKind {
    pub fn title(self) -> &'static str {
        match self {
            PlaceKind::Places => "PLACES",
            PlaceKind::Bookmarks => "BOOKMARKS",
            PlaceKind::Devices => "DEVICES",
            PlaceKind::Network => "NETWORK",
        }
    }
}

#[derive(Clone, Debug)]
pub struct Place {
    pub kind: PlaceKind,
    pub name: String,
    pub path: PathBuf,
    pub mounted: bool,
    pub device: Option<PathBuf>,
    pub bookmark: bool,
    pub remote_uri: Option<String>,
}

pub fn standard_places(paths: &AppPaths) -> Vec<Place> {
    let mut result = Vec::new();
    let mut seen = HashSet::new();

    let home = paths.home().to_path_buf();
    push_place(
        &mut result,
        &mut seen,
        PlaceKind::Places,
        "Home",
        home.clone(),
    );

    for (key, fallback) in [
        ("XDG_DESKTOP_DIR", "Desktop"),
        ("XDG_DOCUMENTS_DIR", "Documents"),
        ("XDG_DOWNLOAD_DIR", "Downloads"),
        ("XDG_MUSIC_DIR", "Music"),
        ("XDG_PICTURES_DIR", "Pictures"),
        ("XDG_VIDEOS_DIR", "Videos"),
    ] {
        let path = user_dir(paths, key).unwrap_or_else(|| home.join(fallback));
        push_place(
            &mut result,
            &mut seen,
            PlaceKind::Places,
            &file_name(&path),
            path,
        );
    }

    push_place(
        &mut result,
        &mut seen,
        PlaceKind::Places,
        "File System",
        PathBuf::from("/"),
    );

    let trash = home.join(".local/share/Trash/files");
    if trash.is_dir() {
        push_place(&mut result, &mut seen, PlaceKind::Places, "Trash", trash);
    }

    result
}

fn push_place(
    result: &mut Vec<Place>,
    seen: &mut HashSet<PathBuf>,
    kind: PlaceKind,
    name: &str,
    path: PathBuf,
) {
    if !path.is_dir() || !seen.insert(path.clone()) {
        return;
    }
    result.push(Place {
        kind,
        name: name.to_string(),
        path,
        mounted: true,
        device: None,
        bookmark: false,
        remote_uri: None,
    });
}

pub fn bookmark_places(store: &BookmarkStore) -> Vec<Place> {
    store
        .entries()
        .into_iter()
        .map(|bookmark| Place {
            kind: PlaceKind::Bookmarks,
            name: display_name(&bookmark.target),
            mounted: is_remote(&bookmark.target) || bookmark.target.is_dir(),
            path: bookmark.target,
            device: None,
            bookmark: true,
            remote_uri: None,
        })
        .collect()
}

/// Mounted local devices and GVFS-backed portable media, read from
/// `/proc/mounts`. Unmounted block devices are not discovered here (the C#
/// version asks `lsblk`); they can still be mounted from a file manager.
pub fn device_places() -> Vec<Place> {
    let mut result = Vec::new();
    let mut seen = HashSet::new();
    let Ok(mounts) = fs::read_to_string("/proc/mounts") else {
        return result;
    };

    for line in mounts.lines() {
        let mut fields = line.split_whitespace();
        let device = fields.next().unwrap_or_default();
        let mount = fields.next().unwrap_or_default();
        let mount = unescape_mount(mount);
        let mount_path = PathBuf::from(&mount);

        let is_portable = mount.starts_with("/media/")
            || mount.starts_with("/run/media/")
            || mount.starts_with("/mnt/");
        let is_gvfs =
            mount.starts_with("/run/user/") && mount.contains("/gvfs/") && is_device_gvfs(&mount);
        if !is_portable && !is_gvfs {
            continue;
        }
        if !seen.insert(mount_path.clone()) {
            continue;
        }

        result.push(Place {
            kind: PlaceKind::Devices,
            name: gvfs_display_name(&mount_path),
            path: mount_path,
            mounted: true,
            device: if device.starts_with("/dev/") {
                Some(PathBuf::from(device))
            } else {
                None
            },
            bookmark: false,
            remote_uri: None,
        });
    }

    result.sort_by(|a, b| compare_names(&a.name, &b.name));
    result
}

pub fn network_places() -> Vec<Place> {
    let mut result = Vec::new();
    let Ok(mounts) = fs::read_to_string("/proc/mounts") else {
        return result;
    };

    for line in mounts.lines() {
        let mut fields = line.split_whitespace();
        let _device = fields.next();
        let Some(mount) = fields.next() else {
            continue;
        };
        let mount = unescape_mount(mount);
        if !(mount.starts_with("/run/user/") && mount.contains("/gvfs/")) {
            continue;
        }
        if is_device_gvfs(&mount) {
            continue;
        }

        let path = PathBuf::from(&mount);
        result.push(Place {
            kind: PlaceKind::Network,
            name: gvfs_display_name(&path),
            path,
            mounted: true,
            device: None,
            bookmark: false,
            remote_uri: None,
        });
    }

    result.sort_by(|a, b| compare_names(&a.name, &b.name));
    result
}

pub fn all_places(paths: &AppPaths, store: &BookmarkStore) -> Vec<Place> {
    let mut places = standard_places(paths);
    places.extend(bookmark_places(store));
    places.extend(device_places());
    places.extend(network_places());
    places
}

pub fn network_browse_root() -> Option<PathBuf> {
    let root = gvfs_root();
    root.is_dir().then_some(root)
}

fn user_dir(paths: &AppPaths, key: &str) -> Option<PathBuf> {
    let config = fs::read_to_string(paths.config_root().join("user-dirs.dirs")).ok()?;
    for line in config.lines() {
        let line = line.trim();
        if !line.starts_with(key) {
            continue;
        }
        let (_, value) = line.split_once('=')?;
        let value = value.trim().trim_matches('"');
        let expanded = if let Some(rest) = value.strip_prefix("$HOME/") {
            paths.home().join(rest)
        } else if value == "$HOME" {
            paths.home().to_path_buf()
        } else {
            PathBuf::from(value)
        };
        if expanded.is_dir() {
            return Some(expanded);
        }
    }
    None
}

fn unescape_mount(value: &str) -> String {
    let bytes = value.as_bytes();
    let mut result = String::with_capacity(value.len());
    let mut index = 0;
    while index < bytes.len() {
        if bytes[index] == b'\\' && index + 3 < bytes.len() {
            if let Ok(octal) = u8::from_str_radix(&value[index + 1..index + 4], 8) {
                result.push(octal as char);
                index += 4;
                continue;
            }
        }
        result.push(bytes[index] as char);
        index += 1;
    }
    result
}

fn is_device_gvfs(path: &str) -> bool {
    let name = Path::new(path)
        .file_name()
        .map(|name| name.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default();
    name.starts_with("mtp:")
        || name.starts_with("afc:")
        || name.starts_with("gphoto2:")
        || name.starts_with("cdda:")
}

pub fn gvfs_display_name(path: &Path) -> String {
    let raw = path
        .file_name()
        .map(|name| name.to_string_lossy().into_owned())
        .unwrap_or_else(|| path.to_string_lossy().into_owned());
    let lower = raw.to_ascii_lowercase();

    if lower.starts_with("mtp:host=") {
        let rest = &raw["mtp:host=".len()..];
        let parts: Vec<&str> = rest.split('_').filter(|part| !part.is_empty()).collect();
        return if parts.is_empty() {
            "Android Device".to_string()
        } else {
            parts.iter().take(2).cloned().collect::<Vec<_>>().join(" ")
        };
    }
    if lower.starts_with("afc:host=") {
        return "Apple Device".to_string();
    }
    if lower.starts_with("gphoto2:host=") {
        return "Digital Camera".to_string();
    }

    for (prefix, label) in [
        ("sftp:host=", "SFTP"),
        ("ftp:host=", "FTP"),
        ("dav:host=", "WebDAV"),
        ("davs:host=", "WebDAV"),
        ("smb-share:server=", "SMB"),
    ] {
        if lower.starts_with(prefix) {
            let rest = &raw[prefix.len()..];
            let host = rest.split(',').next().unwrap_or(rest);
            return format!("{host} ({label})");
        }
    }

    raw
}

pub fn mount_device(device: &Path) -> Result<PathBuf, String> {
    let output = Command::new("udisksctl")
        .arg("mount")
        .arg("-b")
        .arg(device)
        .output()
        .map_err(|error| error.to_string())?;
    let text = format!(
        "{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );

    for marker in [" at `", " at "] {
        if let Some(start) = text.find(marker) {
            let rest = &text[start + marker.len()..];
            let end = rest.find(['\'', '.', '\n']).unwrap_or(rest.len());
            let mount = rest[..end].trim().trim_end_matches('.');
            let path = PathBuf::from(mount);
            if path.is_dir() {
                return Ok(path);
            }
        }
    }

    Err(if text.trim().is_empty() {
        "mount failed".to_string()
    } else {
        text.trim().to_string()
    })
}

pub fn unmount(place: &Place) -> Result<(), String> {
    if !place.mounted {
        return Ok(());
    }

    let output = if let Some(device) = &place.device {
        Command::new("udisksctl")
            .arg("unmount")
            .arg("-b")
            .arg(device)
            .output()
    } else {
        Command::new("gio")
            .arg("mount")
            .arg("-u")
            .arg(&place.path)
            .output()
    }
    .map_err(|error| error.to_string())?;

    if output.status.success() {
        return Ok(());
    }

    let message = format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    Err(if message.trim().is_empty() {
        "unmount failed".to_string()
    } else {
        message.trim().to_string()
    })
}

pub fn place_exists(place: &Place) -> bool {
    path_exists(&place.path) && (place.path.is_dir() || is_remote(&place.path))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mount_unescaping_handles_spaces() {
        assert_eq!(unescape_mount("/media/My\\040Disk"), "/media/My Disk");
        assert_eq!(unescape_mount("/mnt/plain"), "/mnt/plain");
    }

    #[test]
    fn gvfs_names_are_human_readable() {
        assert_eq!(
            gvfs_display_name(Path::new(
                "/run/user/1000/gvfs/sftp:host=example.com,user=me"
            )),
            "example.com (SFTP)"
        );
        assert_eq!(
            gvfs_display_name(Path::new(
                "/run/user/1000/gvfs/smb-share:server=nas,share=data"
            )),
            "nas (SMB)"
        );
    }

    #[test]
    fn standard_places_always_include_root() {
        let root = std::env::temp_dir().join("ferret-places-test");
        let paths = AppPaths::with_roots(
            root.clone(),
            root.join("data"),
            root.join("config"),
            root.join("cache"),
        );
        let places = standard_places(&paths);
        assert!(places.iter().any(|place| place.path == Path::new("/")));
    }
}
