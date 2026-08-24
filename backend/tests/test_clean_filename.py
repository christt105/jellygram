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
    # "tmdbid_<id>" instead of "[tmdbid-<id>]"). "LasCositas" sits after the
    # year, so the year-truncation in clean_filename drops it along with the
    # rest of the tail - it's the uploader's own signature, not part of the
    # title. Doesn't affect identification either way: a tmdbid is present,
    # so identify_by_filename resolves via identify_by_tmdbid and never uses
    # clean_name for a TMDB search.
    ("Arrietty_y_el_mundo_de_los_diminutos_2010_tmdbid_51739_LasCositas.001", {
        "tmdbid": 51739,
        "clean_name": "Arrietty y el mundo de los diminutos",
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
    # Color-depth tag ("10Bit") wasn't in the noise list and rode into the
    # search query, dragging the TMDB match score down enough to miss.
    ("Toy.Story.4.(2019).(Spanish.English.Subs).BDRip.2160p.x265.10Bit.HDR-EAC3.AC3.by.enjoy.(hispashare.org).mkv", {
        "tmdbid": None,
        "clean_name": "Toy Story 4",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2019
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
    # Two-part uploader nicks joined by a hyphen ("aurora45-xusman") defeat
    # the "by <uploader>" and trailing-group-tag regexes, which only strip a
    # single unhyphenated word. Truncating the movie name at the year sidesteps
    # this instead of extending those regexes' character classes.
    ("The.Surfer.(2024).(Spanish.English.Subs).BDRip.1080p.x264-EAC3.by.aurora45-xusman.(nocturniap2p).mkv", {
        "tmdbid": None,
        "clean_name": "The Surfer",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2024
    }),
    ("Shelter.El.protector.(2026).(Spanish.English.Subs).BDRip.1080p.x264-EAC3.by.diavliyo-xusman.(nocturniap2p).mkv", {
        "tmdbid": None,
        "clean_name": "Shelter El protector",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2026
    }),
    ("Altas.capacidades.(2026).(Spanish).WEB-DL.1080p.x264-EAC3.by.xusman.(nocturniap2p).mkv", {
        "tmdbid": None,
        "clean_name": "Altas capacidades",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2026
    }),
    # A trailing number that's part of the title itself (not a part/disc
    # number) must survive the year truncation.
    ("Stand.By.Me.Doraemon.2.(2020).(Spanish).WEBRip.1080p.x265-AC3.by.s1d3sh0w.(hispashare.org).mkv", {
        "tmdbid": None,
        "clean_name": "Stand By Me Doraemon 2",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2020
    }),
    ("Elysium.(2013).(Spanish.English.Subs).BDrip.1080p.x265-AC3.by.SparroW.mkv", {
        "tmdbid": None,
        "clean_name": "Elysium",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2013
    }),
    # A streaming-platform source tag ("NF" for Netflix) has no dedicated
    # noise pattern - it isn't needed, the year truncation drops it anyway.
    ("Enola.Holmes.(2020).(Spanish.English.Subs).NF.WEB-DL.1080p.x264-EAC3.mkv", {
        "tmdbid": None,
        "clean_name": "Enola Holmes",
        "type": "movie",
        "season": None,
        "episode": None,
        "year": 2020
    }),
    # TV titles with no year at all are unaffected by the movie-only
    # year-truncation branch; episode-marker splitting alone must still
    # isolate the show title from the episode title and release tags.
    ("La.casa.de.la.pradera.1x04 .Apreciemos.la.vida. (Spanish.English .Subs).WEBRip.1080p.x265-EAC3.by .piter332.(hispashare.org).mkv", {
        "tmdbid": None,
        "clean_name": "La casa de la pradera",
        "type": "tv",
        "season": 1,
        "episode": 4,
        "year": None
    }),
    ("Spider-Noir.1x01.Pase.a.mi.despacho.BN.(Spanish.English.Subs).WEBRip.1080p.x265-EAC3.EAC3.Atmos.by.Legan.mkv", {
        "tmdbid": None,
        "clean_name": "Spider-Noir",
        "type": "tv",
        "season": 1,
        "episode": 1,
        "year": None
    }),
])
def test_clean_filename(filename, expected):
    res = TMDB.clean_filename(filename)
    assert res == expected
