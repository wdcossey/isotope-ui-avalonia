// Path patterns for the tier map. Paths are upstream repo-relative, with forward slashes.
// "*" matches within one path segment, "?" one character, "**" any number of segments ("a/**" matches
// everything under a/, "a/**/b.cs" matches a/b.cs and a/x/y/b.cs).

export function globToRegExp(glob) {
  let re = '';
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*') {
      const atStart = i === 0 || glob[i - 1] === '/';
      const atEnd = i + 2 === glob.length;
      const slashNext = glob[i + 2] === '/';
      if (atStart && slashNext) { re += '(?:.*/)?'; i += 2; continue; } // "**/": zero or more whole segments
      if (atStart && atEnd) { re += '.*'; i += 1; continue; }            // trailing "**": everything below
      throw new Error('"**" must be a whole path segment: ' + glob);
    }
    if (c === '*') re += '[^/]*';
    else if (c === '?') re += '[^/]';
    else re += c.replace(/[.+^${}()|[\]\\]/g, '\\$&');
  }
  return new RegExp('^' + re + '$');
}
