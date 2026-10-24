# Faza 4 — raport STOP GATE (strat de oferte și linkuri, mod testare)

Mod testare: fără conturi de afiliere, fără abonament, fără plăți. Structura permite adăugarea ulterioară a linkurilor de afiliere fără modificări în interfață.

| Verificare | Rezultat |
|---|---|
| `npm run typecheck` | ✅ |
| `npm test` | ✅ 64/64 (12 noi pentru Faza 4) |
| `npm run build` | ✅ |
| Server de producție + browser | ✅ redirect 302, informare automată, linkuri doar prin `/go/` |

## Teste obligatorii din promptul fazei
| Cerință | Rezultat |
|---|---|
| Produs fără ofertă | `/go/...` → 404; în interfață apare „fără ofertă”, fără buton |
| Produs cu ofertă | 302 către pagina produsului IKEA/Dedeman, cu `utm_source=casamea3d` (fără a suprascrie parametri existenți) |
| Link de afiliere | are prioritate, nu primește UTM, activează automat `rel="sponsored"` și mesajul de informare; doar domenii 2Performant/Profitshare |
| Schimbare preț | actualizare manuală cu dată → catalogul se recalculează, istoricul se păstrează (`price_history`) |
| Ofertă expirată | afilierea cu `active_to` trecut revine automat la linkul direct; prețurile mai vechi de 14 zile sunt marcate „de reverificat” |
| Contorizare clicuri | tabelul `clicks` nu are coloane de IP, cookie, user-agent sau sesiune; ora rotunjită; pagina de proiect anonimizată (`/p/:id`); roboții nu sunt numărați |

## Ce s-a construit
- Tabele: `retailers`, `offer_links` (direct/afiliere, rețea, valabilitate, ultima verificare), `clicks` (agregat, anonim), `price_history`.
- `GET /go/o/{offerId}` și `GET /go/m/{materialId}`: redirect 302, `no-store`, `X-Robots-Tag: noindex`, `Referrer-Policy: no-referrer`; `/go/` blocat în `robots.txt`.
- `GET /api/outbound`: tipul linkului pentru fiecare ofertă și dacă e nevoie de informare.
- Administrare (doar cu `ADMIN_TOKEN` ≥ 16 caractere, header `x-admin-token`): adăugare link de afiliere (`/api/admin/links`), preț manual cu istoric (`/api/admin/prices`), statistici clicuri (`/api/admin/stats`), verificare linkuri prin HEAD, fără scraping (`/api/admin/check`). Fără token, rutele răspund 404.
- Pagina publică `/despre-linkuri`: cum funcționează linkurile, prețurile, ce se înregistrează la clic, informarea pentru afiliere, imagini.

## Decizii (din cercetarea din 02.10.2026, de revalidat la activare)
- IKEA România și Dedeman nu au program de afiliere găsit public; aplicația nu depinde de unul.
- Fără imagini copiate de pe site-urile retailerilor (termenii Dedeman interzic preluarea conținutului fără acord scris).
- Ordinea ofertelor nu depinde de comision.

## Limite
- Prețurile rămân manuale (nu există feed fără conturi de afiliere).
- Verificarea linkurilor e un endpoint apelat manual; programarea periodică (cron Vercel) se adaugă la publicare.
- Conformitatea juridică și fiscală a monetizării trebuie confirmată de un specialist înainte de activarea primului link plătit.
