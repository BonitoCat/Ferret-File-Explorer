use std::path::PathBuf;

#[derive(Clone, Copy, PartialEq, Debug, Default)]
pub enum OperationType {
    #[default]
    Copy,
    Cut,
}

#[derive(Clone, Default)]
pub struct FileOperations {
    clipboard: Vec<PathBuf>,
    operation_type: OperationType,
}

impl FileOperations {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn copy(&mut self, paths: Vec<PathBuf>) {
        self.clipboard = paths;
        self.operation_type = OperationType::Copy;
    }

    pub fn cut(&mut self, paths: Vec<PathBuf>) {
        self.clipboard = paths;
        self.operation_type = OperationType::Cut;
    }

    pub fn clipboard(&self) -> &[PathBuf] {
        &self.clipboard
    }

    pub fn operation_type(&self) -> OperationType {
        self.operation_type
    }

    pub fn clear_clipboard(&mut self) {
        self.clipboard.clear();
    }
}