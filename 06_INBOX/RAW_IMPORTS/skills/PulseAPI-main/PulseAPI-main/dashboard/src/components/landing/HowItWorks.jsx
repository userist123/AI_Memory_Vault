import { FolderPlus, Link2, MonitorPlay } from 'lucide-react';

export default function HowItWorks() {
    const steps = [
        {
            num: "01",
            title: "Create a Project",
            desc: "Register your client organization account, configure your environments, and establish monitoring projects in seconds.",
            icon: FolderPlus,
            color: "from-indigo-500/20 to-purple-500/5 text-indigo-400"
        },
        {
            num: "02",
            title: "Integrate the SDK",
            desc: "Add our tiny client SDK to your codebase. Works natively as standard middleware for Express, Fastify, Go, or Django.",
            icon: Link2,
            color: "from-purple-500/20 to-indigo-500/5 text-purple-400"
        },
        {
            num: "03",
            title: "Stream Live Observability",
            desc: "Watch your dashboard connect automatically. View status code frequencies, latency metrics, and trace incidents live.",
            icon: MonitorPlay,
            color: "from-indigo-500/20 to-emerald-500/5 text-emerald-400"
        }
    ];

    return (
        <section id="how-it-works" className="py-24 bg-muted/20 border-t border-border/45 relative">
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute bottom-0 right-0 w-[40%] h-[40%] bg-indigo-500/5 rounded-full blur-[120px]" />
            </div>

            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative">
                <div className="text-center max-w-3xl mx-auto mb-20">
                    <h2 className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
                        Onboard in three simple steps
                    </h2>
                    <p className="mt-4 text-base text-muted-foreground">
                        Get deep endpoint observability up and running without changing your infrastructure.
                    </p>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-12 relative">
                    {/* Connecting line for timeline */}
                    <div className="hidden md:block absolute top-[44px] left-[15%] right-[15%] h-[1px] bg-gradient-to-r from-indigo-500/30 via-purple-500/30 to-emerald-500/30 z-0" />

                    {steps.map((s, idx) => {
                        const Icon = s.icon;
                        return (
                            <div key={idx} className="flex flex-col items-center text-center z-10 group">
                                {/* Indicator Circle */}
                                <div className={`relative flex items-center justify-center w-22 h-22 rounded-2xl bg-gradient-to-br ${s.color} border border-border/45 shadow-md transition-all duration-300 group-hover:scale-105 mb-6`}>
                                    <span className="absolute top-2 left-3 text-[10px] font-bold font-mono tracking-widest opacity-40">{s.num}</span>
                                    <Icon className="w-8 h-8 mt-2" />
                                </div>

                                <h3 className="text-base font-semibold text-foreground mb-2 group-hover:text-indigo-600 dark:group-hover:text-indigo-400 transition-colors">
                                    {s.title}
                                </h3>
                                <p className="text-xs text-muted-foreground leading-relaxed max-w-xs">
                                    {s.desc}
                                </p>
                            </div>
                        );
                    })}
                </div>
            </div>
        </section>
    );
}
