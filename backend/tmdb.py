import requests
import os
import re
import difflib
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
                re.search(r"(?<![a-zA-Z0-9])Season\s+\d+(?![a-zA-Z0-9])", n, re.IGNORECASE)):
                return "tv"
            return "movie"
        
        # Remove main extension
        name = os.path.splitext(filename)[0]

        # Check for TMDB id
        tmdbid = None
        match = re.search(r"\[tmdbid-(\d+)\]", name, re.IGNORECASE)
        if match:
            tmdbid = int(match.group(1))
            name = re.sub(r"\[tmdbid-\d+\]", "", name)

        # Remove common noise patterns (resolution, quality, part numbers, etc.)
        noise_patterns = [
            r"\b\d{3,4}p\b",         # 1080p, 720p
            r"\bBlu[- ]?ray\b",
            r"\bHEVC\b",
            r"\bWEB[- ]?DL\b",
            r"\bHDRip\b",
            r"\bDVDRip\b",
            r"\.part\d+",            # .part1, .part2
            r"\.\d+$",               # .001, .002
            r"\[.*?\]",              # [anything]
            r"\(.*?\)",              # (anything)
            r"\bAC3\b",              # 🔥 remove AC3
            r"\bDTS\b",
            r"\bXviD\b",
            r"\bx264\b",
            r"\bH\.?264\b",
            r"\bAAC\b",
            r"\bMP3\b",
        ]

        for pattern in noise_patterns:
            name = re.sub(pattern, "", name, flags=re.IGNORECASE)

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

        # Extract a scene-release year (e.g. "The.Matrix.1999...") into its own
        # field rather than leaving it in the free-text query: TMDB search is
        # a literal text match, and an appended year with no matching digits
        # in the title reliably returns zero results instead of being ignored.
        year = None
        year_match = re.search(r"(?<!\d)(19\d{2}|20\d{2})(?!\d)", name)
        if year_match:
            year = int(year_match.group(1))
            name = name[:year_match.start()] + name[year_match.end():]

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
            
            # Finally discard any trailing " - " text (e.g. "Vikingos - Temporada 3")
            name = re.split(r" - ", name)[0]

        name = re.sub(r"\.(mkv|avi|mp4)$", "", name, flags=re.IGNORECASE)

        # Final cleanup: collapse whitespace, dots and underscores, but preserve
        # hyphens that join word characters (e.g. "Spider-Man").
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
        for sep in ("·", "–", "—", "-"):
            stripped = stripped.replace(sep, " ")
        return re.sub(r"\s+", " ", stripped).strip().lower()

    @staticmethod
    def _best_match(results: list, clean_name: str, media_type: str) -> dict | None:
        """Return the result whose title most closely matches clean_name.

        Scoring combines title similarity (primary) with popularity (tiebreaker).
        A minimum similarity of 0.4 is required to accept any match. Compares
        against both the localized title and ``original_title``/``original_name``:
        scene-release filenames use the original (usually English) title, which
        TMDB_CONTENT_LANGUAGE can localize away from (e.g. "The Wire" -> "Bajo
        escucha" in es-ES), so matching on the localized title alone can pick an
        unrelated result that happens to look more like the filename.
        """
        if not results:
            return None

        query_norm = TMDB._normalize(clean_name)

        def score(r: dict) -> tuple:
            title = TMDB._normalize(r.get("title") or r.get("name") or "")
            original = TMDB._normalize(r.get("original_title") or r.get("original_name") or "")
            sim = difflib.SequenceMatcher(None, query_norm, title).ratio()
            if original:
                sim = max(sim, difflib.SequenceMatcher(None, query_norm, original).ratio())
            pop = float(r.get("popularity") or 0)
            return (sim, pop)

        ranked = sorted(results, key=score, reverse=True)
        best = ranked[0]
        best_sim = score(best)[0]

        if best_sim < 0.4:
            return None

        best["media_type"] = media_type
        best["_match_score"] = best_sim
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
            
