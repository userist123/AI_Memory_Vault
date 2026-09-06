import React, { useState, useEffect } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { Menu, X, User, LogIn, LogOut, LayoutDashboard } from 'lucide-react';
import { Button } from './ui/button';
import { useAuth } from '../contexts/AuthContext';

const navLinks = [
  { href: '#home', label: 'Acasa' },
  { href: '#despre', label: 'Despre' },
  { href: '#servicii', label: 'Servicii' },
  { href: '#portofoliu', label: 'Portofoliu' },
  { href: '#proces', label: 'Proces' },
  { href: '#preturi', label: 'Preturi' },
  { href: '#faq', label: 'FAQ' },
  { href: '#contact', label: 'Contact' },
];

export function Navbar() {
  const [isScrolled, setIsScrolled] = useState(false);
  const [isMobileOpen, setIsMobileOpen] = useState(false);
  const [navVisible, setNavVisible] = useState(false);
  const { user, isAuthenticated, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    const t = setTimeout(() => setNavVisible(true), 300);
    
    const handleScroll = () => {
      setIsScrolled(window.scrollY > 50);
    };
    
    window.addEventListener('scroll', handleScroll, { passive: true });
    
    return () => {
      clearTimeout(t);
      window.removeEventListener('scroll', handleScroll);
    };
  }, []);

  const handleNavClick = (e, href) => {
    e.preventDefault();
    
    // If not on home page, navigate to home first
    if (location.pathname !== '/') {
      navigate('/');
      setTimeout(() => {
        const target = document.querySelector(href);
        if (target) {
          const navHeight = isScrolled ? 56 : 68;
          const targetPosition = target.getBoundingClientRect().top + window.scrollY - navHeight;
          window.scrollTo({ top: targetPosition, behavior: 'smooth' });
        }
      }, 100);
    } else {
      const target = document.querySelector(href);
      if (target) {
        const navHeight = isScrolled ? 56 : 68;
        const targetPosition = target.getBoundingClientRect().top + window.scrollY - navHeight;
        window.scrollTo({ top: targetPosition, behavior: 'smooth' });
      }
    }
    setIsMobileOpen(false);
  };

  const handleLogout = () => {
    logout();
    navigate('/');
    setIsMobileOpen(false);
  };

  const dashboardUrl = user?.role === 'admin' ? '/admin' : '/dashboard';

  return (
    <nav
      className={`fixed top-0 w-full z-50 transition-all duration-500 ${
        isScrolled
          ? 'py-2 bg-background/95 backdrop-blur-xl shadow-neon'
          : 'py-4 bg-background/60 backdrop-blur-md'
      }`}
    >
      <div className="max-w-7xl mx-auto flex items-center justify-between px-4 lg:px-8">
        {/* Logo */}
        <Link
          to="/"
          className="font-heading font-bold text-lg tracking-wider text-primary transition-all duration-500"
          style={{
            opacity: navVisible ? 1 : 0,
            transform: navVisible ? 'translateX(0)' : 'translateX(-20px)',
            textShadow: isScrolled ? '0 0 20px hsl(328 100% 54% / 0.5)' : 'none',
          }}
        >
          CrissCustoms
        </Link>

        {/* Desktop navigation */}
        <ul className="hidden lg:flex items-center gap-6">
          {navLinks.map((link, i) => (
            <li key={link.href}>
              <a
                href={link.href}
                onClick={(e) => handleNavClick(e, link.href)}
                className="text-sm font-medium text-muted-foreground hover:text-primary transition-all duration-300 tracking-wide relative group"
                style={{
                  opacity: navVisible ? 1 : 0,
                  transform: navVisible ? 'translateY(0)' : 'translateY(-10px)',
                  transition: `opacity 400ms ease-out ${i * 50}ms, transform 400ms ease-out ${i * 50}ms, color 200ms`,
                }}
              >
                {link.label}
                <span className="absolute -bottom-1 left-0 w-0 h-0.5 bg-primary group-hover:w-full transition-all duration-300" />
              </a>
            </li>
          ))}
          
          {/* Auth buttons */}
          <li className="flex items-center gap-2 ml-4">
            {isAuthenticated ? (
              <>
                <Link to={dashboardUrl}>
                  <Button 
                    variant="ghost" 
                    size="sm"
                    className="text-muted-foreground hover:text-primary hover:bg-primary/10"
                    style={{
                      opacity: navVisible ? 1 : 0,
                      transition: `opacity 400ms ease-out ${navLinks.length * 50}ms`,
                    }}
                  >
                    <LayoutDashboard className="w-4 h-4 mr-2" />
                    {user?.role === 'admin' ? 'Admin' : 'Dashboard'}
                  </Button>
                </Link>
                <Button 
                  variant="outline" 
                  size="sm"
                  onClick={handleLogout}
                  className="border-primary/30 text-primary hover:bg-primary/10"
                  style={{
                    opacity: navVisible ? 1 : 0,
                    transition: `opacity 400ms ease-out ${(navLinks.length + 1) * 50}ms`,
                  }}
                >
                  <LogOut className="w-4 h-4 mr-2" />
                  Logout
                </Button>
              </>
            ) : (
              <Link to="/login">
                <Button 
                  className="btn-neon text-primary-foreground"
                  size="sm"
                  style={{
                    opacity: navVisible ? 1 : 0,
                    transition: `opacity 400ms ease-out ${navLinks.length * 50}ms`,
                  }}
                >
                  <LogIn className="w-4 h-4 mr-2" />
                  Login
                </Button>
              </Link>
            )}
          </li>
        </ul>

        {/* Mobile hamburger */}
        <button
          type="button"
          onClick={() => setIsMobileOpen(!isMobileOpen)}
          className="lg:hidden p-2 text-primary transition-transform duration-300 z-50 relative"
          style={{
            transform: isMobileOpen ? 'rotate(90deg)' : 'rotate(0deg)',
          }}
          aria-label={isMobileOpen ? 'Inchide meniul' : 'Deschide meniul'}
        >
          {isMobileOpen ? <X size={24} /> : <Menu size={24} />}
        </button>
      </div>

      {/* Mobile menu */}
      <div
        className={`lg:hidden overflow-hidden transition-all duration-400 z-40 ${
          isMobileOpen ? 'max-h-[600px] opacity-100' : 'max-h-0 opacity-0'
        }`}
      >
        <ul className="flex flex-col items-center gap-2 py-4 bg-background/95 backdrop-blur-xl">
          {navLinks.map((link, i) => (
            <li key={link.href}>
              <a
                href={link.href}
                onClick={(e) => handleNavClick(e, link.href)}
                className="text-sm font-medium text-muted-foreground hover:text-primary transition-all duration-300 px-4 py-2 rounded-lg hover:bg-primary/10"
                style={{
                  opacity: isMobileOpen ? 1 : 0,
                  transform: isMobileOpen ? 'translateX(0)' : 'translateX(-20px)',
                  transition: `opacity 300ms ease-out ${i * 40}ms, transform 300ms ease-out ${i * 40}ms, color 200ms, background-color 200ms`,
                }}
              >
                {link.label}
              </a>
            </li>
          ))}
          
          {/* Mobile auth buttons */}
          <li className="flex flex-col items-center gap-2 mt-4 pt-4 border-t border-border w-full">
            {isAuthenticated ? (
              <>
                <Link to={dashboardUrl} onClick={() => setIsMobileOpen(false)}>
                  <Button variant="ghost" size="sm" className="text-muted-foreground hover:text-primary">
                    <LayoutDashboard className="w-4 h-4 mr-2" />
                    {user?.role === 'admin' ? 'Admin Panel' : 'Dashboard'}
                  </Button>
                </Link>
                <Button 
                  variant="outline" 
                  size="sm"
                  onClick={handleLogout}
                  className="border-primary/30 text-primary"
                >
                  <LogOut className="w-4 h-4 mr-2" />
                  Logout
                </Button>
              </>
            ) : (
              <Link to="/login" onClick={() => setIsMobileOpen(false)}>
                <Button className="btn-neon text-primary-foreground">
                  <LogIn className="w-4 h-4 mr-2" />
                  Login
                </Button>
              </Link>
            )}
          </li>
        </ul>
      </div>
    </nav>
  );
}

export default Navbar;
