mod app;
mod ui;

use std::env;
use std::io;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::time::Duration;

use crossterm::event::{self, Event, KeyEventKind};

use ferret_core::bookmarks::BookmarkStore;
use ferret_core::paths::{expand_home, AppPaths};
use ferret_core::settings::Settings;

use app::{App, External};

fn main() -> io::Result<()> {
    let mut start_dir: Option<PathBuf> = None;
    let mut open_flag = false;

    let args: Vec<String> = env::args().skip(1).collect();
    let mut index = 0;
    while index < args.len() {
        match args[index].as_str() {
            "-h" | "--help" => {
                print_help();
                return Ok(());
            }
            "-v" | "--version" => {
                println!("v{}\n", env!("CARGO_PKG_VERSION"));
                return Ok(());
            }
            "-o" | "--open" => {
                open_flag = true;
            }
            "-d" | "--directory" => {
                let Some(value) = args.get(index + 1) else {
                    eprintln!("error: --directory requires a path");
                    std::process::exit(2);
                };
                start_dir = Some(PathBuf::from(value));
                index += 1;
            }
            "-t" | "--tty" => {}
            other
                if other.starts_with("sftp://")
                    || other.starts_with("smb://")
                    || other.starts_with("ftp://")
                    || other.starts_with("dav://")
                    || other.starts_with("davs://") =>
            {
                eprintln!(
                    "warning: network mounting is not implemented in the Rust port; ignoring {other}"
                );
            }
            other if other.starts_with('-') => {
                eprintln!("error: unknown option '{other}'\nTry --help");
                std::process::exit(2);
            }
            other => {
                if start_dir.is_none() {
                    start_dir = Some(PathBuf::from(other));
                }
            }
        }
        index += 1;
    }

    let paths = AppPaths::discover();
    let resolved_start = resolve_start(start_dir, &paths);

    if open_flag {
        match launch_file_manager(&resolved_start) {
            Ok(()) => return Ok(()),
            Err(error) => {
                eprintln!("error: {error}");
                std::process::exit(1);
            }
        }
    }

    paths.migrate_legacy_config();
    let settings = Settings::load_for(&paths);
    let bookmarks = BookmarkStore::new(paths.data_dir().join("bookmarks"));
    let mut application = App::new(paths, settings, bookmarks, resolved_start);

    let mut terminal = ratatui::init();
    let result = run(&mut terminal, &mut application);
    ratatui::restore();
    result
}

fn run(terminal: &mut ratatui::DefaultTerminal, application: &mut App) -> io::Result<()> {
    loop {
        terminal.draw(|frame| ui::draw(frame, application))?;

        if event::poll(Duration::from_millis(200))? {
            if let Event::Key(key) = event::read()? {
                if key.kind == KeyEventKind::Press {
                    if let Some(external) = application.handle_key(key) {
                        run_external(terminal, external)?;
                    }
                }
            }
        }

        application.tick();
        if !application.running {
            break;
        }
    }
    Ok(())
}

fn run_external(terminal: &mut ratatui::DefaultTerminal, external: External) -> io::Result<()> {
    ratatui::restore();

    match external {
        External::Shell(directory) => {
            let shell = env::var("SHELL").unwrap_or_else(|_| "/bin/bash".to_string());
            let _ = Command::new(shell)
                .arg("-i")
                .current_dir(directory)
                .status();
        }
        External::Editor(path) => {
            let _ = Command::new("nano").arg(path).status();
        }
    }

    *terminal = ratatui::init();
    terminal.clear()?;
    Ok(())
}

fn resolve_start(start: Option<PathBuf>, paths: &AppPaths) -> PathBuf {
    let current = env::current_dir().unwrap_or_else(|_| paths.home().to_path_buf());

    let Some(start) = start else {
        return if current.is_dir() {
            current
        } else {
            paths.home().to_path_buf()
        };
    };

    let expanded = expand_home(&start.to_string_lossy(), paths.home());
    if expanded.is_dir() {
        return expanded;
    }

    if expanded.is_file() {
        if let Some(parent) = expanded.parent() {
            if parent.is_dir() {
                return parent.to_path_buf();
            }
        }
    }

    eprintln!(
        "warning: '{}' is not a directory, starting in {}",
        expanded.display(),
        current.display()
    );
    if current.is_dir() {
        current
    } else {
        paths.home().to_path_buf()
    }
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

fn print_help() {
    println!(
        "\n Usage:\n  ferret [OPTIONS]\n\n Options:\n  -h / --help             Show this menu\n  -v / --version          Show installed version\n  -o / --open             Open the starting directory in the default file manager\n  -d / --directory PATH   Start in PATH\n  -t / --tty              Accepted for compatibility; the Rust port always uses tty input\n\n Controls:"
    );
    for line in app::HELP_LINES {
        if line.is_empty() {
            println!();
        } else {
            println!("  {line}");
        }
    }
    println!();
}
