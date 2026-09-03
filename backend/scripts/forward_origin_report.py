"""Exposure report: how much of the archive depends on which forward source.

Reads the document_id / fwd_from_* columns populated by
backend/scripts/backfill_forward_origin.py (and by every new upload since that started being
tracked) and groups files by where their blob actually came from, so it's possible to see how
much of the archive would be at risk if a given channel or chat had its blobs purged.

Usage (from the backend/ directory):
    python scripts/forward_origin_report.py
"""
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from sqlmodel import Session, select

import models  # noqa: F401  registers the table schemas
from database import engine
from models import File

# Telegram's copyright policy can act directly on public channels and chats, unlike private
# pointers, so a source of either of these types is the highest-priority thing to consider
# re-uploading.
PUBLIC_TYPES = {"channel", "chat"}


def classify(file: File) -> tuple:
    if file.document_id is None:
        return ("unknown", None, "Not backfilled yet")
    if file.fwd_from_type is None:
        return ("owned", None, "Uploaded directly (not a forward)")
    return (file.fwd_from_type, file.fwd_from_id, file.fwd_from_name or "(unknown sender)")


def group_by_source(files) -> dict:
    groups: dict = defaultdict(lambda: {"count": 0, "filesize": 0})
    for file in files:
        entry = groups[classify(file)]
        entry["count"] += 1
        entry["filesize"] += file.filesize or 0
    return dict(groups)


def format_size(num_bytes: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if num_bytes < 1024:
            return f"{num_bytes:.1f} {unit}"
        num_bytes /= 1024
    return f"{num_bytes:.1f} PB"


def render(groups: dict) -> str:
    rows = sorted(groups.items(), key=lambda kv: kv[1]["filesize"], reverse=True)
    total_files = sum(v["count"] for _, v in rows)
    total_size = sum(v["filesize"] for _, v in rows)

    lines = [f"{'source':<45} {'type':<12} {'docs':>6} {'size':>10}", "-" * 76]
    for (fwd_type, _fwd_id, name), stats in rows:
        flag = "  *public*" if fwd_type in PUBLIC_TYPES else ""
        lines.append(f"{name[:45]:<45} {fwd_type:<12} {stats['count']:>6} {format_size(stats['filesize']):>10}{flag}")
    lines.append("-" * 76)
    lines.append(f"{'TOTAL':<45} {'':<12} {total_files:>6} {format_size(total_size):>10}")
    lines.append("")
    lines.append(
        "* public channels/chats are the highest-priority risk: Telegram's copyright policy "
        "can act on them directly, and it isn't documented what happens to a private pointer "
        "when the blob it points to is purged."
    )
    return "\n".join(lines)


def main():
    with Session(engine) as session:
        files = session.exec(select(File)).all()
    print(render(group_by_source(files)))


if __name__ == "__main__":
    main()
