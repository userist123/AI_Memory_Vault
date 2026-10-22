// Rulează o dată, la pornirea serverului Next.js: configurația greșită oprește pornirea (lib/env-check.ts).
export async function register(){
  if (process.env.NEXT_RUNTIME !== 'nodejs') return;
  const { envProblems } = await import('./lib/env-check');
  const problems = envProblems();
  if (problems.length){ console.error(`[casa3d] Configurație invalidă, serverul nu pornește:\n - ${problems.join('\n - ')}`); process.exit(1); }
}
