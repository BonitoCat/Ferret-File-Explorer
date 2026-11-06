use std::collections::HashSet;
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

use crossterm::event::{KeyCode, KeyEvent, KeyModifiers};
use ferret_core::bookmarks::BookmarkStore;
use ferret_core::entry::Entry;
use ferret_core::listing::{list_directory, ListOptions};
use ferret_core::navigation::Navigation;
use ferret_core::operations::{self, PasteMode};
use ferret_core::paths::{self, expand_home, AppPaths};
use ferret_core::places::{self, Place, PlaceKind};
use ferret_core::settings::Settings;

/// Work that needs the terminal to be handed back to the parent process.
#[derive(Clone, Debug)]
pub enum External {
    Shell(PathBuf),
    Editor(PathBuf),
}

pub struct Pane {
    pub nav: Navigation,
    pub entries: Vec<Entry>,
    pub selected: usize,
    pub anchor: usize,
    pub extending: bool,
    pub selected_items: HashSet<PathBuf>,
    pub search: Option<String>,
    pub error: Option<String>,
    pub scroll: usize,
}

impl Pane {
    pub fn new(path: &Path, show_hidden: bool) -> Self {
        let mut pane = Self {
            nav: Navigation::new(path),
            entries: Vec::new(),
            selected: 0,
            anchor: 0,
            extending: false,
            selected_items: HashSet::new(),
            search: None,
            error: None,
            scroll: 0,
        };
        pane.reload(show_hidden);
        pane
    }

    pub fn cwd(&self) -> &Path {
        self.nav.current()
    }

    pub fn reload(&mut self, show_hidden: bool) {
        let options = ListOptions {
            show_hidden,
            search: self.search.clone(),
        };

        match list_directory(self.cwd(), &options) {
            Ok(entries) => {
                self.entries = entries;
                self.error = None;
            }
            Err(error) => {
                self.entries.clear();
                self.error = Some(error.to_string());
            }
        }

        if self.entries.is_empty() {
            self.selected = 0;
        } else if self.selected >= self.entries.len() {
            self.selected = self.entries.len() - 1;
        }

        self.selected_items
            .retain(|path| self.entries.iter().any(|entry| &entry.path == path));
        if self.anchor >= self.entries.len() {
            self.anchor = self.selected;
        }
    }

    pub fn current(&self) -> Option<&Entry> {
        self.entries.get(self.selected)
    }

    pub fn navigate_to(&mut self, path: &Path, show_hidden: bool) {
        self.nav.navigate_to(path);
        self.search = None;
        self.selected = 0;
        self.anchor = 0;
        self.extending = false;
        self.scroll = 0;
        self.reload(show_hidden);
    }

    pub fn navigate_up(&mut self, show_hidden: bool) {
        if self.nav.navigate_up() {
            self.search = None;
            self.selected = 0;
            self.anchor = 0;
            self.extending = false;
            self.scroll = 0;
            self.reload(show_hidden);
        }
    }

    pub fn go_back(&mut self, show_hidden: bool) {
        if self.nav.go_back() {
            self.search = None;
            self.selected = 0;
            self.anchor = 0;
            self.extending = false;
            self.reload(show_hidden);
        }
    }

    pub fn go_forward(&mut self, show_hidden: bool) {
        if self.nav.go_forward() {
            self.search = None;
            self.selected = 0;
            self.anchor = 0;
            self.extending = false;
            self.reload(show_hidden);
        }
    }

    pub fn selected_paths(&self) -> Vec<PathBuf> {
        let mut paths: Vec<PathBuf> = if self.selected_items.is_empty() {
            self.current()
                .filter(|entry| !entry.is_parent())
                .map(|entry| vec![entry.path.clone()])
                .unwrap_or_default()
        } else {
            self.selected_items.iter().cloned().collect()
        };
        paths.sort();
        paths
    }
}

#[derive(Clone, Debug)]
pub enum SidebarRow {
    Header(PlaceKind),
    Place(Place),
}

#[derive(Clone, Debug)]
pub enum Dialog {
    Message {
        title: String,
        text: String,
    },
    Confirm {
        title: String,
        message: String,
        action: ConfirmAction,
    },
    Input {
        title: String,
        prompt: String,
        value: String,
        cursor: usize,
        action: InputAction,
    },
    Properties {
        entry: Entry,
    },
    Help,
    OpenWith {
        entry: Entry,
        apps: Vec<DesktopApp>,
        selected: usize,
    },
}

#[derive(Clone, Debug)]
pub enum ConfirmAction {
    Delete {
        paths: Vec<PathBuf>,
        permanent: bool,
    },
    Unmount(Place),
}

#[derive(Clone, Debug)]
pub enum InputAction {
    Search,
    PathJump,
    Rename(PathBuf),
    NewFile,
    NewDir,
}

#[derive(Clone, Debug)]
pub struct DesktopApp {
    pub name: String,
    pub exec: String,
}

pub struct App {
    pub paths: AppPaths,
    pub settings: Settings,
    pub bookmarks: BookmarkStore,
    pub panes: Vec<Pane>,
    pub active: usize,
    pub sidebar_visible: bool,
    pub sidebar_focused: bool,
    pub sidebar_rows: Vec<SidebarRow>,
    pub sidebar_selected: usize,
    pub sidebar_scroll: usize,
    pub clipboard: Vec<PathBuf>,
    pub clipboard_mode: Option<PasteMode>,
    pub dialog: Option<Dialog>,
    pub status: String,
    pub type_ahead: String,
    pub type_ahead_at: Option<Instant>,
    pub show_sizes: bool,
    pub running: bool,
    pub help_scroll: u16,
    last_sidebar_reload: Instant,
}

impl App {
    pub fn new(
        paths: AppPaths,
        settings: Settings,
        bookmarks: BookmarkStore,
        start_dir: PathBuf,
    ) -> Self {
        let show_hidden = settings.show_hidden_files();
        let sidebar_visible = settings.sidebar_visible();
        let mut app = Self {
            paths,
            settings,
            bookmarks,
            panes: vec![Pane::new(&start_dir, show_hidden)],
            active: 0,
            sidebar_visible,
            sidebar_focused: false,
            sidebar_rows: Vec::new(),
            sidebar_selected: 0,
            sidebar_scroll: 0,
            clipboard: Vec::new(),
            clipboard_mode: None,
            dialog: None,
            status: String::new(),
            type_ahead: String::new(),
            type_ahead_at: None,
            show_sizes: false,
            running: true,
            help_scroll: 0,
            last_sidebar_reload: Instant::now(),
        };
        app.reload_sidebar();
        app
    }

    pub fn show_hidden(&self) -> bool {
        self.settings.show_hidden_files()
    }

    pub fn active_pane(&self) -> &Pane {
        &self.panes[self.active.min(self.panes.len() - 1)]
    }

    fn active_pane_mut(&mut self) -> &mut Pane {
        let index = self.active.min(self.panes.len() - 1);
        &mut self.panes[index]
    }

    fn reload_active(&mut self) {
        let show_hidden = self.show_hidden();
        self.active_pane_mut().reload(show_hidden);
    }

    fn reload_all(&mut self) {
        let show_hidden = self.show_hidden();
        for pane in &mut self.panes {
            pane.reload(show_hidden);
        }
    }

    pub fn tick(&mut self) {
        if let Some(at) = self.type_ahead_at {
            if at.elapsed() > Duration::from_secs(1) {
                self.type_ahead.clear();
                self.type_ahead_at = None;
            }
        }

        if self.sidebar_visible && self.last_sidebar_reload.elapsed() > Duration::from_secs(5) {
            self.reload_sidebar();
            self.last_sidebar_reload = Instant::now();
        }
    }

    pub fn handle_key(&mut self, key: KeyEvent) -> Option<External> {
        if self.dialog.is_some() {
            self.handle_dialog_key(key);
            return None;
        }

        match key.code {
            KeyCode::F(1) => {
                self.help_scroll = 0;
                self.dialog = Some(Dialog::Help);
                return None;
            }
            KeyCode::F(10) => {
                self.running = false;
                return None;
            }
            KeyCode::F(9) => {
                self.toggle_sidebar();
                return None;
            }
            KeyCode::F(3) => {
                self.toggle_split();
                return None;
            }
            KeyCode::F(5) => {
                self.reload_active();
                return None;
            }
            KeyCode::F(6) => {
                self.switch_pane(1);
                return None;
            }
            KeyCode::F(4) => {
                self.focus_bookmarks();
                return None;
            }
            KeyCode::F(7) => {
                self.message(
                    "Network",
                    "Connecting to network servers is not implemented in the Rust port yet.",
                );
                return None;
            }
            _ => {}
        }

        if self.sidebar_focused {
            return self.handle_sidebar_key(key);
        }

        self.handle_file_key(key)
    }

    fn handle_sidebar_key(&mut self, key: KeyEvent) -> Option<External> {
        let ctrl = key.modifiers.contains(KeyModifiers::CONTROL);
        match (key.code, ctrl) {
            (KeyCode::Up, _) => self.move_sidebar(-1),
            (KeyCode::Down, _) => self.move_sidebar(1),
            (KeyCode::PageUp, _) => self.move_sidebar(-8),
            (KeyCode::PageDown, _) => self.move_sidebar(8),
            (KeyCode::Home, _) => self.sidebar_first(),
            (KeyCode::End, _) => self.sidebar_last(),
            (KeyCode::Right, true) => {
                self.sidebar_focused = false;
            }
            (KeyCode::Enter, _) | (KeyCode::Right, _) => self.open_sidebar_place(),
            (KeyCode::Esc, _) | (KeyCode::Left, _) | (KeyCode::Char('s'), true) => {
                self.sidebar_focused = false;
            }
            (KeyCode::Delete, _) => self.remove_selected_bookmark(),
            (KeyCode::F(8), _) => self.request_unmount(),
            _ => {}
        }
        None
    }

    fn handle_file_key(&mut self, key: KeyEvent) -> Option<External> {
        let ctrl = key.modifiers.contains(KeyModifiers::CONTROL);
        let alt = key.modifiers.contains(KeyModifiers::ALT);
        // Terminals report Shift+N as an uppercase character (usually with the
        // SHIFT modifier already set). Normalize the letter so the lowercase
        // shortcut arms below match, and infer SHIFT when it was not reported.
        let shift = key.modifiers.contains(KeyModifiers::SHIFT)
            || matches!(key.code, KeyCode::Char(c) if c.is_ascii_uppercase());
        let code = match key.code {
            KeyCode::Char(c) if c.is_ascii_uppercase() => KeyCode::Char(c.to_ascii_lowercase()),
            code => code,
        };

        match (code, ctrl, alt, shift) {
            (KeyCode::Char('c'), true, _, _) => self.copy_or_cut(PasteMode::Copy),
            (KeyCode::Char('x'), true, _, _) => self.copy_or_cut(PasteMode::Cut),
            (KeyCode::Char('v'), true, _, _) => self.paste(),
            (KeyCode::Char('n'), true, _, _) => {
                self.input_dialog("New file", "File name: ", "", InputAction::NewFile)
            }
            (KeyCode::Char('f'), true, _, _) => {
                let value = self.active_pane().search.clone().unwrap_or_default();
                self.input_dialog("Search", "Pattern: ", &value, InputAction::Search);
            }
            (KeyCode::Char('h'), true, _, _) => self.toggle_hidden(),
            (KeyCode::Char('j'), true, _, _) | (KeyCode::Char('s'), false, true, _) => {
                self.toggle_sizes()
            }
            (KeyCode::Char('b'), true, _, _) => self.toggle_bookmark(),
            (KeyCode::Char('l'), true, _, _) => {
                let cwd = self.active_pane().cwd().to_string_lossy().into_owned();
                self.input_dialog("Go to directory", "Path: ", &cwd, InputAction::PathJump);
            }
            (KeyCode::Char('r'), true, _, _) => self.reload_active(),
            (KeyCode::Char('d'), true, _, _) => {
                return Some(External::Shell(self.active_pane().cwd().to_path_buf()));
            }
            (KeyCode::Char('o'), true, _, _) => self.open_in_file_manager(),
            (KeyCode::Char('a'), true, _, _) => self.select_all(),
            (KeyCode::Char('s'), true, _, _) => {
                if self.sidebar_visible {
                    self.sidebar_focused = true;
                }
            }
            (KeyCode::Left, true, false, _) => {
                if self.active == 0 && self.sidebar_visible {
                    self.sidebar_focused = true;
                } else if self.active > 0 {
                    self.active -= 1;
                }
            }
            (KeyCode::Right, true, false, _) => {
                if self.active + 1 < self.panes.len() {
                    self.active += 1;
                }
            }
            (KeyCode::Char('l'), false, true, _) => self.navigate_up(),
            (KeyCode::Char('o'), false, true, _) => self.open_with_dialog(),
            (KeyCode::Char('c'), _, _, true) => self.copy_path(),
            (KeyCode::Char('n'), _, _, true) => {
                self.input_dialog("New directory", "Directory name: ", "", InputAction::NewDir)
            }
            (KeyCode::Char('d'), _, _, true) => self.duplicate(),
            (KeyCode::Char('a'), _, _, true) => self.deselect_all(),
            (KeyCode::Char('o'), _, _, true) => self.open_privileged(),
            (KeyCode::Char('u'), false, true, _) | (KeyCode::Char('u'), _, _, true) => {
                self.request_unmount()
            }
            (KeyCode::Char('r'), _, _, true) => self.message(
                "Network",
                "Connecting to network servers is not implemented in the Rust port yet.",
            ),
            (KeyCode::Enter, true, _, _) => {
                if let Some(entry) = self.active_pane().current().cloned() {
                    if !entry.is_dir() {
                        return Some(External::Editor(entry.path));
                    }
                }
            }
            (KeyCode::Enter, _, _, true) | (KeyCode::Enter, false, true, _) => {
                self.show_properties()
            }
            (KeyCode::Enter, _, _, _) | (KeyCode::Right, false, false, false) => {
                self.open_current()
            }
            (KeyCode::Left, false, false, false)
            | (KeyCode::Esc, false, false, false)
            | (KeyCode::Up, false, true, _) => self.navigate_up_or_clear_search(),
            (KeyCode::Right, false, true, _) => self.go_forward(),
            (KeyCode::Left, false, true, _) => self.go_back(),
            (KeyCode::Up, false, false, true) => self.move_cursor(-1, true),
            (KeyCode::Down, false, false, true) => self.move_cursor(1, true),
            (KeyCode::Up, false, false, false) => self.move_cursor(-1, false),
            (KeyCode::Down, false, false, false) => self.move_cursor(1, false),
            (KeyCode::Up, true, false, _) | (KeyCode::Home, false, false, false) => {
                self.move_cursor_to(0)
            }
            (KeyCode::Down, true, false, _) | (KeyCode::End, false, false, false) => {
                self.move_cursor_to(usize::MAX)
            }
            (KeyCode::PageUp, false, false, _) => self.move_cursor(-10, false),
            (KeyCode::PageDown, false, false, _) => self.move_cursor(10, false),
            (KeyCode::Char(' '), false, false, true) => self.select_region(),
            (KeyCode::Char(' '), false, false, false) => self.toggle_select(),
            (KeyCode::Delete, false, false, true) => self.request_delete(true),
            (KeyCode::Delete, false, false, false) => self.request_delete(false),
            (KeyCode::F(2), _, _, _) => self.start_rename(),
            (KeyCode::Tab, false, false, false) => self.switch_pane(1),
            (KeyCode::Backspace, false, false, _) => self.type_ahead_backspace(),
            (KeyCode::Char(_), false, false, _) => {
                if let KeyCode::Char(c) = key.code {
                    self.type_ahead(c);
                }
            }
            _ => {}
        }

        None
    }

    fn handle_dialog_key(&mut self, key: KeyEvent) {
        let Some(mut dialog) = self.dialog.take() else {
            return;
        };
        let mut keep = true;

        match &mut dialog {
            Dialog::Help => match key.code {
                KeyCode::Esc | KeyCode::Enter | KeyCode::Char('q') => keep = false,
                KeyCode::Up => self.help_scroll = self.help_scroll.saturating_sub(1),
                KeyCode::Down => self.help_scroll = self.help_scroll.saturating_add(1),
                KeyCode::PageUp => self.help_scroll = self.help_scroll.saturating_sub(10),
                KeyCode::PageDown => self.help_scroll = self.help_scroll.saturating_add(10),
                _ => {}
            },
            Dialog::Message { .. } | Dialog::Properties { .. } => {
                if matches!(
                    key.code,
                    KeyCode::Esc | KeyCode::Enter | KeyCode::Char('q') | KeyCode::Char('Q')
                ) {
                    keep = false;
                }
            }
            Dialog::Confirm { action, .. } => match key.code {
                KeyCode::Char('y') | KeyCode::Char('Y') | KeyCode::Enter => {
                    let action = action.clone();
                    keep = false;
                    self.confirm(action);
                }
                KeyCode::Esc | KeyCode::Char('n') | KeyCode::Char('N') => keep = false,
                _ => {}
            },
            Dialog::Input {
                value,
                cursor,
                action,
                ..
            } => match key.code {
                KeyCode::Esc => keep = false,
                KeyCode::Enter => {
                    let action = action.clone();
                    let value = value.clone();
                    keep = false;
                    self.apply_input(action, value);
                }
                KeyCode::Backspace => backspace(value, cursor),
                KeyCode::Delete => delete_forward(value, cursor),
                KeyCode::Left => *cursor = cursor.saturating_sub(1),
                KeyCode::Right => {
                    if *cursor < value.chars().count() {
                        *cursor += 1;
                    }
                }
                KeyCode::Home => *cursor = 0,
                KeyCode::End => *cursor = value.chars().count(),
                KeyCode::Char(c)
                    if !key
                        .modifiers
                        .intersects(KeyModifiers::CONTROL | KeyModifiers::ALT) =>
                {
                    insert_char(value, cursor, c)
                }
                _ => {}
            },
            Dialog::OpenWith {
                entry,
                apps,
                selected,
            } => match key.code {
                KeyCode::Esc => keep = false,
                KeyCode::Up => {
                    if !apps.is_empty() {
                        *selected = selected.saturating_sub(1);
                    }
                }
                KeyCode::Down => {
                    if !apps.is_empty() {
                        *selected = (*selected + 1).min(apps.len() - 1);
                    }
                }
                KeyCode::Enter => {
                    if let Some(app) = apps.get(*selected).cloned() {
                        let path = entry.path.clone();
                        keep = false;
                        self.open_with_app(&path, &app);
                    }
                }
                _ => {}
            },
        }

        if keep {
            self.dialog = Some(dialog);
        }
    }

    fn message(&mut self, title: &str, text: impl Into<String>) {
        self.dialog = Some(Dialog::Message {
            title: title.to_string(),
            text: text.into(),
        });
    }

    fn input_dialog(&mut self, title: &str, prompt: &str, value: &str, action: InputAction) {
        self.dialog = Some(Dialog::Input {
            title: title.to_string(),
            prompt: prompt.to_string(),
            value: value.to_string(),
            cursor: value.chars().count(),
            action,
        });
    }

    fn apply_input(&mut self, action: InputAction, value: String) {
        let trimmed = value.trim().to_string();
        match action {
            InputAction::Search => {
                let pane = self.active_pane_mut();
                pane.search = if trimmed.is_empty() {
                    None
                } else {
                    Some(trimmed.clone())
                };
                pane.selected = 0;
                pane.anchor = 0;
                self.reload_active();
                self.set_status(if trimmed.is_empty() {
                    "Search cleared".to_string()
                } else {
                    format!("Search: {trimmed}")
                });
            }
            InputAction::PathJump => {
                if trimmed.is_empty() {
                    return;
                }
                let show_hidden = self.show_hidden();
                let target = expand_home(&trimmed, self.paths.home());
                if target.is_dir() {
                    let target = paths::normalize_path(&target);
                    self.active_pane_mut().navigate_to(&target, show_hidden);
                } else if target.is_file() {
                    let parent = target
                        .parent()
                        .map(Path::to_path_buf)
                        .unwrap_or_else(|| PathBuf::from("/"));
                    let name = target
                        .file_name()
                        .map(|name| name.to_string_lossy().into_owned())
                        .unwrap_or_default();
                    self.active_pane_mut().navigate_to(&parent, show_hidden);
                    let pane = self.active_pane_mut();
                    if let Some(index) = pane.entries.iter().position(|entry| entry.name == name) {
                        pane.selected = index;
                    }
                } else {
                    self.message(
                        "Go to directory",
                        format!("Not found: {}", target.display()),
                    );
                }
            }
            InputAction::Rename(path) => {
                if trimmed.is_empty() {
                    return;
                }
                match operations::rename(&path, &trimmed) {
                    Ok(new_path) => {
                        self.reload_active();
                        self.select_path(&new_path);
                        self.set_status(format!("Renamed to {trimmed}"));
                    }
                    Err(error) => self.message("Rename failed", error.to_string()),
                }
            }
            InputAction::NewFile | InputAction::NewDir => {
                if trimmed.is_empty() {
                    return;
                }
                if trimmed.contains('/') {
                    self.message("Invalid name", "Names cannot contain '/'.");
                    return;
                }
                let cwd = self.active_pane().cwd().to_path_buf();
                let path = cwd.join(&trimmed);
                let result = if matches!(action, InputAction::NewFile) {
                    operations::create_file(&path)
                } else {
                    operations::create_dir(&path)
                };
                match result {
                    Ok(path) => {
                        self.reload_active();
                        self.select_path(&path);
                        self.set_status(format!("Created {}", path.display()));
                    }
                    Err(error) => self.message("Create failed", error.to_string()),
                }
            }
        }
    }

    fn confirm(&mut self, action: ConfirmAction) {
        match action {
            ConfirmAction::Delete { paths, permanent } => {
                let report = if permanent {
                    operations::delete_permanent(&paths)
                } else {
                    operations::trash(&paths)
                };
                self.active_pane_mut().selected_items.clear();
                self.reload_active();
                if report.failed() {
                    self.message("Delete finished with errors", report.errors.join("\n"));
                } else {
                    self.set_status(format!(
                        "{} {} item(s)",
                        if permanent { "Deleted" } else { "Trashed" },
                        paths.len()
                    ));
                }
            }
            ConfirmAction::Unmount(place) => match places::unmount(&place) {
                Ok(()) => {
                    self.reload_sidebar();
                    self.set_status(format!("Unmounted {}", place.name));
                }
                Err(error) => self.message("Unmount failed", error),
            },
        }
    }

    fn copy_or_cut(&mut self, mode: PasteMode) {
        let paths = self.active_pane().selected_paths();
        if paths.is_empty() {
            self.set_status("Nothing selected".to_string());
            return;
        }
        let count = paths.len();
        self.clipboard = paths;
        self.clipboard_mode = Some(mode);
        self.set_status(format!(
            "{} {count} item(s)",
            if mode == PasteMode::Copy {
                "Copied"
            } else {
                "Cut"
            }
        ));
    }

    fn paste(&mut self) {
        let Some(mode) = self.clipboard_mode else {
            self.set_status("Clipboard is empty".to_string());
            return;
        };
        if self.clipboard.is_empty() {
            self.set_status("Clipboard is empty".to_string());
            return;
        }

        let destination = self.active_pane().cwd().to_path_buf();
        let sources = self.clipboard.clone();
        let report = operations::paste(&destination, mode, &sources);
        if mode == PasteMode::Cut {
            self.clipboard.clear();
            self.clipboard_mode = None;
        }
        self.reload_active();

        if report.failed() {
            self.message("Paste finished with errors", report.errors.join("\n"));
        } else {
            self.set_status(format!("Pasted {} item(s)", report.targets.len()));
        }
    }

    fn duplicate(&mut self) {
        let paths = self.active_pane().selected_paths();
        if paths.is_empty() {
            return;
        }
        let mut errors = Vec::new();
        let mut created = Vec::new();
        for path in &paths {
            match operations::duplicate(path) {
                Ok(copy) => created.push(copy),
                Err(error) => errors.push(format!("{}: {error}", path.display())),
            }
        }
        self.reload_active();
        if let Some(last) = created.last() {
            self.select_path(last);
        }
        if errors.is_empty() {
            self.set_status(format!("Duplicated {} item(s)", created.len()));
        } else {
            self.message("Duplicate finished with errors", errors.join("\n"));
        }
    }

    fn request_delete(&mut self, permanent: bool) {
        let paths = self.active_pane().selected_paths();
        if paths.is_empty() {
            self.set_status("Nothing selected".to_string());
            return;
        }
        let label = if permanent {
            "permanently delete"
        } else {
            "move to trash"
        };
        let message = if paths.len() == 1 {
            format!(
                "Are you sure you want to {label} '{}'? [Y/n]",
                paths[0]
                    .file_name()
                    .map(|name| name.to_string_lossy().into_owned())
                    .unwrap_or_else(|| paths[0].display().to_string())
            )
        } else {
            format!(
                "Are you sure you want to {label} {} items? [Y/n]",
                paths.len()
            )
        };
        self.dialog = Some(Dialog::Confirm {
            title: if permanent {
                "Delete permanently"
            } else {
                "Delete"
            }
            .to_string(),
            message,
            action: ConfirmAction::Delete { paths, permanent },
        });
    }

    fn start_rename(&mut self) {
        let Some(entry) = self.active_pane().current().cloned() else {
            return;
        };
        if entry.is_parent() {
            return;
        }
        self.input_dialog(
            "Rename",
            "New name: ",
            &entry.name,
            InputAction::Rename(entry.path),
        );
    }

    fn open_current(&mut self) {
        let Some(entry) = self.active_pane().current().cloned() else {
            return;
        };
        if entry.is_parent() {
            self.navigate_up();
            return;
        }
        if entry.is_dir() {
            let show_hidden = self.show_hidden();
            self.active_pane_mut().navigate_to(&entry.path, show_hidden);
        } else {
            self.launch_path(&entry.path);
        }
    }

    fn navigate_up(&mut self) {
        let show_hidden = self.show_hidden();
        self.active_pane_mut().navigate_up(show_hidden);
    }

    fn navigate_up_or_clear_search(&mut self) {
        if self.active_pane().search.is_some() {
            let pane = self.active_pane_mut();
            pane.search = None;
            self.reload_active();
            self.set_status("Search cleared".to_string());
        } else {
            self.navigate_up();
        }
    }

    fn go_back(&mut self) {
        let show_hidden = self.show_hidden();
        self.active_pane_mut().go_back(show_hidden);
    }

    fn go_forward(&mut self) {
        let show_hidden = self.show_hidden();
        self.active_pane_mut().go_forward(show_hidden);
    }

    fn move_cursor(&mut self, delta: isize, extend: bool) {
        let pane = self.active_pane_mut();
        if pane.entries.is_empty() {
            return;
        }
        let old = pane.selected;
        let last = pane.entries.len() - 1;
        let new = if delta < 0 {
            old.saturating_sub(delta.unsigned_abs())
        } else {
            (old + delta as usize).min(last)
        };

        pane.selected = new;

        if extend {
            if !pane.extending {
                pane.anchor = old;
                pane.extending = true;
            }
            let (low, high) = if pane.anchor <= new {
                (pane.anchor, new)
            } else {
                (new, pane.anchor)
            };
            for entry in &pane.entries[low..=high] {
                if !entry.is_parent() {
                    pane.selected_items.insert(entry.path.clone());
                }
            }
        } else {
            pane.extending = false;
        }
    }

    fn move_cursor_to(&mut self, index: usize) {
        let pane = self.active_pane_mut();
        if pane.entries.is_empty() {
            return;
        }
        pane.selected = index.min(pane.entries.len() - 1);
        pane.extending = false;
    }

    fn toggle_select(&mut self) {
        let pane = self.active_pane_mut();
        let Some(entry) = pane.current().cloned() else {
            return;
        };
        if entry.is_parent() {
            return;
        }
        if !pane.selected_items.remove(&entry.path) {
            pane.selected_items.insert(entry.path);
        }
        pane.anchor = pane.selected;
        pane.extending = true;
    }

    fn select_region(&mut self) {
        let pane = self.active_pane_mut();
        if pane.entries.is_empty() {
            return;
        }
        if !pane.extending {
            pane.anchor = pane.selected;
            pane.extending = true;
        }
        let (low, high) = if pane.anchor <= pane.selected {
            (pane.anchor, pane.selected)
        } else {
            (pane.selected, pane.anchor)
        };
        for entry in &pane.entries[low..=high] {
            if !entry.is_parent() {
                pane.selected_items.insert(entry.path.clone());
            }
        }
    }

    fn select_all(&mut self) {
        let pane = self.active_pane_mut();
        for entry in &pane.entries {
            if !entry.is_parent() {
                pane.selected_items.insert(entry.path.clone());
            }
        }
        self.set_status(format!(
            "Selected {} item(s)",
            self.active_pane().selected_items.len()
        ));
    }

    fn deselect_all(&mut self) {
        self.active_pane_mut().selected_items.clear();
        self.set_status("Selection cleared".to_string());
    }

    fn select_path(&mut self, path: &Path) {
        let pane = self.active_pane_mut();
        if let Some(index) = pane.entries.iter().position(|entry| entry.path == path) {
            pane.selected = index;
            pane.scroll = index.saturating_sub(3);
        }
    }

    fn toggle_hidden(&mut self) {
        let value = !self.settings.show_hidden_files();
        self.settings.set_show_hidden_files(value);
        let _ = self.settings.save();
        self.reload_all();
        self.set_status(format!(
            "Hidden files {}",
            if value { "shown" } else { "hidden" }
        ));
    }

    fn toggle_sizes(&mut self) {
        self.show_sizes = !self.show_sizes;
        self.set_status(format!(
            "Sizes {}",
            if self.show_sizes { "shown" } else { "hidden" }
        ));
    }

    fn toggle_bookmark(&mut self) {
        let cwd = self.active_pane().cwd().to_path_buf();
        match self.bookmarks.toggle(&cwd) {
            Ok(true) => self.set_status(format!("Bookmarked {}", cwd.display())),
            Ok(false) => self.set_status("Bookmark removed".to_string()),
            Err(error) => self.message("Bookmark failed", error.to_string()),
        }
        self.reload_sidebar();
    }

    fn open_in_file_manager(&mut self) {
        let cwd = self.active_pane().cwd().to_path_buf();
        match launch_file_manager(&cwd) {
            Ok(()) => self.set_status(format!("Opened {}", cwd.display())),
            Err(error) => self.message("Open failed", error),
        }
    }

    fn open_privileged(&mut self) {
        let cwd = self.active_pane().cwd().to_path_buf();
        let status = Command::new("pkexec").arg("nemo").arg(&cwd).status();
        match status {
            Ok(status) if status.success() => {
                self.set_status(format!("Opened {} with privileges", cwd.display()))
            }
            _ => self.message(
                "Open with privileges",
                "Could not launch 'pkexec nemo'. Make sure pkexec and a polkit agent are available.",
            ),
        }
    }

    fn copy_path(&mut self) {
        let cwd = self.active_pane().cwd().to_string_lossy().into_owned();
        if copy_text(&cwd) {
            self.set_status("Path copied to clipboard".to_string());
        } else {
            self.message(
                "Clipboard",
                "No clipboard tool found (tried wl-copy, xclip and xsel).",
            );
        }
    }

    fn show_properties(&mut self) {
        if let Some(entry) = self.active_pane().current().cloned() {
            self.dialog = Some(Dialog::Properties { entry });
        }
    }

    fn open_with_dialog(&mut self) {
        let Some(entry) = self.active_pane().current().cloned() else {
            return;
        };
        if entry.is_dir() {
            self.set_status("Directories open with the file manager".to_string());
            return;
        }
        let apps = discover_apps();
        if apps.is_empty() {
            self.message(
                "Open with",
                "No desktop applications with an Exec entry were found.",
            );
            return;
        }
        self.dialog = Some(Dialog::OpenWith {
            entry,
            apps,
            selected: 0,
        });
    }

    fn open_with_app(&mut self, path: &Path, app: &DesktopApp) {
        let command = build_exec_command(&app.exec, path);
        match Command::new("sh")
            .arg("-c")
            .arg(&command)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
        {
            Ok(_) => self.set_status(format!("Opening with {}", app.name)),
            Err(error) => self.message("Open failed", error.to_string()),
        }
    }

    fn launch_path(&mut self, path: &Path) {
        match launch_detached(path) {
            Ok(()) => self.set_status(format!("Opened {}", path.display())),
            Err(error) => self.message("Open failed", error),
        }
    }

    fn toggle_split(&mut self) {
        if self.panes.len() == 1 {
            let cwd = self.active_pane().cwd().to_path_buf();
            let show_hidden = self.show_hidden();
            self.panes.push(Pane::new(&cwd, show_hidden));
        } else {
            let keep = self.active;
            if let Some(pane) = self.panes.get(keep) {
                let cwd = pane.cwd().to_path_buf();
                let show_hidden = self.show_hidden();
                self.panes = vec![Pane::new(&cwd, show_hidden)];
            } else {
                self.panes.truncate(1);
            }
            self.active = 0;
        }
    }

    fn switch_pane(&mut self, delta: isize) {
        if self.panes.len() < 2 {
            return;
        }
        let count = self.panes.len() as isize;
        self.active = ((self.active as isize + delta).rem_euclid(count)) as usize;
    }

    fn toggle_sidebar(&mut self) {
        self.sidebar_visible = !self.sidebar_visible;
        if !self.sidebar_visible {
            self.sidebar_focused = false;
        } else {
            self.reload_sidebar();
        }
        self.settings.set_sidebar_visible(self.sidebar_visible);
        let _ = self.settings.save();
    }

    fn focus_bookmarks(&mut self) {
        if !self.sidebar_visible {
            self.sidebar_visible = true;
            self.settings.set_sidebar_visible(true);
            let _ = self.settings.save();
            self.reload_sidebar();
        }
        self.sidebar_focused = true;
        if let Some(index) = self.sidebar_rows.iter().position(|row| {
            matches!(
                row,
                SidebarRow::Place(Place {
                    kind: PlaceKind::Bookmarks,
                    ..
                })
            )
        }) {
            self.sidebar_selected = index;
        }
    }

    pub fn reload_sidebar(&mut self) {
        let previous = self
            .sidebar_selected_place()
            .map(|place| place.path.clone());

        let sections: Vec<(PlaceKind, Vec<Place>)> = vec![
            (PlaceKind::Places, places::standard_places(&self.paths)),
            (
                PlaceKind::Bookmarks,
                places::bookmark_places(&self.bookmarks),
            ),
            (PlaceKind::Devices, places::device_places()),
            (PlaceKind::Network, places::network_places()),
        ];

        let mut rows = Vec::new();
        for (kind, entries) in sections {
            if entries.is_empty() {
                continue;
            }
            rows.push(SidebarRow::Header(kind));
            rows.extend(entries.into_iter().map(SidebarRow::Place));
        }

        self.sidebar_rows = rows;

        let restored = previous.and_then(|path| {
            self.sidebar_rows
                .iter()
                .position(|row| matches!(row, SidebarRow::Place(place) if place.path == path))
        });
        self.sidebar_selected = restored
            .or_else(|| {
                self.sidebar_rows
                    .iter()
                    .position(|row| matches!(row, SidebarRow::Place(_)))
            })
            .unwrap_or(0);
    }

    pub fn sidebar_selected_place(&self) -> Option<&Place> {
        match self.sidebar_rows.get(self.sidebar_selected) {
            Some(SidebarRow::Place(place)) => Some(place),
            _ => None,
        }
    }

    fn move_sidebar(&mut self, delta: isize) {
        let count = self.sidebar_rows.len();
        if count == 0 {
            return;
        }
        let mut index = self.sidebar_selected.min(count - 1);
        for _ in 0..count {
            index = (index as isize + delta).rem_euclid(count as isize) as usize;
            if matches!(self.sidebar_rows[index], SidebarRow::Place(_)) {
                self.sidebar_selected = index;
                return;
            }
        }
    }

    fn sidebar_first(&mut self) {
        if let Some(index) = self
            .sidebar_rows
            .iter()
            .position(|row| matches!(row, SidebarRow::Place(_)))
        {
            self.sidebar_selected = index;
        }
    }

    fn sidebar_last(&mut self) {
        if let Some(index) = self
            .sidebar_rows
            .iter()
            .rposition(|row| matches!(row, SidebarRow::Place(_)))
        {
            self.sidebar_selected = index;
        }
    }

    fn open_sidebar_place(&mut self) {
        let Some(place) = self.sidebar_selected_place().cloned() else {
            return;
        };
        if !place.path.is_dir() {
            self.message(
                "Not available",
                format!("{} is not available right now.", place.name),
            );
            return;
        }
        let show_hidden = self.show_hidden();
        self.active_pane_mut().navigate_to(&place.path, show_hidden);
        self.sidebar_focused = false;
    }

    fn remove_selected_bookmark(&mut self) {
        let Some(place) = self.sidebar_selected_place().cloned() else {
            return;
        };
        if !place.bookmark {
            return;
        }
        match self.bookmarks.remove(&place.path) {
            Ok(true) => {
                self.reload_sidebar();
                self.set_status("Bookmark removed".to_string());
            }
            Ok(false) => {}
            Err(error) => self.message("Bookmark failed", error.to_string()),
        }
    }

    fn request_unmount(&mut self) {
        let Some(place) = self.sidebar_selected_place().cloned() else {
            self.set_status("Select a device or network share in the sidebar".to_string());
            return;
        };
        if !matches!(place.kind, PlaceKind::Devices | PlaceKind::Network) {
            self.set_status("Select a device or network share in the sidebar".to_string());
            return;
        }
        self.dialog = Some(Dialog::Confirm {
            title: "Unmount".to_string(),
            message: format!("Unmount '{}'? [Y/n]", place.name),
            action: ConfirmAction::Unmount(place),
        });
    }

    fn type_ahead(&mut self, c: char) {
        let now = Instant::now();
        if self
            .type_ahead_at
            .map(|at| now.duration_since(at) > Duration::from_millis(900))
            .unwrap_or(true)
        {
            self.type_ahead.clear();
        }
        self.type_ahead.push(c);
        self.type_ahead_at = Some(now);

        let needle = self.type_ahead.to_lowercase();
        let pane = self.active_pane_mut();
        if let Some(index) = pane
            .entries
            .iter()
            .position(|entry| !entry.is_parent() && entry.name.to_lowercase().starts_with(&needle))
        {
            pane.selected = index;
            pane.extending = false;
            self.set_status(format!("Jump to: {}", self.type_ahead));
        }
    }

    fn type_ahead_backspace(&mut self) {
        if self.type_ahead.pop().is_some() {
            self.type_ahead_at = Some(Instant::now());
        }
    }

    fn set_status(&mut self, status: String) {
        self.status = status;
    }
}

fn insert_char(value: &mut String, cursor: &mut usize, c: char) {
    let byte = char_to_byte(value, *cursor);
    value.insert(byte, c);
    *cursor += 1;
}

fn backspace(value: &mut String, cursor: &mut usize) {
    if *cursor == 0 {
        return;
    }
    let byte = char_to_byte(value, *cursor - 1);
    value.remove(byte);
    *cursor -= 1;
}

fn delete_forward(value: &mut String, cursor: &mut usize) {
    let byte = char_to_byte(value, *cursor);
    if byte < value.len() {
        value.remove(byte);
    }
}

fn char_to_byte(value: &str, cursor: usize) -> usize {
    value
        .char_indices()
        .nth(cursor)
        .map(|(index, _)| index)
        .unwrap_or(value.len())
}

fn launch_file_manager(path: &Path) -> Result<(), String> {
    let attempts: [(&str, &[&str]); 3] = [("nemo", &[]), ("xdg-open", &[]), ("gio", &["open"])];
    for (program, args) in attempts {
        let mut command = Command::new(program);
        command.args(args).arg(path);
        if command
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .is_ok()
        {
            return Ok(());
        }
    }
    Err("No file manager launcher found (tried nemo, xdg-open and gio).".to_string())
}

fn launch_detached(path: &Path) -> Result<(), String> {
    let attempts: [(&str, &[&str]); 2] = [("xdg-open", &[]), ("gio", &["open"])];
    for (program, args) in attempts {
        let mut command = Command::new(program);
        command.args(args).arg(path);
        if command
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .is_ok()
        {
            return Ok(());
        }
    }
    Err(format!("Could not open {}", path.display()))
}

fn copy_text(text: &str) -> bool {
    let attempts: [(&str, &[&str]); 3] = [
        ("wl-copy", &[]),
        ("xclip", &["-selection", "clipboard"]),
        ("xsel", &["-b", "-i"]),
    ];

    for (program, args) in attempts {
        let Ok(mut child) = Command::new(program)
            .args(args)
            .stdin(Stdio::piped())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
        else {
            continue;
        };
        if let Some(mut stdin) = child.stdin.take() {
            let _ = stdin.write_all(text.as_bytes());
        }
        if child.wait().map(|status| status.success()).unwrap_or(false) {
            return true;
        }
    }

    false
}

fn discover_apps() -> Vec<DesktopApp> {
    let mut dirs = vec![
        PathBuf::from("/usr/share/applications"),
        PathBuf::from("/usr/local/share/applications"),
    ];
    if let Some(data_home) = std::env::var_os("XDG_DATA_HOME") {
        dirs.push(PathBuf::from(data_home).join("applications"));
    } else if let Some(home) = std::env::var_os("HOME") {
        dirs.push(PathBuf::from(home).join(".local/share/applications"));
    }

    let mut apps = Vec::new();
    let mut seen = HashSet::new();

    for dir in dirs {
        let Ok(entries) = fs::read_dir(dir) else {
            continue;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension().and_then(|value| value.to_str()) != Some("desktop") {
                continue;
            }
            let Ok(text) = fs::read_to_string(&path) else {
                continue;
            };
            if let Some(app) = parse_desktop_entry(&text) {
                if seen.insert(app.name.clone()) {
                    apps.push(app);
                }
            }
        }
    }

    apps.sort_by_key(|a| a.name.to_lowercase());
    apps
}

fn parse_desktop_entry(text: &str) -> Option<DesktopApp> {
    let mut in_desktop_section = false;
    let mut name = None;
    let mut exec = None;
    let mut hidden = false;

    for line in text.lines() {
        let line = line.trim();
        if line.starts_with('[') {
            in_desktop_section = line == "[Desktop Entry]";
            continue;
        }
        if !in_desktop_section {
            continue;
        }
        if let Some(value) = line.strip_prefix("Name=") {
            if name.is_none() {
                name = Some(value.trim().to_string());
            }
        } else if let Some(value) = line.strip_prefix("Exec=") {
            if exec.is_none() {
                exec = Some(value.trim().to_string());
            }
        } else if line.eq_ignore_ascii_case("NoDisplay=true")
            || line.eq_ignore_ascii_case("Hidden=true")
            || line.eq_ignore_ascii_case("Terminal=true")
        {
            hidden = true;
        }
    }

    if hidden {
        return None;
    }
    let name = name.filter(|value| !value.is_empty())?;
    let exec = exec.filter(|value| !value.is_empty())?;
    Some(DesktopApp { name, exec })
}

fn build_exec_command(exec: &str, path: &Path) -> String {
    let quoted = shell_quote(&path.to_string_lossy());
    let mut replaced = exec.to_string();
    let mut had_field = false;
    for field in ["%f", "%F", "%u", "%U"] {
        if replaced.contains(field) {
            replaced = replaced.replace(field, &quoted);
            had_field = true;
        }
    }
    for field in ["%c", "%k", "%i", "%d", "%D", "%n", "%N", "%v", "%m"] {
        replaced = replaced.replace(field, "");
    }
    if !had_field {
        replaced.push(' ');
        replaced.push_str(&quoted);
    }
    replaced
}

fn shell_quote(value: &str) -> String {
    let mut result = String::with_capacity(value.len() + 2);
    result.push('\'');
    for character in value.chars() {
        if character == '\'' {
            result.push_str("'\\''");
        } else {
            result.push(character);
        }
    }
    result.push('\'');
    result
}

#[allow(clippy::items_after_test_module)]
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn desktop_entries_are_parsed() {
        let entry = parse_desktop_entry(
            "[Desktop Entry]\nType=Application\nName=Text Editor\nExec=gedit %U\n",
        )
        .expect("entry should parse");
        assert_eq!(entry.name, "Text Editor");
        assert_eq!(entry.exec, "gedit %U");

        assert!(
            parse_desktop_entry("[Desktop Entry]\nName=Hidden\nExec=hidden\nNoDisplay=true\n")
                .is_none()
        );
        assert!(parse_desktop_entry("[Desktop Action new]\nName=New\nExec=new\n").is_none());
    }

    #[test]
    fn exec_commands_quote_paths() {
        let command = build_exec_command("gedit %U", Path::new("/tmp/a b.txt"));
        assert_eq!(command, "gedit '/tmp/a b.txt'");

        let command = build_exec_command("myapp", Path::new("/tmp/it's.txt"));
        assert_eq!(command, "myapp '/tmp/it'\\''s.txt'");
    }

    #[test]
    fn dialog_text_editing_handles_multibyte_characters() {
        let mut value = "héllo".to_string();
        let mut cursor = 1;
        insert_char(&mut value, &mut cursor, '!');
        assert_eq!(value, "h!éllo");
        assert_eq!(cursor, 2);
        backspace(&mut value, &mut cursor);
        assert_eq!(value, "héllo");
        assert_eq!(cursor, 1);
    }

    #[test]
    fn uppercase_letters_trigger_shift_shortcuts() {
        let root = std::env::temp_dir().join(format!(
            "ferret-tui-shift-test-{}-{:?}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        fs::create_dir_all(&root).unwrap();
        let file = root.join("a.txt");
        fs::write(&file, "x").unwrap();

        let paths = AppPaths::with_roots(
            root.clone(),
            root.join("data"),
            root.join("config"),
            root.join("cache"),
        );
        let settings = Settings::load(root.join("settings"));
        let bookmarks = BookmarkStore::new(root.join("bookmarks"));
        let mut app = App::new(paths, settings, bookmarks, root.clone());

        // Crossterm reports Shift+N as an uppercase character, often with the
        // SHIFT modifier set. Both forms must open the new-directory dialog.
        app.handle_key(KeyEvent::new(KeyCode::Char('N'), KeyModifiers::SHIFT));
        assert!(matches!(
            app.dialog,
            Some(Dialog::Input {
                action: InputAction::NewDir,
                ..
            })
        ));
        app.dialog = None;

        // Shift+D duplicates the selected file.
        let index = app
            .active_pane()
            .entries
            .iter()
            .position(|entry| entry.path == file)
            .expect("file should be listed");
        app.active_pane_mut().selected = index;
        app.handle_key(KeyEvent::new(KeyCode::Char('D'), KeyModifiers::SHIFT));
        assert!(root.join("a (1).txt").exists());

        let _ = fs::remove_dir_all(&root);
    }
}

pub const HELP_LINES: &[&str] = &[
    "Navigation",
    "  Up / Down                Navigate items",
    "  Shift + Up / Down        Select a range of items",
    "  Page Up / Page Down      Move one page",
    "  Home / End               First / last item",
    "  Enter / Right            Open directory or file",
    "  Left / Esc / Alt+Up      Go up one directory",
    "  Alt+Left / Alt+Right     Back / forward",
    "  Ctrl+L                   Go to directory by path",
    "  Ctrl+S                   Focus sidebar",
    "  F6 / Ctrl+Left/Right     Switch panes",
    "  Ctrl+D                   Open a shell here",
    "  Ctrl+Enter               Open selected file in nano",
    "  Alt+O                    Open selected file with...",
    "",
    "Editing",
    "  F2                       Rename",
    "  Delete                   Move to trash",
    "  Shift+Delete             Delete permanently",
    "  Space                    Select / deselect item",
    "  Shift+Space              Select region",
    "  Ctrl+A / Shift+A         Select all / deselect all",
    "  Ctrl+B                   Add / remove bookmark",
    "  Ctrl+C / Ctrl+X / Ctrl+V Copy / cut / paste",
    "  Ctrl+N / Shift+N         New file / directory",
    "  Shift+D                  Duplicate",
    "",
    "View and misc",
    "  F1                       This help",
    "  F3                       Toggle split pane",
    "  F4                       Focus bookmarks",
    "  F5 / Ctrl+R              Reload",
    "  F9                       Toggle sidebar",
    "  Ctrl+F                   Search current directory",
    "  Ctrl+H                   Toggle hidden files",
    "  Alt+S / Ctrl+J           Toggle file sizes",
    "  Ctrl+O                   Open in file manager",
    "  Shift+O                  Open in file manager as root",
    "  Shift+C                  Copy current path",
    "  Shift+Enter / Alt+Enter  Properties",
    "  F7                       Connect to network server",
    "  F8                       Unmount device / share",
    "  F10                      Quit",
    "",
    "Sidebar",
    "  Up / Down                Move selection",
    "  Enter / Right            Open place",
    "  Delete                   Remove bookmark",
    "  Escape / Left            Return to file pane",
];
