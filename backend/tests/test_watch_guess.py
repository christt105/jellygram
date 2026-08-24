from crud import guess_watched_file


class ExplicitIdTMDB:
    def identify_by_filename(self, filename):
        return {
            "id": 1292585, "title": "The Boy and the Heron", "media_type": "movie",
            "release_date": "2023-07-14",
        }


class FuzzyMatchTMDB:
    def identify_by_filename(self, filename):
        return {
            "id": 999, "name": "Some Show", "media_type": "tv", "_match_score": 0.73,
            "first_air_date": "2018-11-02",
        }


class NoMatchTMDB:
    def identify_by_filename(self, filename):
        return {}


class RaisingTMDB:
    def identify_by_filename(self, filename):
        raise RuntimeError("network is down")


def test_explicit_tmdb_tag_is_full_confidence_and_sourced_from_filename():
    guess = guess_watched_file(
        ExplicitIdTMDB(), "Hayao Miyazaki and the Heron (2024) [tmdbid-1292585].zip.001"
    )
    assert guess["source"] == "filename"
    assert guess["confidence"] == 1.0
    assert guess["tmdb_id"] == 1292585
    assert guess["media_type"] == "movie"
    assert guess["year"] == 2023
    assert guess["season"] is None
    assert guess["episode"] is None


def test_fuzzy_match_confidence_is_the_similarity_score():
    guess = guess_watched_file(FuzzyMatchTMDB(), "Some.Show.S01E02.mkv")
    assert guess["source"] == "tmdb"
    assert guess["confidence"] == 0.73
    assert guess["tmdb_id"] == 999
    assert guess["title"] == "Some Show"
    assert guess["year"] == 2018
    # Season/episode always come from the filename regex, never from TMDB.
    assert guess["season"] == 1
    assert guess["episode"] == 2


def test_no_match_is_zero_confidence():
    guess = guess_watched_file(NoMatchTMDB(), "Totally Unknown Thing.mkv")
    assert guess["source"] == "tmdb"
    assert guess["confidence"] == 0.0
    assert guess["tmdb_id"] is None
    assert guess["title"] is None
    assert guess["year"] is None
    assert guess["media_type"] == "movie"


def test_tmdb_errors_are_swallowed_into_a_zero_confidence_guess():
    guess = guess_watched_file(RaisingTMDB(), "Whatever.mkv")
    assert guess["confidence"] == 0.0
    assert guess["tmdb_id"] is None
