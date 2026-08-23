import pytest

try:
    from backend.tmdb import TMDB
except ImportError:
    from tmdb import TMDB


@pytest.mark.parametrize("raw,expected", [
    ("Pokémon", "pokemon"),
    ("Pokemon", "pokemon"),
    ("WALL·E", "wall e"),
    ("Wall-E", "wall e"),
    ("Wall E", "wall e"),
    ("Spider-Man", "spider man"),
    ("Amélie", "amelie"),
    ("Jurassic World – Dominion", "jurassic world dominion"),
    ("", ""),
])
def test_normalize(raw, expected):
    assert TMDB._normalize(raw) == expected


@pytest.mark.parametrize("query,candidates,expected", [
    # Accented official title, unaccented query (and vice versa)
    ("Pokemon", ["Pokémon", "Digimon"], "Pokémon"),
    ("Pokémon", ["Pokemon", "Monster Rancher"], "Pokemon"),
    # Middle-dot official title against dashed / spaced filenames
    ("Wall-E", ["WALL·E", "Cars"], "WALL·E"),
    ("Wall E", ["WALL·E", "Up"], "WALL·E"),
    # No regression on hyphenated / accented titles that already worked
    ("Spider-Man", ["Spider-Man", "Superman"], "Spider-Man"),
    ("Amelie", ["Amélie", "Delicatessen"], "Amélie"),
])
def test_best_match_normalizes_diacritics(query, candidates, expected):
    results = [{"name": t, "popularity": 1.0} for t in candidates]
    match = TMDB._best_match(results, query, "tv")
    assert match is not None
    assert (match.get("title") or match.get("name")) == expected


def test_best_match_rejects_below_threshold():
    results = [{"name": "Completely Unrelated Show", "popularity": 50.0}]
    assert TMDB._best_match(results, "Pokémon", "tv") is None


def test_best_match_empty_results():
    assert TMDB._best_match([], "Pokémon", "tv") is None


def test_best_match_falls_back_to_original_name_for_localized_results():
    # TMDB_CONTENT_LANGUAGE localizes "name" (e.g. es-ES's "Bajo escucha" for
    # "The Wire"), but scene-release filenames use the original title. Matching
    # only on the localized name picks whatever unrelated result's localized
    # name happens to look closest to the (untranslated) query instead.
    results = [
        {"name": "Bajo escucha", "original_name": "The Wire", "popularity": 58.0},
        {"name": "The LiveWire", "original_name": "The LiveWire", "popularity": 0.8},
    ]
    match = TMDB._best_match(results, "The Wire", "tv")
    assert match is not None
    assert match.get("original_name") == "The Wire"


def test_search_and_match_passes_year_to_movie_search(monkeypatch):
    captured = {}

    class FakeSearch:
        results = [{"id": 603, "title": "Matrix", "popularity": 47.5}]

        def movie(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setattr("tmdb.tmdb.Search", FakeSearch)

    t = TMDB.__new__(TMDB)
    result = t._search_and_match("The Matrix", "movie", 1999)

    assert captured.get("year") == 1999
    assert result is not None
    assert result["id"] == 603


def test_search_and_match_passes_first_air_date_year_to_tv_search(monkeypatch):
    captured = {}

    class FakeSearch:
        results = [{"id": 1438, "name": "Bajo escucha", "original_name": "The Wire", "popularity": 58.0}]

        def tv(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setattr("tmdb.tmdb.Search", FakeSearch)

    t = TMDB.__new__(TMDB)
    result = t._search_and_match("The Wire", "tv", 2002)

    assert captured.get("first_air_date_year") == 2002
    assert result is not None
    assert result["id"] == 1438


def test_search_and_match_omits_year_kwarg_when_year_is_unknown(monkeypatch):
    captured = {}

    class FakeSearch:
        results = []

        def movie(self, **kwargs):
            captured.update(kwargs)

        def multi(self, **kwargs):
            pass

    monkeypatch.setattr("tmdb.tmdb.Search", FakeSearch)

    t = TMDB.__new__(TMDB)
    t._search_and_match("Some Movie", "movie", None)

    assert "year" not in captured
