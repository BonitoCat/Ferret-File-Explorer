#!/usr/bin/env python3
"""Linux PTY smoke checks: python3 terminal_smoke.py DOTNET_PATH CLI_DLL."""
import errno
import fcntl
import os
import pathlib
import pty
import re
import select
import signal
import struct
import subprocess
import sys
import tempfile
import termios
import time


ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b[()][A-Z0-9]|\x1b[=>]")


def plain(raw):
    # Renderer emits styles per cell, so raw filename searches are incorrect.
    return ANSI.sub("", raw.decode("utf-8", errors="replace"))


class Session:
    def __init__(self, command, env, cwd):
        self.master, self.slave = pty.openpty()
        self.original = termios.tcgetattr(self.slave)
        self.resize(40, 160)

        def controlling_terminal():
            os.setsid()
            fcntl.ioctl(0, termios.TIOCSCTTY, 0)

        self.process = subprocess.Popen(command, stdin=self.slave, stdout=self.slave,
                                        stderr=self.slave, cwd=cwd, env=env,
                                        preexec_fn=controlling_terminal)
        self.output = bytearray()

    def resize(self, rows, columns):
        fcntl.ioctl(self.slave, termios.TIOCSWINSZ, struct.pack("HHHH", rows, columns, 0, 0))
        if hasattr(self, "process") and self.process.poll() is None:
            os.killpg(self.process.pid, signal.SIGWINCH)

    def read(self, duration=0.3):
        end = time.monotonic() + duration
        data = bytearray()
        while time.monotonic() < end:
            ready, _, _ = select.select([self.master], [], [], max(0, end - time.monotonic()))
            if not ready:
                break
            try:
                block = os.read(self.master, 65536)
            except OSError as error:
                if error.errno == errno.EIO:
                    break
                raise
            if not block:
                break
            data.extend(block)
        self.output.extend(data)
        return bytes(data)

    def expect(self, needle, timeout=8):
        data = bytearray()
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            data.extend(self.read(0.15))
            if needle in plain(data):
                return plain(data)
            if self.process.poll() is not None:
                break
        raise AssertionError(f"Expected {needle!r}; recent terminal output:\n{plain(data)[-5000:]}")

    def send(self, value):
        if self.process.poll() is not None:
            raise AssertionError(f"CLI exited unexpectedly: {self.process.returncode}")
        os.write(self.master, value if isinstance(value, bytes) else value.encode("utf-8"))

    def dialog(self, suffix=None):
        self.read(0.25)
        self.send(b"\x0c")  # Ctrl+L
        self.expect("Enter confirm")
        if suffix is None:
            self.send(b"\x1b")
        else:
            self.send(suffix)
            self.read(0.2)
            self.send(b"\r")
        self.read(0.4)

    def close(self):
        if self.process.poll() is None:
            os.killpg(self.process.pid, signal.SIGTERM)
            try:
                self.process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                os.killpg(self.process.pid, signal.SIGKILL)
                self.process.wait(timeout=3)
        os.close(self.master)
        os.close(self.slave)


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    command = [str(pathlib.Path(sys.argv[1]).resolve()), str(pathlib.Path(sys.argv[2]).resolve())]
    with tempfile.TemporaryDirectory(prefix="ferret-pty-") as temporary:
        root = pathlib.Path(temporary)
        env = os.environ.copy()
        for variable, folder in [("HOME", "home"), ("XDG_DATA_HOME", "data"),
                                 ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"),
                                 ("XDG_RUNTIME_DIR", "runtime")]:
            directory = root / folder
            directory.mkdir(mode=0o700)
            env[variable] = str(directory)
        env.update(TERM="xterm-256color", LANG="C.UTF-8", LC_ALL="C.UTF-8")
        # Avoid probing the user's real desktop bus/keyring in this isolated session.
        env.pop("DBUS_SESSION_BUS_ADDRESS", None)
        fixture = root / "fixture"
        fixture.mkdir()
        for folder in ["alpha-dir", "bravo-dir", "unicode ü space"]:
            (fixture / folder).mkdir()
        (fixture / "bravo-dir" / "arrow-navigation-marker.txt").write_text("fixture")
        (fixture / "unicode ü space" / "unicode-path-marker.txt").write_text("fixture")
        (fixture / "visible-smoke.txt").write_text("fixture")

        for argument, expected in [("--help", "Controls:"), ("--version", "v")]:
            result = subprocess.run(command + [argument], cwd=fixture, env=env,
                                    capture_output=True, text=True, timeout=10)
            assert result.returncode == 0 and expected in result.stdout, (argument, result)
        print("PASS redirected help/version", flush=True)

        session = Session(command + ["--directory", str(fixture), "--tty"], env, fixture)
        try:
            session.expect("visible-smoke.txt")
            print("PASS PTY startup and filenames", flush=True)

            # Initial selection is the first child, alpha-dir. Down selects bravo-dir.
            session.send(b"\x1b[B")
            session.read(0.3)
            session.send(b"\r")
            session.expect("arrow-navigation-marker.txt")
            print("PASS arrow selection and Enter navigation", flush=True)

            session.read(0.2)
            session.send(b"\x1bOR")  # xterm F3
            split_output = plain(session.read(0.6))
            assert split_output.count("arrow-navigation-marker.txt") >= 2, "F3 did not draw both panes"
            session.send(b"\x1bOR")
            session.read(0.3)
            session.send(b"\x1b[20~")  # F9
            assert session.read(0.5), "F9 produced no rendering update"
            settings = root / "data" / "fe" / "cli_settings"
            assert "sidebar=false" in settings.read_text(), "F9 did not hide/persist sidebar"
            session.send(b"\x1b[20~")
            session.expect("Sidebar (F9)")
            assert "sidebar=true" in settings.read_text(), "F9 did not restore/persist sidebar"
            print("PASS F3 split panes and F9 sidebar toggles", flush=True)

            for _ in range(3):
                session.dialog()  # Escape repeatedly returns ownership to listener.
            session.dialog("/../unicode ü space")
            # dialog() consumes rendering, inspect complete transcript for marker.
            assert "unicode-path-marker.txt" in plain(session.output), "Unicode/space path navigation failed"
            session.dialog("/..")
            print("PASS repeated Ctrl+L Enter/Escape handoffs and Unicode path", flush=True)

            session.send(b"\x0e")  # Ctrl+N
            session.expect("New file name")
            session.send("created ü smoke.txt")
            session.read(0.2)
            session.send(b"\r")
            deadline = time.monotonic() + 5
            created = fixture / "created ü smoke.txt"
            while not created.exists() and time.monotonic() < deadline:
                session.read(0.15)
            assert created.is_file(), "Ctrl+N did not create the fixture file"
            print("PASS Ctrl+N file creation with Unicode and spaces", flush=True)

            for rows, columns in [(4, 12), (2, 2), (40, 160)]:
                session.resize(rows, columns)
                session.read(0.5)
                assert session.process.poll() is None, f"CLI exited after resize to {rows}x{columns}"
            print("PASS small-window resize and restore", flush=True)

            session.send(b"\x1b[21~")  # F10
            deadline = time.monotonic() + 8
            while session.process.poll() is None and time.monotonic() < deadline:
                session.read(0.15)
            assert session.process.poll() == 0, "F10 failed to exit successfully"
            restored = termios.tcgetattr(session.slave)
            assert restored[3] & termios.ECHO == session.original[3] & termios.ECHO, "Terminal echo was not restored"
            assert restored[3] & termios.ICANON == session.original[3] & termios.ICANON, "Canonical input was not restored"
            print("PASS F10 exit and terminal echo/canonical restoration", flush=True)
        except BaseException:
            print("Recent output:\n" + plain(session.output)[-7000:], file=sys.stderr)
            raise
        finally:
            session.close()


if __name__ == "__main__":
    main()
