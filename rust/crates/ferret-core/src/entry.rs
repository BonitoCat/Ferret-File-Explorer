use std::path::PathBuf;
use std::time::SystemTime;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FileKind {
    Text,
    Image,
    Audio,
    Video,
    Archive,
    Deb,
    Executable,
    Other,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum EntryKind {
    /// The synthetic `..` entry at the top of a directory listing.
    Parent,
    Directory,
    File(FileKind),
}

#[derive(Clone, Debug)]
pub struct Entry {
    pub name: String,
    pub path: PathBuf,
    pub kind: EntryKind,
    pub is_hidden: bool,
    pub is_symlink: bool,
    pub is_broken_link: bool,
    pub is_read_only: bool,
    pub is_executable: bool,
    pub access_denied: bool,
    pub size: u64,
    pub modified: Option<SystemTime>,
    pub created: Option<SystemTime>,
    pub mode: Option<u32>,
    pub uid: Option<u32>,
    pub gid: Option<u32>,
    pub symlink_target: Option<PathBuf>,
}

impl Entry {
    pub fn parent(path: PathBuf) -> Self {
        Self {
            name: "..".to_string(),
            path,
            kind: EntryKind::Parent,
            is_hidden: false,
            is_symlink: false,
            is_broken_link: false,
            is_read_only: false,
            is_executable: false,
            access_denied: false,
            size: 0,
            modified: None,
            created: None,
            mode: None,
            uid: None,
            gid: None,
            symlink_target: None,
        }
    }

    pub fn is_parent(&self) -> bool {
        matches!(self.kind, EntryKind::Parent)
    }

    pub fn is_dir(&self) -> bool {
        matches!(self.kind, EntryKind::Parent | EntryKind::Directory)
    }

    pub fn file_kind(&self) -> Option<FileKind> {
        match self.kind {
            EntryKind::File(kind) => Some(kind),
            _ => None,
        }
    }

    pub fn icon(&self) -> &'static str {
        match self.kind {
            EntryKind::Parent => "   ",
            EntryKind::Directory => "🗁  ",
            EntryKind::File(FileKind::Executable) | EntryKind::File(FileKind::Deb) => "ᐅ  ",
            EntryKind::File(FileKind::Archive) => "🗀  ",
            EntryKind::File(FileKind::Image) => "🖼  ",
            EntryKind::File(FileKind::Video) => "🎞  ",
            EntryKind::File(FileKind::Audio) => "♪  ",
            EntryKind::File(_) => "🗏  ",
        }
    }
}
