import React, { useState } from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import { Card, CardContent } from './ui/card';
import { Avatar, AvatarFallback, AvatarImage } from './ui/avatar';
import { Badge } from './ui/badge';
import { Star, Quote, ChevronLeft, ChevronRight } from 'lucide-react';
import { Button } from './ui/button';

const testimonials = [
  {
    id: 1,
    name: 'Alexandru Ionescu',
    role: 'Proprietar BMW M4',
    avatar: null,
    rating: 5,
    text: 'Echipa CrissCustoms a facut o treaba exceptionala cu masina mea. Full wrap in satin blue, iar rezultatul a depasit toate asteptarile. Recomand cu incredere!',
    verified: true,
    project: 'Full Wrap',
  },
  {
    id: 2,
    name: 'Maria Popescu',
    role: 'Antreprenor',
    avatar: null,
    rating: 5,
    text: 'Am colantat intreaga flota de masini a firmei. Profesionalism desavarsit, preturi corecte si livrare la timp. Parteneriatul nostru continua!',
    verified: true,
    project: 'Fleet Branding',
  },
  {
    id: 3,
    name: 'Andrei Mihai',
    role: 'Proprietar Porsche 911',
    avatar: null,
    rating: 5,
    text: 'PPF pe intreaga masina plus accente carbon. Munca de artisti! Fiecare detaliu a fost tratat cu maxima atentie. CrissCustoms este numarul 1.',
    verified: true,
    project: 'PPF + Accente',
  },
  {
    id: 4,
    name: 'Elena Dumitrescu',
    role: 'Designer',
    avatar: null,
    rating: 5,
    text: 'Mi-au transformat Mercedes-ul intr-o opera de arta. Wrap matte black cu accente gold. Toti prietenii ma intreaba unde am facut-o!',
    verified: true,
    project: 'Custom Design',
  },
];

export function TestimonialsSection() {
  const { ref, isVisible } = useScrollAnimation(0.1);
  const [currentIndex, setCurrentIndex] = useState(0);

  const nextTestimonial = () => {
    setCurrentIndex((prev) => (prev + 1) % testimonials.length);
  };

  const prevTestimonial = () => {
    setCurrentIndex((prev) => (prev - 1 + testimonials.length) % testimonials.length);
  };

  return (
    <section
      id="testimoniale"
      ref={ref}
      className="py-20 px-4 lg:px-8"
      style={{
        background: 'linear-gradient(180deg, hsl(0 0% 3%) 0%, hsl(0 0% 5%) 50%, hsl(0 0% 3%) 100%)',
      }}
    >
      <div className="max-w-7xl mx-auto">
        <div
          className="text-center mb-12 transition-all duration-700"
          style={{
            opacity: isVisible ? 1 : 0,
            transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
          }}
        >
          <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl mb-4 gradient-text">
            Ce spun clientii nostri
          </h2>
          <p className="text-muted-foreground text-lg max-w-2xl mx-auto">
            Satisfactia clientilor este prioritatea noastra. Citeste recenziile celor care ne-au incredibat masinile.
          </p>
        </div>

        {/* Desktop grid */}
        <div className="hidden md:grid md:grid-cols-2 lg:grid-cols-4 gap-6">
          {testimonials.map((testimonial, i) => (
            <Card
              key={testimonial.id}
              className="bg-card/50 border-border hover:border-primary/30 transition-all duration-500 card-hover"
              style={{
                opacity: isVisible ? 1 : 0,
                transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
                transition: `all 0.5s ease-out ${0.2 + i * 0.1}s`,
              }}
            >
              <CardContent className="p-6">
                {/* Quote icon */}
                <Quote className="w-8 h-8 text-primary/30 mb-4" />
                
                {/* Rating */}
                <div className="flex items-center gap-1 mb-4">
                  {[...Array(testimonial.rating)].map((_, idx) => (
                    <Star 
                      key={idx} 
                      className="w-4 h-4 fill-gold text-gold" 
                    />
                  ))}
                </div>

                {/* Text */}
                <p className="text-muted-foreground text-sm leading-relaxed mb-6">
                  "{testimonial.text}"
                </p>

                {/* Author */}
                <div className="flex items-center gap-3">
                  <Avatar className="w-10 h-10 border border-primary/20">
                    <AvatarImage src={testimonial.avatar} />
                    <AvatarFallback className="bg-primary/10 text-primary font-semibold">
                      {testimonial.name.split(' ').map(n => n[0]).join('')}
                    </AvatarFallback>
                  </Avatar>
                  <div>
                    <p className="font-medium text-foreground text-sm">{testimonial.name}</p>
                    <p className="text-xs text-muted-foreground">{testimonial.role}</p>
                  </div>
                </div>

                {/* Verified badge */}
                {testimonial.verified && (
                  <Badge variant="outline" className="mt-4 border-primary/30 text-primary text-xs">
                    ✓ Client verificat • {testimonial.project}
                  </Badge>
                )}
              </CardContent>
            </Card>
          ))}
        </div>

        {/* Mobile carousel */}
        <div className="md:hidden">
          <Card
            className="bg-card/50 border-border transition-all duration-500"
            style={{
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
            }}
          >
            <CardContent className="p-6">
              <Quote className="w-8 h-8 text-primary/30 mb-4" />
              
              <div className="flex items-center gap-1 mb-4">
                {[...Array(testimonials[currentIndex].rating)].map((_, idx) => (
                  <Star key={idx} className="w-4 h-4 fill-gold text-gold" />
                ))}
              </div>

              <p className="text-muted-foreground leading-relaxed mb-6">
                "{testimonials[currentIndex].text}"
              </p>

              <div className="flex items-center gap-3">
                <Avatar className="w-10 h-10 border border-primary/20">
                  <AvatarFallback className="bg-primary/10 text-primary font-semibold">
                    {testimonials[currentIndex].name.split(' ').map(n => n[0]).join('')}
                  </AvatarFallback>
                </Avatar>
                <div>
                  <p className="font-medium text-foreground">{testimonials[currentIndex].name}</p>
                  <p className="text-sm text-muted-foreground">{testimonials[currentIndex].role}</p>
                </div>
              </div>

              {testimonials[currentIndex].verified && (
                <Badge variant="outline" className="mt-4 border-primary/30 text-primary text-xs">
                  ✓ Client verificat
                </Badge>
              )}
            </CardContent>
          </Card>

          {/* Navigation */}
          <div className="flex items-center justify-center gap-4 mt-6">
            <Button
              variant="outline"
              size="icon"
              onClick={prevTestimonial}
              className="border-primary/30 text-primary hover:bg-primary/10"
            >
              <ChevronLeft className="w-4 h-4" />
            </Button>
            
            <div className="flex items-center gap-2">
              {testimonials.map((_, idx) => (
                <button
                  key={idx}
                  onClick={() => setCurrentIndex(idx)}
                  className={`w-2 h-2 rounded-full transition-all duration-300 ${
                    idx === currentIndex 
                      ? 'bg-primary w-6' 
                      : 'bg-muted-foreground/30 hover:bg-muted-foreground/50'
                  }`}
                />
              ))}
            </div>

            <Button
              variant="outline"
              size="icon"
              onClick={nextTestimonial}
              className="border-primary/30 text-primary hover:bg-primary/10"
            >
              <ChevronRight className="w-4 h-4" />
            </Button>
          </div>
        </div>
      </div>
    </section>
  );
}

export default TestimonialsSection;
