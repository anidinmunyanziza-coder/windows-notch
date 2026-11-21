// Extracts the Defaults schema from the original Swift source (the original/
// submodule) into docs/notes/settings-schema.{json,md}. Run: node tools/settings-parse.cjs
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..');
const src = fs.readFileSync(path.join(root, 'original', 'DynamicIsland', 'models', 'Constants.swift'), 'utf8');
const start = src.indexOf('extension Defaults.Keys');
const body = src.slice(start);

// collect MARK positions
const marks = [];
const markRe = /\/\/\s*MARK:\s*-?\s*([^\n\r]+)/g;
let mm;
while ((mm = markRe.exec(body))) marks.push({ pos: mm.index, name: mm[1].trim() });

function sectionAt(pos) {
  let name = 'General';
  for (const m of marks) { if (m.pos < pos) name = m.name; else break; }
  return name;
}

const out = [];
const keyRe = /static let (\w+)\s*=\s*Key<([^>]*(?:\[[^\]]*\][^>]*)?)>\s*\(\s*"([^"]+)"\s*,\s*default:\s*/g;
let m;
while ((m = keyRe.exec(body))) {
  const keyPos = m.index;
  let i = keyRe.lastIndex, depth = 1, inStr = false, strCh = '';
  for (; i < body.length; i++) {
    const c = body[i];
    if (inStr) { if (c === '\\') { i++; continue; } if (c === strCh) inStr = false; continue; }
    if (c === '"' || c === "'") { inStr = true; strCh = c; continue; }
    if (c === '(') depth++;
    if (c === ')') { depth--; if (depth === 0) break; }
  }
  const def = body.slice(keyRe.lastIndex, i).replace(/\s+/g, ' ').trim();
  out.push({ key: m[3], swiftName: m[1], type: m[2].trim(), default: def, section: sectionAt(keyPos) });
  keyRe.lastIndex = i + 1;
}

const seen = new Set();
const uniq = out.filter(o => !seen.has(o.key) && seen.add(o.key));
fs.writeFileSync(path.join(root, 'docs', 'notes', 'settings-schema.json'), JSON.stringify(uniq, null, 2));
let md = '# Defaults schema (extracted from Constants.swift)\n\n| Section | Key | Type | Default |\n|---|---|---|---|\n';
for (const o of uniq) md += `| ${o.section} | ${o.key} | \`${o.type}\` | \`${o.default.replace(/\|/g, '\\|')}\` |\n`;
fs.writeFileSync(path.join(root, 'docs', 'notes', 'settings-schema.md'), md);

const bySec = {};
for (const o of uniq) bySec[o.section] = (bySec[o.section] || 0) + 1;
console.log('keys', uniq.length);
console.log(bySec);
const unresolved = uniq.filter(o => o.type === '');
console.log('unresolved type', unresolved.length);
