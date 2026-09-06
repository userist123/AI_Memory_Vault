import React from 'react';
import { Paintbrush, Target, Megaphone, Lightbulb, Shield, Zap } from 'lucide-react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';

const services = [
  {
    icon: Paintbrush,
    title: 'FULL WRAP',
    subtitle: 'Colantare completa',
    description:
      'Schimbam complet aspectul masinii tale prin colantare integrala cu folii premium, disponibile in finisaje lucioase, mate, satinate sau cu efecte speciale.',
  },
  {
    icon: Target,
    title: 'PARTIAL',
    subtitle: 'Accente si detalii',
    description:
      'Plafon, oglinzi, spoilere, praguri, difuzoare - accentele colorate scot in evidenta liniile masinii fara a modifica complet culoarea originala.',
  },
  {
    icon: Megaphone,
    title: 'RECLAMA',
    subtitle: 'Design comercial',
    description:
      'Designuri personalizate pentru masini de firma, flote si vehicule utilitare. Brandul tau vizibil si usor de citit in trafic.',
  },
  {
    icon: Lightbulb,
    title: 'FARURI/STOPURI',
    subtitle: 'Folii fum premium',
    description:
      'Folii speciale pentru faruri si stopuri, cu transparenta optima si nuante atent selectate. Aspect agresiv sau elegant, functionalitate pastrata.',
  },
  {
    icon: Shield,
    title: 'PPF',
    subtitle: 'Protectie transparenta',
    description:
      'Folii transparente de protectie pe zone expuse sau pe intreaga masina. Prevenire ciobituri, zgarieturi si decolorari.',
  },
  {
    icon: Zap,
    title: 'CUSTOM',
    subtitle: 'Striping sportiv',
    description:
      'Dungi sportive, grafici motorsport, elemente geometrice sau detalii artistice care subliniaza caracterul masinii tale.',
  },
];

export function ServicesSection() {
  const { ref, isVisible } = useScrollAnimation(0.05);

  return (
    <section
      id="servicii"
      ref={ref}
      className="py-20 px-4 lg:px-8 max-w-7xl mx-auto transition-all duration-[800ms] ease-out"
      style={{
        opacity: isVisible ? 1 : 0,
        transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
      }}
    >
      <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl text-center mb-4 text-balance gradient-text">
        Servicii de colantare si personalizare
      </h2>
      <p className="text-center text-muted-foreground text-lg mb-12 max-w-3xl mx-auto">
        De la schimbarea completa a culorii pana la reclame full-color, transformam orice vehicul intr-un statement
        vizual.
      </p>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {services.map((service, i) => (
          <div
            key={service.title}
            className="group relative rounded-2xl border border-primary/15 overflow-hidden cursor-pointer bg-card/30 transition-all duration-500 hover:border-primary/40"
            style={{
              transition: `opacity 500ms ease-out ${i * 80}ms, transform 500ms ease-out ${i * 80}ms`,
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
            }}
          >
            {/* Default state content */}
            <div className="p-6 transition-all duration-500 group-hover:opacity-10 group-hover:scale-95">
              <service.icon className="w-10 h-10 mb-4 text-primary" />
              <h3 className="font-heading font-bold text-base mb-1 text-foreground tracking-wider">
                {service.title}
              </h3>
              <p className="text-primary text-sm mb-3 font-medium">{service.subtitle}</p>
              <p className="text-muted-foreground text-sm leading-relaxed">{service.description}</p>
            </div>

            {/* Hover overlay */}
            <div
              className="absolute inset-0 flex flex-col items-center justify-center p-6 text-center opacity-0 group-hover:opacity-100 transition-all duration-500 scale-95 group-hover:scale-100"
              style={{
                background: 'linear-gradient(135deg, hsl(328 100% 54% / 0.15), hsl(0 0% 0% / 0.9))',
              }}
            >
              <service.icon 
                className="w-12 h-12 mb-4 text-primary" 
                style={{ filter: 'drop-shadow(0 0 15px hsl(328 100% 54% / 0.8))' }} 
              />
              <h3 className="font-heading font-bold text-lg mb-2 text-foreground">{service.title}</h3>
              <p className="text-foreground/80 text-sm leading-relaxed">{service.description}</p>
              <div 
                className="mt-4 w-12 h-0.5 rounded-full bg-primary" 
                style={{ boxShadow: '0 0 10px hsl(328 100% 54%)' }} 
              />
            </div>

            {/* Border glow on hover */}
            <div 
              className="absolute inset-0 rounded-2xl pointer-events-none opacity-0 group-hover:opacity-100 transition-opacity duration-500"
              style={{ boxShadow: 'inset 0 0 1px hsl(328 100% 54%), 0 0 30px hsl(328 100% 54% / 0.3)' }}
            />
          </div>
        ))}
      </div>

      {/* Consultancy note */}
      <div
        className="mt-10 p-6 rounded-lg bg-gold/5 border-l-4 border-gold transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
          transitionDelay: '600ms',
        }}
      >
        <p className="text-muted-foreground">
          <strong className="text-gold">Consultanta personalizata:</strong> Iti oferim consultanta completa pentru
          alegerea culorilor, tipului de folie si a designului, astfel incat masina ta sa arate exact asa cum ti-ai
          imaginat.
        </p>
      </div>
    </section>
  );
}

export default ServicesSection;
