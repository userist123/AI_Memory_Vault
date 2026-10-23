// Pornește Next.js pe calculatorul designerului: ascultă doar pe 127.0.0.1 și activează încrederea „local”
// (CASA3D_LOCAL_TRUST=1, vezi lib/public-gate.ts). Pe un server găzduit se folosește `npm run start:hosted`, fără încredere locală.
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
const [mode, ...rest] = process.argv.slice(2);
if (mode !== 'dev' && mode !== 'start'){ console.error('Folosire: node scripts/run-local.mjs dev|start [opțiuni next]'); process.exit(2); }
// orice formă de adresă e refuzată (-H x, -Hx, -H=x, --hostname x, --hostname=x), nu doar cele separate prin spațiu
if (rest.some(a => /^(-H|--hostname)/.test(a))){ console.error('Adresa e fixată la 127.0.0.1; pentru alt host folosește `npm run start:hosted`.'); process.exit(2); }
const next = createRequire(import.meta.url).resolve('next/dist/bin/next');
// -H vine ultimul: la opțiuni repetate câștigă ultima, deci adresa rămâne 127.0.0.1 oricum
const child = spawn(process.execPath, [next, mode, ...rest, '-H', '127.0.0.1'], { stdio: 'inherit', env: { ...process.env, CASA3D_LOCAL_TRUST: '1' } });
for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => child.kill(sig));
child.on('exit', (code, sig) => process.exit(code ?? (sig ? 1 : 0)));
