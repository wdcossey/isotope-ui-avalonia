// A small C# lexer for text transforms: which characters are code (not comments, strings or char literals), and
// the extent of a member declaration. Shared by tools/sync (drop rules) and tools/rewrite-upstream.mjs (Spike A), so
// there is one implementation. Text-based by decision (#4): the Roslyn step (lib/roslyn.mjs) complements it for new
// rules, and re-expressing the existing drops with it gave the same output (tools/measure-roslyn.mjs).

// Marks which characters of C# source are code: not in a comment, string or char literal (interpolation holes count
// as code). Handles //, /* */, '…', "…", @"…", $"…", $@"…"/@$"…" and raw """…""" strings.
export function codeMask(text) {
  const n = text.length;
  const code = new Uint8Array(n);
  let i = 0;
  function charLit() {
    i++;
    while (i < n && text[i] !== "'") { if (text[i] === '\\') i++; i++; }
    i++;
  }
  function str() {
    let dollars = 0, verbatim = false;
    while (text[i] === '$' || text[i] === '@') { if (text[i] === '$') dollars++; else verbatim = true; i++; }
    let q = 0;
    while (text[i + q] === '"') q++;
    if (q >= 3 && !verbatim) {
      i += q;
      const close = '"'.repeat(q);
      while (i < n) {
        if (text.startsWith(close, i)) { i += q; return; }
        if (dollars && text[i] === '{') {
          let k = 0;
          while (text[i + k] === '{') k++;
          i += k;
          if (k >= dollars) { run(true); i += dollars; }
          continue;
        }
        i++;
      }
      return;
    }
    i++;
    while (i < n) {
      const c = text[i];
      if (verbatim) {
        if (c === '"') { if (text[i + 1] === '"') { i += 2; continue; } i++; return; }
      } else {
        if (c === '\\') { i += 2; continue; }
        if (c === '"' || c === '\n') { i++; return; }
      }
      if (dollars && c === '{') {
        if (text[i + 1] === '{') { i += 2; continue; }
        i++;
        run(true);
        i++;
        continue;
      }
      i++;
    }
  }
  // Scans code; in an interpolation hole, returns at its closing brace (not consumed).
  function run(hole) {
    let depth = 0;
    while (i < n) {
      const c = text[i], d = text[i + 1];
      if (c === '/' && d === '/') { while (i < n && text[i] !== '\n') i++; continue; }
      if (c === '/' && d === '*') { const e = text.indexOf('*/', i + 2); i = e < 0 ? n : e + 2; continue; }
      // A preprocessor line (#region Users' settings, #error "…") holds no code and no literals.
      if (c === '#' && !hole && /^[ \t]*$/.test(text.slice(text.lastIndexOf('\n', i - 1) + 1, i))) {
        while (i < n && text[i] !== '\n') i++;
        continue;
      }
      if (c === "'") { charLit(); continue; }
      if (c === '"' || ((c === '$' || c === '@') && /^[$@]*"/.test(text.slice(i, i + 6)))) { str(); continue; }
      if (hole) {
        if (c === '(' || c === '[' || c === '{') depth++;
        else if (c === ')' || c === ']') depth--;
        else if (c === '}') { if (depth === 0) return; depth--; }
        else if (c === ':' && depth === 0 && d !== ':' && text[i - 1] !== ':') {
          while (i < n && text[i] !== '}') i++; // format clause
          return;
        }
      }
      code[i] = 1;
      i++;
    }
  }
  run(false);
  return code;
}

// The [start, end) character range of the member whose declaration starts at `at`: from its first doc-comment or
// attribute line to the end of the line its body ends on.
export function memberRange(text, code, at) {
  const n = text.length;
  const nextCode = (k) => { while (k < n && (!code[k] || /\s/.test(text[k]))) k++; return k; };
  // Index of the ';' ending the construct that starts at k, at bracket depth 0.
  const toSemicolon = (k) => {
    let depth = 0;
    for (; k < n; k++) {
      if (!code[k]) continue;
      const c = text[k];
      if ('([{'.includes(c)) depth++;
      else if (')]}'.includes(c)) depth--;
      else if (c === ';' && depth === 0) return k;
    }
    throw new Error('no end of member');
  };
  const matchBrace = (k) => {
    let depth = 0;
    for (; k < n; k++) {
      if (!code[k]) continue;
      if (text[k] === '{') depth++;
      else if (text[k] === '}' && --depth === 0) return k;
    }
    throw new Error('unbalanced braces');
  };
  let end = -1, paren = 0;
  for (let k = at; k < n && end < 0; k++) {
    if (!code[k]) continue;
    const c = text[k];
    if (c === '(' || c === '[') paren++;
    else if (c === ')' || c === ']') paren--;
    else if (paren === 0 && c === '{') {
      end = matchBrace(k);
      const j = nextCode(end + 1);
      if (text[j] === '=' && text[j + 1] !== '>') end = toSemicolon(j); // property initializer
    } else if (paren === 0 && c === '=') {
      // '==', '!=', '<=', '>=' (an operator declaration's name) are not the start of a body or initializer.
      if (text[k + 1] === '=' || '=!<>'.includes(text[k - 1])) { if (text[k + 1] === '=') k++; continue; }
      end = toSemicolon(k);
    }
    else if (paren === 0 && c === ';') end = k;
  }
  if (end < 0) throw new Error('no end of member');
  let lineEnd = text.indexOf('\n', end);
  lineEnd = lineEnd < 0 ? n : lineEnd;
  for (let k = end + 1; k < lineEnd; k++)
    if (code[k] && !/\s/.test(text[k])) throw new Error('code follows the member on its last line');
  let start = text.lastIndexOf('\n', at - 1) + 1;
  if (text.slice(start, at).trim() !== '') throw new Error('code precedes the member on its first line');
  for (;;) {
    if (start === 0) break;
    const prevStart = text.lastIndexOf('\n', start - 2) + 1;
    const prev = text.slice(prevStart, start).trim();
    if (prev.startsWith('///') || prev.startsWith('[')) start = prevStart;
    else break;
  }
  return [start, lineEnd];
}

// Blanks [start, end) line by line, keeping each line ending so later line numbers don't move.
export const blank = (text, start, end) => text.slice(0, start) + text.slice(start, end).replace(/[^\r\n]+/g, '') + text.slice(end);

/**
 * Drops the one member whose declaration the regex matches in code (not in a comment or string), with its doc
 * comment and attributes and its body ({...}, => ...; or = ...;). The member's lines are left blank, so later line
 * numbers don't move. Returns {text, found, error}; on an error (no match, several, or a shape the lexer can't bound)
 * the text is returned unchanged.
 */
export function dropMember(text, re) {
  const code = codeMask(text);
  const found = [...text.matchAll(new RegExp(re.source, re.flags.includes('g') ? re.flags : re.flags + 'g'))].filter((m) => code[m.index]);
  if (found.length !== 1) return { text, found: found.length, error: 'matched ' + found.length + ' times in code, expected 1' };
  try {
    const [s, e] = memberRange(text, code, found[0].index);
    return { text: blank(text, s, e), found: 1 };
  } catch (e) {
    return { text, found: 1, error: e.message };
  }
}
