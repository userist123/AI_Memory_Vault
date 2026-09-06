import React, { useState, useEffect } from 'react';
import { Button } from './ui/button';
import { ChevronDown, Play, Sparkles } from 'lucide-react';

export function HeroSection() {
  const [visible, setVisible] = useState(false);
  const [buttonVisible, setButtonVisible] = useState(false);

  useEffect(() => {
    const t1 = setTimeout(() => setVisible(true), 300);
    const t2 = setTimeout(() => setButtonVisible(true), 900);
    return () => {
      clearTimeout(t1);
      clearTimeout(t2);
    };
  }, []);

  const handleClick = (e, href) => {
    e.preventDefault();
    const target = document.querySelector(href);
    if (target) {
      target.scrollIntoView({ behavior: 'smooth' });
    }
  };

  return (
    <section
      id="home"
      className="relative min-h-screen flex flex-col justify-center items-center overflow-hidden"
    >
      {/* Background image with overlay */}
      <div 
        className="absolute inset-0 bg-cover bg-center bg-no-repeat"
        style={{
          backgroundImage: 'url(https://images.unsplash.com/photo-1630769660701-3454835913dc?w=1920&q=80)',
          zIndex: -2,
        }}
      />
      
      {/* Dark overlay with gradient */}
      <div 
        className="absolute inset-0"
        style={{
          zIndex: -1,
          background: 'radial-gradient(ellipse at center, rgba(0,0,0,0.5) 0%, rgba(0,0,0,0.8) 50%, rgba(0,0,0,0.95) 100%)',
        }}
      />

      {/* Neon glow effects */}
      <div
        className="absolute inset-0 pointer-events-none"
        style={{
          zIndex: 0,
          background: 'radial-gradient(ellipse at 20% 80%, hsl(328 100% 54% / 0.15) 0%, transparent 40%), radial-gradient(ellipse at 80% 20%, hsl(348 100% 50% / 0.1) 0%, transparent 40%)',
        }}
      />

      {/* Animated particles effect */}
      <div className="absolute inset-0 pointer-events-none overflow-hidden" style={{ zIndex: 0 }}>
        {[...Array(20)].map((_, i) => (
          <div
            key={i}
            className="absolute w-1 h-1 rounded-full bg-primary/30"
            style={{
              left: `${Math.random() * 100}%`,
              top: `${Math.random() * 100}%`,
              animation: `float ${3 + Math.random() * 4}s ease-in-out infinite`,
              animationDelay: `${Math.random() * 2}s`,
            }}
          />
        ))}
      </div>

      {/* Content */}
      <div
        className="relative z-10 text-center px-4 max-w-5xl mx-auto transition-all duration-1000 ease-out"
        style={{
          opacity: visible ? 1 : 0,
          transform: visible ? 'translateY(0)' : 'translateY(40px)',
        }}
      >
        {/* Badge */}
        <div 
          className="inline-flex items-center gap-2 px-4 py-2 rounded-full bg-primary/10 border border-primary/20 mb-8"
          style={{
            opacity: visible ? 1 : 0,
            transform: visible ? 'translateY(0)' : 'translateY(20px)',
            transition: 'all 0.6s ease-out 0.2s',
          }}
        >
          <Sparkles className="w-4 h-4 text-primary" />
          <span className="text-sm font-medium text-primary">Studio Premium de Colantari Auto</span>
        </div>

        {/* Main title */}
        <h1
          className="font-heading font-bold text-4xl sm:text-5xl md:text-6xl lg:text-7xl mb-4 text-balance gradient-text"
        >
          CrissCustoms & WobArt
        </h1>

        {/* Slogan */}
        <p
          className="font-heading text-lg md:text-xl lg:text-2xl mb-2 tracking-widest uppercase text-foreground/90 transition-all duration-700"
          style={{
            opacity: visible ? 1 : 0,
            transform: visible ? 'translateY(0)' : 'translateY(20px)',
            transitionDelay: '0.1s',
          }}
        >
          Studio colantari auto premium
        </p>

        <p
          className="text-base md:text-lg lg:text-xl mb-4 italic text-gold transition-all duration-700"
          style={{
            opacity: visible ? 1 : 0,
            transform: visible ? 'translateY(0)' : 'translateY(20px)',
            transitionDelay: '0.2s',
          }}
        >
          WHERE LIGHT MEETS ART
        </p>

        <p
          className="text-sm md:text-base text-muted-foreground mb-8 max-w-2xl mx-auto transition-all duration-700"
          style={{
            opacity: visible ? 1 : 0,
            transform: visible ? 'translateY(0)' : 'translateY(20px)',
            transitionDelay: '0.3s',
          }}
        >
          Folii auto, design personalizat si protectie de top pentru masina ta.
          Transformam fiecare masina intr-o piesa de arta, imbinand tehnica
          profesionista de colantare cu designuri indraznete si efecte de lumina
          spectaculoase.
        </p>

        {/* CTA buttons */}
        <div
          className="flex flex-col sm:flex-row gap-4 justify-center transition-all duration-700"
          style={{
            opacity: buttonVisible ? 1 : 0,
            transform: buttonVisible ? 'translateY(0) scale(1)' : 'translateY(20px) scale(0.95)',
          }}
        >
          <a
            href="#contact"
            onClick={(e) => handleClick(e, '#contact')}
          >
            <Button 
              size="lg" 
              className="btn-neon text-primary-foreground font-heading font-semibold px-10 py-6 text-base rounded-full animate-neon-pulse"
            >
              Cere oferta GRATUITA
            </Button>
          </a>
          <a
            href="#portofoliu"
            onClick={(e) => handleClick(e, '#portofoliu')}
          >
            <Button 
              size="lg" 
              variant="outline"
              className="font-heading font-semibold px-10 py-6 text-base rounded-full border-2 border-primary/40 text-foreground hover:bg-primary/10 hover:border-primary/60 transition-all duration-500"
            >
              <Play className="w-4 h-4 mr-2" />
              Vezi portofoliu
            </Button>
          </a>
        </div>
      </div>

      {/* Scroll indicator */}
      <div
        className="absolute bottom-8 left-1/2 -translate-x-1/2 z-10 transition-all duration-1000"
        style={{
          opacity: buttonVisible ? 1 : 0,
          transform: buttonVisible ? 'translateX(-50%) translateY(0)' : 'translateX(-50%) translateY(10px)',
          transitionDelay: '0.5s',
        }}
      >
        <div className="w-8 h-12 rounded-full border-2 border-muted-foreground/30 flex justify-center pt-3 animate-bounce">
          <ChevronDown className="w-4 h-4 text-primary animate-neon-pulse" />
        </div>
      </div>
    </section>
  );
}

export default HeroSection;
