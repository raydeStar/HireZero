// Shared waiting for the headless Chrome smokes. Waits run against a wall-clock deadline rather than a try count, so a
// slow probe under machine load does not shorten or stretch the budget, and a timeout says what the page showed.
import net from 'node:net';

export const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

// A free loopback port per run, so two smokes (or two sessions) never share a fixture listener.
export const freeLocalUrl = () =>
  new Promise((resolve, reject) => {
    const probe = net.createServer().once('error', reject);
    probe.listen(0, '127.0.0.1', () => {
      const {port} = probe.address();
      probe.close(() => resolve('http://127.0.0.1:' + port));
    });
  });

export function waiter({timeout = Number(process.env.HIREZERO_SMOKE_WAIT_MS) || 30000, interval = 100} = {}) {
  async function until(fn, label, ms = timeout) {
    const deadline = Date.now() + ms;
    let last;
    for (;;) {
      try {
        const result = await fn();
        if (result) return result;
        last = undefined;
      } catch (e) {
        last = e;
      }
      if (Date.now() >= deadline) break;
      await delay(interval);
    }
    let detail = '';
    try {
      detail = (await until.diagnose?.()) || '';
    } catch {}
    throw new Error(`Timed out after ${ms} ms: ${label}${last ? ` (last error: ${last.message})` : ''}${detail ? `\n${detail}` : ''}`);
  }
  until.diagnose = null;
  return until;
}

// What the page is telling the owner: visible alerts and the current route.
export const pageDiagnosis = evaluate => () =>
  evaluate("'route '+location.search+'; alerts: '+([...document.querySelectorAll('[role=alert]')].map(e=>e.textContent.trim()).filter(Boolean).join(' | ')||'none')");

// Installed before the app loads. Counts the page's own requests until their bodies are read (or a second after the
// headers, for a response a fixture discards), so a test can wait for the UI to finish its command and refresh instead
// of racing it: the workspace ignores a click while a command holds its lock and renders command buttons from the last
// snapshot it loaded.
export const fetchTracker = `(()=>{let open=0,quiet=performance.now();const settle=()=>setTimeout(()=>{open--;quiet=performance.now();},0);
const original=window.fetch.bind(window);
window.fetch=(...args)=>{open++;let request;try{request=original(...args);}catch(e){settle();throw e;}
return request.then(r=>{let done=false;const once=()=>{if(!done){done=true;settle();}};setTimeout(once,1000);
for(const m of ['json','text','arrayBuffer','blob']){const read=r[m].bind(r);r[m]=()=>{const body=read();body.then(once,once);return body;};}return r;},e=>{settle();throw e;});};
window.__smokeIdle=(ms=150)=>open===0&&performance.now()-quiet>=ms;})()`;

// Clicks a button by its text once it is enabled and, unless told otherwise, once the page has no request in flight.
export const clicker = (evaluate, until) => async (text, {idle = true} = {}) => {
  const find = `[...document.querySelectorAll('button')].find(b=>b.textContent.trim()===${JSON.stringify(text)})`;
  await until(() => evaluate(`(()=>{const b=${find};return !!b&&!b.disabled${idle ? '&&window.__smokeIdle()' : ''};})()`), 'ready to click ' + text);
  await evaluate(`${find}.click()`);
  await delay(150);
};

// Waits for the page's own requests (command, refresh, thread reload) to finish.
export const pageIdle = (evaluate, until) => () => until(() => evaluate('window.__smokeIdle()'), 'page requests settled');
