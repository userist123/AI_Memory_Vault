import React from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from './ui/card';
import { Button } from './ui/button';
import { Badge } from './ui/badge';
import { Check, Star, Zap } from 'lucide-react';

const packages = [
  {
    name: 'Partial Wrap',
    price: 'de la 500',
    description: 'Ideal pentru accente si detalii',
    features: [
      'Plafon sau capota',
      'Oglinzi si manere',
      'Folie premium 3M/Avery',
      'Garantie 3 ani',
      'Consultanta gratuita',
    ],
    popular: false,
    icon: null,
  },
  {
    name: 'Full Wrap',
    price: 'de la 2500',
    description: 'Transformare completa',
    features: [
      'Colantare integrala',
      'Demontare elemente',
      'Folie premium la alegere',
      'Garantie 5 ani',
      'Design personalizat inclus',
      'Consultanta VIP',
    ],
    popular: true,
    icon: Star,
  },
  {
    name: 'PPF Premium',
    price: 'de la 1500',
    description: 'Protectie maxima',
    features: [
      'Folie protectie transparenta',
      'Zone expuse (fata completa)',
      'Auto-regenerare zgarieturi',
      'Garantie 10 ani',
      'Certificat autenticitate',
    ],
    popular: false,
    icon: null,
  },
];

export function PricingSection() {
  const { ref, isVisible } = useScrollAnimation(0.1);

  return (
    <section
      id="preturi"
      ref={ref}
      className="py-20 px-4 lg:px-8 max-w-7xl mx-auto"
    >
      <div
        className="text-center mb-12 transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
        }}
      >
        <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl mb-4 gradient-text">
          Pachete si preturi
        </h2>
        <p className="text-muted-foreground text-lg max-w-2xl mx-auto">
          Preturi transparente pentru servicii premium. Fiecare proiect este unic si pretul final depinde de complexitate.
        </p>
      </div>

      <div className="grid md:grid-cols-3 gap-6 lg:gap-8">
        {packages.map((pkg, i) => (
          <Card
            key={pkg.name}
            className={`relative overflow-hidden transition-all duration-500 ${
              pkg.popular 
                ? 'border-primary shadow-neon-lg scale-105 z-10' 
                : 'border-border hover:border-primary/40'
            } bg-card/50 backdrop-blur`}
            style={{
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
              transition: `all 0.5s ease-out ${0.2 + i * 0.15}s`,
            }}
          >
            {/* Popular badge */}
            {pkg.popular && (
              <div className="absolute top-0 right-0">
                <Badge className="bg-primary text-primary-foreground rounded-none rounded-bl-lg px-4 py-1.5 font-semibold">
                  <Zap className="w-4 h-4 mr-1" />
                  Popular
                </Badge>
              </div>
            )}

            <CardHeader className="text-center pb-4">
              {pkg.icon && (
                <div className="mx-auto w-12 h-12 rounded-full bg-primary/10 flex items-center justify-center mb-4">
                  <pkg.icon className="w-6 h-6 text-primary" />
                </div>
              )}
              <CardTitle className="font-heading text-2xl text-foreground">
                {pkg.name}
              </CardTitle>
              <CardDescription className="text-muted-foreground">
                {pkg.description}
              </CardDescription>
            </CardHeader>

            <CardContent className="text-center">
              <div className="mb-6">
                <span className="text-4xl font-heading font-bold text-foreground">
                  {pkg.price}
                </span>
                <span className="text-muted-foreground ml-1">EUR</span>
              </div>

              <ul className="space-y-3 mb-8 text-left">
                {pkg.features.map((feature, idx) => (
                  <li key={idx} className="flex items-start gap-3">
                    <div className="mt-0.5 flex-shrink-0">
                      <Check className="w-5 h-5 text-primary" />
                    </div>
                    <span className="text-muted-foreground text-sm">{feature}</span>
                  </li>
                ))}
              </ul>

              <Button 
                className={`w-full font-semibold ${
                  pkg.popular 
                    ? 'btn-neon text-primary-foreground' 
                    : 'bg-secondary text-foreground hover:bg-primary hover:text-primary-foreground'
                }`}
                size="lg"
              >
                Solicita oferta
              </Button>
            </CardContent>

            {/* Glow effect for popular */}
            {pkg.popular && (
              <div 
                className="absolute inset-0 pointer-events-none"
                style={{
                  boxShadow: 'inset 0 0 60px hsl(328 100% 54% / 0.1)',
                }}
              />
            )}
          </Card>
        ))}
      </div>

      {/* Note */}
      <p
        className="text-center text-sm text-muted-foreground mt-10 transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transitionDelay: '0.8s',
        }}
      >
        * Preturile sunt orientative si pot varia in functie de tipul vehiculului si complexitatea proiectului.
        Contacteaza-ne pentru o oferta personalizata.
      </p>
    </section>
  );
}

export default PricingSection;
