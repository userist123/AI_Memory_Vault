export const metadata = { title: 'Cum funcționează linkurile — Casa mea 3D' };
export default function Page(){
  return (<main className="home" style={{ maxWidth: 720 }}>
    <a className="btn" href="/">← Proiecte</a>
    <h1 style={{ marginTop: 24 }}>Cum funcționează linkurile spre magazine</h1>
    <p className="lead">Aplicația e în testare. Linkurile duc direct la paginile produselor de pe ikea.com/ro și dedeman.ro. În această etapă nu primim niciun comision pentru cumpărături.</p>
    <h3>Prețuri</h3>
    <p>Fiecare preț e verificat manual și afișat cu data verificării. Prețurile se pot schimba; prețul valabil este cel de pe site-ul magazinului în momentul comenzii. Când un preț e mai vechi de 14 zile, aplicația îl marchează ca „de reverificat”.</p>
    <h3>Ce înregistrăm când apeși un link</h3>
    <p>Doar un contor agregat: produsul, magazinul, ora (rotunjită), tipul de dispozitiv (mobil, tabletă sau desktop) și pagina din aplicație. Nu salvăm adresa IP, nu folosim cookie-uri pentru asta și nu păstrăm identificatorul browserului.</p>
    <h3>Dacă vom folosi linkuri de afiliere</h3>
    <p>Când un link devine de afiliere, aplicația afișează automat mențiunea „Link de afiliere: putem primi un comision, fără cost suplimentar pentru tine” lângă butoanele de cumpărare și marchează linkul corespunzător. Ordinea produselor nu depinde de comision.</p>
    <h3>Imagini</h3>
    <p>Nu copiem imagini de pe site-urile magazinelor. Vizualizările sunt modele 3D generate de aplicație.</p>
  </main>);
}
