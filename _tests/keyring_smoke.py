#!/usr/bin/env python3
"""Run the real credential adapter against an isolated, temporary Secret Service."""
import os
import pathlib
import subprocess
import sys
import tempfile
import time


def main():
    if len(sys.argv) < 3:
        raise SystemExit("Usage: keyring_smoke.py DOTNET_PATH CREDENTIAL_TEST_DLL")
    if "--inside" not in sys.argv:
        subprocess.run(["dbus-run-session", "--", sys.executable, __file__,
                        sys.argv[1], sys.argv[2], "--inside"], check=True, timeout=35)
        return

    with tempfile.TemporaryDirectory(prefix="ferret-keyring-") as temporary:
        env = os.environ.copy()
        for variable, folder in [("HOME", "home"), ("XDG_DATA_HOME", "data"),
                                 ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"),
                                 ("XDG_RUNTIME_DIR", "runtime")]:
            directory = pathlib.Path(temporary) / folder
            directory.mkdir(mode=0o700)
            env[variable] = str(directory)
        with open(pathlib.Path(temporary) / "daemon.log", "w+") as log:
            daemon = subprocess.Popen(["gnome-keyring-daemon", "--foreground", "--unlock", "--components=secrets"],
                                      stdin=subprocess.PIPE, stdout=log, stderr=log, env=env)
            try:
                daemon.stdin.write(b"temporary-test-keyring-password\n")
                daemon.stdin.close()
                time.sleep(0.5)
                if daemon.poll() is not None:
                    log.seek(0)
                    raise RuntimeError("Test keyring failed to start: " + log.read())
                subprocess.run([sys.argv[1], sys.argv[2], "--system-provider"],
                               env=env, check=True, timeout=20)
                print("PASS isolated real keyring store/lookup/delete")
            finally:
                daemon.terminate()
                try:
                    daemon.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    daemon.kill()
                    daemon.wait(timeout=3)


if __name__ == "__main__":
    main()
