use std::path::{Path, PathBuf};

use crate::paths::{normalize_path, parent_of};

#[derive(Clone, Debug)]
pub struct Navigation {
    current: PathBuf,
    history: Vec<PathBuf>,
    index: usize,
}

impl Navigation {
    pub fn new(initial: impl AsRef<Path>) -> Self {
        let current = normalize_path(initial.as_ref());
        Self {
            history: vec![current.clone()],
            current,
            index: 0,
        }
    }

    pub fn current(&self) -> &Path {
        &self.current
    }

    pub fn navigate_to(&mut self, path: impl AsRef<Path>) {
        let path = normalize_path(path.as_ref());
        if path == self.current {
            return;
        }

        self.history.truncate(self.index + 1);
        self.history.push(path.clone());
        self.index = self.history.len() - 1;
        self.current = path;
    }

    pub fn navigate_up(&mut self) -> bool {
        if let Some(parent) = parent_of(&self.current) {
            self.navigate_to(parent);
            true
        } else {
            false
        }
    }

    pub fn go_back(&mut self) -> bool {
        if self.index > 0 {
            self.index -= 1;
            self.current = self.history[self.index].clone();
            true
        } else {
            false
        }
    }

    pub fn go_forward(&mut self) -> bool {
        if self.index + 1 < self.history.len() {
            self.index += 1;
            self.current = self.history[self.index].clone();
            true
        } else {
            false
        }
    }

    pub fn can_go_back(&self) -> bool {
        self.index > 0
    }

    pub fn can_go_forward(&self) -> bool {
        self.index + 1 < self.history.len()
    }

    pub fn history(&self) -> &[PathBuf] {
        &self.history
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn history_truncates_after_back() {
        let mut navigation = Navigation::new("/tmp");
        navigation.navigate_to("/tmp");
        navigation.navigate_to("/");
        assert!(navigation.can_go_back());
        navigation.go_back();
        assert_eq!(navigation.current(), Path::new("/tmp"));
        navigation.navigate_to("/etc");
        assert!(!navigation.can_go_forward());
    }

    #[test]
    fn up_from_root_is_a_no_op() {
        let mut navigation = Navigation::new("/");
        assert!(!navigation.navigate_up());
        assert_eq!(navigation.current(), Path::new("/"));
    }
}
