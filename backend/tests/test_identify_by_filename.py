import pytest
from tmdb import TMDB


def test_identify_by_filename_with_embedded_tmdbid_skips_title_search(monkeypatch):
    t = TMDB.__new__(TMDB)

    monkeypatch.setattr(
        t, "identify_by_tmdbid", lambda tmdbid, content_type: {"id": tmdbid, "media_type": content_type}
    )

    def fail_if_called(*args, **kwargs):
        raise AssertionError("_search_and_match should not run when a tmdbid is embedded in the filename")

    monkeypatch.setattr(t, "_search_and_match", fail_if_called)

    result = t.identify_by_filename("El viento se levanta (2013) [tmdbid-149870].mkv")

    assert result == {"id": 149870, "media_type": "movie"}


def test_identify_by_filename_falls_back_to_tmdbid_type_search(monkeypatch):
    t = TMDB.__new__(TMDB)

    calls = []

    def fake_identify_by_tmdbid(tmdbid, content_type):
        calls.append(tmdbid)
        return {"id": tmdbid, "media_type": content_type}

    monkeypatch.setattr(t, "identify_by_tmdbid", fake_identify_by_tmdbid)
    monkeypatch.setattr(t, "_search_and_match", lambda *a, **k: (_ for _ in ()).throw(
        AssertionError("should not fall through to a title search")
    ))

    result = t.identify_by_filename("Naruto Shippuden - S07E02 - [tmdbid-31910].avi")

    assert calls == [31910]
    assert result["id"] == 31910


def test_identify_by_filename_retries_with_normalized_title_for_accented_names(monkeypatch):
    t = TMDB.__new__(TMDB)
    monkeypatch.setattr(t, "identify_by_tmdbid", lambda *a, **k: None)

    queries = []

    def fake_search_and_match(query, content_type, year=None):
        queries.append(query)
        if query == "Pequeña Miss Sunshine":
            return None
        return {"id": 773, "media_type": "movie", "title": "Little Miss Sunshine"}

    monkeypatch.setattr(t, "_search_and_match", fake_search_and_match)

    result = t.identify_by_filename("Pequeña Miss Sunshine (2006).mkv")

    assert queries == ["Pequeña Miss Sunshine", "pequena miss sunshine"]
    assert result["id"] == 773


def test_identify_by_filename_does_not_retry_when_normalization_is_a_no_op(monkeypatch):
    t = TMDB.__new__(TMDB)
    monkeypatch.setattr(t, "identify_by_tmdbid", lambda *a, **k: None)

    queries = []

    def fake_search_and_match(query, content_type, year=None):
        queries.append(query)
        return None

    monkeypatch.setattr(t, "_search_and_match", fake_search_and_match)

    result = t.identify_by_filename("Show S01E02.mkv")

    assert queries == ["Show"]
    assert result == {}
