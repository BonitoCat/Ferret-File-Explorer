use crate::core_navigation::Navigation as CoreNavigation;
use std::path::{Path, PathBuf};

pub struct Navigation {
    inner: CoreNavigation,
}

impl Navigation {
    pub fn new(initial: &PathBuf) -> Self {
        Self {
            inner: CoreNavigation::new(initial),
        }
    }

    pub fn navigate_to(&mut self, path: &PathBuf) {
        self.inner.navigate_to(path);
    }

    pub fn current(&self) -> &Path {
        self.inner.current()
    }

    pub fn go_back(&mut self) -> bool {
        self.inner.go_back()
    }

    pub fn go_forward(&mut self) -> bool {
        self.inner.go_forward()
    }

    pub fn can_go_back(&self) -> bool {
        self.inner.can_go_back()
    }

    pub fn can_go_forward(&self) -> bool {
        self.inner.can_go_forward()
    }
}