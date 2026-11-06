use std::path::Path;

use crate::entry::FileKind;

/// Fast extension-based classification, matching the C# CLI's fast MIME pass.
pub fn classify(path: &Path, is_executable: bool) -> FileKind {
    let extension = path
        .extension()
        .map(|value| value.to_string_lossy().to_ascii_lowercase())
        .unwrap_or_default();

    let kind = match extension.as_str() {
        "txt" | "log" | "md" | "json" | "jsonc" | "xml" | "yaml" | "yml" | "ini" | "conf"
        | "cfg" | "toml" | "rs" | "cs" | "cpp" | "cc" | "c" | "h" | "hpp" | "py" | "js" | "jsx"
        | "ts" | "tsx" | "sh" | "bash" | "zsh" | "fish" | "lua" | "html" | "css" | "scss"
        | "svgx" | "sql" | "go" | "java" | "kt" | "swift" | "rb" | "php" | "pl" | "vim" | "el"
        | "lisp" | "clj" | "ex" | "exs" | "erl" | "hs" | "ml" | "nim" | "zig" | "dockerfile"
        | "makefile" => FileKind::Text,

        "png" | "jpg" | "jpeg" | "gif" | "webp" | "svg" | "svgz" | "ico" | "bmp" | "tif"
        | "tiff" | "avif" | "heic" | "heif" | "psd" | "xcf" => FileKind::Image,

        "mp3" | "wav" | "flac" | "ogg" | "oga" | "opus" | "m4a" | "aac" | "wma" | "aiff"
        | "mid" | "midi" => FileKind::Audio,

        "mp4" | "mkv" | "webm" | "avi" | "mov" | "m4v" | "wmv" | "flv" | "mpg" | "mpeg" | "3gp" => {
            FileKind::Video
        }

        "zip" | "7z" | "rar" | "tar" | "gz" | "gzip" | "bz2" | "xz" | "zst" | "lz4" | "lzma"
        | "iso" | "rpm" | "jar" | "cab" | "cpio" => FileKind::Archive,

        "deb" => FileKind::Deb,
        "pdf" | "doc" | "docx" | "xls" | "xlsx" | "ppt" | "pptx" | "odt" | "ods" | "odp" => {
            FileKind::Other
        }
        _ => FileKind::Other,
    };

    if is_executable
        && !matches!(
            kind,
            FileKind::Image | FileKind::Audio | FileKind::Video | FileKind::Archive | FileKind::Deb
        )
    {
        FileKind::Executable
    } else {
        kind
    }
}

/// Composite suffixes that should be treated as one archive extension.
pub fn is_archive_name(name: &str) -> bool {
    let lower = name.to_ascii_lowercase();
    lower.ends_with(".tar.gz")
        || lower.ends_with(".tar.bz2")
        || lower.ends_with(".tar.xz")
        || lower.ends_with(".tar.zst")
        || lower.ends_with(".tgz")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn classifies_common_files() {
        assert_eq!(classify(Path::new("/a/readme.md"), false), FileKind::Text);
        assert_eq!(classify(Path::new("/a/cat.png"), false), FileKind::Image);
        assert_eq!(classify(Path::new("/a/song.flac"), false), FileKind::Audio);
        assert_eq!(classify(Path::new("/a/movie.mkv"), false), FileKind::Video);
        assert_eq!(classify(Path::new("/a/a.tar.gz"), false), FileKind::Archive);
        assert_eq!(classify(Path::new("/a/app.deb"), false), FileKind::Deb);
    }

    #[test]
    fn executable_wins_unless_media() {
        assert_eq!(classify(Path::new("/a/run.sh"), true), FileKind::Executable);
        assert_eq!(classify(Path::new("/a/image.png"), true), FileKind::Image);
    }
}
