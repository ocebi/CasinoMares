#!/usr/bin/env python3
"""Publish the WebGL build (Builds/WebGL) to ocebi.github.io/casino on GitHub Pages.

    python3 Tools/deploy_pages.py [--repo ~/Projects/ocebi.github.io] [--no-push]

GitHub Pages sends big files slowly on some networks (about 100 KB/s where other sites manage several MB/s), so the
page stays on GitHub Pages but loads the game files through jsDelivr's CDN, pinned to the commit that holds them.
That takes two commits in the Pages repo: the game files, then the page pointing at that commit. jsDelivr serves files
of up to 20 MB from GitHub, so a bigger file stops the deploy.

jsDelivr drops files from its cache when few people load them, and fetching a big one from GitHub again can fail (a 503
after half a minute or more). The page then loads that file from its own copy on GitHub Pages (mirrorUrl in the
template), so a deploy needs a page built from a template that has that fallback.
"""
import argparse
import os
import re
import subprocess
import sys
import urllib.request

SITE_FOLDER = "casino"
MAX_FILE_BYTES = 20 * 1024 * 1024
BUILD_URL_LINE = 'var buildUrl = "Build";'
MIRROR_URL_LINE = 'var mirrorUrl = "Build";'


def git(repo, *args):
    return subprocess.run(["git", "-C", repo, *args], check=True, capture_output=True, text=True).stdout.strip()


def commit_if_changed(repo, paths, message):
    git(repo, "add", "--", *paths)
    if subprocess.run(["git", "-C", repo, "diff", "--cached", "--quiet"]).returncode != 0:
        git(repo, "commit", "-q", "-m", message)
        print(f"Committed: {message}")


def github_slug(repo):
    url = git(repo, "remote", "get-url", "origin")
    match = re.search(r"github\.com[:/](.+?)(?:\.git)?$", url)
    if not match:
        sys.exit(f"Origin {url} isn't on GitHub.")
    return match.group(1)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo", default=os.path.expanduser("~/Projects/ocebi.github.io"))
    parser.add_argument("--build", default=os.path.join(root, "Builds", "WebGL"))
    parser.add_argument("--no-push", action="store_true", help="commit, but don't push or warm the CDN")
    args = parser.parse_args()

    build_files = os.path.join(args.build, "Build")
    if not os.path.isfile(os.path.join(args.build, "index.html")) or not os.path.isdir(build_files):
        sys.exit(f"No build in {args.build}. Run Casino Mares > Web > Build first.")
    too_big = [name for name in os.listdir(build_files) if os.path.getsize(os.path.join(build_files, name)) > MAX_FILE_BYTES]
    if too_big:
        sys.exit(f"Over jsDelivr's 20 MB limit: {', '.join(too_big)}")

    site = os.path.join(args.repo, SITE_FOLDER)
    subprocess.run(["rsync", "-a", "--delete", "--exclude", ".DS_Store", args.build + "/", site + "/"], check=True)

    # 1. The game files. The page is committed separately, after it points at this commit.
    commit_if_changed(args.repo, [f"{SITE_FOLDER}/Build", f"{SITE_FOLDER}/TemplateData"], "Update Casino Mares game files")
    files_commit = git(args.repo, "log", "-1", "--format=%H", "--", f"{SITE_FOLDER}/Build")

    # 2. The page, loading those files from jsDelivr.
    cdn = f"https://cdn.jsdelivr.net/gh/{github_slug(args.repo)}@{files_commit}/{SITE_FOLDER}/Build"
    page_path = os.path.join(site, "index.html")
    page = open(page_path, encoding="utf-8").read()
    if BUILD_URL_LINE not in page:
        sys.exit(f"index.html has no line {BUILD_URL_LINE!r} to point at the CDN.")
    if MIRROR_URL_LINE not in page:
        sys.exit(f"index.html has no line {MIRROR_URL_LINE!r}: it can't fall back to GitHub Pages when jsDelivr fails.")
    with open(page_path, "w", encoding="utf-8") as file:
        file.write(page.replace(BUILD_URL_LINE, f'var buildUrl = "{cdn}";'))
    commit_if_changed(args.repo, [SITE_FOLDER], "Update Casino Mares page")

    if args.no_push:
        print("Not pushed (--no-push).")
        return
    subprocess.run(["git", "-C", args.repo, "-c", "http.postBuffer=524288000", "push", "-q", "origin", "HEAD"], check=True)
    print("Pushed.")

    # The first request for each file makes jsDelivr fetch it from GitHub; do that now rather than for the first player.
    # It can fail for a big file. That's not fatal, since the page falls back to GitHub Pages, so try again a few times.
    for name in sorted(os.listdir(build_files)):
        for attempt in range(3):
            try:
                with urllib.request.urlopen(f"{cdn}/{name}", timeout=300) as response:
                    size = len(response.read())
                print(f"Cached on jsDelivr: {name} ({size / 1048576:.1f} MB)")
                break
            except OSError as error:
                print(f"jsDelivr didn't send {name} ({error}){', trying again' if attempt < 2 else ''}")
        else:
            print(f"  Players load {name} from GitHub Pages until jsDelivr has it.")


if __name__ == "__main__":
    main()
