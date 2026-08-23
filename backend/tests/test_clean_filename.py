import pytest
from tmdb import TMDB

@pytest.mark.parametrize("filename,expected", [
    ("Pokémon 2: El poder de uno (1999).zip.001", {
        "tmdbid": None,
        "clean_name": "Pokémon 2: El poder de uno",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": None
    }),
    ("Jurassic World: Dominion [Versión Extendida] (1080p).zip.001", {
        "tmdbid": None,
        "clean_name": "Jurassic World: Dominion",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": None
    }),
    ("Hayao Miyazaki and the Heron (2024) [tmdbid-1292585].zip.001", {
        "tmdbid": 1292585,
        "clean_name": "Hayao Miyazaki and the Heron",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": None
    }),
    ("Hijos de la anarquía - S05E10 - Crucificado.mkv.zip.001", {
        "tmdbid": None,
        "clean_name": "Hijos de la anarquía",
        "type": "tv",
        "season": 5,
        "episode": 10,
        "year": None
    }),
    ("Yellowstone 5x08 Un cuchillo y ninguna moneda.mkv", {
        "tmdbid": None,
        "clean_name": "Yellowstone",
        "type": "tv",
        "season": 5,
        "episode": 8,
        "year": None
    }),
    ("One Piece 1x125.mkv", {
        "tmdbid": None,
        "clean_name": "One Piece",
        "type": "tv",
        "season": 1,
        "episode": 125,
        "year": None
    }),
    ("Naruto Shippuden - S07E02 - [tmdbid-31910].avi", {
        "tmdbid": 31910,
        "clean_name": "Naruto Shippuden",
        "type": "tv",
        "season": 7,
        "episode": 2,
        "year": None
    }),
    ("Vikingos - Temporada 3 (Blu-ray 1080p).zip.006", {
        "tmdbid": None,
        "clean_name": "Vikingos",
        "type": "tv",
        "season": 3,
        "episode": None,
        "year": None
    }),
    ("01x01 Mi otra yo.mp4", {
        "tmdbid": None,
        "clean_name": "Mi otra yo",
        "type": "tv",
        "season": 1,
        "episode": 1,
        "year": None
    }),
    ("Dorohedoro - 01.mp4", {
        "tmdbid": None,
        "clean_name": "Dorohedoro",
        "type": "tv",
        "season": 1,
        "episode": 1,
        "year": None
    }),
    ("Dorohedoro S2 - 01.mp4", {
        "tmdbid": None,
        "clean_name": "Dorohedoro",
        "type": "tv",
        "season": 2,
        "episode": 1,
        "year": None
    }),
    # Scene-release format: dots separate every token, including the title itself.
    ("Show.Name.S01E02.1080p.WEB-DL.x264-GROUP.mkv", {
        "tmdbid": None,
        "clean_name": "Show Name",
        "type": "tv",
        "season": 1,
        "episode": 2,
        "year": None
    }),
    # Scene-release movies: no SxxExx marker to truncate the tail at, so the
    # year and release-group both need to be stripped explicitly, or they
    # ride into the TMDB search query and break the match.
    ("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv", {
        "tmdbid": None,
        "clean_name": "The Matrix",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 1999
    }),
    ("Parasite.2019.720p.WEB-DL.x264-GROUP.mkv", {
        "tmdbid": None,
        "clean_name": "Parasite",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2019
    }),
    # A bare hyphenated title with no other scene-release metadata segments
    # (no dots left after the extension is stripped) must not be mistaken
    # for a title with a trailing release-group tag.
    ("Spider-Man", {
        "tmdbid": None,
        "clean_name": "Spider-Man",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": None
    }),
])
def test_clean_filename(filename, expected):
    res = TMDB.clean_filename(filename)
    assert res == expected
