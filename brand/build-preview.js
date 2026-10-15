const fs = require('fs');
const dir = 'C:/Users/davem/repos/Quantos/brand';

const raw = fs.readFileSync(dir + '/quantos-icon.svg', 'utf8');

// Drop the squircle background group so the mark sits on transparency.
// The Background group contains only rect/circle elements, so a non-greedy
// match to the first </g> is the group's own close.
const stripped = raw.replace(/\s*<!-- 1\. MAIN BACKGROUND SQUIRCLE -->[\s\S]*?<g id="Background">[\s\S]*?<\/g>/, '');
if (stripped === raw) {
  console.error('WARNING: background group not removed');
}

// Crop the viewBox to the artwork itself (arcs + cards + orb), excluding the
// squircle margin, so the mark fills its frame and can sit as a capital Q.
const body = stripped.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '');
// The artwork (arcs at R=407+54, orb to r=108.5 with glow) is centred near
// (626, 624), not on the squircle's 626,626 box centre — the previous frame
// sat low-right and made the Q read as sitting low.
const symbol = `<symbol id="mark" viewBox="120 120 1020 1020">${body}</symbol>`;

let html = fs.readFileSync(dir + '/preview.template.html', 'utf8');
html = html.replace('<!--MARK-->', symbol);

// Stamp every build so a stale cached copy is obvious at a glance.
const stamp = new Date().toISOString().replace('T', ' ').slice(0, 19) + ' UTC';
html = html.replace('<p class="kicker">', `<p class="build-stamp">build ${stamp}</p>\n    <p class="kicker">`);

fs.writeFileSync(dir + '/preview.html', html);

console.log('build stamp:', stamp);

console.log('background stripped:', stripped !== raw);
console.log('inlined icon:', body.length, 'bytes');
console.log('mark references:', (html.match(/href="#mark"/g) || []).length);
console.log('external refs:', (html.match(/src="quantos[^"]*"/g) || []).length);