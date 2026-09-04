import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Zap, Menu, X, Github, Sun, Moon } from 'lucide-react';
import { useTheme } from '../../contexts/ThemeContext';

export default function Navbar() {
    const [isOpen, setIsOpen] = useState(false);
    const [scrolled, setScrolled] = useState(false);
    const { currentTheme, switchTheme } = useTheme();

    useEffect(() => {
        const handleScroll = () => {
            if (window.scrollY > 20) {
                setScrolled(true);
            } else {
                setScrolled(false);
            }
        };
        window.addEventListener('scroll', handleScroll);
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    const scrollToSection = (id) => {
        setIsOpen(false);
        const element = document.getElementById(id);
        if (element) {
            element.scrollIntoView({ behavior: 'smooth' });
        }
    };

    return (
        <nav
            className={`fixed top-0 left-0 right-0 z-50 transition-all duration-300 ${scrolled
                    ? 'bg-background/85 backdrop-blur-md border-b border-border/40 shadow-sm'
                    : 'bg-transparent'
                }`}
        >
            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
                <div className="flex items-center justify-between h-16">
                    {/* Logo */}
                    <div className="flex items-center gap-2">
                        <div className="flex items-center justify-center w-8 h-8 rounded-lg bg-indigo-600/10 text-indigo-400 ring-1 ring-indigo-500/20 shadow-sm">
                            <Zap className="w-4.5 h-4.5 fill-indigo-400/20" />
                        </div>
                        <span className="font-semibold text-foreground tracking-tight text-lg">PulseAPI</span>
                    </div>

                    {/* Desktop Navigation */}
                    <div className="hidden md:flex items-center gap-8">
                        <button
                            onClick={() => scrollToSection('features')}
                            className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                        >
                            Features
                        </button>
                        <button
                            onClick={() => scrollToSection('how-it-works')}
                            className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                        >
                            How it Works
                        </button>
                        <button
                            onClick={() => scrollToSection('docs-preview')}
                            className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                        >
                            Documentation
                        </button>
                        <button
                            onClick={() => scrollToSection('about')}
                            className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                        >
                            About
                        </button>
                    </div>

                    {/* Action buttons */}
                    <div className="hidden md:flex items-center gap-4">
                        <a
                            href="https://github.com/Katari-8055/PulseAPI"
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-muted-foreground hover:text-foreground transition-colors"
                        >
                            <Github className="w-5 h-5" />
                        </a>

                        <button
                            onClick={() => switchTheme(currentTheme === 'dark' ? 'light' : 'dark')}
                            className="p-2 rounded-lg hover:bg-muted/50 text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                            title={`Switch to ${currentTheme === 'dark' ? 'Light' : 'Dark'} theme`}
                        >
                            {currentTheme === 'dark' ? <Sun className="w-4.5 h-4.5" /> : <Moon className="w-4.5 h-4.5" />}
                        </button>

                        <Link
                            to="/login"
                            className="text-sm font-medium text-muted-foreground hover:text-foreground transition-colors px-3 py-1.5 rounded-lg hover:bg-muted/50"
                        >
                            Login
                        </Link>
                        <Link
                            to="/register"
                            className="text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 px-4 py-2 rounded-lg transition-colors shadow-sm cursor-pointer"
                        >
                            Sign Up
                        </Link>
                    </div>

                    {/* Mobile menu button */}
                    <div className="md:hidden flex items-center gap-3">
                        <button
                            onClick={() => switchTheme(currentTheme === 'dark' ? 'light' : 'dark')}
                            className="p-2 rounded-lg hover:bg-muted/50 text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                        >
                            {currentTheme === 'dark' ? <Sun className="w-4.5 h-4.5" /> : <Moon className="w-4.5 h-4.5" />}
                        </button>
                        <button
                            onClick={() => setIsOpen(!isOpen)}
                            className="text-muted-foreground hover:text-foreground p-1 rounded-lg hover:bg-muted/50 transition-colors"
                        >
                            {isOpen ? <X className="w-6 h-6" /> : <Menu className="w-6 h-6" />}
                        </button>
                    </div>
                </div>
            </div>

            {/* Mobile menu */}
            {isOpen && (
                <div className="md:hidden border-b border-border/40 bg-background/95 backdrop-blur-lg">
                    <div className="px-2 pt-2 pb-4 space-y-1 sm:px-3 flex flex-col">
                        <button
                            onClick={() => scrollToSection('features')}
                            className="text-left w-full px-3 py-2 rounded-md text-base font-medium text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-all"
                        >
                            Features
                        </button>
                        <button
                            onClick={() => scrollToSection('how-it-works')}
                            className="text-left w-full px-3 py-2 rounded-md text-base font-medium text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-all"
                        >
                            How it Works
                        </button>
                        <button
                            onClick={() => scrollToSection('docs-preview')}
                            className="text-left w-full px-3 py-2 rounded-md text-base font-medium text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-all"
                        >
                            Documentation
                        </button>
                        <button
                            onClick={() => scrollToSection('about')}
                            className="text-left w-full px-3 py-2 rounded-md text-base font-medium text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-all"
                        >
                            About
                        </button>
                        <hr className="border-border/40 my-2" />
                        <div className="flex items-center gap-4 px-3 py-2">
                            <a
                                href="https://github.com"
                                target="_blank"
                                rel="noopener noreferrer"
                                className="text-muted-foreground hover:text-foreground transition-colors flex items-center gap-2 text-sm font-medium"
                            >
                                <Github className="w-5 h-5" /> GitHub
                            </a>
                        </div>
                        <div className="grid grid-cols-2 gap-2 p-2">
                            <Link
                                to="/login"
                                className="text-center text-sm font-medium text-muted-foreground hover:text-foreground hover:bg-muted/50 py-2.5 rounded-lg border border-border/40 transition-colors"
                            >
                                Login
                            </Link>
                            <Link
                                to="/register"
                                className="text-center text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 py-2.5 rounded-lg transition-colors shadow-sm"
                            >
                                Sign Up
                            </Link>
                        </div>
                    </div>
                </div>
            )}
        </nav>
    );
}
