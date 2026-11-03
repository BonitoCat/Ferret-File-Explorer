#!/usr/bin/env python3
"""Isolated Linux GUI smoke: python3 desktop_smoke.py DOTNET GUI_DLL (or APPHOST)."""
import os
import pathlib
import signal
import secrets
import socket
import subprocess
import sys
import tempfile
import time


def wait_for(predicate, description, timeout=15):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = predicate()
        if result:
            return result
        time.sleep(0.1)
    raise AssertionError("Timed out waiting for " + description)


def stop(process):
    if process is not None and process.poll() is None:
        os.killpg(process.pid, signal.SIGTERM)
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=3)


def main():
    if len(sys.argv) not in (2, 3):
        raise SystemExit(__doc__)
    command = [str(pathlib.Path(arg).resolve()) for arg in sys.argv[1:]]
    with tempfile.TemporaryDirectory(prefix="ferret-desktop-") as temporary:
        root = pathlib.Path(temporary)
        env = os.environ.copy()
        for variable, folder in [("HOME", "home"), ("XDG_CONFIG_HOME", "config"),
                                 ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache"),
                                 ("XDG_RUNTIME_DIR", "runtime")]:
            directory = root / folder
            directory.mkdir(mode=0o700)
            env[variable] = str(directory)
        env.pop("DBUS_SESSION_BUS_ADDRESS", None)
        env.pop("WAYLAND_DISPLAY", None)
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
        env["AVALONIA_SCREEN_SCALE_FACTORS"] = "1"
        fixture = root / "fixture"
        fixture.mkdir()
        nested = fixture / "nested"
        nested.mkdir()
        (fixture / "existing.txt").write_text("fixture\n")
        xvfb = gui = None
        with (root / "gui.log").open("w+") as log:
            try:
                # Authenticated TCP avoids WSLg's read-only Unix socket directory.
                for display in range(100 + secrets.randbelow(1000), 3000):
                    with socket.socket() as probe:
                        try:
                            probe.bind(("127.0.0.1", 6000 + display))
                            break
                        except OSError:
                            continue
                else:
                    raise AssertionError("No free X display port")
                authority = root / "Xauthority"
                authority.touch(mode=0o600)
                subprocess.run(["xauth", "-f", str(authority), "add", "localhost:" + str(display),
                                "MIT-MAGIC-COOKIE-1", secrets.token_hex(16)], check=True, capture_output=True)
                env["XAUTHORITY"] = str(authority)
                env["DISPLAY"] = "localhost:" + str(display)
                xvfb = subprocess.Popen(["Xvfb", ":" + str(display), "-screen", "0", "1280x800x24",
                                         "-nolisten", "unix", "-listen", "tcp", "-auth", str(authority)],
                                        stdout=log, stderr=log, start_new_session=True)

                def xdo(*args, required=True):
                    result = subprocess.run(["xdotool", *args], env=env, text=True,
                                            capture_output=True, timeout=5)
                    if required and result.returncode:
                        raise AssertionError("xdotool failed: " + result.stderr)
                    return result.stdout.strip()

                wait_for(lambda: xdo("getdisplaygeometry", required=False), "isolated X display", timeout=5)

                gui = subprocess.Popen([*command, "--path", str(fixture)], env=env,
                                       cwd=fixture, stdout=log, stderr=log, start_new_session=True)

                def window():
                    if gui.poll() is not None:
                        raise AssertionError("GUI exited early: " + str(gui.returncode))
                    return xdo("search", "--onlyvisible", "--pid", str(gui.pid), required=False).splitlines()

                windows = wait_for(window, "visible GUI window")
                main_window = windows[-1]
                xdo("windowfocus", "--sync", main_window)
                time.sleep(0.5)
                xdo("key", "--clearmodifiers", "ctrl+l")
                xdo("type", "--clearmodifiers", "--delay", "5", str(nested))
                xdo("key", "Return")
                time.sleep(0.7)
                # Exercise Ferret's built-in file action through its path bar.
                # Unlike '> command', 'touch name' calls ViewModel.CreateFile.
                xdo("key", "--clearmodifiers", "ctrl+l")
                xdo("type", "--clearmodifiers", "--delay", "5", "touch smoke-created.txt")
                xdo("key", "Return")
                created = nested / "smoke-created.txt"
                wait_for(created.is_file, "file created in navigated fixture directory")
                assert created.read_bytes() == b"", "New file must be empty"
                assert (fixture / "existing.txt").read_text() == "fixture\n", "Existing file changed"
                result = subprocess.run([*command, "--quit"], env=env, cwd=fixture,
                                        stdout=log, stderr=log, timeout=10)
                assert result.returncode == 0, "Quit request failed"
                assert gui.wait(timeout=10) == 0, "GUI did not exit cleanly"
                print("GUI smoke passed: visible window, path navigation, new empty file, resident quit.")
            except Exception:
                log.flush()
                log.seek(0)
                print(log.read()[-12000:], file=sys.stderr)
                raise
            finally:
                stop(gui)
                stop(xvfb)


if __name__ == "__main__":
    main()
