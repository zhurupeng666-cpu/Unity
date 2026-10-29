"""Real HTTP / process-restart check. Build the Go binary first; no third-party Python packages."""
import argparse
import json
import pathlib
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent


def run(binary):
    with tempfile.TemporaryDirectory(prefix="smoke-", dir=ROOT / "work") as folder:
        data = pathlib.Path(folder) / "issues.json"
        base = "http://127.0.0.1:18080"

        def api(path, method="GET", body=None):
            payload = json.dumps(body).encode() if body is not None else None
            req = urllib.request.Request(base + path, data=payload, method=method,
                                         headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=5) as response:
                return json.load(response)

        def start():
            process = subprocess.Popen([str(binary), "-addr", "127.0.0.1:18080", "-data", str(data)],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError("Go process exited during startup")
                try:
                    assert api("/health")["status"] == "ok"
                    return process
                except (OSError, urllib.error.URLError):
                    time.sleep(.1)
            process.terminate()
            process.wait(timeout=5)
            raise RuntimeError("health check timed out")

        process = start()
        try:
            issue = api("/api/issues", "POST", {"title": "Restart smoke check", "description": "Real HTTP request",
                        "priority": "high", "position": {"x": .42, "y": .03, "z": 1.26}})
            assert issue["status"] == "open"
            api(f'/api/issues/{issue["id"]}/status', "PATCH", {"status": "resolved"})
            process.terminate()
            process.wait(timeout=5)
            process = start()
            saved = api("/api/issues")
            assert len(saved) == 1 and saved[0]["id"] == issue["id"] and saved[0]["status"] == "resolved"
            assert saved[0]["position"] == issue["position"]
            print("PASS: HTTP create -> status update -> process restart -> same persisted ID/status/position")
        finally:
            process.terminate()
            process.wait(timeout=5)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("binary", type=pathlib.Path)
    args = parser.parse_args()
    (ROOT / "work").mkdir(exist_ok=True)
    run(args.binary.resolve())
