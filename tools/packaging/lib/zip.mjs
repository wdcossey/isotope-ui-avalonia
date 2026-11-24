// A minimal ZIP reader for .nupkg/.snupkg files (#52): stored and deflated entries, no ZIP64 (a package is far below
// 4 GB). Node has zlib but no ZIP support, and the packaging scripts use the standard library only.
import { inflateRawSync } from 'node:zlib';

const EOCD = 0x06054b50, CENTRAL = 0x02014b50, LOCAL = 0x04034b50;

/** The entries of a ZIP archive: Map(name -> Buffer), in archive order. Names are as stored (forward slashes). */
export function readZip(buf) {
  // The end-of-central-directory record is the last 22 bytes, unless the archive has a comment (up to 64 KiB).
  let eocd = -1;
  if (buf.length < 22) throw new Error('not a ZIP archive (' + buf.length + ' bytes)');
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 22 - 0xffff); i--) {
    if (buf.readUInt32LE(i) === EOCD) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('not a ZIP archive (no end-of-central-directory record)');
  const count = buf.readUInt16LE(eocd + 10);
  let at = buf.readUInt32LE(eocd + 16);
  const entries = new Map();
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(at) !== CENTRAL) throw new Error('corrupt ZIP: bad central directory entry ' + n);
    const method = buf.readUInt16LE(at + 10);
    const compressed = buf.readUInt32LE(at + 20);
    const size = buf.readUInt32LE(at + 24);
    const nameLen = buf.readUInt16LE(at + 28), extraLen = buf.readUInt16LE(at + 30), commentLen = buf.readUInt16LE(at + 32);
    const local = buf.readUInt32LE(at + 42);
    const name = buf.toString('utf8', at + 46, at + 46 + nameLen);
    at += 46 + nameLen + extraLen + commentLen;
    if (buf.readUInt32LE(local) !== LOCAL) throw new Error('corrupt ZIP: bad local header for ' + name);
    const start = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
    const raw = buf.subarray(start, start + compressed);
    let data;
    if (method === 0) data = Buffer.from(raw);
    else if (method === 8) data = inflateRawSync(raw);
    else throw new Error('unsupported ZIP compression method ' + method + ' for ' + name);
    if (data.length !== size) throw new Error('corrupt ZIP: ' + name + ' is ' + data.length + ' bytes, expected ' + size);
    if (!name.endsWith('/')) entries.set(name, data);
  }
  return entries;
}
