#!/usr/bin/env python3
"""Validate knowledge Markdown and build the static portal on the pinned Forma, Folio and Limen packages."""

from __future__ import annotations

import argparse
import datetime as dt
import html
import json
import re
import shutil
import string
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "content"
DIST = ROOT / "dist"
TYPES = {"project", "how-to", "guide", "faq", "reference"}
STATUSES = {"draft", "published", "archived"}
REQUIRED = {
    "id", "title", "summary", "type", "status", "owner", "projects",
    "audience", "tags", "updated", "review_by", "related",
}
SECTION_LABELS = {
    "project": "Projects",
    "how-to": "How-tos",
    "guide": "Guided paths",
    "faq": "FAQs",
    "reference": "Reference",
}


@dataclass
class Page:
    source: Path
    meta: dict
    body: str

    @property
    def url(self) -> str:
        return self.source.relative_to(CONTENT).with_suffix(".html").as_posix()


def scalar(value: str):
    value = value.strip()
    if value.startswith("[") and value.endswith("]"):
        inside = value[1:-1].strip()
        return [] if not inside else [item.strip().strip("'\"") for item in inside.split(",")]
    return value.strip("'\"")


def parse_page(path: Path) -> Page:
    text = path.read_text(encoding="utf-8")
    if not text.startswith("---\n"):
        raise ValueError("missing YAML front matter")
    try:
        raw_meta, body = text[4:].split("\n---\n", 1)
    except ValueError as exc:
        raise ValueError("front matter is not closed with ---") from exc
    meta = {}
    for number, line in enumerate(raw_meta.splitlines(), 2):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if ":" not in line:
            raise ValueError(f"invalid metadata on line {number}")
        key, value = line.split(":", 1)
        meta[key.strip()] = scalar(value)
    return Page(path, meta, body.strip())


def load_pages() -> tuple[list[Page], list[str]]:
    pages, errors = [], []
    for path in sorted(CONTENT.rglob("*.md")):
        try:
            pages.append(parse_page(path))
        except ValueError as exc:
            errors.append(f"{path.relative_to(ROOT)}: {exc}")
    return pages, errors


def validate(pages: list[Page], parse_errors: list[str]) -> tuple[list[str], list[str]]:
    errors = list(parse_errors)
    warnings = []
    ids = {}
    today = dt.date.today()
    for page in pages:
        name = page.source.relative_to(ROOT)
        missing = REQUIRED - page.meta.keys()
        if missing:
            errors.append(f"{name}: missing fields: {', '.join(sorted(missing))}")
            continue
        page_id = page.meta["id"]
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", page_id):
            errors.append(f"{name}: id must be lowercase kebab-case")
        if page_id in ids:
            errors.append(f"{name}: duplicate id '{page_id}' (also {ids[page_id]})")
        ids[page_id] = name
        if page.meta["type"] not in TYPES:
            errors.append(f"{name}: unknown type '{page.meta['type']}'")
        if page.meta["status"] not in STATUSES:
            errors.append(f"{name}: unknown status '{page.meta['status']}'")
        expected_dir = {"how-to": "how-tos", "guide": "guides"}.get(
            page.meta["type"], f"{page.meta['type']}s"
        )
        if page.meta["type"] == "reference":
            expected_dir = "reference"
        if page.source.parent.name != expected_dir:
            errors.append(f"{name}: type belongs in content/{expected_dir}/")
        for field in ("projects", "audience", "tags", "related"):
            if not isinstance(page.meta[field], list):
                errors.append(f"{name}: {field} must be an inline list, for example [one, two]")
        for field in ("updated", "review_by"):
            try:
                parsed = dt.date.fromisoformat(page.meta[field])
                if field == "review_by" and parsed < today and page.meta["status"] == "published":
                    warnings.append(f"{name}: review overdue since {parsed.isoformat()}")
            except (TypeError, ValueError):
                errors.append(f"{name}: {field} must use YYYY-MM-DD")
        if not page.body.startswith("# "):
            errors.append(f"{name}: body must start with one H1 heading")

    known = set(ids)
    for page in pages:
        for related in page.meta.get("related", []):
            if related not in known:
                errors.append(f"{page.source.relative_to(ROOT)}: related id '{related}' does not exist")
    return errors, warnings


def inline_md(text: str) -> str:
    escaped = html.escape(text, quote=False)
    escaped = re.sub(r"`([^`]+)`", r"<code>\1</code>", escaped)
    escaped = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', escaped)
    escaped = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", escaped)
    return escaped


def markdown(text: str) -> str:
    lines = text.splitlines()
    out, paragraph, list_type, in_code, code_lines = [], [], None, False, []

    def flush_paragraph():
        if paragraph:
            out.append("<p>" + inline_md(" ".join(paragraph)) + "</p>")
            paragraph.clear()

    def close_list():
        nonlocal list_type
        if list_type:
            out.append(f"</{list_type}>")
            list_type = None

    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith("```"):
            flush_paragraph(); close_list()
            if in_code:
                out.append("<ef-print-code><pre><code>" + html.escape("\n".join(code_lines)) + "</code></pre></ef-print-code>")
                code_lines, in_code = [], False
            else:
                in_code = True
            i += 1
            continue
        if in_code:
            code_lines.append(line); i += 1; continue
        if line.startswith("|") and i + 1 < len(lines) and re.match(r"^\|?[\s:|-]+\|", lines[i + 1]):
            flush_paragraph(); close_list()
            headers = [c.strip() for c in line.strip("|").split("|")]
            i += 2
            rows = []
            while i < len(lines) and lines[i].startswith("|"):
                rows.append([c.strip() for c in lines[i].strip("|").split("|")]); i += 1
            out.append("<ef-print-table><table><thead><tr>" + "".join(f"<th>{inline_md(c)}</th>" for c in headers) +
                       "</tr></thead><tbody>" + "".join("<tr>" + "".join(f"<td>{inline_md(c)}</td>" for c in row) +
                       "</tr>" for row in rows) + "</tbody></table></ef-print-table>")
            continue
        heading = re.match(r"^(#{1,6})\s+(.+)$", line)
        item = re.match(r"^(\d+\.|[-*])\s+(.+)$", line)
        if heading:
            flush_paragraph(); close_list()
            level = len(heading.group(1))
            title = heading.group(2)
            anchor = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")
            out.append(f'<h{level} id="{anchor}">{inline_md(title)}</h{level}>')
        elif item:
            flush_paragraph()
            wanted = "ol" if item.group(1)[0].isdigit() else "ul"
            if list_type != wanted:
                close_list(); out.append(f"<{wanted}>"); list_type = wanted
            content = item.group(2)
            content = re.sub(r"^\[([ xX])\]\s*", lambda m: "☑ " if m.group(1).lower() == "x" else "☐ ", content)
            out.append("<li>" + inline_md(content) + "</li>")
        elif not line.strip():
            flush_paragraph(); close_list()
        else:
            paragraph.append(line.strip())
        i += 1
    flush_paragraph(); close_list()
    return "\n".join(out)


# Pinned Echelon foundations the portal is built from (see package.json).
FORMA_CSS = Path("@echelon-foundry/design-system/dist/all.css")
FOLIO_PRINT_CSS = Path("@echelon-foundry/print-components/src/styles/print.css")
LIMEN_DIST = Path("@echelon-foundry/limen/dist")
LIMEN_PACKAGE = "@echelon-foundry/limen"


def search_form(prefix: str, interactive: bool) -> str:
    """Forma search pattern. On the home page Limen binds it to the search engine;
    elsewhere it submits natively to the home page's ?q= query."""
    bindings = ' data-event="search" data-on="input" data-bind-value="query"' if interactive else ""
    extras = (
        '<button type="button" class="ef-search__clear" data-event="clear" '
        'data-bind-disabled="clearDisabled">Clear</button>'
        if interactive else ""
    )
    status = '<p class="ef-search__status" role="status" data-text="status"></p>' if interactive else ""
    return (
        '<ef-search class="ef-component-tag">'
        f'<form class="ef-search" role="search" action="{prefix}index.html">'
        '<label class="ef-search__label" for="site-search">Search the knowledge hub</label>'
        '<div class="ef-search__control">'
        '<input id="site-search" name="q" type="search" autocomplete="off" '
        f'placeholder="Search titles, topics, projects, and audiences…"{bindings}>'
        f"{extras}</div>{status}</form></ef-search>"
    )


def search_scripts(prefix: str, cards: list[dict]) -> str:
    """The card index for the Limen engine, the import map for the Limen package,
    and the kernel wiring. JSON is escaped so it cannot close its script element."""
    index = json.dumps(cards, ensure_ascii=False).replace("</", "<\\/")
    # Import-map addresses must be ./, ../ or / relative; a bare path is refused.
    imports = json.dumps({"imports": {LIMEN_PACKAGE: f"{prefix or './'}assets/{LIMEN_PACKAGE}/index.js"}})
    return (
        f'<script type="application/json" id="search-index">{index}</script>'
        f'<script type="importmap">{imports}</script>'
        f'<script type="module" src="{prefix}assets/site/kernel/search.js"></script>'
    )


PAGE_TEMPLATE = ROOT / "site/templates/page.html"


def shell(title: str, body: str, depth: int = 0, cards: list[dict] | None = None) -> str:
    prefix = "../" * depth
    nav = "".join(
        f'<a href="{prefix}index.html#{kind}">{label}</a>'
        for kind, label in SECTION_LABELS.items()
    )
    interactive = cards is not None
    return string.Template(PAGE_TEMPLATE.read_text(encoding="utf-8")).substitute(
        title=html.escape(title),
        prefix=prefix,
        search=search_form(prefix, interactive),
        nav=nav,
        body=body,
        scripts=search_scripts(prefix, cards) if interactive else "",
    )


def node_modules() -> Path:
    modules = ROOT / "node_modules"
    missing = [str(x) for x in (FORMA_CSS, FOLIO_PRINT_CSS, LIMEN_DIST) if not (modules / x).exists()]
    if missing:
        raise SystemExit("Missing pinned site dependencies (run `npm ci`): " + ", ".join(missing))
    return modules


def copy_assets() -> None:
    modules = node_modules()
    assets = DIST / "assets"
    vendored = assets / "@echelon-foundry"
    (vendored / "design-system").mkdir(parents=True)
    (vendored / "print-components").mkdir(parents=True)
    shutil.copy(modules / FORMA_CSS, vendored / "design-system/all.css")
    shutil.copy(modules / FOLIO_PRINT_CSS, vendored / "print-components/print.css")
    shutil.copytree(modules / LIMEN_DIST, vendored / "limen",
                    ignore=shutil.ignore_patterns("*.map", "*.d.ts"))
    shutil.copy(ROOT / "site/style.css", assets / "style.css")
    for part in ("engine", "kernel"):
        shutil.copytree(ROOT / "site" / part, assets / "site" / part)


def build(pages: list[Page]) -> None:
    if DIST.exists():
        shutil.rmtree(DIST)
    DIST.mkdir(parents=True)
    copy_assets()
    published = [p for p in pages if p.meta["status"] == "published"]
    by_id = {p.meta["id"]: p for p in published}
    sections, index = [], []
    for kind, label in SECTION_LABELS.items():
        cards = []
        for page in [p for p in published if p.meta["type"] == kind]:
            terms = " ".join([
                page.meta["title"], page.meta["summary"], *page.meta["projects"],
                *page.meta["audience"], *page.meta["tags"], page.body,
            ])
            pills = "".join(f'<span class="pill">{html.escape(x)}</span>' for x in page.meta["projects"])
            index.append({"key": page.meta["id"], "text": terms})
            cards.append(
                f'<a class="card ef-surface" data-bind-hidden="card:{page.meta["id"]}" href="{page.url}">'
                f'<h3>{html.escape(page.meta["title"])}</h3><p>{html.escape(page.meta["summary"])}</p>'
                f'<div class="pills">{pills}</div></a>'
            )
        sections.append(f'<section id="{kind}"><span class="eyebrow">Browse</span><h2>{label}</h2>'
                        f'<div class="grid">{"".join(cards)}</div></section>')
    home = ('<span class="eyebrow">One place to start</span><h1>Work across projects with confidence.</h1>'
            '<p class="lede">Find the owner, choose the right path, and follow maintained instructions '
            'without guessing which repository has the answer.</p>' + "".join(sections) +
            '<ef-empty-state class="ef-component-tag">'
            '<section class="ef-empty-state" aria-labelledby="empty-title" data-bind-hidden="emptyHidden" hidden>'
            '<div class="ef-empty-state__symbol" aria-hidden="true">□</div>'
            '<h3 id="empty-title">No pages match every search term</h3>'
            '<p>Remove a term or clear the search to see every page.</p></section></ef-empty-state>')
    (DIST / "index.html").write_text(shell("Home", home, cards=index), encoding="utf-8")

    index = []
    for page in published:
        target = DIST / page.url
        target.parent.mkdir(parents=True, exist_ok=True)
        related = [by_id[x] for x in page.meta["related"] if x in by_id]
        related_html = ""
        if related:
            related_html = "<h2>Related</h2><ul>" + "".join(
                f'<li><a href="../{p.url}">{html.escape(p.meta["title"])}</a></li>' for p in related
            ) + "</ul>"
        meta = (f'<div class="meta"><strong>Owner:</strong> {html.escape(page.meta["owner"])} · '
                f'<strong>Reviewed:</strong> {page.meta["updated"]} · '
                f'<strong>Review by:</strong> {page.meta["review_by"]}</div>')
        content = f'<ef-print-section><article class="content"><span class="eyebrow">{SECTION_LABELS[page.meta["type"]]}</span>' \
                  f'{meta}{markdown(page.body)}{related_html}</article></ef-print-section>'
        target.write_text(shell(page.meta["title"], content, 1), encoding="utf-8")
        index.append({
            "id": page.meta["id"], "title": page.meta["title"], "summary": page.meta["summary"],
            "type": page.meta["type"], "url": page.url, "owner": page.meta["owner"],
            "projects": page.meta["projects"], "audience": page.meta["audience"],
            "tags": page.meta["tags"], "updated": page.meta["updated"],
            "review_by": page.meta["review_by"], "related": page.meta["related"],
            "text": re.sub(r"\s+", " ", page.body),
        })
    (DIST / "knowledge-index.json").write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("validate", "build"))
    args = parser.parse_args()
    pages, parse_errors = load_pages()
    errors, warnings = validate(pages, parse_errors)
    for warning in warnings:
        print(f"WARNING: {warning}")
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        print(f"\nValidation failed: {len(errors)} error(s), {len(warnings)} warning(s).")
        return 1
    print(f"Validated {len(pages)} page(s): {len(warnings)} warning(s).")
    if args.command == "build":
        build(pages)
        print(f"Built {len([p for p in pages if p.meta['status'] == 'published'])} published page(s) in {DIST}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
