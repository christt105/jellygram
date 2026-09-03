"""One-off backfill of document_id and forward origin for files uploaded before that started
being tracked (see models.File). Re-fetches each historical message from bot-net, which reads
it straight off Telegram, and fills in the matching file row.

Only touches rows where document_id is still NULL and storage_peer is "bot" (the chat between
the owner and the bot), so it can be re-run safely if interrupted, and skips the handful of
files parked in the user account's Saved Messages, which the bot has no access to.

Usage (from the backend/ directory, against the real deployment's database and bot-net):
    python scripts/backfill_forward_origin.py
    python scripts/backfill_forward_origin.py --dry-run
"""
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import httpx
from sqlmodel import Session, select

import models  # noqa: F401  registers the table schemas
from database import engine
from models import File

BATCH_SIZE = 100  # WTelegramBot's documented limit for GetMessagesById


def pending_files(session):
    return session.exec(
        select(File).where(File.storage_peer == "bot", File.document_id.is_(None))
    ).all()


def batches(items, size):
    for i in range(0, len(items), size):
        yield items[i:i + size]


def apply_origin(file: File, origin: dict) -> None:
    file.document_id = origin.get("document_id")
    file.fwd_from_type = origin.get("fwd_from_type")
    file.fwd_from_id = origin.get("fwd_from_id")
    file.fwd_from_name = origin.get("fwd_from_name")
    file.fwd_from_hidden = bool(origin.get("fwd_from_hidden", False))


def run(bot_net_url: str, dry_run: bool = False) -> dict:
    stats = {"updated": 0, "not_found": 0, "batches": 0}

    with Session(engine) as session:
        files = pending_files(session)
        for batch in batches(files, BATCH_SIZE):
            stats["batches"] += 1
            message_ids = [f.message_id for f in batch]
            response = httpx.post(
                f"{bot_net_url}/messages/forward-origin", json=message_ids, timeout=60
            )
            response.raise_for_status()
            origins = response.json()

            for file in batch:
                origin = origins.get(str(file.message_id))
                if origin is None:
                    stats["not_found"] += 1
                    continue
                apply_origin(file, origin)
                stats["updated"] += 1
                if not dry_run:
                    session.add(file)

        if not dry_run:
            session.commit()

    return stats


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--bot-net-url", default="http://bot-net:8080",
        help="Base URL of the bot-net service (default: %(default)s)"
    )
    parser.add_argument(
        "--dry-run", action="store_true",
        help="Fetch and report what would change without writing to the database"
    )
    args = parser.parse_args()

    stats = run(args.bot_net_url, dry_run=args.dry_run)

    print(f"Batches fetched: {stats['batches']}")
    print(f"Files updated:   {stats['updated']}{' (dry run, not saved)' if args.dry_run else ''}")
    print(f"Not found:       {stats['not_found']} (message deleted or otherwise unreachable)")


if __name__ == "__main__":
    main()
