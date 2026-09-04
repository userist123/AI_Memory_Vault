import { 
    Activity, 
    Bell, 
    LineChart, 
    Calendar, 
    BarChart3, 
    Code2, 
    Webhook, 
    Zap 
} from 'lucide-react';

export default function Features() {
    const list = [
        {
            title: "Real-Time Health Checks",
            desc: "Continuous, localized ping cycles checking HTTP response status codes and uptime at custom intervals.",
            icon: Activity
        },
        {
            title: "Instant Incident Detection",
            desc: "Immediate trigger logic evaluates performance drops or downtime and creates isolated, actionable incident entries.",
            icon: Bell
        },
        {
            title: "Latency Tracking",
            desc: "Detailed latency monitoring showing TTFB, network overhead, and response time histograms over time.",
            icon: LineChart
        },
        {
            title: "Historical Performance",
            desc: "Access historical uptime logs to verify SLAs and identify micro-downtime periods or regional trends.",
            icon: Calendar
        },
        {
            title: "Response Code Analytics",
            desc: "Grouped distributions of 2xx, 3xx, 4xx, and 5xx status codes to quickly separate client bugs from server errors.",
            icon: BarChart3
        },
        {
            title: "Developer-First SDKs",
            desc: "Instrument your application in Node.js, Express, Go, or Python with simple, low-overhead middlewares.",
            icon: Code2
        },
        {
            title: "Webhook Integrations",
            desc: "Deliver payload events directly to Slack channels, Discord threads, or custom web endpoints.",
            icon: Webhook
        },
        {
            title: "Fast Setup",
            desc: "Zero-configuration setup means you see live data streaming on your dashboard in under five minutes.",
            icon: Zap
        }
    ];

    return (
        <section id="features" className="py-24 bg-background relative">
            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative">
                <div className="text-center max-w-3xl mx-auto mb-16">
                    <h2 className="text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
                        Everything you need, nothing you don't
                    </h2>
                    <p className="mt-4 text-base text-muted-foreground">
                        Stop configuring complex Prometheus agents or paying bloated enterprise costs. Get focused API observability built for developer speed.
                    </p>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
                    {list.map((f, idx) => {
                        const Icon = f.icon;
                        return (
                            <div 
                                key={idx} 
                                className="group flex flex-col p-6 rounded-2xl bg-card hover:bg-card/75 border border-border/40 hover:border-border transition-all duration-300"
                            >
                                <div className="inline-flex items-center justify-center w-10 h-10 rounded-xl bg-muted/40 border border-border/45 text-indigo-600 dark:text-indigo-400 group-hover:text-indigo-700 dark:group-hover:text-indigo-300 group-hover:bg-indigo-50 dark:group-hover:bg-muted/70 shadow-sm transition-all mb-5">
                                    <Icon className="w-5 h-5" />
                                </div>
                                <h3 className="text-sm font-semibold text-foreground mb-2 group-hover:text-indigo-600 dark:group-hover:text-indigo-400 transition-colors">
                                    {f.title}
                                </h3>
                                <p className="text-xs text-muted-foreground leading-relaxed flex-1">
                                    {f.desc}
                                </p>
                            </div>
                        );
                    })}
                </div>
            </div>
        </section>
    );
}
