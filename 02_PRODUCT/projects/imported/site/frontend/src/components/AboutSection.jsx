import React from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import { Award, Users, Clock, Shield } from 'lucide-react';

const stats = [
  { icon: Award, value: '500+', label: 'Proiecte Finalizate' },
  { icon: Users, value: '400+', label: 'Clienti Multumiti' },
  { icon: Clock, value: '8+', label: 'Ani Experienta' },
  { icon: Shield, value: '100%', label: 'Garantie Calitate' },
];

export function AboutSection() {
  const { ref, isVisible } = useScrollAnimation(0.1);

  return (
    <section
      id="despre"
      ref={ref}
      className="py-20 px-4 lg:px-8 max-w-7xl mx-auto"
    >
      <div className="grid lg:grid-cols-2 gap-12 items-center">
        {/* Image side */}
        <div
          className="relative transition-all duration-1000 ease-out"
          style={{
            opacity: isVisible ? 1 : 0,
            transform: isVisible ? 'translateX(0)' : 'translateX(-50px)',
          }}
        >
          <div className="relative rounded-2xl overflow-hidden">
            <img
              src="https://images.unsplash.com/photo-1632823469850-2f77dd9c7f93?w=800&q=80"
              alt="Car wrapping process"
              className="w-full h-auto object-cover rounded-2xl"
            />
            {/* Neon border effect */}
            <div 
              className="absolute inset-0 rounded-2xl pointer-events-none"
              style={{
                boxShadow: 'inset 0 0 0 2px hsl(328 100% 54% / 0.3), 0 0 40px hsl(328 100% 54% / 0.2)',
              }}
            />
          </div>
          
          {/* Floating stats card */}
          <div 
            className="absolute -bottom-6 -right-6 bg-card p-6 rounded-xl border border-border shadow-neon-lg"
            style={{
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translate(0, 0)' : 'translate(20px, 20px)',
              transition: 'all 0.8s ease-out 0.3s',
            }}
          >
            <p className="text-3xl font-heading font-bold text-primary mb-1">8+</p>
            <p className="text-sm text-muted-foreground">Ani de Experienta</p>
          </div>
        </div>

        {/* Content side */}
        <div
          className="transition-all duration-1000 ease-out"
          style={{
            opacity: isVisible ? 1 : 0,
            transform: isVisible ? 'translateX(0)' : 'translateX(50px)',
            transitionDelay: '0.2s',
          }}
        >
          <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl mb-6 text-balance">
            <span className="gradient-text">Despre</span>{' '}
            <span className="text-foreground">CrissCustoms & WobArt</span>
          </h2>
          
          <p className="text-muted-foreground mb-6 leading-relaxed">
            Suntem un studio premium de colantari auto din Romania, dedicat transformarii 
            fiecarei masini intr-o opera de arta. Cu peste 8 ani de experienta si sute de 
            proiecte finalizate, combinam pasiunea pentru automobile cu expertiza tehnica 
            de cel mai inalt nivel.
          </p>
          
          <p className="text-muted-foreground mb-8 leading-relaxed">
            Echipa noastra de profesionisti foloseste doar materiale premium de la branduri 
            de top precum 3M, Avery Dennison si KPMF, asigurand rezultate exceptionale si 
            durabilitate garantata. De la full wrap-uri complete pana la accente subtile, 
            transformam viziunea ta in realitate.
          </p>

          {/* Stats grid */}
          <div className="grid grid-cols-2 gap-4">
            {stats.map((stat, index) => (
              <div
                key={stat.label}
                className="flex items-center gap-4 p-4 rounded-xl bg-card/50 border border-border hover:border-primary/30 transition-all duration-300 card-hover"
                style={{
                  opacity: isVisible ? 1 : 0,
                  transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
                  transition: `all 0.5s ease-out ${0.4 + index * 0.1}s`,
                }}
              >
                <div className="p-3 rounded-lg bg-primary/10">
                  <stat.icon className="w-6 h-6 text-primary" />
                </div>
                <div>
                  <p className="text-2xl font-heading font-bold text-foreground">{stat.value}</p>
                  <p className="text-sm text-muted-foreground">{stat.label}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

export default AboutSection;
