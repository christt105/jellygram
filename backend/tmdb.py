import requests
import os
import re
import difflib
import math
import unicodedata
import tmdbsimple as tmdb

from functools import lru_cache
from config import TMDB_API_KEY, TMDB_CONTENT_LANGUAGE
from logger import logger

tmdb.REQUESTS_TIMEOUT = 15

class TMDB:
    def __init__(self, api_key: str = TMDB_API_KEY):
        self.api_key = api_key
        tmdb.API_KEY = self.api_key
        session = requests.Session()
        tmdb.REQUESTS_SESSION = session

    @staticmethod
    def clean_filename(filename: str) -> dict:
        
        # Detect type (movie or tv show)
        def detect_hint_type(n: str) -> str:
            if (re.search(r"[Ss]\d{1,2}[Ee]\d{1,3}", n) or 
                re.search(r"(?<!\d)\d{1,2}x\d{1,3}(?!\d)", n) or
                re.search(r"(?<![a-zA-Z0-9])[Ss](\d{1,2})\s*[-_]\s*(\d{1,3})(?!\d)", n, re.IGNORECASE) or
                re.search(r"\s+[-_]\s+(\d{1,3})(?!\d)$", n) or
                re.search(r"(?<![a-zA-Z0-9])Temporada\s+\d+(?![a-zA-Z0-9])", n, re.IGNORECASE) or
                re.search(r"(?<![a-zA-Z0-9])Season\s+\d+(?![a-zA-Z0-9])", n, re.IGNORECASE) or
                # Anime-style absolute episode numbering with no season/episode
                # separator (e.g. "One Piece 1085"): low-confidence "tv" hint,
                # not a full season/episode mapping. Restricted to 4+ digit
                # trailing numbers to avoid flagging real movie titles that
                # end in a smaller number (e.g. "Fahrenheit 451").
                re.search(r"[A-Za-z]\s+\d{4,}$", n)):
                return "tv"
            return "movie"
        
        # Remove main extension
        name = os.path.splitext(filename)[0]

        # Check for TMDB id (accepts both "[tmdbid-123]" and the bracket-less
        # "tmdbid_123"/"tmdbid-123" some uploaders use)
        tmdbid = None
        match = re.search(r"\[?tmdbid[-_](\d+)\]?", name, re.IGNORECASE)
        if match:
            tmdbid = int(match.group(1))
            name = re.sub(r"\[?tmdbid[-_]\d+\]?", "", name, flags=re.IGNORECASE)

        # Extract a release year before the noise patterns below strip it: a
        # year in parentheses (human-typed titles) or bare between dots
        # (scene releases) both get discarded by the "(anything)"/generic
        # cleanup otherwise, losing the one signal that disambiguates
        # same-titled TMDB entries from different years (e.g. "La Cena
        # (2025)" vs. older unrelated films also called "La cena").
        #
        # The year has to stand on its own: bounded by a separator (dot,
        # underscore, space, parenthesis) or the string edge, never glued to
        # letters. Uploader nicknames routinely end in digits
        # ("...by.Mony2007"), and reading one as a release year filters the
        # TMDB search by a wrong year and returns nothing at all.
        # Prefer the rightmost year-shaped token, and only treat it as a real
        # year if something survives on at least one side of it: a bare
        # numeric filename like "2012.mkv" is a title, not a year with an
        # empty title, and "1917 (2019).mkv" has two candidates, the actual
        # release year is the one that leaves "1917" as the title.
        year = None
        year_prefix = None
        year_matches = list(re.finditer(r"(?<![^\W_])(19\d{2}|20\d{2})(?![^\W_])", name))
        if year_matches:
            year_match = year_matches[-1]
            candidate_prefix = name[:year_match.start()]
            candidate_suffix = name[year_match.end():]
            has_prefix = bool(re.sub(r"[\s.\-_(\[]+$", "", candidate_prefix).strip())
            has_suffix = bool(candidate_suffix.strip(" .-_)]"))
            if has_prefix or has_suffix:
                year = int(year_match.group(1))
                year_prefix = candidate_prefix
                name = candidate_prefix + candidate_suffix

        # Remove common noise patterns (resolution, quality, part numbers, etc.)
        noise_patterns = [
            r"\b[a-z]?\d{3,4}p\b",   # 1080p, 720p, m1080p
            r"\b(?:Micro)?4K\b",
            r"\bHDR10\+?\b",
            r"\bHDR\b",
            r"\b(?:8|10|12)-?[Bb]it\b",   # 10Bit, 8-bit (color depth)
            r"\bBlu[- ]?ray\b",
            r"\bHEVC\b",
            r"\bWEB[- ]?DL\b",
            r"\b(?:BD|BR|DVD|HD|WEB)Rip\b",
            r"\b(?:BD|BR|DVD)?Remux\b",
            r"\.part\d+",            # .part1, .part2
            r"\.\d+$",               # .001, .002
            r"\[.*?\]",              # [anything]
            r"\(.*?\)",              # (anything)
            r"\bE?AC3\b",            # 🔥 remove AC3 / EAC3
            r"\bDTS\b",
            r"\bFLAC\b",
            r"\bXviD\b",
            r"\bx26[45]\b",
            r"\bH\.?26[45]\b",
            r"\bAAC\b",
            r"\bMP3\b",
            r"\bExt\.?\b",            # "Ext"/"Ext." (Extended cut/edition)
            r"\bLas\s*Cositas\b",     # uploader signature tag, not part of any title
        ]

        for pattern in noise_patterns:
            name = re.sub(pattern, "", name, flags=re.IGNORECASE)

        # Strip a trailing "by <uploader>" credit, the convention Spanish
        # release groups use ("...x265-AC3.by.CHINAKO"). Guarded three ways so
        # a real title is never truncated: the name has to be dot-separated
        # (an untouched scene-style release), the credit has to sit at the very
        # end, and the nickname has to be at least three characters - which is
        # what keeps a title like "Stand.by.Me" intact.
        name = name.strip()
        if "." in name:
            name = re.sub(r"\bby[\s._-]+[A-Za-z0-9_]{3,}[\s._-]*$", "", name, flags=re.IGNORECASE)

        # Strip a trailing scene release-group tag (e.g. ".x264-GROUP"), the
        # last hyphen-joined segment scene releases append before the
        # extension. Left in, it rides straight into the TMDB search query,
        # which returns zero results for an unrecognized trailing word. Only
        # applied when a dot survives elsewhere in the name, i.e. this still
        # looks like an unprocessed scene release with other metadata
        # segments - otherwise a plain hyphenated title with no other tags
        # (e.g. a bare "Spider-Man") would lose its second word.
        name = name.strip()
        if "." in name:
            name = re.sub(r"-[A-Za-z0-9]+$", "", name)

        # Remove extra stacked extensions (e.g. .mkv.zip.001)
        name = re.sub(r"\.(zip|7z|rar|mkv|avi|mp4)$", "", name, flags=re.IGNORECASE)

        # Strip whitespace left by noise patterns before parsing
        name = name.strip()

        # Extract season and episode if TV type
        season = None
        episode = None
        
        # Try SxxExx
        match_sxe = re.search(r"[Ss](\d{1,2})[Ee](\d{1,3})", name)
        if match_sxe:
            season = int(match_sxe.group(1))
            episode = int(match_sxe.group(2))
        else:
            # Try xxNxx (e.g. 5x08)
            match_cross = re.search(r"(?<!\d)(\d{1,2})x(\d{1,3})(?!\d)", name)
            if match_cross:
                season = int(match_cross.group(1))
                episode = int(match_cross.group(2))
            else:
                # Try Sxx - xx or Sxx_xx
                match_s_dash = re.search(r"(?<![a-zA-Z0-9])[Ss](\d{1,2})\s*[-_]\s*(\d{1,3})(?!\d)", name)
                if match_s_dash:
                    season = int(match_s_dash.group(1))
                    episode = int(match_s_dash.group(2))
                else:
                    # Try - xx at the end of name
                    match_dash = re.search(r"\s+[-_]\s+(\d{1,3})(?!\d)$", name)
                    if match_dash:
                        season = 1
                        episode = int(match_dash.group(1))
                    else:
                        # Try "Temporada X" or "Season X"
                        match_season = re.search(r"(?<![a-zA-Z0-9])(?:Temporada|Season)\s+(\d+)(?![a-zA-Z0-9])", name, re.IGNORECASE)
                        if match_season:
                            season = int(match_season.group(1))

        content_type = detect_hint_type(name)

        # For movies, the title always sits before the release year in every
        # convention seen so far - everything after it is quality/codec/
        # uploader metadata. The noise-pattern denylist above has to be
        # extended every time a new tag shows up (10Bit, NF, a two-part
        # uploader nick like "aurora45-xusman") and still misses the next
        # one; truncating at the year sidesteps the denylist entirely for
        # the common case where a year was actually found.
        if content_type == "movie" and year_prefix is not None:
            truncated = re.sub(r"[\s.\-_(\[]+$", "", year_prefix)
            if truncated.strip():
                name = truncated

        # Remove episode markers (e.g. "1x125", "S05E10") from the clean name
        if content_type == "tv":
            marker_pattern = (
                r"[Ss]\d{1,2}[Ee]\d{1,3}"
                r"|(?<!\d)\d{1,2}x\d{1,3}(?!\d)"
                r"|(?<![a-zA-Z0-9])[Ss]\d{1,2}\s*[-_]\s*\d{1,3}(?!\d)"
                r"|\s+[-_]\s+\d{1,3}(?!\d)$"
            )
            parts = re.split(marker_pattern, name, flags=re.IGNORECASE)
            if parts:
                before = parts[0].strip(" -_")
                if before:
                    name = before
                elif len(parts) > 1:
                    after = parts[1].strip(" -_")
                    if " - " in after:
                        name = after.split(" - ")[0].strip()
                    else:
                        name = after
            
            # Finally discard a trailing season-only tail the marker_pattern
            # above doesn't catch (e.g. "Vikingos - Temporada 3"). Scoped to
            # "Temporada"/"Season" specifically, not any " - " text, since a
            # real subtitle joined the same way (e.g. "Fullmetal Alchemist -
            # Brotherhood") must survive - discarding it here previously lost
            # "Brotherhood" and matched the wrong (unrelated) TMDB show.
            tail_match = re.search(r" - (?:Temporada|Season)\s+\d+.*$", name, re.IGNORECASE)
            if tail_match:
                name = name[:tail_match.start()]

        name = re.sub(r"\.(mkv|avi|mp4)$", "", name, flags=re.IGNORECASE)

        # Final cleanup: collapse whitespace, dots and underscores, but preserve
        # hyphens that join word characters (e.g. "Spider-Man").
        name = re.sub(r"[•·]+", " ", name)       # bullet/middle-dot → spaces
        name = re.sub(r"\.+", " ", name)         # dots → spaces
        name = re.sub(r"_+", " ", name)          # underscores → spaces
        name = re.sub(r"(?<![\w])-+|-+(?![\w])", " ", name)  # leading/trailing dashes → spaces
        name = re.sub(r"\s+", " ", name)
        name = name.strip()

        return {
            "tmdbid": tmdbid,
            "clean_name": name,
            "type": content_type,
            "season": season,
            "episode": episode,
            "year": year
        }
    
    def identify_by_tmdbid(self, tmdbid: int, content_type: str) -> dict:
        """Identify a movie or series by its TMDB ID."""
        
        if not tmdbid:
            raise ValueError("No TMDB ID provided.")
        
        # TMDB does not provide unique IDs for movies and series, so we need to check both types.
        
        try_movie = None
        try_series = None
        
        if content_type == "movie":
            try_movie = self.get_movie(tmdbid)
            if try_movie:
                return try_movie
        elif content_type == "tv":
            try_series = self.get_tv(tmdbid)
            if try_series:
                return try_series
        
        # Fallback: if type-specific search yielded nothing, try both types
        if not try_movie and not try_series:
            try_movie = self.get_movie(tmdbid)
            try_series = self.get_tv(tmdbid)
        
        if try_movie:
            return try_movie
        elif try_series:
            return try_series
        
        return {}

    @staticmethod
    @lru_cache(maxsize=512)
    def _cached_get_tv(tmdbid: int, language: str):
        try:
            info = tmdb.TV(tmdbid).info(language=language)
            info["media_type"] = "tv"
            return info
        except Exception as e:
            logger.error(f"Invalid TMDB ID for series: {tmdbid}. Error: {e}")
            return None

    @staticmethod
    @lru_cache(maxsize=512)
    def _cached_get_movie(tmdbid: int, language: str):
        try:
            info = tmdb.Movies(tmdbid).info(language=language)
            info["media_type"] = "movie"
            return info
        except Exception as e:
            logger.error(f"Invalid TMDB ID for movie: {tmdbid}. Error: {e}")
            return None

    def get_tv(self, tmdbid: int):
        res = self._cached_get_tv(tmdbid, TMDB_CONTENT_LANGUAGE)
        return dict(res) if res else None

    def get_movie(self, tmdbid: int):
        res = self._cached_get_movie(tmdbid, TMDB_CONTENT_LANGUAGE)
        return dict(res) if res else None
            
    @staticmethod
    def _normalize(text: str) -> str:
        """Normalize a title for fuzzy matching.

        Strips diacritics (via NFKD decomposition) and maps special
        separators (·, –, —, -) to spaces, so that variants such as
        ``Pokémon``/``Pokemon`` or ``WALL·E``/``Wall-E``/``Wall E``
        collapse to the same comparable form.
        """
        if not text:
            return ""
        decomposed = unicodedata.normalize("NFKD", text)
        stripped = "".join(c for c in decomposed if not unicodedata.combining(c))
        for sep in ("·", "•", "–", "—", "-"):
            stripped = stripped.replace(sep, " ")
        return re.sub(r"\s+", " ", stripped).strip().lower()

    MIN_SIM = 0.4
    POP_WEIGHT = 0.15

    @staticmethod
    def _best_match(results: list, clean_name: str, media_type: str) -> dict | None:
        """Return the result whose title most closely matches clean_name.

        Only candidates with a title similarity of at least MIN_SIM are
        considered at all; among those, ranking is ``similarity + POP_WEIGHT *
        log10(popularity + 1)`` rather than similarity alone. Compares against
        both the localized title and ``original_title``/``original_name``:
        scene-release filenames use the original (usually English) title,
        which TMDB_CONTENT_LANGUAGE can localize away from (e.g. "The Wire" ->
        "Bajo escucha" in es-ES), so matching on the localized title alone can
        pick an unrelated result that happens to look more like the filename.

        The popularity term exists because TMDB often has a near-exact but
        obscure/wrong title (e.g. "Parasites", popularity ~1) alongside the
        real, far more popular result under a translated or slightly
        different title (e.g. "Parásitos", popularity ~40) that scores a
        little lower on raw text similarity. Using log10(popularity) keeps
        this from swamping similarity for run-of-the-mill popularity gaps
        (e.g. a sequel's popularity naturally varying a bit from the next
        one's must not flip which one "Kung Fu Panda 3" resolves to) while
        still letting a large, genuine gap - orders of magnitude, as with
        mismatched-language duplicates - break a close-but-not-exact tie in
        the right direction.
        """
        if not results:
            return None

        query_norm = TMDB._normalize(clean_name)

        def sim(r: dict) -> float:
            title = TMDB._normalize(r.get("title") or r.get("name") or "")
            original = TMDB._normalize(r.get("original_title") or r.get("original_name") or "")
            s = difflib.SequenceMatcher(None, query_norm, title).ratio()
            if original:
                s = max(s, difflib.SequenceMatcher(None, query_norm, original).ratio())
            return s

        candidates = [(sim(r), float(r.get("popularity") or 0), r) for r in results]
        candidates = [c for c in candidates if c[0] >= TMDB.MIN_SIM]

        if not candidates:
            return None

        best_s, _, best = max(
            candidates,
            key=lambda t: t[0] + TMDB.POP_WEIGHT * math.log10(t[1] + 1),
        )

        best["media_type"] = media_type
        best["_match_score"] = best_s
        return best

    def _search_and_match(self, query: str, content_type: str, year: int | None = None) -> dict | None:
        """Search TMDB for ``query`` and return the best matching result.

        ``year``, when known, is passed as TMDB's own year filter rather than
        appended to the query text: TMDB search is a literal text match, and a
        year with no matching digits anywhere in the title reliably returns
        zero results instead of just being ignored.
        """
        search = tmdb.Search()
        response = None

        if content_type == "movie":
            kwargs = {"year": year} if year else {}
            search.movie(query=query, language=TMDB_CONTENT_LANGUAGE, **kwargs)
            response = self._best_match(search.results, query, "movie")
        elif content_type == "tv":
            kwargs = {"first_air_date_year": year} if year else {}
            search.tv(query=query, language=TMDB_CONTENT_LANGUAGE, **kwargs)
            response = self._best_match(search.results, query, "tv")

        if not response:
            search.multi(query=query, language=TMDB_CONTENT_LANGUAGE)
            response = self._best_match(search.results, query,
                                        search.results[0].get("media_type", "movie")
                                        if search.results else "movie")

        return response

    def identify_by_filename(self, filename: str) -> dict:
        """Identify a movie or series by its filename."""
        file = self.clean_filename(filename)
        response = None
        if file["tmdbid"]:
            response = self.identify_by_tmdbid(file["tmdbid"], file["type"])

        if not response:
            response = self._search_and_match(file["clean_name"], file["type"], file["year"])

            if not response:
                normalized = self._normalize(file["clean_name"])
                if normalized and normalized != file["clean_name"].lower():
                    response = self._search_and_match(normalized, file["type"], file["year"])

        return response or {}

    @staticmethod
    def _format_search_result(r: dict) -> dict | None:
        """Shape a raw TMDB result into the compact form the frontend expects."""
        m_type = r.get("media_type")
        if not m_type or m_type not in ["movie", "tv"]:
            return None
        title = r.get("title") if m_type == "movie" else r.get("name")
        release_date = r.get("release_date") if m_type == "movie" else r.get("first_air_date")
        year = release_date.split("-")[0] if release_date else "Unknown"
        return {
            "id": r.get("id"),
            "title": title,
            "media_type": m_type,
            "year": year,
            "poster_path": r.get("poster_path"),
            "overview": r.get("overview")
        }

    def search(self, query: str, media_type: str = "multi") -> list:
        """Search movies/series on TMDB by title, or by TMDB ID when the query is numeric."""
        query = (query or "").strip()
        if query.isdigit():
            return self._search_by_id(int(query), media_type)

        search = tmdb.Search()
        results = []
        if media_type == "movie":
            search.movie(query=query, language=TMDB_CONTENT_LANGUAGE)
            results = getattr(search, 'results', [])
            for r in results:
                r["media_type"] = "movie"
        elif media_type == "tv":
            search.tv(query=query, language=TMDB_CONTENT_LANGUAGE)
            results = getattr(search, 'results', [])
            for r in results:
                r["media_type"] = "tv"
        else:
            search.multi(query=query, language=TMDB_CONTENT_LANGUAGE)
            results = getattr(search, 'results', [])

        return [f for r in results if (f := self._format_search_result(r))]

    def _search_by_id(self, tmdb_id: int, media_type: str) -> list:
        """Look up a TMDB item directly by its ID, honoring the requested media type."""
        raw = []
        if media_type in ("movie", "multi"):
            movie = self.get_movie(tmdb_id)
            if movie:
                raw.append(movie)
        if media_type in ("tv", "multi"):
            tv = self.get_tv(tmdb_id)
            if tv:
                raw.append(tv)
        return [f for r in raw if (f := self._format_search_result(r))]
            
