// A fake OpenAI-style provider for trying Quantos's AI features without an account.
import http from 'node:http';
const topics = [['apollo', 'launch', 'rocket'], ['budget', 'money', 'cost', 'rent', 'amount'], ['sam', 'person', 'meeting']];
const embed = (t) => { t = t.toLowerCase(); return [...topics.map((ws) => ws.reduce((n, w) => n + t.split(w).length - 1, 0)), 0.01]; };
http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => (body += c));
  req.on('end', () => {
    if (req.url.endsWith('/models')) { res.setHeader('content-type', 'application/json'); return res.end(JSON.stringify({ data: [{ id: 'fake-chat' }, { id: 'fake-embed' }] })); }
    if (req.url.endsWith('/embeddings')) {
      const input = JSON.parse(body).input;
      res.setHeader('content-type', 'application/json');
      return res.end(JSON.stringify({ data: input.map((t, i) => ({ index: i, embedding: embed(t) })) }));
    }
    if (req.url.endsWith('/chat/completions')) {
      const msgs = JSON.parse(body).messages;
      const user = msgs[msgs.length - 1].content;
      const titles = [...user.matchAll(/<note title="([^"]+)">/g)].map((m) => m[1]);
      const text = /Instruction:/.test(user)
        ? 'Rewritten: ' + (/<text>\n([\s\S]*?)\n<\/text>/.exec(user)?.[1] ?? '').toUpperCase()
        : `Based on ${titles.map((t) => `[[${t}]]`).join(' and ') || 'nothing'}: the Apollo launch is planned for **December**.`;
      res.setHeader('content-type', 'text/event-stream');
      for (const w of text.split(/(?<= )/)) res.write(`data: ${JSON.stringify({ choices: [{ delta: { content: w } }] })}\n\n`);
      return res.end('data: [DONE]\n\n');
    }
    res.statusCode = 404; res.end();
  });
}).listen(11555, '127.0.0.1');
