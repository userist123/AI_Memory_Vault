import React from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from './ui/accordion';

const faqs = [
  {
    question: 'Cat dureaza o colantare completa?',
    answer: 'O colantare full wrap dureaza in medie 3-5 zile lucratoare, in functie de complexitatea proiectului si tipul vehiculului. Pentru colantari partiale sau accente, timpul este de 1-2 zile.',
  },
  {
    question: 'Ce garantie oferiti pentru folii?',
    answer: 'Oferim garantie 3-5 ani pentru colantari wrap si pana la 10 ani pentru foliile PPF, in functie de producator. Garantia acopera decolorarea, craparea si desprinderea foliei in conditii normale de utilizare.',
  },
  {
    question: 'Pot spala masina la spalatorie automata dupa colantare?',
    answer: 'Recomandam spalarea manuala sau spalatorii touchless pentru a prelungi durata de viata a foliei. Spalatoriile cu perii pot deteriora margintile foliei in timp.',
  },
  {
    question: 'Se poate indeparta folia fara a afecta vopseaua originala?',
    answer: 'Da, foliile de calitate pot fi indepartate profesional fara a afecta vopseaua originala a masinii. Este important ca indepartarea sa fie facuta de profesionisti.',
  },
  {
    question: 'Ce marci de folii folositi?',
    answer: 'Folosim exclusiv folii premium de la producatori de top: 3M, Avery Dennison, KPMF si Hexis. Alegem impreuna cu clientul marca si tipul de folie potrivit proiectului.',
  },
  {
    question: 'Cat costa o colantare full wrap?',
    answer: 'Pretul variaza in functie de tipul vehiculului, complexitatea proiectului si tipul de folie ales. Preturile incep de la 2500 EUR pentru masini compacte. Contacteaza-ne pentru o oferta personalizata.',
  },
  {
    question: 'Trebuie sa pregatesc masina inainte de colantare?',
    answer: 'Masina trebuie sa fie curata. Noi ne ocupam de pregatirea profesionala a suprafetelor, care include degresare, curatare cu argila si decontaminare.',
  },
  {
    question: 'Oferiti servicii de design personalizat?',
    answer: 'Da, avem o echipa de designeri care pot crea grafici personalizate, logo-uri si designuri unice pentru masina ta. Serviciul de design este inclus in pachetele Full Wrap.',
  },
];

export function FAQSection() {
  const { ref, isVisible } = useScrollAnimation(0.1);

  return (
    <section
      id="faq"
      ref={ref}
      className="py-20 px-4 lg:px-8 max-w-4xl mx-auto"
    >
      <div
        className="text-center mb-12 transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
        }}
      >
        <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl mb-4 gradient-text">
          Intrebari frecvente
        </h2>
        <p className="text-muted-foreground text-lg">
          Raspunsuri la cele mai comune intrebari despre serviciile noastre
        </p>
      </div>

      <Accordion
        type="single"
        collapsible
        className="space-y-4"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
          transition: 'all 0.7s ease-out 0.2s',
        }}
      >
        {faqs.map((faq, i) => (
          <AccordionItem
            key={i}
            value={`item-${i}`}
            className="border border-border rounded-lg px-6 bg-card/30 hover:border-primary/30 transition-colors duration-300 data-[state=open]:border-primary/50"
            style={{
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
              transition: `all 0.5s ease-out ${0.3 + i * 0.05}s`,
            }}
          >
            <AccordionTrigger className="text-left font-heading font-medium text-foreground hover:text-primary py-4 hover:no-underline">
              {faq.question}
            </AccordionTrigger>
            <AccordionContent className="text-muted-foreground pb-4 leading-relaxed">
              {faq.answer}
            </AccordionContent>
          </AccordionItem>
        ))}
      </Accordion>

      {/* Additional help */}
      <div
        className="mt-12 text-center p-6 rounded-xl bg-card/50 border border-border transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transitionDelay: '0.8s',
        }}
      >
        <p className="text-muted-foreground mb-4">
          Nu ai gasit raspunsul pe care il cautai?
        </p>
        <a href="#contact">
          <button className="btn-neon text-primary-foreground font-semibold px-6 py-2 rounded-full">
            Contacteaza-ne
          </button>
        </a>
      </div>
    </section>
  );
}

export default FAQSection;
