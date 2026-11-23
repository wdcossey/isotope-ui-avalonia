// UPSTREAM.lock: "key=value" lines and "#" comments. Updating keeps comments, order and unknown keys.
export function parseLock(text) {
  const values = {};
  for (const line of text.split(/\r?\n/)) {
    const m = /^([A-Za-z_]+)=(.*)$/.exec(line);
    if (m) values[m[1]] = m[2].trim();
  }
  if (!/^[0-9a-f]{40}$/.test(values.baseline ?? '')) throw new Error('UPSTREAM.lock: baseline must be a full 40-character commit sha');
  if (!values.repo) throw new Error('UPSTREAM.lock: no repo= line');
  return values;
}

export function updateLock(text, updates) {
  const seen = new Set();
  const lines = text.replace(/\r\n/g, '\n').split('\n').map((line) => {
    const m = /^([A-Za-z_]+)=/.exec(line);
    if (m && m[1] in updates) { seen.add(m[1]); return m[1] + '=' + updates[m[1]]; }
    return line;
  });
  if (lines.at(-1) === '') lines.pop();
  for (const k of Object.keys(updates)) if (!seen.has(k)) lines.push(k + '=' + updates[k]);
  return lines.join('\n') + '\n';
}
