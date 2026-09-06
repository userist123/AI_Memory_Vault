import React from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import { MessageSquare, Palette, CheckCircle, Wrench, Sparkles, Truck } from 'lucide-react';

const steps = [
  {
    icon: MessageSquare,
    title: 'Consultanta',
    description: 'Discutam ideile tale si stabilim impreuna obiectivele proiectului.',
  },
  {
    icon: Palette,
    title: 'Design',
    description: 'Cream mockup-uri digitale pentru a vizualiza rezultatul final.',
  },
  {
    icon: CheckCircle,
    title: 'Aprobare',
    description: 'Validezi designul si alegem impreuna materialele premium.',
  },
  {
    icon: Wrench,
    title: 'Pregatire',
    description: 'Curatam si pregatim suprafetele pentru o aplicare perfecta.',
  },
  {
    icon: Sparkles,
    title: 'Colantare',
    description: 'Aplicam foliile cu precizie, fara bule sau imperfectiuni.',
  },
  {
    icon: Truck,
    title: 'Livrare',
    description: 'Predare cu control de calitate si garantie extinsa.',
  },
];

export function ProcessSection() {
  const { ref, isVisible } = useScrollAnimation(0.1);

  return (
    <section
      id="proces"
      ref={ref}
      className="py-20 px-4 lg:px-8"
      style={{
        background: 'linear-gradient(180deg, hsl(0 0% 3%) 0%, hsl(0 0% 6%) 50%, hsl(0 0% 3%) 100%)',
      }}
    >
      <div className="max-w-7xl mx-auto">
        <div
          className="text-center mb-16 transition-all duration-700"
          style={{
            opacity: isVisible ? 1 : 0,
            transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
          }}
        >
          <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl mb-4 gradient-text">
            Procesul nostru
          </h2>
          <p className="text-muted-foreground text-lg max-w-2xl mx-auto">
            De la prima consultatie pana la livrare, te ghidam prin fiecare etapa pentru o experienta fara griji
          </p>
        </div>

        <div className="relative">
          {/* Connection line */}
          <div 
            className="hidden lg:block absolute top-1/2 left-0 right-0 h-0.5 bg-gradient-to-r from-transparent via-primary/30 to-transparent -translate-y-1/2"
            style={{
              opacity: isVisible ? 1 : 0,
              transition: 'opacity 1s ease-out 0.5s',
            }}
          />

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-6 gap-8 lg:gap-4">
            {steps.map((step, i) => (
              <div
                key={step.title}
                className="relative flex flex-col items-center text-center group"
                style={{
                  opacity: isVisible ? 1 : 0,
                  transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
                  transition: `all 0.5s ease-out ${0.2 + i * 0.1}s`,
                }}
              >
                {/* Step number */}
                <div 
                  className="absolute -top-3 right-0 lg:right-auto lg:left-1/2 lg:-translate-x-1/2 w-6 h-6 rounded-full bg-primary/20 flex items-center justify-center text-xs font-bold text-primary"
                  style={{
                    boxShadow: '0 0 15px hsl(328 100% 54% / 0.3)',
                  }}
                >
                  {i + 1}
                </div>

                {/* Icon container */}
                <div 
                  className="relative w-20 h-20 rounded-2xl bg-card border border-border flex items-center justify-center mb-4 transition-all duration-500 group-hover:border-primary/50 group-hover:shadow-neon"
                >
                  <step.icon className="w-8 h-8 text-primary transition-transform duration-500 group-hover:scale-110" />
                  
                  {/* Glow effect on hover */}
                  <div 
                    className="absolute inset-0 rounded-2xl opacity-0 group-hover:opacity-100 transition-opacity duration-500"
                    style={{
                      boxShadow: 'inset 0 0 20px hsl(328 100% 54% / 0.2)',
                    }}
                  />
                </div>

                <h3 className="font-heading font-semibold text-foreground mb-2 group-hover:text-primary transition-colors">
                  {step.title}
                </h3>
                <p className="text-sm text-muted-foreground leading-relaxed">
                  {step.description}
                </p>

                {/* Arrow connector for mobile/tablet */}
                {i < steps.length - 1 && (
                  <div className="lg:hidden w-0.5 h-8 bg-primary/20 mt-4" />
                )}
              </div>
            ))}
          </div>
        </div>

        {/* CTA */}
        <div
          className="text-center mt-16 transition-all duration-700"
          style={{
            opacity: isVisible ? 1 : 0,
            transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
            transitionDelay: '1s',
          }}
        >
          <p className="text-muted-foreground mb-4">
            Pregatit sa incepi transformarea masinii tale?
          </p>
          <a href="#contact">
            <button className="btn-neon text-primary-foreground font-heading font-semibold px-8 py-3 rounded-full animate-neon-pulse">
              Programeaza consultatie gratuita
            </button>
          </a>
        </div>
      </div>
    </section>
  );
}

export default ProcessSection;
