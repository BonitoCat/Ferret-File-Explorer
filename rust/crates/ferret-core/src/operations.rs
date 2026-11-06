use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::SystemTime;

use crate::format;
use crate::paths::{file_name, is_inside, normalize_path, path_exists, unique_path};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PasteMode {
    Copy,
    Cut,
}

#[derive(Clone, Debug, Default)]
pub struct OpReport {
    pub targets: Vec<PathBuf>,
    pub errors: Vec<String>,
}

impl OpReport {
    fn error(&mut self, path: &Path, error: impl std::fmt::Display) {
        self.errors.push(format!("{}: {error}", path.display()));
    }

    pub fn failed(&self) -> bool {
        !self.errors.is_empty()
    }
}

pub fn create_file(path: &Path) -> io::Result<PathBuf> {
    use std::fs::OpenOptions;
    OpenOptions::new().write(true).create_new(true).open(path)?;
    Ok(path.to_path_buf())
}

pub fn create_dir(path: &Path) -> io::Result<PathBuf> {
    fs::create_dir_all(path)?;
    Ok(path.to_path_buf())
}

pub fn rename(old_path: &Path, new_name: &str) -> io::Result<PathBuf> {
    let name = new_name.trim();
    if name.is_empty() || name.contains('/') {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "invalid file name",
        ));
    }

    let parent = old_path.parent().unwrap_or_else(|| Path::new("/"));
    let new_path = parent.join(name);
    if new_path == old_path {
        return Ok(new_path);
    }
    if path_exists(&new_path) {
        return Err(io::Error::new(
            io::ErrorKind::AlreadyExists,
            format!("{} already exists", new_path.display()),
        ));
    }

    fs::rename(old_path, &new_path)?;
    Ok(new_path)
}

pub fn duplicate(path: &Path) -> io::Result<PathBuf> {
    let destination = unique_path(path);
    copy_entry(path, &destination)?;
    Ok(destination)
}

pub fn paste(destination_dir: &Path, mode: PasteMode, sources: &[PathBuf]) -> OpReport {
    let mut report = OpReport::default();

    for source in sources {
        if !path_exists(source) {
            continue;
        }

        if mode == PasteMode::Cut && source.parent() == Some(destination_dir) {
            continue;
        }

        let name = source
            .file_name()
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(file_name(source)));
        let mut destination = destination_dir.join(name);

        if path_exists(&destination) {
            destination = unique_path(&destination);
        }

        if is_inside(source, &destination) {
            report.error(source, "cannot paste a directory inside itself");
            continue;
        }

        let result = match mode {
            PasteMode::Copy => copy_entry(source, &destination),
            PasteMode::Cut => move_entry(source, &destination),
        };

        match result {
            Ok(()) => report.targets.push(destination),
            Err(error) => report.error(source, error),
        }
    }

    report
}

pub fn copy_entry(source: &Path, destination: &Path) -> io::Result<()> {
    let metadata = fs::symlink_metadata(source)?;

    if metadata.file_type().is_symlink() {
        let target = fs::read_link(source)?;
        make_symlink(&target, destination)?;
        return Ok(());
    }

    if metadata.is_dir() {
        fs::create_dir_all(destination)?;
        for child in fs::read_dir(source)? {
            let child = child?;
            copy_entry(&child.path(), &destination.join(child.file_name()))?;
        }
        return Ok(());
    }

    if let Some(parent) = destination.parent() {
        fs::create_dir_all(parent)?;
    }
    fs::copy(source, destination)?;
    Ok(())
}

pub fn move_entry(source: &Path, destination: &Path) -> io::Result<()> {
    if let Some(parent) = destination.parent() {
        fs::create_dir_all(parent)?;
    }

    match fs::rename(source, destination) {
        Ok(()) => Ok(()),
        Err(_) => {
            copy_entry(source, destination)?;
            delete_one(source)
        }
    }
}

pub fn delete_permanent(paths: &[PathBuf]) -> OpReport {
    let mut report = OpReport::default();
    for path in paths {
        if !path_exists(path) {
            continue;
        }
        match delete_one(path) {
            Ok(()) => {}
            Err(error) => report.error(path, error),
        }
    }
    report
}

fn delete_one(path: &Path) -> io::Result<()> {
    let metadata = fs::symlink_metadata(path)?;
    if metadata.file_type().is_symlink() || !metadata.is_dir() {
        fs::remove_file(path)
    } else {
        fs::remove_dir_all(path)
    }
}

/// Move paths to the freedesktop trash. `gio trash` is preferred so the same
/// trash implementation as the desktop is used; a small spec-compatible
/// fallback is used when gio is unavailable.
pub fn trash(paths: &[PathBuf]) -> OpReport {
    let mut report = OpReport::default();
    if paths.is_empty() {
        return report;
    }

    let mut command = Command::new("gio");
    command.arg("trash").arg("--");
    for path in paths {
        command.arg(path);
    }

    match command.status() {
        Ok(status) if status.success() => return report,
        _ => {}
    }

    trash_fallback(paths, &mut report);
    report
}

fn trash_fallback(paths: &[PathBuf], report: &mut OpReport) {
    let home = std::env::var_os("HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("/"));
    let root = home.join(".local/share/Trash");
    let files_dir = root.join("files");
    let info_dir = root.join("info");

    if let Err(error) = fs::create_dir_all(&files_dir).and_then(|_| fs::create_dir_all(&info_dir)) {
        report.errors.push(format!("trash is unavailable: {error}"));
        return;
    }

    let now = SystemTime::now();
    for source in paths {
        if !path_exists(source) {
            continue;
        }

        let absolute = normalize_path(source);
        let name = absolute
            .file_name()
            .map(|name| name.to_string_lossy().into_owned())
            .unwrap_or_else(|| "item".to_string());
        let destination = unique_path(&files_dir.join(&name));

        if let Err(error) = move_entry(&absolute, &destination) {
            report.error(source, error);
            continue;
        }

        let info_path = info_dir.join(format!(
            "{}.trashinfo",
            destination
                .file_name()
                .map(|name| name.to_string_lossy().into_owned())
                .unwrap_or(name)
        ));
        let info = format!(
            "[Trash Info]\nPath={}\nDeletionDate={}\n",
            percent_encode(absolute.to_string_lossy().as_bytes()),
            format::iso_utc(now)
        );
        if let Err(error) = fs::write(&info_path, info) {
            let _ = move_entry(&destination, &absolute);
            report.error(source, error);
        }
    }
}

fn percent_encode(bytes: &[u8]) -> String {
    let mut result = String::with_capacity(bytes.len());
    for &byte in bytes {
        let keep = byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_' | b'.' | b'~' | b'/');
        if keep {
            result.push(byte as char);
        } else {
            result.push_str(&format!("%{byte:02X}"));
        }
    }
    result
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
            "symlinks are not supported on this platform",
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
        let dir = std::env::temp_dir().join(format!("ferret-ops-{nanos}"));
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn duplicate_uses_numbered_name() {
        let dir = temp_dir();
        let source = dir.join("note.txt");
        fs::write(&source, "hello").unwrap();

        let first = duplicate(&source).unwrap();
        let second = duplicate(&source).unwrap();
        assert_eq!(first, dir.join("note (1).txt"));
        assert_eq!(second, dir.join("note (2).txt"));
        assert_eq!(fs::read_to_string(first).unwrap(), "hello");

        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn paste_copy_and_cut() {
        let root = temp_dir();
        let source_dir = root.join("source");
        let destination = root.join("destination");
        fs::create_dir(&source_dir).unwrap();
        fs::create_dir(&destination).unwrap();
        fs::write(source_dir.join("a.txt"), "a").unwrap();

        let report = paste(&destination, PasteMode::Copy, &[source_dir.join("a.txt")]);
        assert!(report.errors.is_empty(), "{:?}", report.errors);
        assert_eq!(fs::read_to_string(destination.join("a.txt")).unwrap(), "a");
        assert!(source_dir.join("a.txt").exists());

        let report = paste(&destination, PasteMode::Cut, &[source_dir.join("a.txt")]);
        assert!(report.errors.is_empty(), "{:?}", report.errors);
        assert!(!source_dir.join("a.txt").exists());
        assert_eq!(
            fs::read_to_string(destination.join("a (1).txt")).unwrap(),
            "a"
        );

        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn recursive_copy_preserves_tree() {
        let root = temp_dir();
        let source = root.join("tree");
        fs::create_dir_all(source.join("nested")).unwrap();
        fs::write(source.join("nested/file"), "x").unwrap();

        let destination = root.join("copy");
        copy_entry(&source, &destination).unwrap();
        assert_eq!(
            fs::read_to_string(destination.join("nested/file")).unwrap(),
            "x"
        );

        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn permanent_delete_removes_trees_and_links() {
        let root = temp_dir();
        let tree = root.join("tree");
        fs::create_dir_all(tree.join("nested")).unwrap();
        fs::write(tree.join("nested/file"), "x").unwrap();

        let link = root.join("link");
        #[cfg(unix)]
        std::os::unix::fs::symlink(&tree, &link).unwrap();

        let report = delete_permanent(&[tree, link]);
        assert!(report.errors.is_empty(), "{:?}", report.errors);
        assert!(!root.join("tree").exists());

        fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn rename_refuses_existing_destination() {
        let root = temp_dir();
        let one = root.join("one");
        let two = root.join("two");
        fs::write(&one, "1").unwrap();
        fs::write(&two, "2").unwrap();
        assert!(rename(&one, "two").is_err());
        fs::remove_dir_all(root).unwrap();
    }
}
