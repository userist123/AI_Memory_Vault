import React from 'react';
import { Link } from 'react-router-dom';
import { Facebook, Instagram, Youtube, Phone, Mail, MapPin } from 'lucide-react';

const socialLinks = [
  { icon: Facebook, href: '#', label: 'Facebook' },
  { icon: Instagram, href: '#', label: 'Instagram' },
  { icon: Youtube, href: '#', label: 'YouTube' },
];

const quickLinks = [
  { label: 'Acasa', href: '#home' },
  { label: 'Servicii', href: '#servicii' },
  { label: 'Portofoliu', href: '#portofoliu' },
  { label: 'Preturi', href: '#preturi' },
  { label: 'Contact', href: '#contact' },
];

const services = [
  'Full Wrap',
  'Partial Wrap',
  'PPF Protectie',
  'Colantare Reclama',
  'Folii Faruri',
  'Custom Design',
];

export function Footer() {
  const handleNavClick = (e, href) => {
    e.preventDefault();
    const target = document.querySelector(href);
    if (target) {
      target.scrollIntoView({ behavior: 'smooth' });
    }
  };

  return (
    <footer className="bg-card border-t border-border">
      <div className="max-w-7xl mx-auto px-4 lg:px-8 py-12">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-8 mb-8">
          {/* Brand column */}
          <div>
            <Link to="/" className="inline-block mb-4">
              <h3 className="font-heading font-bold text-2xl gradient-text">
                CrissCustoms
              </h3>
            </Link>
            <p className="text-muted-foreground text-sm mb-4 leading-relaxed">
              Studio premium de colantari auto din Romania. Transformam fiecare masina intr-o opera de arta.
            </p>
            <p className="text-gold italic text-sm">Where Light Meets Art</p>
            
            {/* Social links */}
            <div className="flex items-center gap-3 mt-6">
              {socialLinks.map((social) => (
                <a
                  key={social.label}
                  href={social.href}
                  aria-label={social.label}
                  className="w-10 h-10 rounded-full bg-secondary flex items-center justify-center text-muted-foreground hover:text-primary hover:bg-primary/10 transition-all duration-300"
                >
                  <social.icon className="w-5 h-5" />
                </a>
              ))}
            </div>
          </div>

          {/* Quick links */}
          <div>
            <h4 className="font-heading font-semibold text-foreground mb-4">
              Navigare rapida
            </h4>
            <ul className="space-y-2">
              {quickLinks.map((link) => (
                <li key={link.label}>
                  <a
                    href={link.href}
                    onClick={(e) => handleNavClick(e, link.href)}
                    className="text-muted-foreground hover:text-primary transition-colors text-sm"
                  >
                    {link.label}
                  </a>
                </li>
              ))}
            </ul>
          </div>

          {/* Services */}
          <div>
            <h4 className="font-heading font-semibold text-foreground mb-4">
              Servicii
            </h4>
            <ul className="space-y-2">
              {services.map((service) => (
                <li key={service}>
                  <span className="text-muted-foreground text-sm">{service}</span>
                </li>
              ))}
            </ul>
          </div>

          {/* Contact info */}
          <div>
            <h4 className="font-heading font-semibold text-foreground mb-4">
              Contact
            </h4>
            <ul className="space-y-3">
              <li className="flex items-start gap-3">
                <Phone className="w-4 h-4 text-primary mt-0.5" />
                <a 
                  href="tel:+40722123456" 
                  className="text-muted-foreground hover:text-primary transition-colors text-sm"
                >
                  +40 722 123 456
                </a>
              </li>
              <li className="flex items-start gap-3">
                <Mail className="w-4 h-4 text-primary mt-0.5" />
                <a 
                  href="mailto:contact@crisscustoms.ro" 
                  className="text-muted-foreground hover:text-primary transition-colors text-sm"
                >
                  contact@crisscustoms.ro
                </a>
              </li>
              <li className="flex items-start gap-3">
                <MapPin className="w-4 h-4 text-primary mt-0.5" />
                <span className="text-muted-foreground text-sm">
                  Str. Industriei 123, Bucuresti
                </span>
              </li>
            </ul>
          </div>
        </div>

        {/* Bottom bar */}
        <div className="pt-8 border-t border-border flex flex-col md:flex-row items-center justify-between gap-4">
          <p className="text-muted-foreground text-sm">
            © {new Date().getFullYear()} CrissCustoms & WobArt. Toate drepturile rezervate.
          </p>
          <div className="flex items-center gap-6 text-sm">
            <a href="#" className="text-muted-foreground hover:text-primary transition-colors">
              Politica de confidentialitate
            </a>
            <a href="#" className="text-muted-foreground hover:text-primary transition-colors">
              Termeni si conditii
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
}

export default Footer;
