import React, { useState } from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';
import { Input } from './ui/input';
import { Textarea } from './ui/textarea';
import { Button } from './ui/button';
import { Label } from './ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from './ui/select';
import { Phone, Mail, MapPin, Clock, Send, CheckCircle } from 'lucide-react';
import { toast } from 'sonner';

const contactInfo = [
  {
    icon: Phone,
    label: 'Telefon',
    value: '+40 722 123 456',
    href: 'tel:+40722123456',
  },
  {
    icon: Mail,
    label: 'Email',
    value: 'contact@crisscustoms.ro',
    href: 'mailto:contact@crisscustoms.ro',
  },
  {
    icon: MapPin,
    label: 'Adresa',
    value: 'Str. Industriei 123, Bucuresti',
    href: '#',
  },
  {
    icon: Clock,
    label: 'Program',
    value: 'Luni - Vineri: 09:00 - 18:00',
    href: null,
  },
];

const services = [
  'Full Wrap',
  'Partial Wrap',
  'PPF Protectie',
  'Colantare Reclama',
  'Faruri/Stopuri',
  'Custom Design',
  'Altceva',
];

export function ContactSection() {
  const { ref, isVisible } = useScrollAnimation(0.1);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSubmitted, setIsSubmitted] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    email: '',
    phone: '',
    service: '',
    vehicle: '',
    message: '',
  });

  const handleChange = (e) => {
    const { name, value } = e.target;
    setFormData(prev => ({ ...prev, [name]: value }));
  };

  const handleServiceChange = (value) => {
    setFormData(prev => ({ ...prev, service: value }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setIsSubmitting(true);

    // Simulate form submission
    await new Promise(resolve => setTimeout(resolve, 1500));

    setIsSubmitting(false);
    setIsSubmitted(true);
    toast.success('Mesajul a fost trimis cu succes! Te vom contacta in curand.');
    
    // Reset form after delay
    setTimeout(() => {
      setIsSubmitted(false);
      setFormData({
        name: '',
        email: '',
        phone: '',
        service: '',
        vehicle: '',
        message: '',
      });
    }, 3000);
  };

  return (
    <section
      id="contact"
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
            Contacteaza-ne
          </h2>
          <p className="text-muted-foreground text-lg max-w-2xl mx-auto">
            Solicita o oferta gratuita sau programeaza o consultatie. Echipa noastra iti sta la dispozitie.
          </p>
        </div>

        <div className="grid lg:grid-cols-2 gap-8">
          {/* Contact info */}
          <div
            className="space-y-6 transition-all duration-700"
            style={{
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translateX(0)' : 'translateX(-30px)',
              transitionDelay: '0.2s',
            }}
          >
            <h3 className="font-heading font-semibold text-2xl text-foreground mb-6">
              Informatii de contact
            </h3>
            
            <div className="grid gap-4">
              {contactInfo.map((info, i) => (
                <Card
                  key={info.label}
                  className="bg-card/50 border-border hover:border-primary/30 transition-all duration-300"
                  style={{
                    opacity: isVisible ? 1 : 0,
                    transform: isVisible ? 'translateX(0)' : 'translateX(-20px)',
                    transition: `all 0.4s ease-out ${0.3 + i * 0.1}s`,
                  }}
                >
                  <CardContent className="p-4 flex items-center gap-4">
                    <div className="p-3 rounded-lg bg-primary/10">
                      <info.icon className="w-5 h-5 text-primary" />
                    </div>
                    <div>
                      <p className="text-sm text-muted-foreground">{info.label}</p>
                      {info.href ? (
                        <a 
                          href={info.href}
                          className="font-medium text-foreground hover:text-primary transition-colors"
                        >
                          {info.value}
                        </a>
                      ) : (
                        <p className="font-medium text-foreground">{info.value}</p>
                      )}
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>

            {/* Map placeholder */}
            <div 
              className="mt-8 rounded-xl overflow-hidden border border-border h-[200px] relative"
              style={{
                background: 'linear-gradient(135deg, hsl(328 100% 54% / 0.05), hsl(0 0% 5%))',
              }}
            >
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="text-center">
                  <MapPin className="w-12 h-12 text-primary/50 mx-auto mb-2" />
                  <p className="text-muted-foreground">Bucuresti, Romania</p>
                </div>
              </div>
            </div>
          </div>

          {/* Contact form */}
          <Card
            className="bg-card/50 border-border transition-all duration-700"
            style={{
              opacity: isVisible ? 1 : 0,
              transform: isVisible ? 'translateX(0)' : 'translateX(30px)',
              transitionDelay: '0.3s',
            }}
          >
            <CardHeader>
              <CardTitle className="font-heading text-xl">Solicita oferta gratuita</CardTitle>
            </CardHeader>
            <CardContent>
              {isSubmitted ? (
                <div className="flex flex-col items-center justify-center py-12 text-center">
                  <div className="w-16 h-16 rounded-full bg-primary/20 flex items-center justify-center mb-4 animate-neon-pulse">
                    <CheckCircle className="w-8 h-8 text-primary" />
                  </div>
                  <h3 className="font-heading font-semibold text-xl text-foreground mb-2">
                    Mesaj trimis cu succes!
                  </h3>
                  <p className="text-muted-foreground">
                    Te vom contacta in cel mai scurt timp posibil.
                  </p>
                </div>
              ) : (
                <form onSubmit={handleSubmit} className="space-y-4">
                  <div className="grid sm:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label htmlFor="name">Nume complet *</Label>
                      <Input
                        id="name"
                        name="name"
                        value={formData.name}
                        onChange={handleChange}
                        placeholder="Ion Popescu"
                        required
                        className="bg-input border-border focus:border-primary"
                      />
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="phone">Telefon *</Label>
                      <Input
                        id="phone"
                        name="phone"
                        type="tel"
                        value={formData.phone}
                        onChange={handleChange}
                        placeholder="+40 722 123 456"
                        required
                        className="bg-input border-border focus:border-primary"
                      />
                    </div>
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="email">Email *</Label>
                    <Input
                      id="email"
                      name="email"
                      type="email"
                      value={formData.email}
                      onChange={handleChange}
                      placeholder="email@example.com"
                      required
                      className="bg-input border-border focus:border-primary"
                    />
                  </div>

                  <div className="grid sm:grid-cols-2 gap-4">
                    <div className="space-y-2">
                      <Label htmlFor="service">Serviciu dorit</Label>
                      <Select value={formData.service} onValueChange={handleServiceChange}>
                        <SelectTrigger className="bg-input border-border">
                          <SelectValue placeholder="Selecteaza serviciul" />
                        </SelectTrigger>
                        <SelectContent>
                          {services.map(service => (
                            <SelectItem key={service} value={service}>
                              {service}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="vehicle">Marca si model</Label>
                      <Input
                        id="vehicle"
                        name="vehicle"
                        value={formData.vehicle}
                        onChange={handleChange}
                        placeholder="Ex: BMW M4 2023"
                        className="bg-input border-border focus:border-primary"
                      />
                    </div>
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="message">Mesaj</Label>
                    <Textarea
                      id="message"
                      name="message"
                      value={formData.message}
                      onChange={handleChange}
                      placeholder="Descrie proiectul tau sau intrebarile pe care le ai..."
                      rows={4}
                      className="bg-input border-border focus:border-primary resize-none"
                    />
                  </div>

                  <Button
                    type="submit"
                    disabled={isSubmitting}
                    className="w-full btn-neon text-primary-foreground font-semibold py-6"
                    size="lg"
                  >
                    {isSubmitting ? (
                      <span className="flex items-center gap-2">
                        <span className="w-4 h-4 border-2 border-primary-foreground/30 border-t-primary-foreground rounded-full animate-spin" />
                        Se trimite...
                      </span>
                    ) : (
                      <span className="flex items-center gap-2">
                        <Send className="w-4 h-4" />
                        Trimite cererea
                      </span>
                    )}
                  </Button>

                  <p className="text-xs text-muted-foreground text-center">
                    Prin trimiterea acestui formular, esti de acord cu prelucrarea datelor tale personale.
                  </p>
                </form>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </section>
  );
}

export default ContactSection;
