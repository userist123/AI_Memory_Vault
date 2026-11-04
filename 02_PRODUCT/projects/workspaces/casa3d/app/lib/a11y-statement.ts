// Declarația de accesibilitate (UI/UX P3-17), în limba cititorului. Conținutul descrie ce e măsurat în acest cod
// (scripts/check-a11y.mjs, testele de interfață) și limitările cunoscute; se actualizează odată cu ele.
// Raportul detaliat pe criterii: ../ACCESSIBILITY_CONFORMANCE_REPORT.md.
import type { Lang } from './i18n';

export const STATEMENT_DATE = '2026-10-23';
export interface Statement { title: string; intro: string; sections: { h: string; p?: string[]; list?: string[] }[] }

const ro: Statement = {
  title: 'Declarație de accesibilitate',
  intro: 'Casa mea 3D (aplicația de proiectare și linkurile trimise clienților) își propune să fie utilizabilă de cât mai multe persoane, inclusiv de persoanele cu dizabilități, conform Legii nr. 232/2022 privind cerințele de accesibilitate aplicabile produselor și serviciilor (care transpune Directiva (UE) 2019/882).',
  sections: [
    { h: 'Standardul urmărit', p: ['Ghidurile WCAG 2.2, nivel AA, și cerințele pentru web și software din standardul european EN 301 549.'] },
    { h: 'Stadiul conformității', p: ['Parțial conformă: cele mai multe cerințe sunt îndeplinite, iar excepțiile de mai jos sunt cunoscute și descrise.'] },
    { h: 'Ce verificăm la fiecare modificare', list: [
      'Verificare automată cu axe-core (reguli WCAG 2.0, 2.1 și 2.2, nivel A și AA) pe pagina principală, editor (mai multe etape și paleta de comenzi), documentele de tipărit și linkul clientului, pe calculator și pe telefon: zero încălcări la data declarației.',
      'Contrast al textului de cel puțin 4,5:1 în paginile verificate.',
      'Alternative fără tragere pentru plan și vederea 3D (butoane pentru apropiere, deplasare și rotire) și cote tastate la desenare (criteriul 2.5.7).',
      'Ținte de atingere de cel puțin 24 × 24 px (criteriul 2.5.8); pe linkul clientului, pe telefon, 44 × 44 px.',
      'Animațiile se opresc când sistemul cere mișcare redusă.',
      'Linkul clientului (aprobări, comentarii, istoric) e construit pentru tastatură și cititoare de ecran și verificat automat; testele manuale cu NVDA, VoiceOver și TalkBack sunt planificate.',
    ] },
    { h: 'Limitări cunoscute', list: [
      'Vederea 3D (desen WebGL) nu are un echivalent text complet. Aceleași informații sunt disponibile ca text: numele camerelor, suprafețele, materialele, piesele și bugetul, pe planșe și în documente.',
      'Desenarea în planul 2D cere mouse, atingere sau cote tastate. Toate obiectele planului (camere, piese, pereți, uși, ferestre, structură) sunt într-o listă pentru tastatură și cititor de ecran, cu descrieri („Perete interior · 3,20 m · Hol, Bucătărie”), din care se aleg și se încadrează.',
      'Fișierele PDF obținute cu „Salvează PDF” sunt generate de browser și nu au structură etichetată (tagged PDF); paginile din care provin sunt accesibile.',
    ] },
    { h: 'Cum a fost evaluată', p: [`Autoevaluare și teste automate în codul aplicației, la data de ${STATEMENT_DATE}. Nu a fost făcut încă un audit extern.`] },
    { h: 'Feedback și contact', p: ['Dacă întâmpinați o barieră pe un link primit de la un designer, scrieți-i direct, din caseta de comentarii a linkului sau pe adresa lui de contact.'] },
    { h: 'Procedura de sesizare', p: ['Dacă răspunsul nu vă mulțumește, vă puteți adresa autorității de supraveghere a pieței competente potrivit Legii nr. 232/2022.'] },
  ],
};
const en: Statement = {
  title: 'Accessibility statement',
  intro: 'Casa mea 3D (the design application and the links sent to clients) aims to be usable by as many people as possible, including people with disabilities, in line with Romanian Law 232/2022 on accessibility requirements for products and services (transposing Directive (EU) 2019/882).',
  sections: [
    { h: 'Standard', p: ['WCAG 2.2 level AA and the web and software requirements of the European standard EN 301 549.'] },
    { h: 'Conformance status', p: ['Partially conformant: most requirements are met; the exceptions below are known and described.'] },
    { h: 'What we check on every change', list: [
      'Automated checking with axe-core (WCAG 2.0, 2.1 and 2.2 rules, levels A and AA) on the home page, the editor (several stages and the command palette), the print documents and the client link, on desktop and phone: zero violations at the date of this statement.',
      'Text contrast of at least 4.5:1 on the checked pages.',
      'Non-dragging alternatives for the plan and the 3D view (buttons to zoom, pan and rotate) and typed dimensions while drawing (criterion 2.5.7).',
      'Touch targets of at least 24 × 24 px (criterion 2.5.8); on the client link, on phones, 44 × 44 px.',
      'Animations stop when the system asks for reduced motion.',
      'The client link (approvals, comments, history) is built for keyboards and screen readers and checked automatically; manual tests with NVDA, VoiceOver and TalkBack are planned.',
    ] },
    { h: 'Known limitations', list: [
      'The 3D view (WebGL drawing) has no complete text equivalent. The same information is available as text: room names, areas, materials, pieces and budget, on the slides and in the documents.',
      'Drawing in the 2D plan needs a mouse, touch or typed dimensions. All plan objects (rooms, pieces, walls, doors, windows, structure) are in a keyboard and screen-reader list with descriptions (“Interior wall · 3.20 m · Hall, Kitchen”), from which they can be selected and framed.',
      'PDF files made with “Save PDF” are produced by the browser and are not tagged; the pages they come from are accessible.',
    ] },
    { h: 'How it was assessed', p: [`Self-assessment and automated tests in the application code, on ${STATEMENT_DATE}. No external audit yet.`] },
    { h: 'Feedback and contact', p: ['If you meet a barrier on a link you received from a designer, write to them directly, in the link’s comment box or at their contact address.'] },
    { h: 'Enforcement', p: ['If the answer does not satisfy you, you can contact the market surveillance authority competent under Romanian Law 232/2022.'] },
  ],
};
export const statementFor = (lang: Lang): Statement => (lang === 'ro' ? ro : en);
