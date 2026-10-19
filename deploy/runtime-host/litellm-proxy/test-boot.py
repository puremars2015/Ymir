#!/usr/bin/env python3
"""不操作正式服務；用暫時 Unix socket 驗證開機等待與逾時重試設定。"""
import configparser
from pathlib import Path
import shlex
import socket
import subprocess
import tempfile
import threading
import unittest

ROOT = Path(__file__).resolve().parent


def config(name):
    result = configparser.ConfigParser(interpolation=None, strict=False)
    result.read(ROOT / name)
    return result


class BootReadinessTests(unittest.TestCase):
    def setUp(self):
        self.patch = config("boot-readiness.conf")
        temp_root = "/tmp/opencode" if Path("/tmp/opencode").is_dir() else None
        self.temp = tempfile.TemporaryDirectory(prefix="ymir-boot-", dir=temp_root)
        self.addCleanup(self.temp.cleanup)
        self.path = str(Path(self.temp.name) / "bridge" / "litellm.sock")

    def command(self):
        args = shlex.split(self.patch["Service"]["ExecStartPre"])
        self.assertEqual(args[:4], ["/usr/bin/timeout", "60", "/bin/sh", "-c"])
        args[1] = "0.3"
        args[4] = args[4].replace("/run/ymir-litellm/litellm.sock", self.path).replace("sleep 1", "sleep 0.01")
        return args

    def bind(self):
        Path(self.path).parent.mkdir(exist_ok=True)
        connection = socket.socket(socket.AF_UNIX)
        connection.bind(self.path)
        self.addCleanup(connection.close)

    def run_guard(self):
        return subprocess.run(self.command(), timeout=3, check=False).returncode

    def test_template_and_existing_host_patch_match(self):
        template = config("ymir-litellm-proxy.container")
        for section in self.patch.sections():
            for key, value in self.patch[section].items():
                self.assertEqual(template[section][key], value)

    def test_missing_directory_times_out_instead_of_starting_container(self):
        self.assertEqual(self.run_guard(), 124)

    def test_regular_file_is_not_ready(self):
        Path(self.path).parent.mkdir()
        Path(self.path).touch()
        self.assertEqual(self.run_guard(), 124)

    def test_socket_is_ready(self):
        self.bind()
        self.assertEqual(self.run_guard(), 0)

    def test_late_bridge_recovers_without_manual_proxy_restart(self):
        timer = threading.Timer(0.06, self.bind)
        timer.start()
        try:
            self.assertEqual(self.run_guard(), 0)
        finally:
            timer.join()

    def test_retry_after_a_previous_timeout(self):
        self.assertEqual(self.run_guard(), 124)
        self.bind()
        self.assertEqual(self.run_guard(), 0)

    def test_restart_policy_is_backed_off_and_not_rate_limited(self):
        self.assertEqual(self.patch["Unit"]["StartLimitIntervalSec"], "0")
        self.assertEqual(self.patch["Service"]["RestartSec"], "10")
        self.assertEqual(self.patch["Service"]["Restart"], "on-failure")
        self.assertGreater(int(self.patch["Service"]["TimeoutStartSec"]), 60)

    def test_installer_shell_syntax(self):
        subprocess.run(["bash", "-n", str(ROOT / "fix-boot.sh")], check=True)


if __name__ == "__main__":
    unittest.main()
