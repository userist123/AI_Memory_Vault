import { Link } from 'react-router-dom';
import { Zap, Github, ArrowRight } from 'lucide-react';

export default function Footer() {
    const currentYear = new Date().getFullYear();

    const scrollToTop = () => {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    };

    const scrollToDocs = () => {
        const docs = document.getElementById('docs-preview');
        if (docs) {
            docs.scrollIntoView({ behavior: 'smooth' });
        }
    };

    return (
        <footer className="relative bg-background border-t border-border/40 overflow-hidden">
            {/* Background spotlight */}
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute bottom-[-100px] left-[50%] -translate-x-1/2 w-[60%] h-[200px] bg-indigo-500/10 rounded-full blur-[140px]" />
            </div>

            {/* CTA Section */}
            <div className="max-w-4xl mx-auto px-4 py-20 text-center relative border-b border-border/20">
                <h2 className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
                    Start monitoring in minutes
                </h2>
                <p className="mt-4 text-sm text-muted-foreground max-w-md mx-auto leading-relaxed">
                    Set up continuous uptime, latency, and status code checks for your APIs. No credit card required.
                </p>
                <div className="mt-8 flex flex-col sm:flex-row items-center justify-center gap-4">
                    <Link
                        to="/register"
                        className="w-full sm:w-auto flex items-center justify-center gap-2 text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-700 px-6 py-3.5 rounded-xl transition-all shadow-md group cursor-pointer"
                    >
                        Start Monitoring
                        <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
                    </Link>
                    <button
                        onClick={scrollToDocs}
                        className="w-full sm:w-auto flex items-center justify-center gap-2 text-sm font-semibold text-foreground bg-muted/60 hover:bg-muted border border-border/40 hover:border-border/80 px-6 py-3.5 rounded-xl transition-all cursor-pointer"
                    >
                        Read Documentation
                    </button>
                </div>
            </div>

            {/* Links & Brand section */}
            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12 relative">
                <div className="flex flex-col md:flex-row items-center justify-between gap-6">
                    {/* Brand */}
                    <div className="flex items-center gap-2 cursor-pointer" onClick={scrollToTop}>
                        <div className="flex items-center justify-center w-7 h-7 rounded-lg bg-indigo-600/10 text-indigo-400 ring-1 ring-indigo-500/20">
                            <Zap className="w-4 h-4 fill-indigo-400/20" />
                        </div>
                        <span className="font-semibold text-foreground text-sm tracking-tight">PulseAPI</span>
                    </div>

                    {/* Nav Links */}
                    <div className="flex flex-wrap justify-center gap-x-8 gap-y-4 text-xs font-medium text-muted-foreground">
                        <button onClick={scrollToDocs} className="hover:text-foreground transition-colors cursor-pointer">Documentation</button>
                        <a href="https://github.com" target="_blank" rel="noopener noreferrer" className="hover:text-foreground transition-colors">GitHub</a>
                        <a href="#" className="hover:text-foreground transition-colors">Privacy Policy</a>
                        <a href="#" className="hover:text-foreground transition-colors">Terms of Service</a>
                        <a href="mailto:support@pulseapi.com" className="hover:text-foreground transition-colors">Contact</a>
                    </div>
                </div>

                <div className="mt-8 pt-8 border-t border-border/10 flex flex-col sm:flex-row items-center justify-between gap-4">
                    <span className="text-[11px] text-muted-foreground/70">
                        &copy; {currentYear} PulseAPI Inc. All rights reserved.
                    </span>
                    <span className="text-[11px] text-muted-foreground/50 font-mono">
                        Handcrafted for modern backend architectures.
                    </span>
                </div>
            </div>
        </footer>
    );
}
