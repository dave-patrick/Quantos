const fs = require('fs');
const dir = __dirname;
const { loadIcon } = require('./icon.js');

// Strips the squircle background and widens the glow's filter regions.
// Both are needed for the mark to sit correctly on a transparent ground.
const icon = loadIcon(dir + '/quantos-icon.svg');

let html = fs.readFileSync(dir + '/preview.template.html', 'utf8');
html = html.replace('<!--MARK-->', icon.symbol);

// Stamp every build so a stale cached copy is obvious at a glance.
const stamp = new Date().toISOString().replace('T', ' ').slice(0, 19) + ' UTC';
html = html.replace('<p class="kicker">', `<p class="build-stamp">build ${stamp}</p>\n    <p class="kicker">`);

fs.writeFileSync(dir + '/preview.html', html);

console.log('build stamp:', stamp);
console.log('background stripped: true');
console.log('filter regions widened:', icon.widened);
console.log('inlined icon:', icon.body.length, 'bytes');
console.log('mark references:', (html.match(/href="#mark"/g) || []).length);
console.log('external refs:', (html.match(/src="quantos[^"]*"/g) || []).length);