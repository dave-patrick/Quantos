// Fetch the source fonts from Google Fonts into fonts/.
// The binaries are NOT committed - they are third-party redistributions and
// only needed to regenerate the wordmark, not to use it.
const fs = require('fs');
const path = require('path');
const cfg = require('./set.js');

const dir = path.join(__dirname, 'fonts');
const UA = 'Mozilla/5.0';   // plain UA gets us real TrueType, not EOT or woff2

async function main() {
  fs.mkdirSync(dir, { recursive: true });
  for (const o of cfg.options) {
    const dest = path.join(dir, o.file);
    if (fs.existsSync(dest)) { console.log('have    ' + o.file); continue; }

    const family = o.name.replace(/ /g, '+');
    const css = await (await fetch(`https://fonts.googleapis.com/css2?family=${family}`, {
      headers: { 'User-Agent': UA }
    })).text();
    const url = (css.match(/url\((https:\/\/[^)]+\.ttf)\)/) || [])[1];
    if (!url) throw new Error(`no ttf url for ${o.name}`);

    const buf = Buffer.from(await (await fetch(url)).arrayBuffer());
    const sig = buf.readUInt32BE(0).toString(16);
    if (sig !== '00010000') throw new Error(`${o.file} is not TrueType (sig 0x${sig})`);
    fs.writeFileSync(dest, buf);
    console.log('fetched ' + o.file + '  ' + buf.length + ' bytes');
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });