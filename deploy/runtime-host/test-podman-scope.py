"""Verify argv preservation and the actual upload writer against an existing container.

Only creates a unique /tmp directory; never reads user files or calls a model.
Use --owner ymir as root to test that account; --wrapper tests the installed wrapper.
"""
import argparse
import os
from pathlib import Path
import pwd
import re
import subprocess
import textwrap

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("container")
parser.add_argument("--owner")
parser.add_argument("--wrapper")
args = parser.parse_args()
if args.owner and os.geteuid() != 0:
    parser.error("--owner requires root")


def execute(*arguments, content=None, environment=None):
    command = ([args.wrapper] if args.wrapper else [
        "/usr/bin/systemd-run", "--user", "--scope", "--quiet",
        "--expand-environment=no", "--", "/usr/bin/podman",
    ]) + list(arguments)
    if args.owner:
        account = pwd.getpwnam(args.owner)
        command = ["runuser", "-u", args.owner, "--", "env",
                   "HOME=" + account.pw_dir,
                   f"XDG_RUNTIME_DIR=/run/user/{account.pw_uid}",
                   f"DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/{account.pw_uid}/bus",
                   *command]
    env = os.environ.copy()
    env.update(environment or {})
    result = subprocess.run(command, input=content, capture_output=True, cwd="/", env=env, timeout=30)
    if result.returncode:
        raise RuntimeError(f"Container probe failed ({result.returncode}): " + result.stderr.decode(errors="replace"))
    return result.stdout


# Exact shell script argument and filename must arrive without systemd expansion.
literal = '${1##*/}|${1%/*}|$1|$HOME|$$'
out = execute("exec", args.container, "bash", "-c", 'printf "%s" "$1"', "ymir-literal", literal)
assert out.decode() == literal
print("PASS literal argv: shell parameters and environment syntax preserved")
execute("exec", "-i", "--env", "YMIR_SCOPE_PROBE", args.container,
        "bash", "-c", 'read -r line; test "$line" = scope-stdin && test "$YMIR_SCOPE_PROBE" = probe',
        content=b"scope-stdin\n", environment={"YMIR_SCOPE_PROBE": "probe"})
try:
    execute("exec", args.container, "bash", "-c", "exit 17")
except RuntimeError as error:
    assert "(17)" in str(error)
else:
    raise AssertionError("Wrapper did not preserve the container exit code")
print("PASS stdin, inherited environment and nonzero exit code")

source = Path(__file__).resolve().parents[2] / "src/Modules/VibeMaker/Ymir.VibeMaker.Infrastructure/Files/RuntimeWorkspaceFileWriter.cs"
match = re.search(r'internal const string WriteScript = """\n(.*?)\n\s*""";', source.read_text(), re.S)
assert match, "Could not locate the upload writer script"
writer = textwrap.dedent(match.group(1))
directory = execute("exec", args.container, "mktemp", "-d", "/tmp/ymir-upload-scope-XXXXXXXX").decode().strip()
assert re.fullmatch(r"/tmp/ymir-upload-scope-[A-Za-z0-9]{8}", directory)
try:
    filename = "uploads/測試 報告 01.txt"
    content = "upload scope regression\n繁體中文\n".encode() + b"\x00\xff"
    execute("exec", "-i", "--workdir", directory, args.container,
            "bash", "-c", writer, "ymir-write", filename, str(len(content)), content=content)
    uploaded = execute("exec", "--workdir", directory, args.container, "cat", "--", filename)
    assert uploaded == content
    print("PASS real upload writer: Chinese filename, spaces, binary stdin and exact bytes")
    # Retain the writer's symlink escape protection.
    execute("exec", "--workdir", directory, args.container, "ln", "-s", "/tmp", "escape")
    try:
        execute("exec", "-i", "--workdir", directory, args.container,
                "bash", "-c", writer, "ymir-write", "escape/scope-escape-must-not-exist", "1", content=b"x")
    except RuntimeError as error:
        assert "(4)" in str(error), str(error)
    else:
        raise AssertionError("Writer accepted a symlink outside its working directory")
    print("PASS upload writer still rejects symlink escapes")
finally:
    # Remove only the unique directory created by this probe, never workspace data.
    execute("exec", args.container, "rm", "-rf", "--", directory)
