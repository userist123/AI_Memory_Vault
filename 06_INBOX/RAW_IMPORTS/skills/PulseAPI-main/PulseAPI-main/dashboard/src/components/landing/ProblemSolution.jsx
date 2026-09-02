import { AlertCircle, CheckCircle2, ShieldAlert, Sparkles, Clock, Compass } from 'lucide-react';

export default function ProblemSolution() {
    const scenarios = [
        {
            problemTitle: "Silent Outages",
            problemDesc: "I didn't know my authentication server went down until customers started complaining on social media.",
            solutionTitle: "Continuous Active Pings",
            solutionDesc: "We ping your endpoints automatically every 60 seconds from multiple geographic regions. If they fail, we detect it instantly.",
            icon: Clock,
            color: "text-amber-500 bg-amber-500/10 border-amber-500/20"
        },
        {
            problemTitle: "Lagging Notifications",
            problemDesc: "Third-party services were throwing 500 errors, but our alert notifications took over 20 minutes to trigger.",
            solutionTitle: "Sub-Second Alert Webhooks",
            solutionDesc: "Direct integration with Slack, Discord, PagerDuty, and custom webhooks ensures alerts land in your channels in real time.",
            icon: ShieldAlert,
            color: "text-red-500 bg-red-500/10 border-red-500/20"
        },
        {
            problemTitle: "Painful Root-Cause Debugging",
            problemDesc: "Users got server errors on product checkouts, but finding which backend database query stalled took hours of sorting raw log files.",
            solutionTitle: "Granular Endpoint Metrics",
            solutionDesc: "Drill down into response codes, header payloads, and precise network latencies to isolate issues instantly.",
            icon: Compass,
            color: "text-indigo-400 bg-indigo-500/10 border-indigo-500/20"
        }
    ];

    return (
        <section id="problem-solution" className="py-24 bg-muted/20 border-y border-border/45 relative">
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute top-[50%] left-[50%] -translate-x-1/2 -translate-y-1/2 w-[70%] h-[40%] bg-indigo-500/5 rounded-full blur-[140px]" />
            </div>

            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative">
                <div className="text-center max-w-3xl mx-auto mb-16">
                    <h2 className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
                        Designed for modern backend teams
                    </h2>
                    <p className="mt-4 text-base text-muted-foreground">
                        Traditional monitoring tools are bloated and built for network admins. PulseAPI is built directly for the developers who write and deploy the code.
                    </p>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
                    {scenarios.map((s, idx) => {
                        const IconComponent = s.icon;
                        return (
                            <div 
                                key={idx} 
                                className="flex flex-col rounded-2xl bg-card border border-border/50 p-6 shadow-sm hover:border-border hover:bg-card/75 transition-all group"
                            >
                                <div className={`inline-flex items-center justify-center w-10 h-10 rounded-xl mb-6 border ${s.color}`}>
                                    <IconComponent className="w-5 h-5" />
                                </div>

                                <div className="space-y-4 flex-1 flex flex-col justify-between">
                                    {/* The Problem */}
                                    <div className="space-y-2 border-l-2 border-red-500/30 pl-4">
                                        <div className="flex items-center gap-1.5">
                                            <AlertCircle className="w-3.5 h-3.5 text-red-500/85" />
                                            <h4 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground/60">The Friction</h4>
                                        </div>
                                        <p className="text-xs italic text-muted-foreground leading-relaxed">
                                            "{s.problemDesc}"
                                        </p>
                                    </div>

                                    {/* The Solution */}
                                    <div className="space-y-2 border-l-2 border-emerald-500/30 pl-4 pt-2">
                                        <div className="flex items-center gap-1.5">
                                            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500" />
                                            <h4 className="text-xs font-semibold uppercase tracking-wider text-emerald-500">The Solution</h4>
                                        </div>
                                        <h3 className="text-sm font-semibold text-foreground">
                                            {s.solutionTitle}
                                        </h3>
                                        <p className="text-xs text-muted-foreground leading-relaxed">
                                            {s.solutionDesc}
                                        </p>
                                    </div>
                                </div>
                            </div>
                        );
                    })}
                </div>
            </div>
        </section>
    );
}
