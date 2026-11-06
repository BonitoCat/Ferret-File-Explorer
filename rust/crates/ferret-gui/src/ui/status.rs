#[derive(Clone, Copy, PartialEq)]
pub enum Status {
    Ready,
    Processing,
    Error,
    Success,
}

pub struct StatusBar {
    pub message: String,
    pub status: Status,
}

impl StatusBar {
    pub fn new() -> Self {
        Self {
            message: String::new(),
            status: Status::Ready,
        }
    }

    pub fn set_message(&mut self, message: impl Into<String>, status: Status) {
        self.message = message.into();
        self.status = status;
    }

    pub fn message(&self) -> &str {
        &self.message
    }

    pub fn status(&self) -> Status {
        self.status
    }
}

impl Default for StatusBar {
    fn default() -> Self {
        Self::new()
    }
}