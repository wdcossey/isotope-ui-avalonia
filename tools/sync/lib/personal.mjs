// Personal-data check of generated (vendored) files (issue #10): after transforms, every generated text file is scanned
// for emails, user-folder paths and drive-letter paths, so CLAUDE.md's no-personal-data rule holds for vendored code
// without a person reading every sync. A hit fails the sync and --check. Fix it with a transform rule; only a genuine
// example that is not personal data goes in the allowlist (tools/sync/personal-data.json), as the vendored path and
// the sha256 of the exact line text (trimmed). A hash, so the tracked allowlist doesn't itself carry the paths the
// repo-wide scan in CLAUDE.md looks for. An entry that matches no hit fails too, so the allowlist can't go stale.
// allowlist: {"allow": [{"file", "sha256", "why"}]}
import { createHash } from 'node:crypto';

// Reserved names (RFC 2606): example.com, .net and .org with their subdomains, and the .test, .example, .invalid and
// .localhost top-level domains. Other example.* domains (example.io) can be registered, so they count.
const harmlessDomain = (d) => /(^|\.)example\.(com|net|org)$/i.test(d) || /\.(test|example|invalid|localhost)$/i.test(d);

export const PATTERNS = [
  // The lookbehind keeps a long line linear: the local part can only start where a run of its characters starts.
  { kind: 'email', needs: '@', re: /(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,})(?![A-Za-z0-9-])/g, harmless: (m) => harmlessDomain(m[1]) },
  // A user folder at the root of a path, then a name: "Users" (any case) after a drive letter, a UNC share, a file URL
  // or a leading separator, or a lowercase "home" after a leading slash. Not a folder of that name inside a longer path
  // (a "Users" folder under src) or a web URL, a relative one, or "Home/End" in prose.
  { kind: 'user-folder path', re: /(?<![\w.:\\/-])(?:(?:file:\/{2,}(?:[A-Za-z]:)?|[A-Za-z]:|[\\/]{2,}[^\s\\/]+)?[\\/]+[Uu][Ss][Ee][Rr][Ss]|(?:file:\/\/)?\/home)[\\/]+[^\s\\/"'\x60<>|]+/g },
  { kind: 'drive-letter path', re: /(?<![\w])[A-Za-z]:[\\/]/g },
];

export const lineHash = (line) => createHash('sha256').update(line.trim(), 'utf8').digest('hex');

export function loadAllowlist(json) {
  if (!json || !Array.isArray(json.allow)) throw new Error('personal-data allowlist: expected {"allow": [...]}');
  const seen = new Set();
  return json.allow.map((e, i) => {
    const where = 'personal-data allowlist entry #' + (i + 1);
    if (!e || typeof e.file !== 'string' || !e.file) throw new Error(where + ': needs a "file" (the vendored path)');
    if (typeof e.sha256 !== 'string' || !/^[0-9a-f]{64}$/.test(e.sha256)) throw new Error(where + ': "sha256" must be 64 lowercase hex digits');
    if (typeof e.why !== 'string' || !e.why.trim()) throw new Error(where + ': needs a "why" (why it is an example, not personal data)');
    const key = e.file + '\0' + e.sha256;
    if (seen.has(key)) throw new Error(where + ': duplicate of an earlier entry');
    seen.add(key);
    return { file: e.file, sha256: e.sha256, why: e.why };
  });
}

/** Hits in one text: [{line (1-based), kind, text (the match), lineText}], one per match. Linear in the line length. */
export function scanText(text) {
  const hits = [];
  const lines = text.split('\n');
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].replace(/\r$/, '');
    for (const p of PATTERNS) {
      if (p.needs && !line.includes(p.needs)) continue;
      p.re.lastIndex = 0;
      for (const m of line.matchAll(p.re)) if (!p.harmless?.(m)) hits.push({ line: i + 1, kind: p.kind, text: m[0], lineText: line });
    }
  }
  return hits;
}

const PRINTABLE_RUN = /[\x20-\x7e]{4,}/g;
const runs = (s) => s.match(PRINTABLE_RUN) ?? [];

/**
 * The text to scan, and whether it is binary. Text has no NUL byte: UTF-8 when the bytes are valid UTF-8, UTF-16 when
 * they start with its byte-order mark, otherwise Latin-1, which keeps every ASCII character where it was. Anything else
 * is binary, or UTF-16 with no mark (ASCII in UTF-16 is valid UTF-8, NULs included): its runs of four or more printable ASCII characters, read as single bytes and as
 * UTF-16 at both byte alignments, one run per line (like "strings"), so random bytes rarely look like a hit.
 */
export function decodeForScan(buf) {
  const even = (b) => b.subarray(0, b.length - (b.length % 2));
  if (buf[0] === 0xff && buf[1] === 0xfe) return { text: even(buf.subarray(2)).toString('utf16le'), binary: false };
  if (buf[0] === 0xfe && buf[1] === 0xff) return { text: Buffer.from(even(buf.subarray(2))).swap16().toString('utf16le'), binary: false };
  if (!buf.includes(0)) {
    const utf8 = buf.toString('utf8');
    return { text: Buffer.from(utf8, 'utf8').equals(buf) ? utf8 : buf.toString('latin1'), binary: false };
  }
  const utf16 = (b) => (b.length % 2 ? Buffer.concat([b, Buffer.alloc(1)]) : b).toString('utf16le'); // padded, so the last character stays
  const strings = [buf.toString('latin1'), utf16(buf), utf16(buf.subarray(1))].flatMap(runs);
  return { text: strings.join('\n'), binary: true };
}

/**
 * Checks generated files (Map: vendor-relative path -> Buffer) against the allowlist, every file whatever its encoding
 * (decodeForScan; in a binary file the "line" is the printable string it found). Errors name the file, line, kind and
 * line hash, never the matched text, so a CI log doesn't repeat it. Returns {errors, hits (all hits, allowlisted or not)}.
 */
export function checkPersonalData(files, allow = []) {
  const errors = [];
  const hits = [];
  const used = new Set();
  const allowed = new Map(allow.map((e) => [e.file + '\0' + e.sha256, e]));
  for (const [p, buf] of [...files].sort(([a], [b]) => (a < b ? -1 : 1))) {
    const { text, binary } = decodeForScan(buf);
    const where = (line) => p + (binary ? ' (binary file, printable string ' + line + ')' : ':' + line);
    const reported = new Set();
    for (const h of scanText(text)) {
      const sha256 = lineHash(h.lineText);
      const entry = allowed.get(p + '\0' + sha256);
      hits.push({ file: p, ...h, sha256, allowed: !!entry });
      if (entry) { used.add(entry); continue; }
      if (reported.has(h.line)) continue; // one error per line, naming its first hit
      reported.add(h.line);
      errors.push('personal data in ' + where(h.line) + ' (' + h.kind + '). Fix it with a transform rule in ' +
        'tools/sync/transforms.json, never by hand. Only if it is a genuine example, not personal data, allowlist the line in ' +
        'tools/sync/personal-data.json: {"file": ' + JSON.stringify(p) + ', "sha256": "' + sha256 + '", "why": "<why it is an example>"}');
    }
  }
  for (const e of allow) if (!used.has(e)) errors.push('personal-data allowlist entry for ' + e.file + ' (' + e.why + ') matches no line of the generated files: remove it from tools/sync/personal-data.json');
  return { errors, hits };
}
