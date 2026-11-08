import fs from 'node:fs';

const port = process.env.CDP_PORT || '9222';
const out = process.argv[2] || 'captures/cdp-search.png';
const query = process.argv[3] || 'seguridad';

const list = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
const page = list.find(t => t.type === 'page');
if (!page) { console.error('NO_PAGE'); process.exit(1); }
console.log('TARGET=' + page.url);

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });

let seq = 0;
const pending = new Map();
ws.onmessage = ev => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
};
const send = (method, params = {}) => new Promise(res => {
  const id = ++seq;
  pending.set(id, res);
  ws.send(JSON.stringify({ id, method, params }));
});

await send('Runtime.enable');
await send('Page.enable');
await send('Runtime.evaluate', {
  expression: `window.chrome.webview.postMessage({type:'search', q:${JSON.stringify(query)}})`
});
await new Promise(r => setTimeout(r, 9000));

const href = await send('Runtime.evaluate', { expression: 'location.href', returnByValue: true });
const title = await send('Runtime.evaluate', { expression: 'document.title', returnByValue: true });
console.log('URL=' + href.result?.result?.value);
console.log('TITLE=' + title.result?.result?.value);

const shot = await send('Page.captureScreenshot', { format: 'png' });
fs.writeFileSync(out, Buffer.from(shot.result.data, 'base64'));
console.log('SAVED=' + out);
ws.close();
