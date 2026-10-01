#!/usr/bin/env python3
"""Serve the WebGL build (Builds/WebGL) on this machine and the local network, for testing on phones.

    python3 Tools/serve_webgl.py [--port 8765] [--dir Builds/WebGL]

Open the printed address on a phone that's on the same Wi-Fi. Gzipped Unity files are sent with
Content-Encoding: gzip so the browser unpacks them itself (the build also works without that, just slower).
Nothing is cached, so a fresh build shows up on reload.
"""
import argparse
import functools
import http.server
import os
import socket
import socketserver

TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".webmanifest": "application/manifest+json",
    ".svg": "image/svg+xml",
}


class Handler(http.server.SimpleHTTPRequestHandler):
    def guess_type(self, path):
        # Unity names compressed files *.wasm.unityweb, *.data.gz and so on; the type comes from the inner extension.
        base, ext = os.path.splitext(path)
        if ext in (".unityweb", ".gz", ".br"):
            ext = os.path.splitext(base)[1]
        return TYPES.get(ext) or super().guess_type(path)

    def end_headers(self):
        path = self.translate_path(self.path.split("?", 1)[0])
        if path.endswith(".gz") or (path.endswith(".unityweb") and _is_gzip(path)):
            self.send_header("Content-Encoding", "gzip")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, format, *args):
        if "404" in (format % args):
            super().log_message(format, *args)


def _is_gzip(path):
    try:
        with open(path, "rb") as file:
            return file.read(2) == b"\x1f\x8b"
    except OSError:
        return False


def _lan_address():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        try:
            probe.connect(("10.255.255.255", 1))
            return probe.getsockname()[0]
        except OSError:
            return None


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--dir", default=os.path.join(root, "Builds", "WebGL"))
    args = parser.parse_args()

    if not os.path.isfile(os.path.join(args.dir, "index.html")):
        raise SystemExit(f"No build in {args.dir}. Run Casino Mares > Web > Build first.")

    handler = functools.partial(Handler, directory=args.dir)
    socketserver.ThreadingTCPServer.allow_reuse_address = True
    try:
        server = socketserver.ThreadingTCPServer(("0.0.0.0", args.port), handler)
    except OSError as error:
        raise SystemExit(f"Can't use port {args.port} ({error.strerror}). Pick another with --port.")
    with server:
        print(f"Serving {args.dir}")
        print(f"  This Mac:  http://localhost:{args.port}/")
        lan = _lan_address()
        if lan:
            print(f"  Phone:     http://{lan}:{args.port}/   (same Wi-Fi)")
        print("Ctrl+C to stop.")
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
