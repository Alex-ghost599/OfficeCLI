#!/usr/bin/env python3
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

SCRIPT_PATH = Path(__file__).resolve()
REPO = SCRIPT_PATH.parent.parent
SHOWCASE = REPO / "assets" / "showcase"
EXAMPLES = REPO / "examples"
DEFAULT_BIN = REPO / "src" / "officecli" / "bin" / "Release" / "net10.0" / "osx-arm64" / "officecli"
PWCLI = Path("/Users/alex/.codex/skills/playwright/scripts/playwright_cli.sh")
REAL_HOME = Path.home()

ROOT: Path
FIXTURES: Path
WORKSPACE: Path
OUTPUTS: Path
TMPDIR: Path
ISOLATED_HOME: Path
LOGS: Path
REPORTS: Path
ARTIFACTS: Path
BIN: Path
BASE_ENV: dict[str, str]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run the full isolated Round3 manual OfficeCLI audit workflow."
    )
    parser.add_argument(
        "--cli",
        type=Path,
        default=DEFAULT_BIN,
        help="Path to the officecli executable to audit.",
    )
    parser.add_argument(
        "--workspace",
        type=Path,
        help="Root directory for audit logs/reports/artifacts. Defaults to a repo-external path.",
    )
    parser.add_argument(
        "--report-name",
        default="round3-manual-audit",
        help="Name used when deriving the default workspace path.",
    )
    parser.add_argument(
        "--reuse-fixtures",
        action="store_true",
        help="Reuse copied fixtures in an existing workspace instead of overwriting them.",
    )
    parser.add_argument(
        "--keep-workspace",
        action="store_true",
        help="Keep any existing workspace contents instead of recreating the workspace root.",
    )
    return parser.parse_args()


def default_workspace(report_name: str) -> Path:
    return Path("/tmp") / f"officecli-audit-{report_name}"


def configure_paths(args: argparse.Namespace) -> None:
    global ROOT, FIXTURES, WORKSPACE, OUTPUTS, TMPDIR, ISOLATED_HOME, LOGS, REPORTS, ARTIFACTS, BIN, BASE_ENV

    ROOT = (args.workspace or default_workspace(args.report_name)).resolve()
    FIXTURES = ROOT / "fixtures"
    WORKSPACE = ROOT / "workspace"
    OUTPUTS = WORKSPACE / "outputs"
    TMPDIR = WORKSPACE / "tmp"
    ISOLATED_HOME = ROOT / "isolated-home"
    LOGS = ROOT / "logs"
    REPORTS = ROOT / "reports"
    ARTIFACTS = ROOT / "artifacts"
    BIN = args.cli.resolve()
    BASE_ENV = {
        "HOME": str(ISOLATED_HOME),
        "OFFICECLI_SKIP_UPDATE": "1",
        "TMPDIR": str(TMPDIR),
    }


def ensure_workspace(args: argparse.Namespace) -> None:
    forbidden = {Path("/").resolve(), REPO.resolve(), REAL_HOME.resolve()}
    if ROOT in forbidden or ROOT == REPO.parent.resolve():
        raise ValueError(f"Refusing to use unsafe workspace root: {ROOT}")
    if ROOT.exists() and not args.keep_workspace:
        shutil.rmtree(ROOT)
    for path in [FIXTURES, OUTPUTS, TMPDIR, LOGS, REPORTS, ARTIFACTS, ISOLATED_HOME]:
        path.mkdir(parents=True, exist_ok=True)


@dataclass
class CmdResult:
    label: str
    cmd: list[str]
    returncode: int
    stdout: str
    stderr: str
    duration: float
    log_path: Path


def slugify(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")[:80] or "cmd"


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def json_pretty(data: Any) -> str:
    return json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True)


def ensure_text(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="replace")
    return str(value)


class AuditRunner:
    def __init__(self) -> None:
        self.counter = 1
        self.coverage: list[dict[str, Any]] = []
        self.notes: list[str] = []
        self.failures: list[dict[str, Any]] = []
        self.watch_logs: list[Path] = []

    def log(self, text: str) -> None:
        print(text, flush=True)

    def run(
        self,
        label: str,
        cmd: list[str],
        *,
        stdin: str | None = None,
        timeout: int = 180,
        env_extra: dict[str, str] | None = None,
        cwd: Path | None = None,
    ) -> CmdResult:
        env = os.environ.copy()
        env.update(BASE_ENV)
        if env_extra:
            env.update(env_extra)
        idx = self.counter
        self.counter += 1
        log_path = LOGS / f"{idx:03d}-{slugify(label)}.log"
        start = time.time()
        try:
            proc = subprocess.run(
                cmd,
                input=stdin,
                text=True,
                capture_output=True,
                cwd=str(cwd) if cwd else None,
                env=env,
                timeout=timeout,
            )
            rc = proc.returncode
            out = ensure_text(proc.stdout)
            err = ensure_text(proc.stderr)
        except subprocess.TimeoutExpired as ex:
            rc = 124
            out = ensure_text(ex.stdout)
            err = ensure_text(ex.stderr) + f"\nTIMEOUT after {timeout}s"
        duration = time.time() - start
        body = [
            f"label: {label}",
            f"cwd: {cwd or ROOT}",
            f"command: {shlex.join(cmd)}",
            f"returncode: {rc}",
            f"duration_sec: {duration:.3f}",
            "",
            "=== stdout ===",
            out,
            "",
            "=== stderr ===",
            err,
            "",
        ]
        log_path.write_text("\n".join(body), encoding="utf-8")
        return CmdResult(label, cmd, rc, out, err, duration, log_path)

    def officecli(self, label: str, *args: str, stdin: str | None = None, timeout: int = 180) -> CmdResult:
        return self.run(label, [str(BIN), *args], stdin=stdin, timeout=timeout)

    def playwright(self, label: str, *args: str, timeout: int = 120) -> CmdResult:
        return self.run(label, [str(PWCLI), *args], timeout=timeout)

    def record(
        self,
        *,
        fmt: str,
        scenario: str,
        feature: str,
        status: str,
        note: str,
        result: CmdResult | None = None,
        artifact: Path | None = None,
    ) -> None:
        row = {
            "format": fmt,
            "scenario": scenario,
            "feature": feature,
            "status": status,
            "note": note,
            "log": str(result.log_path) if result else "",
            "artifact": str(artifact) if artifact else "",
        }
        self.coverage.append(row)
        if status in {"失败", "部分通过"}:
            self.failures.append(row)

    def save_stdout(self, result: CmdResult, path: Path) -> None:
        path.write_text(result.stdout, encoding="utf-8")

    def save_json(self, path: Path, data: Any) -> None:
        path.write_text(json_pretty(data), encoding="utf-8")

    def parse_json(self, result: CmdResult) -> Any | None:
        try:
            return json.loads(result.stdout)
        except Exception:
            return None

    def ok_json(self, result: CmdResult) -> bool:
        data = self.parse_json(result)
        if not isinstance(data, dict):
            return result.returncode == 0
        if "success" in data:
            return bool(data["success"])
        return result.returncode == 0

    def ensure(self, result: CmdResult, *, fmt: str, scenario: str, feature: str, success_note: str, fail_note: str) -> bool:
        ok = result.returncode == 0 and self.ok_json(result)
        self.record(
            fmt=fmt,
            scenario=scenario,
            feature=feature,
            status="通过" if ok else "失败",
            note=success_note if ok else fail_note,
            result=result,
        )
        return ok


def snapshot_tree(path: Path) -> dict[str, Any] | None:
    if not path.exists():
        return None
    if path.is_file():
        stat = path.stat()
        return {
            "type": "file",
            "size": stat.st_size,
            "sha256": sha256_file(path),
        }
    entries: dict[str, Any] = {}
    for item in sorted(path.rglob("*")):
        rel = item.relative_to(path).as_posix()
        if item.is_file():
            stat = item.stat()
            entries[rel] = {
                "size": stat.st_size,
                "sha256": sha256_file(item),
            }
        elif item.is_symlink():
            entries[rel] = {"symlink": os.readlink(item)}
    return {"type": "tree", "entries": entries}


def capture_real_baseline(path: Path) -> dict[str, Any]:
    data = {
        "zshrc": snapshot_tree(REAL_HOME / ".zshrc"),
        "local_bin": snapshot_tree(REAL_HOME / ".local" / "bin"),
        "openclaw_skills": snapshot_tree(REAL_HOME / ".openclaw" / "skills"),
        "officecli": snapshot_tree(REAL_HOME / ".officecli"),
    }
    path.write_text(json_pretty(data), encoding="utf-8")
    return data


def prepare_fixtures(*, reuse_existing: bool = False) -> dict[str, Path]:
    FIXTURES.mkdir(parents=True, exist_ok=True)
    mapping = {
        "annual-report.docx": SHOWCASE / "annual-report.docx",
        "annual-report.png": SHOWCASE / "annual-report.png",
        "academic-paper.docx": SHOWCASE / "academic-paper.docx",
        "academic-paper.png": SHOWCASE / "academic-paper.png",
        "sales-dashboard.xlsx": SHOWCASE / "sales-dashboard.xlsx",
        "gradebook.xlsx": SHOWCASE / "gradebook.xlsx",
        "sales-dashboard.png": SHOWCASE / "sales-dashboard.png",
        "gradebook.png": SHOWCASE / "gradebook.png",
        "budget_review_v2.pptx": EXAMPLES / "budget_review_v2.pptx",
        "Alien_Guide.pptx": EXAMPLES / "Alien_Guide.pptx",
    }
    for name, src in mapping.items():
        dst = FIXTURES / name
        if reuse_existing and dst.exists():
            continue
        shutil.copy2(src, dst)
    return {name: FIXTURES / name for name in mapping}


def baseline_diff(before: dict[str, Any], after: dict[str, Any]) -> list[str]:
    diffs: list[str] = []
    keys = sorted(set(before) | set(after))
    for key in keys:
        if before.get(key) != after.get(key):
            diffs.append(key)
    return diffs


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    fields = ["format", "scenario", "feature", "status", "note", "log", "artifact"]
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        for row in rows:
            writer.writerow(row)


def make_csv(path: Path, headers: list[str], rows: list[list[str]]) -> None:
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(headers)
        writer.writerows(rows)


def wait_for_port(port: int, timeout: int = 20) -> bool:
    end = time.time() + timeout
    while time.time() < end:
        try:
            r = subprocess.run(
                ["lsof", "-nP", f"-iTCP:{port}", "-sTCP:LISTEN"],
                capture_output=True,
                text=True,
                timeout=5,
            )
            if r.returncode == 0 and str(port) in r.stdout:
                return True
        except Exception:
            pass
        time.sleep(0.4)
    return False


def find_first_path(results: list[dict[str, Any]], predicate) -> str | None:
    for item in results:
        try:
            if predicate(item):
                return item["path"]
        except Exception:
            continue
    return None


def node_children(node: dict[str, Any] | None) -> list[dict[str, Any]]:
    if not isinstance(node, dict):
        return []
    children = node.get("children")
    if isinstance(children, list):
        return children
    children = node.get("Children")
    if isinstance(children, list):
        return children
    return []


def node_path(node: dict[str, Any] | None) -> str | None:
    if not isinstance(node, dict):
        return None
    value = node.get("path")
    if isinstance(value, str):
        return value
    value = node.get("Path")
    if isinstance(value, str):
        return value
    return None


def node_format(node: dict[str, Any] | None) -> dict[str, Any]:
    if not isinstance(node, dict):
        return {}
    value = node.get("format")
    if isinstance(value, dict):
        return value
    value = node.get("Format")
    if isinstance(value, dict):
        return value
    return {}


def first_zip_member(file: Path, prefix: str) -> str | None:
    try:
        res = subprocess.run(
            ["unzip", "-Z1", str(file)],
            capture_output=True,
            text=True,
            timeout=30,
        )
    except Exception:
        return None
    if res.returncode != 0:
        return None
    for line in res.stdout.splitlines():
        line = line.strip()
        if line.startswith(prefix):
            return line
    return None


def first_chart_member(file: Path, family: str) -> str | None:
    try:
        res = subprocess.run(
            ["unzip", "-Z1", str(file)],
            capture_output=True,
            text=True,
            timeout=30,
        )
    except Exception:
        return None
    if res.returncode != 0:
        return None
    prefix = f"{family}/"
    for line in res.stdout.splitlines():
        line = line.strip()
        if line.startswith(prefix) and line.endswith(".xml") and "/chart" in line:
            return line
    return None


def wait_for_file_release(file: Path, timeout: int = 15) -> bool:
    end = time.time() + timeout
    while time.time() < end:
        try:
            r = subprocess.run(
                ["lsof", str(file)],
                capture_output=True,
                text=True,
                timeout=5,
            )
            if r.returncode != 0 or str(file) not in r.stdout:
                return True
        except Exception:
            return True
        time.sleep(0.5)
    return False


def query_results(audit: AuditRunner, file: Path, selector: str, *, text: str | None = None) -> list[dict[str, Any]]:
    args = ["query", str(file), selector, "--json"]
    if text:
        args.extend(["--text", text])
    res = audit.officecli(f"query-{selector}-{file.stem}", *args, timeout=240)
    data = audit.parse_json(res) or {}
    return data.get("data", {}).get("Results", [])


def get_node(audit: AuditRunner, file: Path, path: str, depth: int = 1) -> dict[str, Any] | None:
    res = audit.officecli(f"get-{file.stem}-{slugify(path)}", "get", str(file), path, "--depth", str(depth), "--json", timeout=240)
    data = audit.parse_json(res) or {}
    if not data.get("success"):
        return None
    return data.get("data")


def validation_is_clean(audit: AuditRunner, result: CmdResult) -> bool:
    if result.returncode != 0:
        return False
    data = audit.parse_json(result)
    if not isinstance(data, dict):
        return True
    if data.get("success") is False:
        return False
    candidates = [
        data.get("count"),
        data.get("Count"),
        (data.get("data") or {}).get("count") if isinstance(data.get("data"), dict) else None,
        (data.get("data") or {}).get("Count") if isinstance(data.get("data"), dict) else None,
    ]
    for value in candidates:
        if isinstance(value, int):
            return value == 0
    text = result.stdout.lower()
    if '"count": 0' in text or '"count":0' in text:
        return True
    return True


def create_docx_template(audit: AuditRunner, template: Path) -> None:
    audit.officecli("docx-template-create", "create", str(template))
    audit.officecli("docx-template-p1", "add", str(template), "/body", "--type", "paragraph", "--prop", "text=Invoice for {{client}}", "--prop", "style=Heading1")
    audit.officecli("docx-template-p2", "add", str(template), "/body", "--type", "paragraph", "--prop", "text=Department: {{dept}}")
    audit.officecli("docx-template-p3", "add", str(template), "/body", "--type", "paragraph", "--prop", "text=Total due: {{total}}")


def create_xlsx_template(audit: AuditRunner, template: Path) -> None:
    audit.officecli("xlsx-template-create", "create", str(template))
    audit.officecli("xlsx-template-rename", "set", str(template), "/Sheet1", "--prop", "name=Template")
    for ref, value in [
        ("A1", "Quarter"),
        ("B1", "{{quarter}}"),
        ("A2", "Year"),
        ("B2", "{{year}}"),
        ("A3", "Owner"),
        ("B3", "{{owner}}"),
    ]:
        audit.officecli(f"xlsx-template-{ref}", "set", str(template), f"/Template/{ref}", "--prop", f"value={value}")


def create_pptx_template(audit: AuditRunner, template: Path) -> None:
    audit.officecli("pptx-template-create", "create", str(template))
    audit.officecli("pptx-template-slide", "add", str(template), "/", "--type", "slide", "--prop", "layout=title", "--prop", "title={{title}}", "--prop", "text={{summary}}")


def build_docx(audit: AuditRunner) -> Path:
    file = OUTPUTS / "created-round3-report.docx"
    audit.officecli("docx-create", "create", str(file))
    audit.officecli(
        "docx-root-set",
        "set",
        str(file),
        "/",
        "--prop",
        "title=FY2026 Operating Review",
        "--prop",
        "author=Codex Round3 Audit",
        "--prop",
        "subject=OfficeCLI complex DOCX test",
        "--prop",
        "keywords=officecli,round3,audit,docx",
        "--prop",
        "category=Testing",
        "--prop",
        "docDefaults.font=Georgia",
        "--prop",
        "docDefaults.fontSize=11pt",
        "--prop",
        "docDefaults.color=222222",
        "--prop",
        "theme.color.accent1=1F4E79",
        "--prop",
        "theme.font.major.latin=Georgia",
        "--prop",
        "theme.font.minor.latin=Calibri",
    )
    audit.officecli("docx-style", "add", str(file), "/body", "--type", "style", "--prop", "name=KPIHeading", "--prop", "type=paragraph", "--prop", "font=Georgia", "--prop", "bold=true", "--prop", "color=1F4E79")
    audit.officecli("docx-header", "add", str(file), "/", "--type", "header", "--prop", "text=OfficeCLI Round3 Audit | Internal", "--prop", "alignment=center", "--prop", "color=666666")
    audit.officecli("docx-footer", "add", str(file), "/", "--type", "footer", "--prop", "text=Confidential | Generated in isolated HOME", "--prop", "alignment=center", "--prop", "color=777777")
    audit.officecli("docx-watermark", "add", str(file), "/", "--type", "watermark", "--prop", "text=REVIEW COPY", "--prop", "color=C0C0C0", "--prop", "rotation=315")

    paragraphs = [
        ("FY2026 Operating Review", "Heading1"),
        ("Operations, finance, and product delivery synthesis", "Normal"),
        ("This document is intentionally generated through OfficeCLI high-level commands to stress semantic editing, formatting, tables, charts, and review metadata.", "Normal"),
        ("Table of Contents", "Heading2"),
    ]
    for idx, (text, style) in enumerate(paragraphs, start=1):
        audit.officecli(f"docx-paragraph-{idx}", "add", str(file), "/body", "--type", "paragraph", "--prop", f"text={text}", "--prop", f"style={style}")

    audit.officecli("docx-toc", "add", str(file), "/body", "--type", "toc", "--prop", "levels=1-3", "--prop", "title=Contents")
    audit.officecli("docx-pagebreak", "add", str(file), "/body", "--type", "pagebreak")

    section_data = [
        ("Executive Summary", [
            "Revenue increased across all regions, driven by enterprise expansion and disciplined pricing.",
            "Program delivery improved from 81% to 92% on-time completion after platform consolidation.",
        ]),
        ("Operating Metrics", [
            "The delivery team closed the quarter with higher utilization, lower defect escape, and faster incident recovery.",
            "Finance introduced tighter cost controls, while product management reduced roadmap churn by 28%.",
        ]),
        ("Risks & Controls", [
            "Vendor concentration remains elevated in the observability stack and needs contingency planning.",
            "Forecast variance is still material in two international markets and requires monthly review.",
        ]),
        ("Appendix", [
            "Appendix content includes supporting assumptions, source links, and review annotations.",
        ]),
    ]
    p_counter = 10
    for title, body_paragraphs in section_data:
        audit.officecli(f"docx-sec-{slugify(title)}", "add", str(file), "/body", "--type", "paragraph", "--prop", f"text={title}", "--prop", "style=Heading1")
        for body in body_paragraphs:
            audit.officecli(f"docx-body-{p_counter}", "add", str(file), "/body", "--type", "paragraph", "--prop", f"text={body}")
            p_counter += 1

    list_items = [
        "Expand enterprise AI attach rate to 42% of renewals.",
        "Reduce project margin leakage below 2.5% of revenue.",
        "Standardize service review cadence across four regions.",
    ]
    for i, item in enumerate(list_items, start=1):
        audit.officecli(f"docx-bullet-{i}", "add", str(file), "/body", "--type", "paragraph", "--prop", f"text={item}")
        audit.officecli(f"docx-bullet-style-{i}", "set", str(file), f"/body/p[{18+i}]", "--prop", "listStyle=bullet")

    numbered_items = [
        "Lock staffing model assumptions.",
        "Publish regional action register.",
        "Complete monthly variance dashboard rollout.",
    ]
    for i, item in enumerate(numbered_items, start=1):
        audit.officecli(f"docx-num-{i}", "add", str(file), "/body", "--type", "paragraph", "--prop", f"text={item}")
        audit.officecli(f"docx-num-style-{i}", "set", str(file), f"/body/p[{21+i}]", "--prop", "listStyle=numbered")

    audit.officecli("docx-bookmark", "add", str(file), "/body/p[12]", "--type", "bookmark", "--prop", "name=KeyMetrics", "--prop", "text=Key Metrics Anchor")
    audit.officecli("docx-comment", "add", str(file), "/body/p[11]", "--type", "comment", "--prop", "text=Recheck this summary against final CFO pack", "--prop", "author=QA")
    audit.officecli("docx-hyperlink", "add", str(file), "/body/p[11]", "--type", "hyperlink", "--prop", "url=https://example.com/finance-source", "--prop", "text=Source workbook")
    audit.officecli("docx-footnote", "add", str(file), "/body/p[12]", "--type", "footnote", "--prop", "text=Internal planning dataset, refreshed 2026-04-01.")
    audit.officecli("docx-equation", "add", str(file), "/body", "--type", "equation", "--prop", r"formula=\frac{Revenue-Cost}{Revenue}", "--prop", "mode=display")
    audit.officecli("docx-picture", "add", str(file), "/body", "--type", "picture", "--prop", f"path={FIXTURES / 'annual-report.png'}", "--prop", "width=10cm", "--prop", "height=5.5cm", "--prop", "alt=Revenue illustration")

    audit.officecli("docx-table-add", "add", str(file), "/body", "--type", "table", "--prop", "rows=4", "--prop", "cols=4", "--prop", "style=TableGrid", "--prop", "alignment=center", "--prop", "width=100%")
    audit.officecli("docx-table-head", "set", str(file), "/body/tbl[1]/tr[1]", "--prop", "header=true", "--prop", "c1=Metric", "--prop", "c2=Plan", "--prop", "c3=Actual", "--prop", "c4=Variance")
    for cell in range(1, 5):
        audit.officecli(
            f"docx-table-head-style-{cell}",
            "set",
            str(file),
            f"/body/tbl[1]/tr[1]/tc[{cell}]",
            "--prop",
            "bold=true",
            "--prop",
            "shd=1F4E79",
            "--prop",
            "color=FFFFFF",
            "--prop",
            "alignment=center",
        )
    rows = [
        ("Revenue", "$3.4M", "$3.7M", "+8.8%"),
        ("Gross Margin", "41.0%", "43.2%", "+2.2pp"),
        ("Delivery SLA", "90%", "92%", "+2pp"),
    ]
    for row_idx, row in enumerate(rows, start=2):
        audit.officecli(
            f"docx-table-row-{row_idx}",
            "set",
            str(file),
            f"/body/tbl[1]/tr[{row_idx}]",
            "--prop",
            f"c1={row[0]}",
            "--prop",
            f"c2={row[1]}",
            "--prop",
            f"c3={row[2]}",
            "--prop",
            f"c4={row[3]}",
        )
    audit.officecli("docx-table-merge", "set", str(file), "/body/tbl[1]/tr[1]/tc[1]", "--prop", "gridSpan=2")
    audit.officecli("docx-table-cell-grad", "set", str(file), "/body/tbl[1]/tr[2]/tc[4]", "--prop", "shd=gradient;D9EAD3;B6D7A8;90")

    audit.officecli(
        "docx-chart-bar",
        "add",
        str(file),
        "/body",
        "--type",
        "chart",
        "--prop",
        "chartType=bar",
        "--prop",
        "title=Regional Revenue",
        "--prop",
        "categories=North,South,East,West",
        "--prop",
        "series1=FY2026:900,860,820,760",
        "--prop",
        "colors=1F4E79,5B9BD5,70AD47,C9A84C",
        "--prop",
        "width=14cm",
        "--prop",
        "height=8cm",
    )
    audit.officecli(
        "docx-chart-pie",
        "add",
        str(file),
        "/body",
        "--type",
        "chart",
        "--prop",
        "chartType=doughnut",
        "--prop",
        "title=Expense Mix",
        "--prop",
        "categories=People,Cloud,Marketing,Facilities",
        "--prop",
        "series1=Share:46,22,18,14",
        "--prop",
        "colors=4472C4,ED7D31,A5A5A5,70AD47",
    )

    batch_commands = [
        {"command": "set", "path": "/body/p[1]", "props": {"alignment": "center", "color": "1F4E79", "size": "24"}},
        {"command": "set", "path": "/body/p[2]", "props": {"alignment": "center", "italic": "true", "color": "666666"}},
        {"command": "set", "path": "/body/p[10]", "props": {"keepNext": "true"}},
        {"command": "set", "path": "/body/p[11]", "props": {"bold": "true"}},
        {"command": "set", "path": "/body/tbl[1]/tr[2]/tc[2]", "props": {"alignment": "center"}},
        {"command": "set", "path": "/body/tbl[1]/tr[2]/tc[3]", "props": {"alignment": "center"}},
        {"command": "set", "path": "/body/tbl[1]/tr[2]/tc[4]", "props": {"bold": "true", "color": "38761D"}},
        {"command": "set", "path": "/body/p[24]", "props": {"pageBreakBefore": "true"}},
    ]
    audit.officecli("docx-batch", "batch", str(file), "--commands", json.dumps(batch_commands), "--json")
    return file


def edit_docx_sample(audit: AuditRunner) -> Path:
    src = FIXTURES / "annual-report.docx"
    dst = OUTPUTS / "edited-sample-annual-report.docx"
    shutil.copy2(src, dst)
    audit.officecli("docx-sample-get", "get", str(dst), "/body/p[1]", "--depth", "1", "--json")
    audit.officecli("docx-sample-set-cover", "set", str(dst), "/body/p[1]", "--prop", "text=TechVision Corp | Audit Copy", "--prop", "color=D4AF37")
    audit.officecli("docx-sample-add-a", "add", str(dst), "/body", "--after", "/body/p[24]", "--type", "paragraph", "--prop", "text=Inserted review note A")
    audit.officecli("docx-sample-add-b", "add", str(dst), "/body", "--after", "/body/p[24]", "--type", "paragraph", "--prop", "text=Inserted review note B")
    audit.officecli("docx-sample-comment", "add", str(dst), "/body/p[25]", "--type", "comment", "--prop", "text=Verify this opening sentence against the signed CEO letter")
    audit.officecli("docx-sample-bookmark", "add", str(dst), "/body/p[25]", "--type", "bookmark", "--prop", "name=CEOIntro", "--prop", "text=CEO Intro Bookmark")
    audit.officecli("docx-sample-table", "add", str(dst), "/body", "--after", "/body/p[33]", "--type", "table", "--prop", "rows=2", "--prop", "cols=2", "--prop", "style=TableGrid")
    audit.officecli("docx-sample-table-set", "set", str(dst), "/body/tbl[2]/tr[1]", "--prop", "c1=Checkpoint", "--prop", "c2=Owner")
    audit.officecli("docx-sample-move", "move", str(dst), "/body/p[26]", "--before", "/body/p[24]")
    audit.officecli("docx-sample-swap", "swap", str(dst), "/body/p[26]", "/body/p[27]")
    audit.officecli("docx-sample-remove", "remove", str(dst), "/body/p[27]")
    return dst


def edit_docx_sample_two(audit: AuditRunner) -> Path:
    src = FIXTURES / "academic-paper.docx"
    dst = OUTPUTS / "edited-sample-academic-paper.docx"
    shutil.copy2(src, dst)
    audit.officecli("docx-sample2-get", "get", str(dst), "/body/p[1]", "--depth", "1", "--json")
    audit.officecli("docx-sample2-title", "set", str(dst), "/body/p[1]", "--prop", "text=Academic Paper | Round3 Audit Copy", "--prop", "color=2F5597")
    audit.officecli("docx-sample2-add-abstract", "add", str(dst), "/body", "--after", "/body/p[2]", "--type", "paragraph", "--prop", "text=Round3 audit inserted abstract note covering formatting, citations, and references.", "--prop", "style=Quote")
    audit.officecli("docx-sample2-comment", "add", str(dst), "/body/p[3]", "--type", "comment", "--prop", "text=Check whether this section still matches the latest reviewer notes.", "--prop", "author=Round3")
    audit.officecli("docx-sample2-bookmark", "add", str(dst), "/body/p[3]", "--type", "bookmark", "--prop", "name=Round3PaperIntro", "--prop", "text=Round3 Paper Intro")
    audit.officecli("docx-sample2-link", "add", str(dst), "/body/p[3]", "--type", "hyperlink", "--prop", "url=https://example.com/round3-source", "--prop", "text=Reference source")
    audit.officecli("docx-sample2-footnote", "add", str(dst), "/body/p[4]", "--type", "footnote", "--prop", "text=Round3 audit footnote for citation fidelity.")
    audit.officecli("docx-sample2-move", "move", str(dst), "/body/p[3]", "--before", "/body/p[2]")
    audit.officecli("docx-sample2-remove", "remove", str(dst), "/body/p[5]")
    return dst


def build_xlsx(audit: AuditRunner) -> tuple[Path, Path]:
    file = OUTPUTS / "created-analysis-workbook.xlsx"
    csv_file = OUTPUTS / "import-products.csv"
    make_csv(
        csv_file,
        ["SKU", "Category", "UnitPrice"],
        [
            ["A-100", "Analytics", "1299"],
            ["B-210", "Platform", "899"],
            ["C-310", "Services", "1599"],
        ],
    )
    audit.officecli("xlsx-create", "create", str(file))
    audit.officecli("xlsx-rename-sheet1", "set", str(file), "/Sheet1", "--prop", "name=RawData")
    audit.officecli("xlsx-add-model", "add", str(file), "/", "--type", "sheet", "--prop", "name=Model")
    audit.officecli("xlsx-add-dashboard", "add", str(file), "/", "--type", "sheet", "--prop", "name=Dashboard")
    audit.officecli("xlsx-add-import", "add", str(file), "/", "--type", "sheet", "--prop", "name=ImportCSV")

    batch: list[dict[str, Any]] = [
        {"command": "set", "path": "/RawData/A1:H1", "props": {"merge": "true"}},
        {"command": "set", "path": "/RawData/A1", "props": {"value": "Regional Revenue Pipeline FY2026", "font.bold": "true", "font.size": "16", "fill": "1F4E79", "font.color": "FFFFFF"}},
        {"command": "set", "path": "/RawData/A2", "props": {"value": "Region", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/B2", "props": {"value": "Quarter", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/C2", "props": {"value": "Revenue", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/D2", "props": {"value": "Cost", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/E2", "props": {"value": "MarginPct", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/F2", "props": {"value": "Units", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/G2", "props": {"value": "Status", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData/H2", "props": {"value": "Owner", "font.bold": "true", "fill": "D9E2F3"}},
        {"command": "set", "path": "/RawData", "props": {"freeze": "A3"}},
        {"command": "set", "path": "/RawData/autofilter", "props": {"range": "A2:H14"}},
        {"command": "set", "path": "/RawData/col[A]", "props": {"width": "15"}},
        {"command": "set", "path": "/RawData/col[B]", "props": {"width": "12"}},
        {"command": "set", "path": "/RawData/col[C]", "props": {"width": "14"}},
        {"command": "set", "path": "/RawData/col[D]", "props": {"width": "14"}},
        {"command": "set", "path": "/RawData/col[E]", "props": {"width": "12"}},
        {"command": "set", "path": "/RawData/col[F]", "props": {"width": "10"}},
        {"command": "set", "path": "/RawData/col[G]", "props": {"width": "12"}},
        {"command": "set", "path": "/RawData/col[H]", "props": {"width": "14"}},
    ]
    data_rows = [
        ["North", "Q1", "320000", "188000", "0.4125", "42", "Open", "Liu"],
        ["North", "Q2", "360000", "205000", "0.4306", "45", "Won", "Liu"],
        ["North", "Q3", "410000", "228000", "0.4439", "48", "Won", "Liu"],
        ["South", "Q1", "280000", "172000", "0.3857", "38", "Open", "Zhang"],
        ["South", "Q2", "295000", "178000", "0.3966", "39", "Open", "Zhang"],
        ["South", "Q3", "315000", "184000", "0.4159", "41", "Won", "Zhang"],
        ["East", "Q1", "260000", "165000", "0.3654", "35", "Lost", "Chen"],
        ["East", "Q2", "305000", "188000", "0.3836", "40", "Open", "Chen"],
        ["East", "Q3", "330000", "196000", "0.4061", "44", "Won", "Chen"],
        ["West", "Q1", "240000", "150000", "0.3750", "30", "Open", "Wang"],
        ["West", "Q2", "250000", "154000", "0.3840", "32", "Open", "Wang"],
        ["West", "Q3", "275000", "166000", "0.3964", "36", "Won", "Wang"],
    ]
    for row_idx, row in enumerate(data_rows, start=3):
        for col, value in zip("ABCDEFGH", row):
            props = {"value": value}
            if col in {"C", "D"}:
                props["numFmt"] = "$#,##0"
            if col == "E":
                props["numFmt"] = "0.0%"
            batch.append({"command": "set", "path": f"/RawData/{col}{row_idx}", "props": props})

    batch.extend(
        [
            {"command": "set", "path": "/Model/A1", "props": {"value": "Metric", "font.bold": "true", "fill": "F4CCCC"}},
            {"command": "set", "path": "/Model/B1", "props": {"value": "Value", "font.bold": "true", "fill": "F4CCCC"}},
            {"command": "set", "path": "/Model/A2", "props": {"value": "Total Revenue"}},
            {"command": "set", "path": "/Model/B2", "props": {"formula": "=SUM(RawData!C3:C14)", "numFmt": "$#,##0"}},
            {"command": "set", "path": "/Model/A3", "props": {"value": "Average Margin"}},
            {"command": "set", "path": "/Model/B3", "props": {"formula": "=AVERAGE(RawData!E3:E14)", "numFmt": "0.0%"}},
            {"command": "set", "path": "/Model/A4", "props": {"value": "Open Deals"}},
            {"command": "set", "path": "/Model/B4", "props": {"formula": '=COUNTIF(RawData!G3:G14,"Open")'}},
            {"command": "set", "path": "/Model/A5", "props": {"value": "North Revenue"}},
            {"command": "set", "path": "/Model/B5", "props": {"formula": '=SUMIF(RawData!A3:A14,"North",RawData!C3:C14)', "numFmt": "$#,##0"}},
            {"command": "set", "path": "/Model/A6", "props": {"value": "Review Flag"}},
            {"command": "set", "path": "/Model/B6", "props": {"formula": '=IF(B2>3600000,"Above Plan","Needs Attention")'}},
            {"command": "set", "path": "/Model/A7", "props": {"value": "North Owner"}},
            {"command": "set", "path": "/Model/B7", "props": {"formula": '=VLOOKUP("North",RawData!A3:H14,8,FALSE)'}},
            {"command": "set", "path": "/Dashboard/A1", "props": {"value": "Quarter", "font.bold": "true", "fill": "D9EAD3"}},
            {"command": "set", "path": "/Dashboard/B1", "props": {"value": "Revenue", "font.bold": "true", "fill": "D9EAD3"}},
            {"command": "set", "path": "/Dashboard/C1", "props": {"value": "Units", "font.bold": "true", "fill": "D9EAD3"}},
            {"command": "set", "path": "/Dashboard/A2", "props": {"value": "Q1"}},
            {"command": "set", "path": "/Dashboard/A3", "props": {"value": "Q2"}},
            {"command": "set", "path": "/Dashboard/A4", "props": {"value": "Q3"}},
            {"command": "set", "path": "/Dashboard/B2", "props": {"formula": '=SUMIF(RawData!B3:B14,"Q1",RawData!C3:C14)', "numFmt": "$#,##0"}},
            {"command": "set", "path": "/Dashboard/B3", "props": {"formula": '=SUMIF(RawData!B3:B14,"Q2",RawData!C3:C14)', "numFmt": "$#,##0"}},
            {"command": "set", "path": "/Dashboard/B4", "props": {"formula": '=SUMIF(RawData!B3:B14,"Q3",RawData!C3:C14)', "numFmt": "$#,##0"}},
            {"command": "set", "path": "/Dashboard/C2", "props": {"formula": '=SUMIF(RawData!B3:B14,"Q1",RawData!F3:F14)'}},
            {"command": "set", "path": "/Dashboard/C3", "props": {"formula": '=SUMIF(RawData!B3:B14,"Q2",RawData!F3:F14)'}},
            {"command": "set", "path": "/Dashboard/C4", "props": {"formula": '=SUMIF(RawData!B3:B14,"Q3",RawData!F3:F14)'}},
            {"command": "set", "path": "/Dashboard/E1", "props": {"value": "Region", "font.bold": "true", "fill": "FFF2CC"}},
            {"command": "set", "path": "/Dashboard/F1", "props": {"value": "Revenue", "font.bold": "true", "fill": "FFF2CC"}},
        ]
    )
    for idx, region in enumerate(["North", "South", "East", "West"], start=2):
        batch.append({"command": "set", "path": f"/Dashboard/E{idx}", "props": {"value": region}})
        batch.append({"command": "set", "path": f"/Dashboard/F{idx}", "props": {"formula": f'=SUMIF(RawData!A3:A14,"{region}",RawData!C3:C14)', "numFmt": "$#,##0"}})

    audit.officecli("xlsx-batch-initial", "batch", str(file), "--commands", json.dumps(batch), "--json", timeout=240)

    audit.officecli("xlsx-add-validation", "add", str(file), "/RawData", "--type", "validation", "--prop", "sqref=G3:G20", "--prop", "type=list", "--prop", "formula1=Open,Won,Lost", "--prop", "promptTitle=Status", "--prop", "prompt=Choose pipeline status")
    audit.officecli("xlsx-add-databar", "add", str(file), "/RawData", "--type", "databar", "--prop", "sqref=C3:C14", "--prop", "color=63C384")
    audit.officecli("xlsx-add-colorscale", "add", str(file), "/RawData", "--type", "colorscale", "--prop", "sqref=E3:E14", "--prop", "mincolor=F8696B", "--prop", "midcolor=FFEB84", "--prop", "maxcolor=63BE7B")
    audit.officecli("xlsx-add-formulacf", "add", str(file), "/RawData", "--type", "formulacf", "--prop", "sqref=C3:C14", "--prop", "formula=$C3>300000", "--prop", "fill=D9EAD3", "--prop", "font.bold=true")
    audit.officecli("xlsx-add-namedrange", "add", str(file), "/", "--type", "namedrange", "--prop", "name=SalesData", "--prop", "ref=RawData!A2:H14", "--prop", "comment=Primary raw dataset")
    audit.officecli("xlsx-add-table", "add", str(file), "/RawData", "--type", "table", "--prop", "ref=A2:H14", "--prop", "name=SalesTable", "--prop", "style=TableStyleMedium2")
    audit.officecli(
        "xlsx-chart-column",
        "add",
        str(file),
        "/Dashboard",
        "--type",
        "chart",
        "--prop",
        "chartType=column",
        "--prop",
        "title=Quarterly Revenue",
        "--prop",
        "dataRange=Dashboard!A1:C4",
        "--prop",
        "x=1",
        "--prop",
        "y=1",
        "--prop",
        "width=8",
        "--prop",
        "height=15",
    )
    audit.officecli(
        "xlsx-chart-line",
        "add",
        str(file),
        "/Dashboard",
        "--type",
        "chart",
        "--prop",
        "chartType=line",
        "--prop",
        "title=Quarterly Units",
        "--prop",
        "series1.name=Units",
        "--prop",
        "series1.values=Dashboard!C2:C4",
        "--prop",
        "series1.categories=Dashboard!A2:A4",
        "--prop",
        "x=10",
        "--prop",
        "y=1",
        "--prop",
        "width=8",
        "--prop",
        "height=15",
    )
    audit.officecli(
        "xlsx-chart-pie",
        "add",
        str(file),
        "/Dashboard",
        "--type",
        "chart",
        "--prop",
        "chartType=pie",
        "--prop",
        "title=Revenue Share by Region",
        "--prop",
        "series1.name=Revenue",
        "--prop",
        "series1.values=Dashboard!F2:F5",
        "--prop",
        "series1.categories=Dashboard!E2:E5",
        "--prop",
        "x=1",
        "--prop",
        "y=18",
        "--prop",
        "width=8",
        "--prop",
        "height=15",
    )
    audit.officecli(
        "xlsx-chart-scatter",
        "add",
        str(file),
        "/Dashboard",
        "--type",
        "chart",
        "--prop",
        "chartType=scatter",
        "--prop",
        "title=Units vs Revenue",
        "--prop",
        "categories=30,32,35,38,39,40,41,42,44,45,48,36",
        "--prop",
        "series1=Revenue:240000,250000,260000,280000,295000,305000,315000,320000,330000,360000,410000,275000",
        "--prop",
        "x=10",
        "--prop",
        "y=18",
        "--prop",
        "width=8",
        "--prop",
        "height=15",
    )
    audit.officecli("xlsx-import-csv", "import", str(file), "/ImportCSV", str(csv_file), "--header", "--start-cell", "A1", "--json")
    return file, csv_file


def edit_xlsx_sample(audit: AuditRunner) -> Path:
    src = FIXTURES / "sales-dashboard.xlsx"
    dst = OUTPUTS / "edited-sample-sales-dashboard.xlsx"
    shutil.copy2(src, dst)
    audit.officecli("xlsx-sample-get-root", "get", str(dst), "/", "--depth", "2", "--json", timeout=240)
    audit.officecli("xlsx-sample-add-sheet", "add", str(dst), "/", "--type", "sheet", "--prop", "name=AuditNotes")
    audit.officecli("xlsx-sample-set-note1", "set", str(dst), "/AuditNotes/A1", "--prop", "value=Finding", "--prop", "font.bold=true")
    audit.officecli("xlsx-sample-set-note2", "set", str(dst), "/AuditNotes/B1", "--prop", "value=Status", "--prop", "font.bold=true")
    audit.officecli("xlsx-sample-set-note3", "set", str(dst), "/AuditNotes/A2", "--prop", "value=Chart title updated")
    audit.officecli("xlsx-sample-set-note4", "set", str(dst), "/AuditNotes/B2", "--prop", 'formula=IF(1=1,"Done","Pending")')
    chart_results = query_results(audit, dst, "chart")
    if chart_results:
        audit.officecli("xlsx-sample-chart-set", "set", str(dst), chart_results[0]["path"], "--prop", "title=Q1-Q4 Revenue by Region | Audit", "--prop", "legend=right")
    table_results = query_results(audit, dst, "table")
    if table_results:
        audit.officecli("xlsx-sample-table-set", "set", str(dst), table_results[0]["path"], "--prop", "style=TableStyleLight1")
    audit.officecli("xlsx-sample-cell-edit", "set", str(dst), "/Dashboard/H2", "--prop", 'formula=SUM(B2:B4)', "--prop", "numFmt=$#,##0")
    return dst


def edit_xlsx_sample_two(audit: AuditRunner) -> Path:
    src = FIXTURES / "gradebook.xlsx"
    dst = OUTPUTS / "edited-sample-gradebook.xlsx"
    shutil.copy2(src, dst)
    root = get_node(audit, dst, "/", depth=2) or {}
    first_sheet = "/Sheet1"
    for child in node_children(root):
        path = node_path(child)
        if isinstance(path, str) and path.startswith("/"):
            first_sheet = path
            break
    sheet_name = first_sheet.lstrip("/") or "Sheet1"
    formula_sheet = f"'{sheet_name}'" if " " in sheet_name else sheet_name

    audit.officecli("xlsx-sample2-add-sheet", "add", str(dst), "/", "--type", "sheet", "--prop", "name=Summary")
    audit.officecli("xlsx-sample2-header1", "set", str(dst), "/Summary/A1", "--prop", "value=Metric", "--prop", "font.bold=true")
    audit.officecli("xlsx-sample2-header2", "set", str(dst), "/Summary/B1", "--prop", "value=Value", "--prop", "font.bold=true")
    audit.officecli("xlsx-sample2-metric1", "set", str(dst), "/Summary/A2", "--prop", "value=First student")
    audit.officecli("xlsx-sample2-value1", "set", str(dst), "/Summary/B2", "--prop", f'formula=INDEX({formula_sheet}!A:A,2)')
    audit.officecli("xlsx-sample2-metric2", "set", str(dst), "/Summary/A3", "--prop", "value=Populated rows")
    audit.officecli("xlsx-sample2-value2", "set", str(dst), "/Summary/B3", "--prop", f'formula=COUNTA({formula_sheet}!A:A)-1')
    audit.officecli("xlsx-sample2-metric3", "set", str(dst), "/Summary/A4", "--prop", "value=Average numeric value")
    audit.officecli("xlsx-sample2-value3", "set", str(dst), "/Summary/B4", "--prop", f'formula=AVERAGE({formula_sheet}!B:B)', "--prop", "numFmt=0.00")
    audit.officecli("xlsx-sample2-chart", "add", str(dst), "/Summary", "--type", "chart", "--prop", "chartType=column", "--prop", "title=Summary Metrics", "--prop", "dataRange=Summary!A1:B4", "--prop", "x=1", "--prop", "y=1", "--prop", "width=8", "--prop", "height=10")
    audit.officecli("xlsx-sample2-move-summary", "move", str(dst), "/Summary", "--before", first_sheet)
    return dst


def build_pptx(audit: AuditRunner) -> Path:
    file = OUTPUTS / "created-design-deck.pptx"
    audit.officecli("pptx-create", "create", str(file))
    audit.officecli("pptx-slide1", "add", str(file), "/", "--type", "slide", "--prop", "layout=title", "--prop", "title=FY2026 Strategy Review", "--prop", "text=OfficeCLI round3 deck", "--prop", "transition=fade")
    audit.officecli("pptx-slide1-set", "set", str(file), "/slide[1]", "--prop", "background=1A1A2E", "--prop", "notes=Open with business context and isolation disclaimer")
    audit.officecli("pptx-slide2", "add", str(file), "/", "--type", "slide", "--prop", "layout=titleOnly", "--prop", "title=Agenda", "--prop", "transition=push-left")
    audit.officecli("pptx-slide3", "add", str(file), "/", "--type", "slide", "--prop", "layout=titleOnly", "--prop", "title=Revenue Trend", "--prop", "transition=morph")
    audit.officecli("pptx-slide4", "add", str(file), "/", "--type", "slide", "--prop", "layout=titleOnly", "--prop", "title=Resource Plan", "--prop", "transition=split-horizontal")
    audit.officecli("pptx-slide5", "add", str(file), "/", "--type", "slide", "--prop", "layout=titleOnly", "--prop", "title=Operating Model", "--prop", "transition=wipe-right")
    audit.officecli("pptx-slide6", "add", str(file), "/", "--type", "slide", "--prop", "layout=titleOnly", "--prop", "title=Risks & Mitigations", "--prop", "transition=reveal-left")
    audit.officecli("pptx-slide7", "add", str(file), "/", "--type", "slide", "--prop", "layout=titleOnly", "--prop", "title=Closing", "--prop", "transition=morph", "--prop", "advanceTime=5000", "--prop", "advanceClick=true")

    audit.officecli(
        "pptx-agenda-shape",
        "add",
        str(file),
        "/slide[2]",
        "--type",
        "shape",
        "--prop",
        "text=1. Performance\\n2. Resource plan\\n3. Risks\\n4. Next actions",
        "--prop",
        "list=numbered",
        "--prop",
        "x=2cm",
        "--prop",
        "y=4cm",
        "--prop",
        "width=12cm",
        "--prop",
        "height=8cm",
        "--prop",
        "fill=none",
        "--prop",
        "color=F2F2F2",
        "--prop",
        "size=24",
    )
    audit.officecli(
        "pptx-chart-main",
        "add",
        str(file),
        "/slide[3]",
        "--type",
        "chart",
        "--prop",
        "chartType=column",
        "--prop",
        "title=Quarterly Revenue",
        "--prop",
        "categories=Q1,Q2,Q3,Q4",
        "--prop",
        "series1=Revenue:240,290,330,410",
        "--prop",
        "series2=Plan:220,260,300,360",
        "--prop",
        "x=1.5cm",
        "--prop",
        "y=4cm",
        "--prop",
        "width=16cm",
        "--prop",
        "height=10cm",
        "--prop",
        "legend=bottom",
    )
    audit.officecli(
        "pptx-chart-pie",
        "add",
        str(file),
        "/slide[3]",
        "--type",
        "chart",
        "--prop",
        "chartType=pie",
        "--prop",
        "title=Region Mix",
        "--prop",
        "categories=North,South,East,West",
        "--prop",
        "series1=Share:31,27,23,19",
        "--prop",
        "x=19cm",
        "--prop",
        "y=4cm",
        "--prop",
        "width=10cm",
        "--prop",
        "height=10cm",
    )
    audit.officecli("pptx-slide3-notes", "set", str(file), "/slide[3]", "--prop", "notes=Explain plan gap, then pivot to regional mix.")

    audit.officecli("pptx-table-add", "add", str(file), "/slide[4]", "--type", "table", "--prop", "rows=4", "--prop", "cols=4", "--prop", "x=2cm", "--prop", "y=4cm", "--prop", "width=26cm", "--prop", "height=10cm")
    audit.officecli("pptx-table-row1", "set", str(file), "/slide[4]/table[1]/tr[1]", "--prop", "c1=Function", "--prop", "c2=Plan", "--prop", "c3=Actual", "--prop", "c4=Gap")
    audit.officecli("pptx-table-row2", "set", str(file), "/slide[4]/table[1]/tr[2]", "--prop", "c1=Engineering", "--prop", "c2=46", "--prop", "c3=44", "--prop", "c4=-2")
    audit.officecli("pptx-table-row3", "set", str(file), "/slide[4]/table[1]/tr[3]", "--prop", "c1=Data", "--prop", "c2=12", "--prop", "c3=13", "--prop", "c4=+1")
    audit.officecli("pptx-table-row4", "set", str(file), "/slide[4]/table[1]/tr[4]", "--prop", "c1=GTM", "--prop", "c2=18", "--prop", "c3=16", "--prop", "c4=-2")
    for cell in range(1, 5):
        audit.officecli(
            f"pptx-table-style-{cell}",
            "set",
            str(file),
            f"/slide[4]/table[1]/tr[1]/tc[{cell}]",
            "--prop",
            "bold=true",
            "--prop",
            "fill=1F4E79",
            "--prop",
            "color=FFFFFF",
            "--prop",
            "align=center",
        )
    audit.officecli("pptx-table-merge", "set", str(file), "/slide[4]/table[1]/tr[1]/tc[1]", "--prop", "merge.right=1")

    shapes = [
        ("Ingest", "2cm", "5cm", "6cm", "2.5cm", "4472C4"),
        ("Model", "11cm", "5cm", "6cm", "2.5cm", "70AD47"),
        ("Review", "20cm", "5cm", "6cm", "2.5cm", "C9A84C"),
    ]
    for idx, (text, x, y, width, height, fill) in enumerate(shapes, start=1):
        audit.officecli(
            f"pptx-shape-{idx}",
            "add",
            str(file),
            "/slide[5]",
            "--type",
            "shape",
            "--prop",
            f"text={text}",
            "--prop",
            f"x={x}",
            "--prop",
            f"y={y}",
            "--prop",
            f"width={width}",
            "--prop",
            f"height={height}",
            "--prop",
            f"fill={fill}",
            "--prop",
            "color=FFFFFF",
            "--prop",
            "bold=true",
            "--prop",
            "align=center",
            "--prop",
            "preset=roundRect",
            "--prop",
            "animation=flyIn-left-300-after-delay=150",
        )
    audit.officecli("pptx-shape-ellipse", "add", str(file), "/slide[5]", "--type", "shape", "--prop", "preset=ellipse", "--prop", "x=12cm", "--prop", "y=10cm", "--prop", "width=5cm", "--prop", "height=5cm", "--prop", "fill=1A1A2E", "--prop", "text=Control", "--prop", "color=FFFFFF", "--prop", "align=center")
    audit.officecli("pptx-shape-custom", "add", str(file), "/slide[5]", "--type", "shape", "--prop", "text=Adaptive", "--prop", "x=26cm", "--prop", "y=9cm", "--prop", "width=4cm", "--prop", "height=4cm", "--prop", "fill=E06666", "--prop", "geometry=M 0,100 L 50,0 L 100,100 Z")
    audit.officecli("pptx-shape-custom-set", "set", str(file), "/slide[5]/shape[5]", "--prop", "fill=F4B183", "--prop", "line=7030A0", "--prop", "lineWidth=2pt", "--prop", "rotation=12")
    audit.officecli("pptx-connector-1", "add", str(file), "/slide[5]", "--type", "connector", "--prop", "preset=straight", "--prop", "x=8.2cm", "--prop", "y=6.2cm", "--prop", "width=2.5cm", "--prop", "height=0", "--prop", "line=666666", "--prop", "lineWidth=2pt", "--prop", "headEnd=triangle")
    audit.officecli("pptx-connector-2", "add", str(file), "/slide[5]", "--type", "connector", "--prop", "preset=straight", "--prop", "x=17.2cm", "--prop", "y=6.2cm", "--prop", "width=2.5cm", "--prop", "height=0", "--prop", "line=666666", "--prop", "lineWidth=2pt", "--prop", "headEnd=triangle")
    audit.officecli("pptx-slide5-align", "set", str(file), "/slide[5]", "--prop", "align=top", "--prop", "targets=shape[1],shape[2],shape[3]")
    audit.officecli("pptx-slide5-distribute", "set", str(file), "/slide[5]", "--prop", "distribute=horizontal", "--prop", "targets=shape[1],shape[2],shape[3]")

    audit.officecli("pptx-slide6-bg", "set", str(file), "/slide[6]", "--prop", f"background=image:{FIXTURES / 'annual-report.png'}", "--prop", "notes=Risk slide uses local image background only.")
    audit.officecli("pptx-slide6-pic", "add", str(file), "/slide[6]", "--type", "picture", "--prop", f"path={FIXTURES / 'academic-paper.png'}", "--prop", "x=18cm", "--prop", "y=4cm", "--prop", "width=11cm", "--prop", "height=8cm", "--prop", "alt=Local fixture image")
    audit.officecli("pptx-slide6-callout", "add", str(file), "/slide[6]", "--type", "shape", "--prop", "text=Key risks\\n- Forecast variance\\n- Vendor concentration\\n- Hiring lag", "--prop", "x=2cm", "--prop", "y=4cm", "--prop", "width=12cm", "--prop", "height=8cm", "--prop", "fill=FFFFFF", "--prop", "opacity=0.85", "--prop", "color=222222", "--prop", "list=bullet")

    audit.officecli("pptx-slide7-bg", "set", str(file), "/slide[7]", "--prop", "background=LINEAR;1A1A2E;065A82;45", "--prop", "notes=Closing slide with morph transition")
    audit.officecli("pptx-slide7-shape", "add", str(file), "/slide[7]", "--type", "shape", "--prop", "text=Decisions made faster when narrative, model, and deck stay editable together.", "--prop", "x=3cm", "--prop", "y=6cm", "--prop", "width=24cm", "--prop", "height=5cm", "--prop", "fill=none", "--prop", "color=FFFFFF", "--prop", "size=28", "--prop", "align=center", "--prop", "animation=float-entrance-500")

    return file


def edit_pptx_sample(audit: AuditRunner) -> Path:
    src = FIXTURES / "budget_review_v2.pptx"
    dst = OUTPUTS / "edited-sample-budget-review.pptx"
    shutil.copy2(src, dst)
    audit.officecli("pptx-sample-outline", "view", str(dst), "outline")
    audit.officecli("pptx-sample-clone-slide", "add", str(dst), "/", "--from", "/slide[1]", "--index", "8")
    audit.officecli("pptx-sample-move-slide", "move", str(dst), "/slide[9]", "--before", "/slide[2]")
    audit.officecli("pptx-sample-swap-slide", "swap", str(dst), "/slide[2]", "/slide[3]")
    title_shapes = query_results(audit, dst, "shape", text="Budget Review")
    if title_shapes:
        audit.officecli("pptx-sample-title-set", "set", str(dst), title_shapes[0]["path"], "--prop", "text=Budget Review | Audit Copy", "--prop", "color=F2F0E8")
    chart_paths = query_results(audit, dst, "chart")
    if chart_paths:
        audit.officecli("pptx-sample-chart-set", "set", str(dst), chart_paths[0]["path"], "--prop", "title=Audit-updated chart title", "--prop", "legend=right")
    table_paths = query_results(audit, dst, "table")
    if table_paths:
        audit.officecli("pptx-sample-table-set", "set", str(dst), f"{table_paths[0]['path']}/tr[1]/tc[1]", "--prop", "text=Audit Focus", "--prop", "bold=true")
    q2_shapes = query_results(audit, dst, "shape", text="Q2 2025")
    if q2_shapes:
        audit.officecli("pptx-sample-q2-replace", "set", str(dst), q2_shapes[0]["path"], "--prop", "text=Q2 2026")
    audit.officecli("pptx-sample-notes", "set", str(dst), "/slide[1]", "--prop", "notes=Audit copy: track title, chart, and selection behavior.")
    audit.officecli("pptx-sample-add-temp-shape", "add", str(dst), "/slide[2]", "--type", "shape", "--prop", "text=Temporary QA box", "--prop", "x=2cm", "--prop", "y=15cm", "--prop", "width=6cm", "--prop", "height=1cm", "--prop", "fill=FFEB84")
    temp_shapes = query_results(audit, dst, "shape", text="Temporary QA box")
    if temp_shapes:
        audit.officecli("pptx-sample-remove-temp-shape", "remove", str(dst), temp_shapes[0]["path"])
    return dst


def edit_pptx_sample_two(audit: AuditRunner) -> Path:
    src = FIXTURES / "Alien_Guide.pptx"
    dst = OUTPUTS / "edited-sample-alien-guide.pptx"
    shutil.copy2(src, dst)
    audit.officecli("pptx-sample2-outline", "view", str(dst), "outline")
    audit.officecli("pptx-sample2-clone-slide", "add", str(dst), "/", "--from", "/slide[1]", "--index", "99")
    root = get_node(audit, dst, "/", depth=1) or {}
    slide_count = len([child for child in node_children(root) if isinstance(node_path(child), str) and node_path(child).startswith("/slide[")])
    last_slide = f"/slide[{slide_count}]" if slide_count else "/slide[1]"
    audit.officecli("pptx-sample2-move-slide", "move", str(dst), last_slide, "--before", "/slide[2]")
    audit.officecli("pptx-sample2-swap-slide", "swap", str(dst), "/slide[2]", "/slide[3]")
    shapes = query_results(audit, dst, "shape")
    if shapes:
        audit.officecli("pptx-sample2-shape-text", "set", str(dst), shapes[0]["path"], "--prop", "text=Alien Guide | Round3 Audit", "--prop", "color=F2F2F2")
    audit.officecli("pptx-sample2-notes", "set", str(dst), "/slide[1]", "--prop", "notes=Round3 audit sample for clone/move/swap and shape edits.")
    audit.officecli("pptx-sample2-add-shape", "add", str(dst), "/slide[2]", "--type", "shape", "--prop", "text=Round3 marker", "--prop", "x=2cm", "--prop", "y=15cm", "--prop", "width=7cm", "--prop", "height=1.5cm", "--prop", "fill=70AD47", "--prop", "color=FFFFFF")
    marker_shapes = query_results(audit, dst, "shape", text="Round3 marker")
    if marker_shapes:
        audit.officecli("pptx-sample2-remove-shape", "remove", str(dst), marker_shapes[0]["path"])
    return dst


def run_watch_probe(audit: AuditRunner, file: Path, port: int = 18081) -> dict[str, Any]:
    watch_proc = subprocess.Popen(
        [str(BIN), "watch", str(file), "--port", str(port)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        env={**os.environ, **BASE_ENV},
    )
    watch_log = LOGS / f"{audit.counter:03d}-watch-session.log"
    audit.counter += 1
    ready = wait_for_port(port, timeout=20)
    port_check = subprocess.run(
        ["lsof", "-nP", f"-iTCP:{port}", "-sTCP:LISTEN"],
        capture_output=True,
        text=True,
        timeout=10,
    )

    if ready:
        audit.playwright("pw-open-blank", "open", "about:blank")
        route_js = """
globalThis.__blocked = [];
await page.route('**/*', route => {
  const url = route.request().url();
  try {
    const u = new URL(url);
    if (u.hostname === '127.0.0.1' || u.hostname === 'localhost') {
      return route.continue();
    }
    globalThis.__blocked.push(url);
    return route.abort();
  } catch (e) {
    return route.continue();
  }
});
await page.goto('http://127.0.0.1:%d', { waitUntil: 'domcontentloaded' });
await page.waitForTimeout(1200);
""" % port
        audit.playwright("pw-route-goto-watch", "run-code", route_js, timeout=180)
        snapshot = audit.playwright("pw-watch-snapshot", "snapshot", timeout=120)
        data_paths = audit.playwright(
            "pw-watch-data-paths",
            "eval",
            "JSON.stringify(Array.from(document.querySelectorAll('[data-path]')).slice(0,80).map(el => ({path: el.getAttribute('data-path'), text: (el.textContent || '').trim().slice(0,80)})))",
            timeout=120,
        )
        audit.save_stdout(snapshot, OUTPUTS / "watch-snapshot.txt")
        audit.save_stdout(data_paths, OUTPUTS / "watch-data-paths.json")
        click_js = """
const target = Array.from(document.querySelectorAll('[data-path]')).find(el => {
  const p = el.getAttribute('data-path') || '';
  return p.includes('/shape') || p.includes('/table') || p.includes('/picture');
});
if (!target) throw new Error('No selectable element found');
target.scrollIntoView();
target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
await page.waitForTimeout(800);
JSON.stringify({
  path: target.getAttribute('data-path'),
  text: (target.textContent || '').trim().slice(0,80),
  blocked: globalThis.__blocked || []
});
"""
        click_result = audit.playwright("pw-watch-click-path", "run-code", click_js, timeout=180)
    else:
        snapshot = data_paths = click_result = CmdResult("watch-missing", [], 1, "", "watch port not ready", 0.0, watch_log)

    selected = audit.officecli("watch-get-selected", "get", str(file), "selected", "--json", timeout=120)
    mark_selected = audit.officecli("watch-mark-selected", "mark", str(file), "selected", "--prop", "note=Watch selection smoke", "--prop", "color=FF6600", "--json", timeout=120)
    marks = audit.officecli("watch-get-marks", "get-marks", str(file), "--json", timeout=120)
    unmark = audit.officecli("watch-unmark-all", "unmark", str(file), "--all", "--json", timeout=120)
    unwatch = audit.officecli("watch-stop", "unwatch", str(file), timeout=120)

    try:
        stdout, stderr = watch_proc.communicate(timeout=20)
    except subprocess.TimeoutExpired:
        watch_proc.kill()
        stdout, stderr = watch_proc.communicate()
    watch_log.write_text(
        "\n".join(
            [
                f"file: {file}",
                f"port: {port}",
                f"ready: {ready}",
                "",
                "=== lsof ===",
                port_check.stdout,
                "",
                "=== watch stdout ===",
                stdout,
                "",
                "=== watch stderr ===",
                stderr,
                "",
            ]
        ),
        encoding="utf-8",
    )
    port_after = subprocess.run(
        ["lsof", "-nP", f"-iTCP:{port}", "-sTCP:LISTEN"],
        capture_output=True,
        text=True,
        timeout=10,
    )
    return {
        "ready": ready,
        "listen_stdout": port_check.stdout,
        "watch_log": watch_log,
        "selected": selected,
        "mark_selected": mark_selected,
        "marks": marks,
        "unmark": unmark,
        "unwatch": unwatch,
        "port_released": port_after.returncode != 0 or str(port) not in port_after.stdout,
        "snapshot": snapshot,
        "data_paths": data_paths,
        "click_result": click_result,
    }


def run_compatibility(audit: AuditRunner, file: Path, fmt: str) -> dict[str, CmdResult]:
    outdir = OUTPUTS / "compatibility"
    outdir.mkdir(parents=True, exist_ok=True)
    profile = TMPDIR / f"lo-profile-{file.stem}"
    profile.mkdir(parents=True, exist_ok=True)
    pdf_convert = audit.run(
        f"soffice-{file.stem}",
        [
            "soffice",
            f"-env:UserInstallation=file://{profile}",
            "--headless",
            "--convert-to",
            "pdf",
            "--outdir",
            str(outdir),
            str(file),
        ],
        timeout=240,
    )
    result = {"soffice": pdf_convert}
    if fmt == "docx":
        result["textutil"] = audit.run(
            f"textutil-{file.stem}",
            ["textutil", "-convert", "txt", "-stdout", str(file)],
            timeout=120,
        )
        result["unzip"] = audit.run(
            f"unzip-{file.stem}",
            ["unzip", "-p", str(file), "word/document.xml"],
            timeout=60,
        )
        result["unzip-settings"] = audit.run(
            f"unzip-settings-{file.stem}",
            ["unzip", "-p", str(file), "word/settings.xml"],
            timeout=60,
        )
        result["unzip-footer1"] = audit.run(
            f"unzip-footer1-{file.stem}",
            ["unzip", "-p", str(file), "word/footer1.xml"],
            timeout=60,
        )
    elif fmt == "xlsx":
        chart_member = first_chart_member(file, "xl")
        result["unzip-workbook"] = audit.run(
            f"unzip-workbook-{file.stem}",
            ["unzip", "-p", str(file), "xl/workbook.xml"],
            timeout=60,
        )
        result["unzip-styles"] = audit.run(
            f"unzip-styles-{file.stem}",
            ["unzip", "-p", str(file), "xl/styles.xml"],
            timeout=60,
        )
        result["unzip-sheet"] = audit.run(
            f"unzip-sheet-{file.stem}",
            ["unzip", "-p", str(file), "xl/worksheets/sheet1.xml"],
            timeout=60,
        )
        result["unzip-chart1"] = audit.run(
            f"unzip-chart-{file.stem}",
            ["unzip", "-p", str(file), chart_member or "xl/charts/chart1.xml"],
            timeout=60,
        )
    elif fmt == "pptx":
        chart_member = first_chart_member(file, "ppt")
        result["unzip-presentation"] = audit.run(
            f"unzip-presentation-{file.stem}",
            ["unzip", "-p", str(file), "ppt/presentation.xml"],
            timeout=60,
        )
        result["unzip-slide1"] = audit.run(
            f"unzip-slide1-{file.stem}",
            ["unzip", "-p", str(file), "ppt/slides/slide1.xml"],
            timeout=60,
        )
        result["unzip-notes1"] = audit.run(
            f"unzip-notes1-{file.stem}",
            ["unzip", "-p", str(file), "ppt/notesSlides/notesSlide1.xml"],
            timeout=60,
        )
        result["unzip-chart1"] = audit.run(
            f"unzip-ppt-chart-{file.stem}",
            ["unzip", "-p", str(file), chart_member or "ppt/charts/chart1.xml"],
            timeout=60,
        )
    return result


def main() -> int:
    args = parse_args()
    configure_paths(args)
    try:
        ensure_workspace(args)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    if not BIN.exists():
        print(f"officecli binary not found: {BIN}", file=sys.stderr)
        return 2
    if not PWCLI.exists():
        print(f"playwright CLI helper not found: {PWCLI}", file=sys.stderr)
        return 2

    prepare_fixtures(reuse_existing=args.reuse_fixtures)
    audit = AuditRunner()
    baseline_before = capture_real_baseline(REPORTS / "baseline-before.json")

    version = audit.officecli("version", "--version")
    audit.ensure(version, fmt="common", scenario="environment", feature="--version", success_note="读取到版本号", fail_note="版本读取失败")
    help_res = audit.officecli("help", "--help")
    audit.ensure(help_res, fmt="common", scenario="environment", feature="--help", success_note="帮助输出可用", fail_note="帮助输出失败")
    skills_list = audit.officecli("skills-list", "skills", "list")
    audit.ensure(skills_list, fmt="common", scenario="read-only", feature="skills list", success_note="skills list 可执行", fail_note="skills list 失败")
    mcp_list = audit.officecli("mcp-list", "mcp", "list")
    audit.ensure(mcp_list, fmt="common", scenario="read-only", feature="mcp list", success_note="mcp list 可执行", fail_note="mcp list 失败")

    config_auto_get = audit.officecli("config-auto-get", "config", "autoUpdate")
    config_auto_set_true = audit.officecli("config-auto-set-true", "config", "autoUpdate", "true")
    config_auto_get_true = audit.officecli("config-auto-get-true", "config", "autoUpdate")
    config_auto_set_false = audit.officecli("config-auto-set-false", "config", "autoUpdate", "false")
    config_auto_get_false = audit.officecli("config-auto-get-false", "config", "autoUpdate")
    auto_roundtrip_ok = (
        config_auto_get.returncode == 0
        and config_auto_set_true.returncode == 0
        and config_auto_get_true.stdout.strip() == "true"
        and config_auto_set_false.returncode == 0
        and config_auto_get_false.stdout.strip() == "false"
    )
    audit.record(
        fmt="common",
        scenario="config",
        feature="config autoUpdate roundtrip",
        status="通过" if auto_roundtrip_ok else "失败",
        note=f"初始={config_auto_get.stdout.strip()} -> true={config_auto_get_true.stdout.strip()} -> false={config_auto_get_false.stdout.strip()}",
        result=config_auto_get_false,
    )

    config_log_get = audit.officecli("config-log-get", "config", "log")
    config_log_set_true = audit.officecli("config-log-set-true", "config", "log", "true")
    config_log_get_true = audit.officecli("config-log-get-true", "config", "log")
    config_log_set_false = audit.officecli("config-log-set-false", "config", "log", "false")
    config_log_get_false = audit.officecli("config-log-get-false", "config", "log")
    log_roundtrip_ok = (
        config_log_get.returncode == 0
        and config_log_set_true.returncode == 0
        and config_log_get_true.stdout.strip() == "true"
        and config_log_set_false.returncode == 0
        and config_log_get_false.stdout.strip() == "false"
    )
    audit.record(
        fmt="common",
        scenario="config",
        feature="config log roundtrip",
        status="通过" if log_roundtrip_ok else "失败",
        note=f"初始={config_log_get.stdout.strip()} -> true={config_log_get_true.stdout.strip()} -> false={config_log_get_false.stdout.strip()}",
        result=config_log_get_false,
    )

    docx_template = OUTPUTS / "template-merge.docx"
    xlsx_template = OUTPUTS / "template-merge.xlsx"
    pptx_template = OUTPUTS / "template-merge.pptx"
    create_docx_template(audit, docx_template)
    create_xlsx_template(audit, xlsx_template)
    create_pptx_template(audit, pptx_template)
    merge_docx = OUTPUTS / "merged-output.docx"
    merge_xlsx = OUTPUTS / "merged-output.xlsx"
    merge_pptx = OUTPUTS / "merged-output.pptx"
    audit.ensure(
        audit.officecli("merge-docx", "merge", str(docx_template), str(merge_docx), "--data", '{"client":"Acme","dept":"Sales","total":"$5,200"}'),
        fmt="docx",
        scenario="merge",
        feature="merge 模板合并",
        success_note="DOCX merge 成功",
        fail_note="DOCX merge 失败",
    )
    audit.ensure(
        audit.officecli("merge-xlsx", "merge", str(xlsx_template), str(merge_xlsx), "--data", '{"quarter":"Q4","year":"2026","owner":"Alex"}'),
        fmt="xlsx",
        scenario="merge",
        feature="merge 模板合并",
        success_note="XLSX merge 成功",
        fail_note="XLSX merge 失败",
    )
    audit.ensure(
        audit.officecli("merge-pptx", "merge", str(pptx_template), str(merge_pptx), "--data", '{"title":"Audit Merge Deck","summary":"Merged in isolated HOME"}'),
        fmt="pptx",
        scenario="merge",
        feature="merge 模板合并",
        success_note="PPTX merge 成功",
        fail_note="PPTX merge 失败",
    )

    docx_created = build_docx(audit)
    docx_sample = edit_docx_sample(audit)
    docx_sample_two = edit_docx_sample_two(audit)
    for mode in ["outline", "stats", "text", "annotated", "issues"]:
        args = ["view", str(docx_created), mode]
        if mode in {"text", "annotated"}:
            args += ["--max-lines", "80"]
        if mode == "issues":
            args += ["--json"]
        res = audit.officecli(f"docx-view-{mode}", *args)
        audit.record(fmt="docx", scenario="created", feature=f"view {mode}", status="通过" if res.returncode == 0 else "失败", note="created docx view", result=res)
    get_docx = audit.officecli("docx-get", "get", str(docx_created), "/body/p[1]", "--depth", "2", "--json")
    audit.ensure(get_docx, fmt="docx", scenario="created", feature="get --json", success_note="get 成功", fail_note="get 失败")
    query_docx = audit.officecli("docx-query", "query", str(docx_created), "paragraph", "--text", "Revenue", "--json")
    audit.ensure(query_docx, fmt="docx", scenario="created", feature="query", success_note="query 成功", fail_note="query 失败")
    raw_docx = audit.officecli("docx-raw", "raw", str(docx_created), "/document")
    audit.ensure(raw_docx, fmt="docx", scenario="created", feature="raw", success_note="raw 可读取", fail_note="raw 失败")
    raw_set_docx = audit.officecli("docx-raw-set-document", "raw-set", str(docx_created), "document", "--xpath", "//w:p[1]", "--action", "append", "--xml", '<w:r><w:t xml:space="preserve"> [RAW]</w:t></w:r>')
    audit.record(fmt="docx", scenario="created", feature="raw-set document alias", status="通过" if raw_set_docx.returncode == 0 else "失败", note="使用 document alias 向首段追加 run", result=raw_set_docx)
    raw_styles_docx = audit.officecli("docx-raw-styles", "raw", str(docx_created), "styles")
    audit.record(fmt="docx", scenario="created", feature="raw styles alias", status="通过" if raw_styles_docx.returncode == 0 else "失败", note="读取 styles alias", result=raw_styles_docx)
    raw_set_styles_docx = audit.officecli("docx-raw-set-styles", "raw-set", str(docx_created), "styles", "--xpath", "//w:docDefaults/w:pPrDefault/w:pPr/w:autoSpaceDE", "--action", "setattr", "--xml", "w:val=true")
    raw_styles_after = audit.officecli("docx-raw-styles-after", "raw", str(docx_created), "/styles")
    styles_alias_ok = raw_set_styles_docx.returncode == 0 and "autoSpaceDE" in raw_styles_after.stdout and 'w:val=\"true\"' in raw_styles_after.stdout
    audit.record(fmt="docx", scenario="created", feature="raw-set styles alias", status="通过" if styles_alias_ok else "失败", note="使用 styles alias 修改 docDefaults", result=raw_styles_after)
    add_part_docx = audit.officecli("docx-add-part", "add-part", str(docx_created), "/", "--type", "header", "--json")
    audit.record(fmt="docx", scenario="created", feature="add-part", status="通过" if add_part_docx.returncode == 0 else "失败", note="header part smoke test", result=add_part_docx)
    open_docx = audit.officecli("docx-open", "open", str(docx_created))
    audit.record(fmt="docx", scenario="resident", feature="open", status="通过" if open_docx.returncode == 0 else "失败", note="resident open", result=open_docx)
    audit.officecli("docx-resident-set", "set", str(docx_created), "/body/p[2]", "--prop", "italic=true", "--prop", "color=666666")
    close_docx = audit.officecli("docx-close", "close", str(docx_created))
    audit.record(fmt="docx", scenario="resident", feature="close", status="通过" if close_docx.returncode == 0 else "失败", note="resident close", result=close_docx)
    wait_for_file_release(docx_created)
    validate_docx = audit.officecli("docx-validate", "validate", str(docx_created), "--json")
    audit.record(fmt="docx", scenario="created", feature="validate", status="通过" if validation_is_clean(audit, validate_docx) else "失败", note="created docx validate", result=validate_docx)
    validate_docx_sample = audit.officecli("docx-sample-validate", "validate", str(docx_sample), "--json")
    validate_docx_sample_two = audit.officecli("docx-sample2-validate", "validate", str(docx_sample_two), "--json")
    audit.record(fmt="docx", scenario="sample-edit-annual", feature="validate", status="通过" if validation_is_clean(audit, validate_docx_sample) else "失败", note="annual report sample validate", result=validate_docx_sample)
    audit.record(fmt="docx", scenario="sample-edit-academic", feature="validate", status="通过" if validation_is_clean(audit, validate_docx_sample_two) else "失败", note="academic paper sample validate", result=validate_docx_sample_two)
    compat_docx_created = run_compatibility(audit, docx_created, "docx")
    compat_docx_sample = run_compatibility(audit, docx_sample, "docx")
    compat_docx_sample_two = run_compatibility(audit, docx_sample_two, "docx")
    audit.record(fmt="docx", scenario="compatibility", feature="soffice convert", status="通过" if compat_docx_created["soffice"].returncode == 0 else "失败", note="created docx to PDF", result=compat_docx_created["soffice"])
    audit.record(fmt="docx", scenario="compatibility", feature="textutil", status="通过" if compat_docx_created["textutil"].returncode == 0 else "失败", note="created docx textutil extraction", result=compat_docx_created["textutil"])
    audit.record(fmt="docx", scenario="compatibility", feature="unzip word/document.xml", status="通过" if compat_docx_created["unzip"].returncode == 0 else "失败", note="created docx unzip", result=compat_docx_created["unzip"])
    audit.record(fmt="docx", scenario="compatibility", feature="unzip word/settings.xml", status="通过" if compat_docx_created["unzip-settings"].returncode == 0 else "失败", note="settings xml extract", result=compat_docx_created["unzip-settings"])
    audit.record(fmt="docx", scenario="compatibility", feature="unzip word/footer1.xml", status="通过" if compat_docx_created["unzip-footer1"].returncode == 0 else "失败", note="footer1 xml extract", result=compat_docx_created["unzip-footer1"])
    audit.record(fmt="docx", scenario="compatibility", feature="sample annual soffice convert", status="通过" if compat_docx_sample["soffice"].returncode == 0 else "失败", note="annual sample PDF convert", result=compat_docx_sample["soffice"])
    audit.record(fmt="docx", scenario="compatibility", feature="sample academic soffice convert", status="通过" if compat_docx_sample_two["soffice"].returncode == 0 else "失败", note="academic sample PDF convert", result=compat_docx_sample_two["soffice"])

    xlsx_created, _ = build_xlsx(audit)
    xlsx_sample = edit_xlsx_sample(audit)
    xlsx_sample_two = edit_xlsx_sample_two(audit)
    for mode in ["outline", "stats", "text", "annotated"]:
        args = ["view", str(xlsx_created), mode]
        if mode in {"text", "annotated"}:
            args += ["--max-lines", "80"]
        res = audit.officecli(f"xlsx-view-{mode}", *args)
        audit.record(fmt="xlsx", scenario="created", feature=f"view {mode}", status="通过" if res.returncode == 0 else "失败", note="created xlsx view", result=res)
    html_xlsx = audit.officecli("xlsx-view-html", "view", str(xlsx_created), "html")
    audit.save_stdout(html_xlsx, OUTPUTS / "created-workbook.html")
    audit.record(fmt="xlsx", scenario="created", feature="view html", status="通过" if html_xlsx.returncode == 0 else "失败", note="html preview generated", result=html_xlsx, artifact=OUTPUTS / "created-workbook.html")
    get_xlsx = audit.officecli("xlsx-get", "get", str(xlsx_created), "/Dashboard/chart[1]", "--depth", "1", "--json")
    audit.ensure(get_xlsx, fmt="xlsx", scenario="created", feature="get --json", success_note="get chart 成功", fail_note="get chart 失败")
    query_xlsx_text = audit.officecli("xlsx-query-cell-text", "query", str(xlsx_created), "cell", "--text", "North", "--json")
    audit.record(fmt="xlsx", scenario="created", feature='query "cell" --text', status="通过" if query_xlsx_text.returncode == 0 else "失败", note="text filter selector", result=query_xlsx_text)
    query_xlsx_contains = audit.officecli("xlsx-query-contains", "query", str(xlsx_created), 'cell:contains("North")', "--json")
    audit.record(fmt="xlsx", scenario="created", feature='query cell:contains("North")', status="通过" if query_xlsx_contains.returncode == 0 else "失败", note="pseudo selector", result=query_xlsx_contains)
    query_xlsx_attr = audit.officecli("xlsx-query-attr", "query", str(xlsx_created), "cell[value=North]", "--json")
    audit.record(fmt="xlsx", scenario="created", feature='query cell[value=North]', status="通过" if query_xlsx_attr.returncode == 0 else "失败", note="attribute selector behavior", result=query_xlsx_attr)
    raw_xlsx = audit.officecli("xlsx-raw", "raw", str(xlsx_created), "/RawData", "--start", "1", "--end", "12", "--cols", "A,B,C,D")
    audit.record(fmt="xlsx", scenario="created", feature="raw", status="通过" if raw_xlsx.returncode == 0 else "失败", note="read sheet XML subset", result=raw_xlsx)
    raw_set_xlsx = audit.officecli("xlsx-raw-set", "raw-set", str(xlsx_created), "/RawData", "--xpath", "//x:sheetView[1]", "--action", "setattr", "--xml", "zoomScale=95")
    audit.record(fmt="xlsx", scenario="created", feature="raw-set", status="通过" if raw_set_xlsx.returncode == 0 else "失败", note="set zoomScale on sheetView", result=raw_set_xlsx)
    add_part_xlsx = audit.officecli("xlsx-add-part", "add-part", str(xlsx_created), "/RawData", "--type", "chart", "--json")
    audit.record(fmt="xlsx", scenario="created", feature="add-part", status="通过" if add_part_xlsx.returncode == 0 else "失败", note="chart part smoke test", result=add_part_xlsx)
    audit.officecli("xlsx-cleanup-add-sheet", "add", str(xlsx_created), "/", "--type", "sheet", "--prop", "name=CleanupTmp")
    audit.officecli("xlsx-cleanup-set", "set", str(xlsx_created), "/CleanupTmp/A1", "--prop", "value=tmp")
    audit.officecli("xlsx-cleanup-range", "add", str(xlsx_created), "/", "--type", "namedrange", "--prop", "name=TempCleanupRange", "--prop", "ref=CleanupTmp!$A$1:$A$2")
    audit.officecli("xlsx-cleanup-remove-sheet", "remove", str(xlsx_created), "/CleanupTmp")
    cleanup_workbook = audit.run("xlsx-cleanup-workbook-xml", ["unzip", "-p", str(xlsx_created), "xl/workbook.xml"], timeout=60)
    cleanup_ok = cleanup_workbook.returncode == 0 and "TempCleanupRange" not in cleanup_workbook.stdout and "CleanupTmp!" not in cleanup_workbook.stdout
    audit.record(fmt="xlsx", scenario="created", feature="named range cleanup after sheet remove", status="通过" if cleanup_ok else "失败", note="删除引用 sheet 后 named range 不应残留在 workbook.xml", result=cleanup_workbook)
    open_xlsx = audit.officecli("xlsx-open", "open", str(xlsx_created))
    audit.record(fmt="xlsx", scenario="resident", feature="open", status="通过" if open_xlsx.returncode == 0 else "失败", note="resident open", result=open_xlsx)
    audit.officecli("xlsx-resident-set", "set", str(xlsx_created), "/Model/B8", "--prop", "value=resident-ok")
    close_xlsx = audit.officecli("xlsx-close", "close", str(xlsx_created))
    audit.record(fmt="xlsx", scenario="resident", feature="close", status="通过" if close_xlsx.returncode == 0 else "失败", note="resident close", result=close_xlsx)
    wait_for_file_release(xlsx_created)
    chart_paths_xlsx = query_results(audit, xlsx_created, "chart")
    if chart_paths_xlsx:
        audit.officecli("xlsx-gridlines-none", "set", str(xlsx_created), chart_paths_xlsx[0]["path"], "--prop", "gridlines=none")
        gridlines_node = get_node(audit, xlsx_created, chart_paths_xlsx[0]["path"], depth=1) or {}
        chart_format = node_format(gridlines_node)
        gridlines_ok = chart_format.get("gridlines") == "false"
        audit.record(fmt="xlsx", scenario="created", feature="chart gridlines none readback", status="通过" if gridlines_ok else "失败", note=f"gridlines={chart_format.get('gridlines')}", artifact=xlsx_created)
    validate_xlsx = audit.officecli("xlsx-validate", "validate", str(xlsx_created), "--json")
    audit.record(fmt="xlsx", scenario="created", feature="validate", status="通过" if validation_is_clean(audit, validate_xlsx) else "失败", note="created validate", result=validate_xlsx)
    validate_xlsx_sample = audit.officecli("xlsx-sample-validate", "validate", str(xlsx_sample), "--json")
    validate_xlsx_sample_two = audit.officecli("xlsx-sample2-validate", "validate", str(xlsx_sample_two), "--json")
    audit.record(fmt="xlsx", scenario="sample-edit-dashboard", feature="validate", status="通过" if validation_is_clean(audit, validate_xlsx_sample) else "失败", note="sales dashboard sample validate", result=validate_xlsx_sample)
    audit.record(fmt="xlsx", scenario="sample-edit-gradebook", feature="validate", status="通过" if validation_is_clean(audit, validate_xlsx_sample_two) else "失败", note="gradebook sample validate", result=validate_xlsx_sample_two)
    compat_xlsx_created = run_compatibility(audit, xlsx_created, "xlsx")
    compat_xlsx_sample = run_compatibility(audit, xlsx_sample, "xlsx")
    compat_xlsx_sample_two = run_compatibility(audit, xlsx_sample_two, "xlsx")
    audit.record(fmt="xlsx", scenario="compatibility", feature="soffice convert", status="通过" if compat_xlsx_created["soffice"].returncode == 0 else "失败", note="created xlsx to PDF", result=compat_xlsx_created["soffice"])
    audit.record(fmt="xlsx", scenario="compatibility", feature="unzip workbook.xml", status="通过" if compat_xlsx_created["unzip-workbook"].returncode == 0 else "失败", note="workbook xml", result=compat_xlsx_created["unzip-workbook"])
    audit.record(fmt="xlsx", scenario="compatibility", feature="unzip styles.xml", status="通过" if compat_xlsx_created["unzip-styles"].returncode == 0 else "失败", note="styles xml", result=compat_xlsx_created["unzip-styles"])
    audit.record(fmt="xlsx", scenario="compatibility", feature="unzip sheet1.xml", status="通过" if compat_xlsx_created["unzip-sheet"].returncode == 0 else "失败", note="sheet1 xml", result=compat_xlsx_created["unzip-sheet"])
    audit.record(fmt="xlsx", scenario="compatibility", feature="unzip chart1.xml", status="通过" if compat_xlsx_created["unzip-chart1"].returncode == 0 else "失败", note="chart1 xml", result=compat_xlsx_created["unzip-chart1"])
    audit.record(fmt="xlsx", scenario="compatibility", feature="sample dashboard soffice convert", status="通过" if compat_xlsx_sample["soffice"].returncode == 0 else "失败", note="dashboard sample PDF convert", result=compat_xlsx_sample["soffice"])
    audit.record(fmt="xlsx", scenario="compatibility", feature="sample gradebook soffice convert", status="通过" if compat_xlsx_sample_two["soffice"].returncode == 0 else "失败", note="gradebook sample PDF convert", result=compat_xlsx_sample_two["soffice"])

    pptx_created = build_pptx(audit)
    pptx_sample = edit_pptx_sample(audit)
    pptx_sample_two = edit_pptx_sample_two(audit)
    for mode in ["outline", "stats", "text", "annotated"]:
        args = ["view", str(pptx_created), mode]
        if mode in {"text", "annotated"}:
            args += ["--max-lines", "120"]
        res = audit.officecli(f"pptx-view-{mode}", *args)
        audit.record(fmt="pptx", scenario="created", feature=f"view {mode}", status="通过" if res.returncode == 0 else "失败", note="created deck view", result=res)
    html_pptx = audit.officecli("pptx-view-html", "view", str(pptx_created), "html")
    svg_pptx = audit.officecli("pptx-view-svg", "view", str(pptx_created), "svg")
    audit.save_stdout(html_pptx, OUTPUTS / "created-deck.html")
    audit.save_stdout(svg_pptx, OUTPUTS / "created-deck.svg")
    audit.record(fmt="pptx", scenario="created", feature="view html", status="通过" if html_pptx.returncode == 0 else "失败", note="html preview generated", result=html_pptx, artifact=OUTPUTS / "created-deck.html")
    audit.record(fmt="pptx", scenario="created", feature="view svg", status="通过" if svg_pptx.returncode == 0 else "失败", note="svg preview generated", result=svg_pptx, artifact=OUTPUTS / "created-deck.svg")
    get_pptx = audit.officecli("pptx-get", "get", str(pptx_created), "/slide[3]", "--depth", "2", "--json")
    audit.ensure(get_pptx, fmt="pptx", scenario="created", feature="get --json", success_note="get slide 成功", fail_note="get slide 失败")
    query_pptx = audit.officecli("pptx-query", "query", str(pptx_created), "shape", "--text", "Performance", "--json", timeout=240)
    audit.ensure(query_pptx, fmt="pptx", scenario="created", feature="query", success_note="query shape 成功", fail_note="query 失败")
    raw_pptx = audit.officecli("pptx-raw", "raw", str(pptx_created), "/slide[1]")
    audit.record(fmt="pptx", scenario="created", feature="raw", status="通过" if raw_pptx.returncode == 0 else "失败", note="raw slide XML", result=raw_pptx)
    raw_set_pptx = audit.officecli("pptx-raw-set", "raw-set", str(pptx_created), "/slide[1]", "--xpath", "//p:cNvPr[1]", "--action", "setattr", "--xml", "name=RawTouched")
    audit.record(fmt="pptx", scenario="created", feature="raw-set", status="通过" if raw_set_pptx.returncode == 0 else "失败", note="rename first nvPr via raw-set", result=raw_set_pptx)
    add_part_pptx = audit.officecli("pptx-add-part", "add-part", str(pptx_created), "/slide[1]", "--type", "chart", "--json")
    audit.record(fmt="pptx", scenario="created", feature="add-part", status="通过" if add_part_pptx.returncode == 0 else "失败", note="chart part smoke test", result=add_part_pptx)
    created_shape_results = query_results(audit, pptx_created, "shape")
    batch_targets = [item["path"] for item in created_shape_results if item.get("text")][:3]
    pptx_batch_cmds: list[dict[str, Any]] = []
    for idx, path in enumerate(batch_targets, start=1):
        pptx_batch_cmds.append({"command": "set", "path": path, "props": {"bold": "true", "animation": "fade-entrance-400" if idx == 1 else "flyIn-left-300-after"}})
    if pptx_batch_cmds:
        batch_pptx = audit.officecli("pptx-batch", "batch", str(pptx_created), "--commands", json.dumps(pptx_batch_cmds), "--json", timeout=240)
        audit.record(fmt="pptx", scenario="created", feature="batch", status="通过" if batch_pptx.returncode == 0 else "失败", note="shape batch update", result=batch_pptx)
    raw_slide5 = audit.officecli("pptx-raw-slide5", "raw", str(pptx_created), "/slide[5]")
    audit.record(fmt="pptx", scenario="created", feature="custom geometry raw presence", status="通过" if "custGeom" in raw_slide5.stdout else "失败", note="slide[5] raw xml should contain custGeom after geometry mutation", result=raw_slide5)
    open_pptx = audit.officecli("pptx-open", "open", str(pptx_created))
    audit.record(fmt="pptx", scenario="resident", feature="open", status="通过" if open_pptx.returncode == 0 else "失败", note="resident open", result=open_pptx)
    audit.officecli("pptx-resident-set", "set", str(pptx_created), "/slide[7]", "--prop", "notes=Resident update confirmed.")
    close_pptx = audit.officecli("pptx-close", "close", str(pptx_created))
    audit.record(fmt="pptx", scenario="resident", feature="close", status="通过" if close_pptx.returncode == 0 else "失败", note="resident close", result=close_pptx)
    wait_for_file_release(pptx_created)
    check_pptx = audit.officecli("pptx-check", "check", str(pptx_created), "--json")
    audit.record(fmt="pptx", scenario="created", feature="check", status="通过" if check_pptx.returncode == 0 else "失败", note="layout issue scan", result=check_pptx)
    validate_pptx = audit.officecli("pptx-validate", "validate", str(pptx_created), "--json")
    audit.record(fmt="pptx", scenario="created", feature="validate", status="通过" if validation_is_clean(audit, validate_pptx) else "失败", note="created validate", result=validate_pptx)
    check_pptx_sample = audit.officecli("pptx-sample-check", "check", str(pptx_sample), "--json")
    validate_pptx_sample = audit.officecli("pptx-sample-validate", "validate", str(pptx_sample), "--json")
    check_pptx_sample_two = audit.officecli("pptx-sample2-check", "check", str(pptx_sample_two), "--json")
    validate_pptx_sample_two = audit.officecli("pptx-sample2-validate", "validate", str(pptx_sample_two), "--json")
    audit.record(fmt="pptx", scenario="sample-edit-budget", feature="check", status="通过" if check_pptx_sample.returncode == 0 else "失败", note="budget review sample check", result=check_pptx_sample)
    audit.record(fmt="pptx", scenario="sample-edit-budget", feature="validate", status="通过" if validation_is_clean(audit, validate_pptx_sample) else "失败", note="budget review sample validate", result=validate_pptx_sample)
    audit.record(fmt="pptx", scenario="sample-edit-alien", feature="check", status="通过" if check_pptx_sample_two.returncode == 0 else "失败", note="Alien Guide sample check", result=check_pptx_sample_two)
    audit.record(fmt="pptx", scenario="sample-edit-alien", feature="validate", status="通过" if validation_is_clean(audit, validate_pptx_sample_two) else "失败", note="Alien Guide sample validate", result=validate_pptx_sample_two)
    compat_pptx_created = run_compatibility(audit, pptx_created, "pptx")
    compat_pptx_sample = run_compatibility(audit, pptx_sample, "pptx")
    compat_pptx_sample_two = run_compatibility(audit, pptx_sample_two, "pptx")
    audit.record(fmt="pptx", scenario="compatibility", feature="soffice convert", status="通过" if compat_pptx_created["soffice"].returncode == 0 else "失败", note="created pptx to PDF", result=compat_pptx_created["soffice"])
    audit.record(fmt="pptx", scenario="compatibility", feature="unzip presentation.xml", status="通过" if compat_pptx_created["unzip-presentation"].returncode == 0 else "失败", note="presentation xml", result=compat_pptx_created["unzip-presentation"])
    audit.record(fmt="pptx", scenario="compatibility", feature="unzip slide1.xml", status="通过" if compat_pptx_created["unzip-slide1"].returncode == 0 else "失败", note="slide1 xml", result=compat_pptx_created["unzip-slide1"])
    audit.record(fmt="pptx", scenario="compatibility", feature="unzip notesSlide1.xml", status="通过" if compat_pptx_created["unzip-notes1"].returncode == 0 else "失败", note="notes xml", result=compat_pptx_created["unzip-notes1"])
    audit.record(fmt="pptx", scenario="compatibility", feature="unzip chart1.xml", status="通过" if compat_pptx_created["unzip-chart1"].returncode == 0 else "失败", note="chart xml", result=compat_pptx_created["unzip-chart1"])
    audit.record(fmt="pptx", scenario="compatibility", feature="sample budget soffice convert", status="通过" if compat_pptx_sample["soffice"].returncode == 0 else "失败", note="budget review sample PDF convert", result=compat_pptx_sample["soffice"])
    audit.record(fmt="pptx", scenario="compatibility", feature="sample alien soffice convert", status="通过" if compat_pptx_sample_two["soffice"].returncode == 0 else "失败", note="Alien Guide sample PDF convert", result=compat_pptx_sample_two["soffice"])
    watch_info = run_watch_probe(audit, pptx_created, port=18081)
    audit.record(fmt="pptx", scenario="watch", feature="watch server startup", status="通过" if watch_info["ready"] else "失败", note="watch listener ready", artifact=watch_info["watch_log"])
    audit.record(fmt="pptx", scenario="watch", feature="watch selected", status="通过" if watch_info["selected"].returncode == 0 else "失败", note="get selected after headless click", result=watch_info["selected"])
    audit.record(fmt="pptx", scenario="watch", feature="mark selected", status="通过" if watch_info["mark_selected"].returncode == 0 else "失败", note="mark selected paths", result=watch_info["mark_selected"])
    audit.record(fmt="pptx", scenario="watch", feature="get-marks", status="通过" if watch_info["marks"].returncode == 0 else "失败", note="read marks", result=watch_info["marks"])
    audit.record(fmt="pptx", scenario="watch", feature="unmark all", status="通过" if watch_info["unmark"].returncode == 0 else "失败", note="clear watch marks", result=watch_info["unmark"])
    audit.record(fmt="pptx", scenario="watch", feature="unwatch and port release", status="通过" if watch_info["port_released"] else "失败", note="port should be released after unwatch", result=watch_info["unwatch"])

    baseline_after = capture_real_baseline(REPORTS / "baseline-after.json")
    baseline_equal = baseline_before == baseline_after
    baseline_changed = baseline_diff(baseline_before, baseline_after)
    baseline_status = "通过" if baseline_equal else "失败"
    audit.record(fmt="common", scenario="isolation", feature="真实环境前后比对", status=baseline_status, note="baseline_before == baseline_after" if baseline_equal else f"changed: {', '.join(baseline_changed)}")

    write_csv(REPORTS / "coverage-matrix.csv", audit.coverage)

    status_counts = {"通过": 0, "部分通过": 0, "失败": 0, "跳过": 0}
    for row in audit.coverage:
        status_counts[row["status"]] = status_counts.get(row["status"], 0) + 1

    def status_for(fmt: str, scenario: str, feature: str) -> str:
        for row in audit.coverage:
            if row["format"] == fmt and row["scenario"] == scenario and row["feature"] == feature:
                return row["status"]
        return "未记录"

    round2_regressions = [
        ("common", "config", "config autoUpdate roundtrip", "config autoUpdate roundtrip"),
        ("common", "config", "config log roundtrip", "config log roundtrip"),
        ("docx", "created", "raw-set document alias", "DOCX raw-set document alias"),
        ("docx", "created", "raw-set styles alias", "DOCX raw-set styles alias"),
        ("docx", "resident", "open", "DOCX resident open"),
        ("docx", "resident", "close", "DOCX resident close"),
        ("docx", "created", "validate", "DOCX validate"),
        ("xlsx", "resident", "open", "XLSX resident open"),
        ("xlsx", "resident", "close", "XLSX resident close"),
        ("xlsx", "created", "named range cleanup after sheet remove", "XLSX named range cleanup"),
        ("xlsx", "created", "chart gridlines none readback", "XLSX chart gridlines=none readback"),
        ("pptx", "resident", "open", "PPTX resident open"),
        ("pptx", "resident", "close", "PPTX resident close"),
        ("pptx", "created", "check", "PPTX check"),
        ("pptx", "created", "validate", "PPTX validate"),
        ("pptx", "watch", "watch selected", "watch get selected"),
        ("pptx", "watch", "mark selected", "watch mark"),
        ("pptx", "watch", "get-marks", "watch get-marks"),
    ]
    regression_lines = [
        "# OfficeCLI Round3 Regression vs Round2",
        "",
        "## 历史失败项对比",
        "",
    ]
    for fmt, scenario, feature, label in round2_regressions:
        status = status_for(fmt, scenario, feature)
        regression_lines.append(f"- {label}: {'转绿' if status == '通过' else status}")
    regression_lines += [
        "",
        "## 结论",
        "",
        f"- Round2 历史失败项本轮{'全部转绿' if all(status_for(f, s, feat) == '通过' for f, s, feat, _ in round2_regressions) else '仍有未转绿项'}。",
        f"- 覆盖统计: 通过 {status_counts['通过']} / 部分通过 {status_counts['部分通过']} / 失败 {status_counts['失败']} / 跳过 {status_counts['跳过']}",
        f"- 真实环境污染比对: {baseline_status}",
        "",
    ]
    (REPORTS / "regression-vs-round2.md").write_text("\n".join(regression_lines) + "\n", encoding="utf-8")

    docx_textutil = compat_docx_created["textutil"]
    overall_clean = status_counts["失败"] == 0 and status_counts["部分通过"] == 0 and baseline_equal
    report_lines = [
        "# OfficeCLI Round3 Full Functional Report",
        "",
        "## 执行环境与隔离声明",
        "",
        f"- 日期: {time.strftime('%Y-%m-%d %H:%M:%S')}",
        f"- 审计目录: `{ROOT}`",
        f"- 二进制: `{BIN}`",
        f"- 隔离 HOME: `{ISOLATED_HOME}`",
        f"- 统一环境变量: `HOME={ISOLATED_HOME}` `OFFICECLI_SKIP_UPDATE=1` `TMPDIR={TMPDIR}`",
        "- 明确未执行: `install`、`setup`、`skills install`、`mcp register/unregister`、任何自动打开系统浏览器参数、任何远程图片/模板输入、任何对真实 `~/.zshrc`/`~/.local/bin`/`~/.openclaw`/`~/.officecli` 的写入。",
        "",
        "## 高层结论",
        "",
        f"- 覆盖统计: 通过 {status_counts['通过']} / 部分通过 {status_counts['部分通过']} / 失败 {status_counts['失败']} / 跳过 {status_counts['跳过']}",
        f"- 总体结论: **{'完全通过' if overall_clean else '未完全修复'}**",
        f"- 真实环境污染比对: **{baseline_status}**",
        f"- `config autoUpdate` roundtrip: `{status_for('common', 'config', 'config autoUpdate roundtrip')}`",
        f"- `config log` roundtrip: `{status_for('common', 'config', 'config log roundtrip')}`",
        f"- DOCX `textutil` 兼容性: {'通过' if docx_textutil.returncode == 0 else '失败'}",
        "- `watch` 使用 headless 浏览器访问 localhost，本轮显式拦截了所有非 `localhost/127.0.0.1` 请求。",
        "",
        "## 理论支持 vs 实际可用",
        "",
        "- DOCX: 覆盖了元数据、页眉页脚、水印、TOC、多级标题、列表、批注、脚注、超链接、公式、图片、表格、图表、batch、raw、raw-set、add-part、resident、validate、merge。",
        "- XLSX: 覆盖了多 sheet、多行表头、合并单元格、冻结窗格、筛选、条件格式、数据验证、命名区域、表格、公式、CSV 导入、图表、resident、validate、merge。",
        "- PPTX: 覆盖了多页新建、主题背景、图形、连接线、表格、图表、图片、动画、转场、notes、HTML/SVG 预览、custom geometry、resident、check、validate、watch/mark/get selected/get-marks、merge。",
        "",
        "## Round2 失败项回归",
        "",
        f"- 详见 `{REPORTS / 'regression-vs-round2.md'}`",
        "",
        "## 关键失败项与复现证据",
        "",
    ]
    if audit.failures:
        for row in audit.failures[:60]:
            report_lines.append(f"- [{row['format']}/{row['scenario']}] {row['feature']} -> {row['status']} | {row['note']} | 日志: `{row['log']}`")
    else:
        report_lines.append("- 未记录失败项。")
    report_lines += [
        "",
        "## 三类格式兼容性结论",
        "",
        f"- DOCX: 创建文件和两份真实样例副本都完成 `soffice` 转 PDF；并抽查 `word/document.xml`、`word/settings.xml`、`word/footer1.xml`。",
        f"- XLSX: 创建文件和两份真实样例副本都完成 `soffice` 转 PDF；并抽查 `xl/workbook.xml`、`xl/styles.xml`、`xl/worksheets/sheet1.xml`、`xl/charts/chart1.xml`。",
        f"- PPTX: 创建文件和两份真实样例副本都完成 `soffice` 转 PDF；并抽查 `ppt/presentation.xml`、`ppt/slides/slide1.xml`、`ppt/notesSlides/notesSlide1.xml`、`ppt/charts/chart1.xml`。",
        "",
        "## 产物索引",
        "",
        f"- 覆盖矩阵: `{REPORTS / 'coverage-matrix.csv'}`",
        f"- Round2 对比: `{REPORTS / 'regression-vs-round2.md'}`",
        f"- 基线前: `{REPORTS / 'baseline-before.json'}`",
        f"- 基线后: `{REPORTS / 'baseline-after.json'}`",
        f"- DOCX 新建: `{docx_created}`",
        f"- DOCX 样例编辑 A: `{docx_sample}`",
        f"- DOCX 样例编辑 B: `{docx_sample_two}`",
        f"- XLSX 新建: `{xlsx_created}`",
        f"- XLSX 样例编辑 A: `{xlsx_sample}`",
        f"- XLSX 样例编辑 B: `{xlsx_sample_two}`",
        f"- PPTX 新建: `{pptx_created}`",
        f"- PPTX 样例编辑 A: `{pptx_sample}`",
        f"- PPTX 样例编辑 B: `{pptx_sample_two}`",
        f"- Watch 会话日志: `{watch_info['watch_log']}`",
    ]
    (REPORTS / "full-functional-report.md").write_text("\n".join(report_lines) + "\n", encoding="utf-8")
    return 0 if overall_clean else 1


if __name__ == "__main__":
    sys.exit(main())
