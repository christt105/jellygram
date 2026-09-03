from datetime import datetime, timezone

from models import File
from scripts import forward_origin_report as report


def make_file(message_id, filesize, document_id=None, fwd_from_type=None, fwd_from_id=None, fwd_from_name=None):
    return File(
        message_id=message_id,
        filename=f"{message_id}.mkv",
        filesize=filesize,
        created_at=datetime.now(timezone.utc),
        collection_id=1,
        document_id=document_id,
        fwd_from_type=fwd_from_type,
        fwd_from_id=fwd_from_id,
        fwd_from_name=fwd_from_name
    )


def test_classify_owned_file():
    file = make_file(1, 100, document_id=555)
    assert report.classify(file) == ("owned", None, "Uploaded directly (not a forward)")


def test_classify_not_yet_backfilled_file():
    file = make_file(2, 100)
    assert report.classify(file) == ("unknown", None, "Not backfilled yet")


def test_classify_forwarded_file():
    file = make_file(3, 100, document_id=999, fwd_from_type="channel", fwd_from_id="-100", fwd_from_name="Doraemon")
    assert report.classify(file) == ("channel", "-100", "Doraemon")


def test_classify_forwarded_file_with_no_recorded_name():
    file = make_file(4, 100, document_id=999, fwd_from_type="chat", fwd_from_id="-200")
    assert report.classify(file) == ("chat", "-200", "(unknown sender)")


def test_group_by_source_aggregates_count_and_size():
    files = [
        make_file(1, 1000, document_id=1, fwd_from_type="channel", fwd_from_id="-1", fwd_from_name="Doraemon"),
        make_file(2, 2000, document_id=2, fwd_from_type="channel", fwd_from_id="-1", fwd_from_name="Doraemon"),
        make_file(3, 500, document_id=3),
    ]

    groups = report.group_by_source(files)

    assert groups[("channel", "-1", "Doraemon")] == {"count": 2, "filesize": 3000}
    assert groups[("owned", None, "Uploaded directly (not a forward)")] == {"count": 1, "filesize": 500}


def test_format_size_picks_the_right_unit():
    assert report.format_size(500) == "500.0 B"
    assert report.format_size(1536) == "1.5 KB"
    assert report.format_size(5 * 1024 ** 3) == "5.0 GB"


def test_render_flags_public_sources_and_sorts_by_size():
    groups = {
        ("channel", "-1", "Doraemon"): {"count": 858, "filesize": 500 * 1024 ** 3},
        ("owned", None, "Uploaded directly (not a forward)"): {"count": 219, "filesize": 10 * 1024 ** 3},
        ("user", "42", "Ana"): {"count": 3, "filesize": 1024 ** 3},
    }

    output = report.render(groups)
    lines = output.splitlines()

    assert "Doraemon" in lines[2]
    assert "*public*" in lines[2]
    assert "TOTAL" in output
    assert "*public*" not in [l for l in lines if "Ana" in l][0]
