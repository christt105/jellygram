import pytest
from tmdb import TMDB

@pytest.mark.parametrize("filename,expected", [
    ("Pokémon 2: El poder de uno (1999).zip.001", {
        "tmdbid": None,
        "clean_name": "Pokémon 2: El poder de uno",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 1999
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
        "year": 2024
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
    # Real cinegram library filenames (uploader convention: bracket-less
    # "tmdbid_<id>" instead of "[tmdbid-<id>]").
    ("Arrietty_y_el_mundo_de_los_diminutos_2010_tmdbid_51739_LasCositas.001", {
        "tmdbid": 51739,
        "clean_name": "Arrietty y el mundo de los diminutos LasCositas",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2010
    }),
    ("El viento se levanta (2013) [tmdbid-149870] - LasCositas.zip.001", {
        "tmdbid": 149870,
        "clean_name": "El viento se levanta",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2013
    }),
    # A parenthesized year must still be extracted (used to disambiguate
    # same-titled TMDB entries from different years, e.g. "La Cena (2025)"
    # vs. older unrelated films also called "La cena"), not just a bare
    # scene-release year between dots.
    ("La Cena (2025).zip.001", {
        "tmdbid": None,
        "clean_name": "La Cena",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2025
    }),
    # A hyphen-joined subtitle before the episode marker must survive: only
    # a literal "Temporada N"/"Season N" tail gets discarded, not any text
    # after " - ".
    ("Fullmetal Alchemist - Brotherhood 1x01.mkv", {
        "tmdbid": None,
        "clean_name": "Fullmetal Alchemist Brotherhood",
        "type": "tv",
        "season": 1,
        "episode": 1,
        "year": None
    }),
    # A bullet (U+2022, distinct from the middle dot U+00B7 in "WALL·E"'s
    # official title) must normalize to a space too, or the TMDB search
    # query itself ("Wall•E") returns unrelated results.
    ("Wall•E (1080p).zip.001", {
        "tmdbid": None,
        "clean_name": "Wall E",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": None
    }),
    # Without an embedded tmdbid, "Las Cositas" (Christian's own uploader
    # signature tag) rides into the TMDB search query and used to return
    # zero results outright - not a scoring issue, TMDB's search endpoint
    # itself returns nothing for the padded query.
    ("La Princesa Mononoke (1997) - Las Cositas.zip.001", {
        "tmdbid": None,
        "clean_name": "La Princesa Mononoke",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 1997
    }),
    # Spanish scene-release convention (hispashare & co.): a "by.<uploader>"
    # credit plus codec/source tags the original noise list didn't cover
    # (x265, EAC3/FLAC, Micro4K, HDR10, DVDRemux, letter-prefixed "m1080p").
    # With no episode marker to truncate the tail at, a movie kept all of it
    # in the search query.
    ("Spider-Man.(2002).(Spanish.English.Subs).Micro4K.2160p.HDR10.x265-AC3.by.CHINAKO.(hispashare.org).mkv", {
        "tmdbid": None,
        "clean_name": "Spider-Man",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2002
    }),
    ("Lightyear.(2022).(Spanish.English.Subs).WEB-DL.m1080p.x265-AC3.by.yamil.mkv", {
        "tmdbid": None,
        "clean_name": "Lightyear",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2022
    }),
    # An uploader nickname ending in digits must not be read as a release
    # year: doing so filters the TMDB search by a bogus year, which returns
    # nothing at all rather than just ranking badly.
    ("Sterling.Point.1x04.Llevame.a.los.acantilados.(Spanish.English.Subs).WEBRip.1080p.x264-AC3.by.Mony2007.mkv", {
        "tmdbid": None,
        "clean_name": "Sterling Point",
        "type": "tv",
        "season": 1,
        "episode": 4,
        "year": None
    }),
    # The "by <uploader>" strip must not eat a title that genuinely ends in
    # "by <short word>".
    ("Stand.by.Me.1986.1080p.BluRay.x264-GROUP.mkv", {
        "tmdbid": None,
        "clean_name": "Stand by Me",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 1986
    }),
    ("Spider-Man No Way Home Ext. (1080p).001", {
        "tmdbid": None,
        "clean_name": "Spider-Man No Way Home",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": None
    }),
])
def test_clean_filename(filename, expected):
    res = TMDB.clean_filename(filename)
    assert res == expected
