import React, { useState, useEffect } from 'react';
import { Navbar } from '../components/Navbar';
import { HeroSection } from '../components/HeroSection';
import { AboutSection } from '../components/AboutSection';
import { ServicesSection } from '../components/ServicesSection';
import { PortfolioSection } from '../components/PortfolioSection';
import { ProcessSection } from '../components/ProcessSection';
import { PricingSection } from '../components/PricingSection';
import { TestimonialsSection } from '../components/TestimonialsSection';
import { FAQSection } from '../components/FAQSection';
import { ContactSection } from '../components/ContactSection';
import { Footer } from '../components/Footer';
import { useAuth } from '../contexts/AuthContext';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '../components/ui/dialog';
import { Button } from '../components/ui/button';
import { Link } from 'react-router-dom';
import { MessageCircle, X, Send, Phone, Mail, Sparkles, Gift, ArrowRight } from 'lucide-react';
import { Input } from '../components/ui/input';
import { toast } from 'sonner';

export default function HomePage() {
  const { isAuthenticated, user } = useAuth();
  const [showWelcomePopup, setShowWelcomePopup] = useState(false);
  const [showPromoPopup, setShowPromoPopup] = useState(false);
  const [showChatWidget, setShowChatWidget] = useState(false);
  const [chatMessage, setChatMessage] = useState('');
  const [chatMessages, setChatMessages] = useState([
    { from: 'bot', text: 'Buna! Sunt asistentul virtual CrissCustoms. Cum te pot ajuta?' }
  ]);

  useEffect(() => {
    // Show welcome popup for first-time visitors (guests)
    const hasVisited = localStorage.getItem('crisscustoms_visited');
    if (!hasVisited && !isAuthenticated) {
      setTimeout(() => {
        setShowWelcomePopup(true);
        localStorage.setItem('crisscustoms_visited', 'true');
      }, 3000);
    }

    // Show promo popup after scrolling
    const handleScroll = () => {
      if (window.scrollY > 1500 && !showPromoPopup && !isAuthenticated) {
        const hasSeenPromo = sessionStorage.getItem('crisscustoms_promo_seen');
        if (!hasSeenPromo) {
          setShowPromoPopup(true);
          sessionStorage.setItem('crisscustoms_promo_seen', 'true');
        }
      }
    };

    window.addEventListener('scroll', handleScroll);
    return () => window.removeEventListener('scroll', handleScroll);
  }, [isAuthenticated, showPromoPopup]);

  const handleSendChat = () => {
    if (!chatMessage.trim()) return;
    
    setChatMessages(prev => [...prev, { from: 'user', text: chatMessage }]);
    setChatMessage('');
    
    // Simulate bot response
    setTimeout(() => {
      let response = 'Multumim pentru mesaj! Un consultant te va contacta in curand. Pentru urgente, suna-ne la +40 722 123 456.';
      
      if (chatMessage.toLowerCase().includes('pret') || chatMessage.toLowerCase().includes('cost')) {
        response = 'Preturile noastre incep de la 500€ pentru partial wrap si de la 2500€ pentru full wrap. Pentru o oferta personalizata, te rog sa ne lasi datele de contact sau suna-ne la +40 722 123 456.';
      } else if (chatMessage.toLowerCase().includes('programare') || chatMessage.toLowerCase().includes('disponibil')) {
        response = 'Pentru programari, te rugam sa completezi formularul de contact sau sa ne suni direct la +40 722 123 456. In general, avem disponibilitate in 1-2 saptamani.';
      }
      
      setChatMessages(prev => [...prev, { from: 'bot', text: response }]);
    }, 1000);
  };

  return (
    <div className="min-h-screen bg-background">
      <Navbar />
      <main>
        <HeroSection />
        <AboutSection />
        <ServicesSection />
        <PortfolioSection />
        <ProcessSection />
        <PricingSection />
        <TestimonialsSection />
        <FAQSection />
        <ContactSection />
      </main>
      <Footer />

      {/* Floating Action Button - Chat Widget (for guests) */}
      {!isAuthenticated && (
        <>
          <button
            onClick={() => setShowChatWidget(!showChatWidget)}
            className="fab group"
            style={{ bottom: '2rem', right: '2rem' }}
          >
            {showChatWidget ? (
              <X className="w-6 h-6 text-white" />
            ) : (
              <MessageCircle className="w-6 h-6 text-white" />
            )}
          </button>

          {/* Chat Widget */}
          <div
            className={`fixed bottom-24 right-8 w-80 glass-card rounded-2xl overflow-hidden z-50 transition-all duration-300 ${
              showChatWidget 
                ? 'opacity-100 translate-y-0 pointer-events-auto' 
                : 'opacity-0 translate-y-4 pointer-events-none'
            }`}
          >
            <div className="bg-gradient-to-r from-primary to-accent p-4">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-full bg-white/20 flex items-center justify-center">
                  <MessageCircle className="w-5 h-5 text-white" />
                </div>
                <div>
                  <p className="font-semibold text-white">CrissCustoms Chat</p>
                  <p className="text-xs text-white/80">De obicei raspundem in cateva minute</p>
                </div>
              </div>
            </div>
            
            <div className="h-64 overflow-y-auto p-4 space-y-3">
              {chatMessages.map((msg, idx) => (
                <div
                  key={idx}
                  className={`flex ${msg.from === 'user' ? 'justify-end' : 'justify-start'}`}
                >
                  <div
                    className={`max-w-[80%] p-3 rounded-xl text-sm ${
                      msg.from === 'user'
                        ? 'bg-primary text-white rounded-br-none'
                        : 'bg-card border border-border rounded-bl-none'
                    }`}
                  >
                    {msg.text}
                  </div>
                </div>
              ))}
            </div>
            
            <div className="p-4 border-t border-border">
              <div className="flex gap-2">
                <Input
                  placeholder="Scrie un mesaj..."
                  value={chatMessage}
                  onChange={(e) => setChatMessage(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSendChat()}
                  className="glass-input text-sm"
                />
                <Button size="icon" className="btn-neon" onClick={handleSendChat}>
                  <Send className="w-4 h-4" />
                </Button>
              </div>
              <div className="flex items-center justify-center gap-4 mt-3">
                <a href="tel:+40722123456" className="flex items-center gap-1 text-xs text-muted-foreground hover:text-primary transition-colors">
                  <Phone className="w-3 h-3" />
                  Suna-ne
                </a>
                <a href="mailto:contact@crisscustoms.ro" className="flex items-center gap-1 text-xs text-muted-foreground hover:text-primary transition-colors">
                  <Mail className="w-3 h-3" />
                  Email
                </a>
              </div>
            </div>
          </div>
        </>
      )}

      {/* Welcome Popup (for first-time guests) */}
      <Dialog open={showWelcomePopup} onOpenChange={setShowWelcomePopup}>
        <DialogContent className="glass-card border-primary/30 max-w-md">
          <DialogHeader className="text-center">
            <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-gradient-to-br from-primary to-accent flex items-center justify-center animate-neon-pulse">
              <Sparkles className="w-8 h-8 text-white" />
            </div>
            <DialogTitle className="font-heading text-2xl gradient-text">
              Bine ai venit la CrissCustoms!
            </DialogTitle>
            <DialogDescription className="text-muted-foreground mt-2">
              Transforma-ti masina intr-o opera de arta. Suntem specialisti in colantari auto premium cu peste 8 ani de experienta.
            </DialogDescription>
          </DialogHeader>
          
          <div className="space-y-3 py-4">
            <div className="flex items-center gap-3 p-3 rounded-lg bg-card/50">
              <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center">
                <span className="text-primary font-bold">1</span>
              </div>
              <span className="text-sm">Exploreaza portofoliul nostru</span>
            </div>
            <div className="flex items-center gap-3 p-3 rounded-lg bg-card/50">
              <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center">
                <span className="text-primary font-bold">2</span>
              </div>
              <span className="text-sm">Cere o oferta personalizata GRATUITA</span>
            </div>
            <div className="flex items-center gap-3 p-3 rounded-lg bg-card/50">
              <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center">
                <span className="text-primary font-bold">3</span>
              </div>
              <span className="text-sm">Programeaza o consultatie</span>
            </div>
          </div>

          <DialogFooter className="flex-col sm:flex-col gap-2">
            <Button 
              className="w-full btn-neon" 
              onClick={() => {
                setShowWelcomePopup(false);
                document.querySelector('#contact')?.scrollIntoView({ behavior: 'smooth' });
              }}
            >
              Cere oferta gratuita
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
            <Button 
              variant="ghost" 
              className="w-full"
              onClick={() => setShowWelcomePopup(false)}
            >
              Exploreza site-ul
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Promo Popup (scroll-triggered for guests) */}
      <Dialog open={showPromoPopup} onOpenChange={setShowPromoPopup}>
        <DialogContent className="glass-card-pink border-primary/30 max-w-md">
          <DialogHeader className="text-center">
            <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-gold/20 flex items-center justify-center">
              <Gift className="w-8 h-8 text-gold animate-bounce-slow" />
            </div>
            <DialogTitle className="font-heading text-2xl">
              <span className="text-gold">-10%</span> la prima comanda!
            </DialogTitle>
            <DialogDescription className="text-muted-foreground mt-2">
              Inscrie-te acum si beneficiezi de 10% reducere la primul tau proiect de colantare.
            </DialogDescription>
          </DialogHeader>
          
          <div className="py-4">
            <Input 
              placeholder="Adresa ta de email"
              type="email"
              className="glass-input mb-3"
            />
            <Button 
              className="w-full btn-neon"
              onClick={() => {
                toast.success('Te-ai inscris cu succes! Vei primi codul de reducere pe email.');
                setShowPromoPopup(false);
              }}
            >
              Vreau reducerea!
            </Button>
          </div>

          <p className="text-xs text-center text-muted-foreground">
            Prin inscriere, esti de acord cu primirea ofertelor promotionale.
          </p>
        </DialogContent>
      </Dialog>

      {/* Logged-in user floating dashboard button */}
      {isAuthenticated && (
        <Link 
          to={user?.role === 'admin' ? '/admin' : '/dashboard'}
          className="fab"
          style={{ bottom: '2rem', right: '2rem' }}
        >
          <span className="text-white font-semibold text-xs">
            {user?.role === 'admin' ? 'Admin' : 'Dashboard'}
          </span>
        </Link>
      )}
    </div>
  );
}
