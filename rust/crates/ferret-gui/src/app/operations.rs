use std::sync::mpsc;
use eframe::egui;

#[derive(Clone, Debug, PartialEq)]
pub enum OperationStatus {
    Idle,
    Running,
    Completed,
    Failed(String),
}

#[derive(Clone, Debug)]
pub struct Operation {
    pub id: usize,
    pub name: String,
    pub status: OperationStatus,
    pub progress: f32,
}

pub enum OperationEvent {
    Start { id: usize, name: String },
    Progress { id: usize, progress: f32 },
    Complete { id: usize },
    Error { id: usize, message: String },
}

pub struct Operations {
    sender: mpsc::Sender<OperationEvent>,
    receiver: mpsc::Receiver<OperationEvent>,
    operations: std::collections::HashMap<usize, Operation>,
    next_id: usize,
}

impl Operations {
    pub fn new() -> Self {
        let (sender, receiver) = mpsc::channel();
        Self {
            sender,
            receiver,
            operations: std::collections::HashMap::new(),
            next_id: 1,
        }
    }

    pub fn start_operation(&mut self, name: String) -> usize {
        let id = self.next_id;
        self.next_id += 1;

        let op = Operation {
            id,
            name: name.clone(),
            status: OperationStatus::Running,
            progress: 0.0,
        };
        self.operations.insert(id, op);
        let _ = self.sender.send(OperationEvent::Start { id, name });
        id
    }

    pub fn update_progress(&mut self, id: usize, progress: f32) {
        if let Some(op) = self.operations.get_mut(&id) {
            op.progress = progress.clamp(0.0, 1.0);
        }
    }

    pub fn complete_operation(&mut self, id: usize) {
        if let Some(op) = self.operations.get_mut(&id) {
            op.status = OperationStatus::Completed;
            op.progress = 1.0;
        }
    }

    pub fn fail_operation(&mut self, id: usize, message: String) {
        if let Some(op) = self.operations.get_mut(&id) {
            op.status = OperationStatus::Failed(message);
        }
    }

    pub fn update(&mut self, ctx: &egui::Context) {
        while let Ok(event) = self.receiver.try_recv() {
            match event {
                OperationEvent::Start { id, name } => {
                    if let Some(op) = self.operations.get_mut(&id) {
                        op.status = OperationStatus::Running;
                        op.progress = 0.0;
                    }
                    println!("Operation started: {} (id: {})", name, id);
                }
                OperationEvent::Progress { id, progress } => {
                    if let Some(op) = self.operations.get_mut(&id) {
                        op.progress = progress.clamp(0.0, 1.0);
                    }
                }
                OperationEvent::Complete { id } => {
                    if let Some(op) = self.operations.get_mut(&id) {
                        op.status = OperationStatus::Completed;
                        op.progress = 1.0;
                    }
                    ctx.request_repaint();
                }
                OperationEvent::Error { id, message } => {
                    if let Some(op) = self.operations.get_mut(&id) {
                        op.status = OperationStatus::Failed(message.clone());
                    }
                    eprintln!("Operation {} failed: {}", id, message);
                }
            }
        }
    }

    pub fn active_operations(&self) -> Vec<&Operation> {
        self.operations.values()
            .filter(|op| matches!(op.status, OperationStatus::Running))
            .collect()
    }
}

impl Default for Operations {
    fn default() -> Self {
        Self::new()
    }
}