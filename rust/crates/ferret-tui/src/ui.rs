use ratatui::layout::{Constraint, Layout, Rect};
use ratatui::style::{Color, Modifier, Style};
use ratatui::text::{Line, Span};
use ratatui::widgets::{Block, Borders, Clear, Paragraph, Wrap};
use ratatui::Frame;
use unicode_width::UnicodeWidthStr;

use ferret_core::entry::{Entry, EntryKind, FileKind};
use ferret_core::format;
use ferret_core::paths;

use crate::app::{App, DesktopApp, Dialog, SidebarRow, HELP_LINES};

pub fn draw(frame: &mut Frame, app: &mut App) {
    let area = frame.area();
    let rows = Layout::vertical([
        Constraint::Length(1),
        Constraint::Min(1),
        Constraint::Length(1),
    ])
    .split(area);

    draw_header(frame, rows[0], app);
    draw_body(frame, rows[1], app);
    draw_status(frame, rows[2], app);

    match &app.dialog {
        Some(Dialog::Help) => draw_help(frame, area, app.help_scroll),
        Some(dialog) => draw_dialog(frame, area, dialog, app),
        None => {}
    }
}

fn draw_header(frame: &mut Frame, area: Rect, app: &App) {
    let cwd = app.active_pane().cwd();
    let location = if app.bookmarks.is_bookmark_dir(cwd) {
        "Bookmarks".to_string()
    } else {
        cwd.to_string_lossy().into_owned()
    };
    let text = format!(" File-Explorer ({location})");

    let style = if paths::is_privileged() {
        Style::default().fg(Color::White).bg(Color::Red)
    } else if app.sidebar_focused {
        Style::default().fg(Color::LightBlue)
    } else {
        Style::default().fg(Color::White)
    };

    frame.render_widget(Paragraph::new(text).style(style), area);
}

fn draw_body(frame: &mut Frame, area: Rect, app: &mut App) {
    let sidebar_width = if app.sidebar_visible && area.width > 24 {
        (area.width / 5)
            .clamp(16, 30)
            .min(area.width.saturating_sub(16))
    } else {
        0
    };

    let panes_area = if sidebar_width > 0 {
        let chunks =
            Layout::horizontal([Constraint::Length(sidebar_width), Constraint::Min(1)]).split(area);
        draw_sidebar(frame, chunks[0], app);
        chunks[1]
    } else {
        area
    };

    let count = app.panes.len();
    let constraints: Vec<Constraint> = if count <= 1 {
        vec![Constraint::Percentage(100)]
    } else {
        vec![Constraint::Percentage(50); count]
    };
    let chunks = Layout::horizontal(constraints).split(panes_area);

    for (index, chunk) in chunks.iter().copied().enumerate() {
        draw_pane(frame, chunk, app, index);
        if index + 1 < count && chunk.width > 0 {
            let separator = Rect {
                x: chunk.x + chunk.width - 1,
                y: chunk.y,
                width: 1,
                height: chunk.height,
            };
            frame.render_widget(
                Block::default()
                    .borders(Borders::RIGHT)
                    .border_style(Style::default().fg(Color::DarkGray)),
                separator,
            );
        }
    }
}

fn draw_sidebar(frame: &mut Frame, area: Rect, app: &mut App) {
    let block = Block::default()
        .borders(Borders::RIGHT)
        .border_style(Style::default().fg(Color::DarkGray));
    let inner = block.inner(area);
    frame.render_widget(block, area);

    let height = inner.height as usize;
    if height == 0 || app.sidebar_rows.is_empty() {
        return;
    }

    if app.sidebar_selected >= app.sidebar_rows.len() {
        app.sidebar_selected = app.sidebar_rows.len() - 1;
    }
    if app.sidebar_selected < app.sidebar_scroll {
        app.sidebar_scroll = app.sidebar_selected;
    } else if app.sidebar_selected >= app.sidebar_scroll + height {
        app.sidebar_scroll = app.sidebar_selected + 1 - height;
    }

    let scroll = app.sidebar_scroll;
    let focused = app.sidebar_focused;
    let selected_index = app.sidebar_selected;

    let mut lines: Vec<Line> = Vec::with_capacity(height);
    for (index, row) in app
        .sidebar_rows
        .iter()
        .enumerate()
        .skip(scroll)
        .take(height)
    {
        match row {
            SidebarRow::Header(kind) => {
                lines.push(Line::from(Span::styled(
                    format!(" {} ", kind.title()),
                    Style::default()
                        .fg(Color::Yellow)
                        .add_modifier(Modifier::BOLD),
                )));
            }
            SidebarRow::Place(place) => {
                let selected = index == selected_index;
                let style = if selected {
                    if focused {
                        Style::default().fg(Color::White).bg(Color::Blue)
                    } else {
                        Style::default().fg(Color::Cyan)
                    }
                } else {
                    Style::default().fg(Color::Gray)
                };
                let symbol = match place.kind {
                    ferret_core::places::PlaceKind::Places => "▪",
                    ferret_core::places::PlaceKind::Bookmarks => "★",
                    ferret_core::places::PlaceKind::Devices => "▣",
                    ferret_core::places::PlaceKind::Network => "☁",
                };
                lines.push(Line::from(vec![
                    Span::styled(if selected { "▸ " } else { "  " }, style),
                    Span::styled(format!("{symbol} "), style),
                    Span::styled(place.name.clone(), style),
                ]));
            }
        }
    }

    frame.render_widget(Paragraph::new(lines), inner);
}

fn draw_pane(frame: &mut Frame, area: Rect, app: &mut App, index: usize) {
    let height = area.height as usize;
    if height == 0 {
        return;
    }

    let show_sizes = app.show_sizes;
    let active = app.active == index;
    let pane = &mut app.panes[index];

    if pane.entries.is_empty() {
        let text = pane
            .error
            .clone()
            .map(|error| format!(" {error}"))
            .unwrap_or_else(|| " (empty)".to_string());
        frame.render_widget(
            Paragraph::new(text).style(Style::default().fg(Color::DarkGray)),
            area,
        );
        return;
    }

    if pane.selected >= pane.entries.len() {
        pane.selected = pane.entries.len() - 1;
    }
    if pane.selected < pane.scroll {
        pane.scroll = pane.selected;
    } else if pane.selected >= pane.scroll + height {
        pane.scroll = pane.selected + 1 - height;
    }

    let scroll = pane.scroll;
    let mut lines: Vec<Line> = Vec::with_capacity(height);
    for (entry_index, entry) in pane.entries.iter().enumerate().skip(scroll).take(height) {
        lines.push(entry_line(
            entry,
            entry_index == pane.selected,
            pane.selected_items.contains(&entry.path),
            active,
            show_sizes,
            area.width,
        ));
    }

    frame.render_widget(Paragraph::new(lines), area);
}

fn entry_line(
    entry: &Entry,
    cursor: bool,
    multi_selected: bool,
    active: bool,
    show_sizes: bool,
    width: u16,
) -> Line<'static> {
    let mut style = match entry.kind {
        EntryKind::Parent => Style::default().fg(Color::DarkGray),
        EntryKind::Directory => Style::default()
            .fg(Color::Cyan)
            .add_modifier(Modifier::BOLD),
        EntryKind::File(FileKind::Executable) => Style::default().fg(Color::Green),
        EntryKind::File(FileKind::Archive) => Style::default().fg(Color::Magenta),
        EntryKind::File(FileKind::Image) => Style::default().fg(Color::LightMagenta),
        EntryKind::File(FileKind::Audio) => Style::default().fg(Color::LightCyan),
        EntryKind::File(FileKind::Video) => Style::default().fg(Color::LightBlue),
        EntryKind::File(FileKind::Deb) => Style::default().fg(Color::LightYellow),
        EntryKind::File(FileKind::Text) => Style::default().fg(Color::Gray),
        EntryKind::File(FileKind::Other) => Style::default().fg(Color::Gray),
    };

    if entry.is_hidden {
        style = style.add_modifier(Modifier::DIM);
    }
    if entry.access_denied || entry.is_broken_link {
        style = Style::default().fg(Color::Red);
    } else if entry.is_symlink {
        style = style.fg(Color::LightCyan);
    }

    let marker = if multi_selected {
        "●"
    } else if cursor && active {
        "▌"
    } else {
        " "
    };
    let marker_color = if multi_selected {
        Color::Yellow
    } else if cursor && active {
        Color::Cyan
    } else {
        Color::DarkGray
    };

    let mut name = entry.name.clone();
    if entry.is_broken_link {
        name.push_str("  [broken]");
    }
    if entry.access_denied {
        name.push_str("  [denied]");
    }

    let mut spans = vec![
        Span::styled(format!("{marker} "), Style::default().fg(marker_color)),
        Span::styled(entry.icon_ascii().to_string(), style),
        Span::raw(" "),
        Span::styled(name, style),
    ];

    if show_sizes && !entry.is_dir() {
        let size = format::format_size(entry.size);
        let prefix: usize = spans
            .iter()
            .map(|span| UnicodeWidthStr::width(span.content.as_ref()))
            .sum();
        let target = width as usize;
        let padding = target.saturating_sub(prefix + size.len()).max(1);
        spans.push(Span::raw(" ".repeat(padding)));
        spans.push(Span::styled(size, Style::default().fg(Color::DarkGray)));
    }

    let line = Line::from(spans);
    if cursor {
        let background = if active {
            Color::Rgb(45, 52, 70)
        } else {
            Color::Rgb(32, 32, 36)
        };
        line.style(Style::default().bg(background))
    } else {
        line
    }
}

fn draw_status(frame: &mut Frame, area: Rect, app: &App) {
    let pane = app.active_pane();
    let selected = pane.selected_items.len();
    let base = if app.status.is_empty() {
        format!(" {} item(s)", pane.entries.len())
    } else {
        format!(" {}", app.status)
    };
    let selection = if selected > 0 {
        format!("  ·  {selected} selected")
    } else {
        String::new()
    };
    let type_ahead = if app.type_ahead.is_empty() {
        String::new()
    } else {
        format!("  ·  jump: {}", app.type_ahead)
    };
    let text = format!("{base}{selection}{type_ahead}  ·  F1 Help  F10 Quit");
    frame.render_widget(
        Paragraph::new(text).style(Style::default().fg(Color::Gray)),
        area,
    );
}

fn draw_help(frame: &mut Frame, area: Rect, scroll: u16) {
    let rect = Rect {
        x: area.x + 2,
        y: area.y + 1,
        width: area.width.saturating_sub(4),
        height: area.height.saturating_sub(2),
    };
    frame.render_widget(Clear, rect);
    let block = Block::default()
        .borders(Borders::ALL)
        .border_style(Style::default().fg(Color::Cyan))
        .title(" Help · Up/Down to scroll · Esc to close ");
    let inner = block.inner(rect);
    frame.render_widget(block, rect);

    let skip = scroll as usize;
    let height = inner.height as usize;
    let mut lines = Vec::with_capacity(height);
    for text in HELP_LINES.iter().skip(skip).take(height) {
        let header = !text.starts_with("  ") && !text.is_empty();
        let style = if header {
            Style::default()
                .fg(Color::Yellow)
                .add_modifier(Modifier::BOLD)
        } else {
            Style::default().fg(Color::Gray)
        };
        lines.push(Line::from(Span::styled((*text).to_string(), style)));
    }
    frame.render_widget(Paragraph::new(lines), inner);
}

fn draw_dialog(frame: &mut Frame, area: Rect, dialog: &Dialog, app: &App) {
    match dialog {
        Dialog::Help => {}
        Dialog::Message { title, text } => {
            let line_count = text.lines().count().max(1) as u16;
            let rect = centered(
                area,
                area.width.saturating_sub(8).min(76),
                (line_count + 4).min(area.height),
            );
            frame.render_widget(Clear, rect);
            let block = Block::default()
                .borders(Borders::ALL)
                .border_style(Style::default().fg(Color::Cyan))
                .title(format!(" {title} "));
            let inner = block.inner(rect);
            frame.render_widget(block, rect);
            frame.render_widget(
                Paragraph::new(text.clone())
                    .wrap(Wrap { trim: false })
                    .style(Style::default().fg(Color::Gray)),
                inner,
            );
        }
        Dialog::Confirm { title, message, .. } => {
            let rect = centered(
                area,
                area.width.saturating_sub(8).min(76),
                7.min(area.height),
            );
            frame.render_widget(Clear, rect);
            let block = Block::default()
                .borders(Borders::ALL)
                .border_style(Style::default().fg(Color::Yellow))
                .title(format!(" {title} "));
            let inner = block.inner(rect);
            frame.render_widget(block, rect);
            frame.render_widget(
                Paragraph::new(message.clone())
                    .wrap(Wrap { trim: false })
                    .style(Style::default().fg(Color::White)),
                inner,
            );
        }
        Dialog::Input {
            title,
            prompt,
            value,
            cursor,
            ..
        } => {
            let rect = centered(
                area,
                area.width.saturating_sub(8).min(76),
                7.min(area.height),
            );
            frame.render_widget(Clear, rect);
            let block = Block::default()
                .borders(Borders::ALL)
                .border_style(Style::default().fg(Color::Cyan))
                .title(format!(" {title} "));
            let inner = block.inner(rect);
            frame.render_widget(block, rect);

            let mut display = String::new();
            let cursor_index = *cursor;
            for (index, character) in value.chars().enumerate() {
                if index == cursor_index {
                    display.push('▏');
                }
                display.push(character);
            }
            if cursor_index >= value.chars().count() {
                display.push('▏');
            }

            let lines = vec![
                Line::from(Span::styled(
                    prompt.clone(),
                    Style::default().fg(Color::Gray),
                )),
                Line::from(Span::styled(
                    display,
                    Style::default()
                        .fg(Color::White)
                        .add_modifier(Modifier::BOLD),
                )),
                Line::from(""),
                Line::from(Span::styled(
                    "Enter: confirm   Esc: cancel",
                    Style::default().fg(Color::DarkGray),
                )),
            ];
            frame.render_widget(Paragraph::new(lines), inner);
        }
        Dialog::Properties { entry } => {
            let lines = property_lines(entry);
            let height = (lines.len() as u16 + 2).min(area.height);
            let rect = centered(area, area.width.saturating_sub(8).min(86), height);
            frame.render_widget(Clear, rect);
            let block = Block::default()
                .borders(Borders::ALL)
                .border_style(Style::default().fg(Color::Cyan))
                .title(" Properties ");
            let inner = block.inner(rect);
            frame.render_widget(block, rect);
            frame.render_widget(Paragraph::new(lines), inner);
        }
        Dialog::OpenWith {
            entry,
            apps,
            selected,
        } => {
            let height = (apps.len() as u16 + 6).min(area.height);
            let rect = centered(area, area.width.saturating_sub(8).min(76), height);
            frame.render_widget(Clear, rect);
            let block = Block::default()
                .borders(Borders::ALL)
                .border_style(Style::default().fg(Color::Cyan))
                .title(format!(" Open {} with… ", entry.name));
            let inner = block.inner(rect);
            frame.render_widget(block, rect);

            let visible = inner.height.saturating_sub(2) as usize;
            let mut start = 0usize;
            if visible > 0 && *selected >= visible {
                start = *selected + 1 - visible;
            }
            let mut lines = vec![Line::from(Span::styled(
                "Choose an application (Up/Down, Enter, Esc):",
                Style::default().fg(Color::Gray),
            ))];
            for (index, app) in apps.iter().enumerate().skip(start).take(visible) {
                lines.push(app_line(app, index == *selected));
            }
            frame.render_widget(Paragraph::new(lines), inner);
            let _ = app;
        }
    }
}

fn app_line(app: &DesktopApp, selected: bool) -> Line<'static> {
    let style = if selected {
        Style::default().fg(Color::White).bg(Color::Blue)
    } else {
        Style::default().fg(Color::Gray)
    };
    Line::from(vec![
        Span::styled(if selected { "▸ " } else { "  " }, style),
        Span::styled(app.name.clone(), style),
    ])
}

fn property_lines(entry: &Entry) -> Vec<Line<'static>> {
    let kind = match entry.kind {
        EntryKind::Parent => "Parent directory".to_string(),
        EntryKind::Directory => "Directory".to_string(),
        EntryKind::File(kind) => format!("File ({})", file_kind_label(kind)),
    };

    let mut lines = vec![
        property("Name", entry.name.clone()),
        property("Path", entry.path.to_string_lossy().into_owned()),
        property("Type", kind),
        property(
            "Size",
            if entry.is_dir() {
                "—".to_string()
            } else {
                format!("{} ({} bytes)", format::format_size(entry.size), entry.size)
            },
        ),
    ];

    if let Some(modified) = entry.modified {
        lines.push(property("Modified", format::format_time(modified)));
    }
    if let Some(created) = entry.created {
        lines.push(property("Created", format::format_time(created)));
    }
    if let Some(mode) = entry.mode {
        lines.push(property(
            "Permissions",
            format!(
                "{}  ({:04o})",
                format::format_permissions(mode & 0o777),
                mode & 0o777
            ),
        ));
    }
    if let Some(uid) = entry.uid {
        lines.push(property(
            "Owner",
            format!("{} ({uid})", format::user_name(uid)),
        ));
    }
    if let Some(gid) = entry.gid {
        lines.push(property(
            "Group",
            format!("{} ({gid})", format::group_name(gid)),
        ));
    }
    if let Some(target) = &entry.symlink_target {
        lines.push(property(
            "Link target",
            target.to_string_lossy().into_owned(),
        ));
    }

    let mut flags = Vec::new();
    if entry.is_hidden {
        flags.push("hidden");
    }
    if entry.is_symlink {
        flags.push("symlink");
    }
    if entry.is_broken_link {
        flags.push("broken link");
    }
    if entry.is_read_only {
        flags.push("read only");
    }
    if entry.access_denied {
        flags.push("access denied");
    }
    if entry.is_executable {
        flags.push("executable");
    }
    if !flags.is_empty() {
        lines.push(property("Flags", flags.join(", ")));
    }

    lines
}

fn property(label: &str, value: String) -> Line<'static> {
    Line::from(vec![
        Span::styled(
            format!("{label:<12}"),
            Style::default()
                .fg(Color::Yellow)
                .add_modifier(Modifier::BOLD),
        ),
        Span::styled(value, Style::default().fg(Color::Gray)),
    ])
}

fn file_kind_label(kind: FileKind) -> &'static str {
    match kind {
        FileKind::Text => "text",
        FileKind::Image => "image",
        FileKind::Audio => "audio",
        FileKind::Video => "video",
        FileKind::Archive => "archive",
        FileKind::Deb => "package",
        FileKind::Executable => "executable",
        FileKind::Other => "other",
    }
}

fn centered(area: Rect, width: u16, height: u16) -> Rect {
    let width = width.max(1).min(area.width);
    let height = height.max(1).min(area.height);
    Rect {
        x: area.x + (area.width - width) / 2,
        y: area.y + (area.height - height) / 2,
        width,
        height,
    }
}

trait EntryIcon {
    fn icon_ascii(&self) -> &'static str;
}

impl EntryIcon for Entry {
    fn icon_ascii(&self) -> &'static str {
        match self.kind {
            EntryKind::Parent => " ",
            EntryKind::Directory => "▸",
            EntryKind::File(FileKind::Executable) => "»",
            EntryKind::File(FileKind::Deb) => "◆",
            EntryKind::File(FileKind::Archive) => "▣",
            EntryKind::File(FileKind::Image) => "▧",
            EntryKind::File(FileKind::Video) => "▶",
            EntryKind::File(FileKind::Audio) => "♪",
            EntryKind::File(FileKind::Text) => "≡",
            EntryKind::File(FileKind::Other) => "·",
        }
    }
}
