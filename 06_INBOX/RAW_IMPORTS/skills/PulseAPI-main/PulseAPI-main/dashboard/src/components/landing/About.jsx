import { CheckCircle2, Heart } from 'lucide-react';

export default function About() {
    return (
        <section id="about" className="py-24 bg-muted/20 border-t border-border/45 relative">
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute top-[20%] left-[10%] w-[35%] h-[35%] bg-purple-500/5 rounded-full blur-[140px]" />
            </div>

            <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 relative">
                <div className="grid grid-cols-1 md:grid-cols-2 gap-12 items-center">
                    
                    {/* Visual Badge details */}
                    <div className="space-y-6">
                        <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-purple-500/10 text-purple-700 dark:text-purple-400 border border-purple-500/20 text-xs font-semibold">
                            <Heart className="w-3.5 h-3.5 fill-purple-500/20 dark:fill-purple-400/20" />
                            Our Philosophy
                        </div>
                        
                        <h2 className="text-3xl font-bold tracking-tight text-foreground leading-tight">
                            Built by developers, <br />
                            for developers.
                        </h2>
                        
                        <p className="text-sm text-muted-foreground leading-relaxed">
                            PulseAPI was born out of frustration. We spent years managing complicated SaaS alert stacks that either flooded our channels with false positives or failed entirely due to minor configuration changes.
                        </p>
                        <p className="text-sm text-muted-foreground leading-relaxed">
                            We believe monitoring should be quiet, reliable, and invisible until something breaks. Our focus is to deliver key metric insights with zero infrastructure overhead.
                        </p>
                    </div>

                    {/* Features list side */}
                    <div className="rounded-2xl border border-border/50 bg-card p-6 sm:p-8 space-y-6">
                        <h3 className="text-sm font-semibold text-foreground uppercase tracking-wider font-mono">Core Product Standards</h3>
                        
                        <div className="space-y-4">
                            <div className="flex gap-3">
                                <CheckCircle2 className="w-5 h-5 text-indigo-600 dark:text-indigo-400 flex-shrink-0 mt-0.5" />
                                <div>
                                    <h4 className="text-sm font-semibold text-foreground">Zero Server Bloat</h4>
                                    <p className="text-xs text-muted-foreground mt-1">Our tracking client functions asynchronously, ensuring no request processing blocking or overhead latency.</p>
                                </div>
                            </div>
                            <div className="flex gap-3">
                                <CheckCircle2 className="w-5 h-5 text-indigo-600 dark:text-indigo-400 flex-shrink-0 mt-0.5" />
                                <div>
                                    <h4 className="text-sm font-semibold text-foreground">Focused Alert Thresholds</h4>
                                    <p className="text-xs text-muted-foreground mt-1">Easily filter transient latency spikes from actual outages, preventing alert fatigue in developer channels.</p>
                                </div>
                            </div>
                            <div className="flex gap-3">
                                <CheckCircle2 className="w-5 h-5 text-indigo-600 dark:text-indigo-400 flex-shrink-0 mt-0.5" />
                                <div>
                                    <h4 className="text-sm font-semibold text-foreground">Secure by Design</h4>
                                    <p className="text-xs text-muted-foreground mt-1">Sensitive header attributes and token hashes are redacted locally on client servers before dispatching metrics.</p>
                                </div>
                            </div>
                        </div>
                    </div>

                </div>
            </div>
        </section>
    );
}
