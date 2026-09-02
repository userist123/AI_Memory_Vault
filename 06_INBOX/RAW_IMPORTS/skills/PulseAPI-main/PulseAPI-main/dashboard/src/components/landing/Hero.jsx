import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { Play, ArrowRight, CheckCircle2, AlertTriangle, XCircle, Terminal, Copy, Check } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

export default function Hero() {
    const [copied, setCopied] = useState(false);
    const [pingTick, setPingTick] = useState(0);
    const [endpoints, setEndpoints] = useState([
        { path: 'GET /api/v1/auth/session', status: 200, latency: 42, history: [40, 45, 42, 38, 45, 41, 42] },
        { path: 'POST /api/v1/payments/charge', status: 200, latency: 184, history: [195, 172, 210, 180, 192, 188, 184] },
        { path: 'GET /api/v1/products', status: 200, latency: 95, history: [90, 88, 120, 95, 93, 105, 95] },
        { path: 'PUT /api/v1/users/profile', status: 200, latency: 61, history: [58, 62, 70, 60, 65, 63, 61] }
    ]);
    const [incidents, setIncidents] = useState([
        { time: '10 mins ago', endpoint: 'POST /api/v1/payments/charge', message: 'Latency spiked to 1,200ms', type: 'warning' },
        { time: '2 hours ago', endpoint: 'GET /api/v1/products', message: '502 Bad Gateway returned from server', type: 'critical' }
    ]);

    const codeSnippet = `const express = require('express');
const monitoring = require('./monitoring');

const app = express();

// Apply monitoring middleware early
app.use(monitoring({
    serviceName: 'blog-api',
    apiKey: process.env.MONITORING_API_KEY
}));`;

    const copyCode = () => {
        navigator.clipboard.writeText(codeSnippet);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
    };

    // Simulate real-time health checks pinging and updating latency
    useEffect(() => {
        const interval = setInterval(() => {
            setPingTick(prev => (prev + 1) % 15);
            setEndpoints(prevEndpoints => 
                prevEndpoints.map(ep => {
                    const noise = Math.floor(Math.random() * 9) - 4; // -4ms to +4ms fluctuation
                    const newLatency = Math.max(10, ep.latency + noise);
                    const newHistory = [...ep.history.slice(1), newLatency];
                    return {
                        ...ep,
                        latency: newLatency,
                        history: newHistory
                    };
                })
            );
        }, 3000);

        return () => clearInterval(interval);
    }, []);

    const scrollToDocs = () => {
        const docs = document.getElementById('docs-preview');
        if (docs) {
            docs.scrollIntoView({ behavior: 'smooth' });
        }
    };

    return (
        <section className="relative pt-32 pb-24 md:pt-40 md:pb-32 overflow-hidden bg-background">
            {/* Background elements matching the premium auth styling */}
            <div className="absolute inset-0 pointer-events-none">
                <div className="absolute top-[-10%] left-[-10%] w-[50%] h-[50%] bg-indigo-600/10 rounded-full blur-[140px]" />
                <div className="absolute top-[20%] right-[-10%] w-[40%] h-[40%] bg-purple-600/10 rounded-full blur-[140px]" />
            </div>

            <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative">
                <div className="text-center max-w-3xl mx-auto mb-16 md:mb-24">
                    {/* Tiny badge */}
                    <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-indigo-500/10 text-indigo-600 dark:text-indigo-400 border border-indigo-500/20 text-xs font-semibold mb-6">
                        <span className="w-1.5 h-1.5 rounded-full bg-indigo-500 dark:bg-indigo-400 animate-pulse" />
                        Continuous API Monitoring
                    </div>
                    
                    <h1 className="text-4xl sm:text-5xl md:text-6xl font-bold tracking-tight text-foreground leading-[1.1] mb-6">
                        Catch API failures <br />
                        <span className="text-transparent bg-clip-text bg-gradient-to-r from-indigo-600 via-violet-600 to-indigo-700 dark:from-indigo-400 dark:via-purple-400 dark:to-indigo-500">
                            before your customers do.
                        </span>
                    </h1>

                    <p className="text-base sm:text-lg md:text-xl text-muted-foreground font-normal max-w-2xl mx-auto mb-10 leading-relaxed">
                        Continuously monitor API uptime, response latency, and status codes. Drop in our SDK in seconds and get instant alerts when endpoints degrade.
                    </p>

                    <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
                        <Link
                            to="/register"
                            className="w-full sm:w-auto flex items-center justify-center gap-2 text-sm font-semibold text-white bg-indigo-600 hover:bg-indigo-700 px-6 py-3.5 rounded-xl transition-all shadow-md group cursor-pointer"
                        >
                            Start Monitoring
                            <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
                        </Link>
                        <button
                            onClick={scrollToDocs}
                            className="w-full sm:w-auto flex items-center justify-center gap-2 text-sm font-semibold text-foreground bg-muted/60 hover:bg-muted border border-border/50 hover:border-border px-6 py-3.5 rounded-xl transition-all cursor-pointer"
                        >
                            Read Documentation
                        </button>
                    </div>
                </div>

                {/* Dashboard Preview mockup */}
                <div className="max-w-5xl mx-auto relative">
                    {/* Shadow decoration under the card */}
                    <div className="absolute -inset-1.5 bg-gradient-to-r from-indigo-500 to-purple-500 rounded-2xl blur-lg opacity-25 group-hover:opacity-40 transition duration-1000" />

                    <div className="relative rounded-2xl bg-slate-900/60 border border-slate-800 backdrop-blur-xl overflow-hidden shadow-2xl">
                        {/* Mock window controls */}
                        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-800/80 bg-slate-900/80">
                            <div className="flex items-center gap-1.5">
                                <span className="w-3 h-3 rounded-full bg-red-500/80" />
                                <span className="w-3 h-3 rounded-full bg-yellow-500/80" />
                                <span className="w-3 h-3 rounded-full bg-green-500/80" />
                            </div>
                            <span className="text-[11px] font-mono text-slate-500">production-dashboard.pulseapi.com</span>
                            <div className="w-12" /> {/* spacing element */}
                        </div>

                        <div className="p-4 sm:p-6 lg:p-8 grid grid-cols-1 lg:grid-cols-3 gap-6">
                            {/* Column 1: Monitoring endpoints & Live pings */}
                            <div className="lg:col-span-2 space-y-6">
                                <div className="flex items-center justify-between">
                                    <h3 className="text-sm font-semibold text-slate-200">Active Endpoints</h3>
                                    <div className="flex items-center gap-2">
                                        <span className="relative flex h-2 w-2">
                                            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
                                            <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
                                        </span>
                                        <span className="text-xs font-mono text-slate-400">Live feed updates</span>
                                    </div>
                                </div>

                                <div className="space-y-3">
                                    {endpoints.map((ep, idx) => (
                                        <div 
                                            key={idx} 
                                            className="flex flex-col sm:flex-row sm:items-center justify-between p-3.5 rounded-xl bg-slate-950/40 border border-slate-800/60 hover:border-slate-700/80 transition-all gap-3"
                                        >
                                            <div className="flex items-center gap-3">
                                                <span className="px-1.5 py-0.5 rounded text-[10px] font-mono bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                                                    200 OK
                                                </span>
                                                <span className="text-xs font-mono font-medium text-slate-300">{ep.path}</span>
                                            </div>
                                            
                                            <div className="flex items-center justify-between sm:justify-end gap-6">
                                                {/* Mini Sparkline Chart */}
                                                <div className="flex items-end gap-[3px] h-6 px-2">
                                                    {ep.history.map((h, hIdx) => {
                                                        // Scale height to look nice
                                                        const maxVal = Math.max(...ep.history);
                                                        const minVal = Math.min(...ep.history);
                                                        const heightPercent = maxVal === minVal ? 50 : ((h - minVal) / (maxVal - minVal)) * 60 + 20;
                                                        return (
                                                            <div 
                                                                key={hIdx}
                                                                style={{ height: `${heightPercent}%` }}
                                                                className={`w-[4px] rounded-t-sm transition-all duration-300 ${
                                                                    hIdx === ep.history.length - 1 ? 'bg-indigo-400' : 'bg-slate-700/60'
                                                                }`}
                                                            />
                                                        );
                                                    })}
                                                </div>
                                                
                                                <div className="text-right">
                                                    <div className="text-xs font-semibold text-slate-100 font-mono">{ep.latency}ms</div>
                                                    <div className="text-[10px] text-slate-500">latency</div>
                                                </div>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>

                            {/* Column 2: Quick Metrics & Alert feed */}
                            <div className="space-y-6">
                                <h3 className="text-sm font-semibold text-slate-200">Global Overview</h3>
                                
                                <div className="grid grid-cols-2 gap-4">
                                    <div className="p-4 rounded-xl bg-slate-950/40 border border-slate-800/60">
                                        <div className="text-[10px] text-slate-500 font-medium uppercase tracking-wider mb-1">Uptime (24h)</div>
                                        <div className="text-lg font-mono font-bold text-emerald-400">99.98%</div>
                                    </div>
                                    <div className="p-4 rounded-xl bg-slate-950/40 border border-slate-800/60">
                                        <div className="text-[10px] text-slate-500 font-medium uppercase tracking-wider mb-1">Avg Latency</div>
                                        <div className="text-lg font-mono font-bold text-indigo-400">95.5ms</div>
                                    </div>
                                </div>

                                <div className="p-4 rounded-xl bg-slate-950/30 border border-slate-800/60 space-y-4">
                                    <h4 className="text-xs font-semibold text-slate-300">Recent Incident Feed</h4>
                                    <div className="space-y-3">
                                        {incidents.map((inc, iIdx) => (
                                            <div key={iIdx} className="flex gap-2.5 items-start text-xs border-b border-slate-800/40 pb-2.5 last:border-0 last:pb-0">
                                                {inc.type === 'warning' ? (
                                                    <AlertTriangle className="w-3.5 h-3.5 text-amber-500 flex-shrink-0 mt-0.5" />
                                                ) : (
                                                    <XCircle className="w-3.5 h-3.5 text-red-500 flex-shrink-0 mt-0.5" />
                                                )}
                                                <div className="space-y-0.5">
                                                    <div className="font-mono text-[11px] text-slate-300">{inc.endpoint}</div>
                                                    <div className="text-slate-400 text-[10px]">{inc.message}</div>
                                                    <div className="text-slate-600 text-[9px]">{inc.time}</div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            </div>
                        </div>

                        {/* Bottom: SDK setup block showing integration complexity is low */}
                        <div className="border-t border-slate-800/80 bg-slate-950/50 p-6 flex flex-col md:flex-row md:items-center justify-between gap-6">
                            <div className="space-y-1 max-w-sm">
                                <div className="flex items-center gap-2 text-indigo-400 font-semibold text-xs font-mono">
                                    <Terminal className="w-4 h-4" />
                                    INTEGRATION SDK
                                </div>
                                <h4 className="text-sm font-semibold text-slate-200">Plug & Play Middleware</h4>
                                <p className="text-xs text-slate-500">Add PulseAPI to your existing backend router with just three lines of configuration code.</p>
                            </div>

                            <div className="flex-1 max-w-md relative rounded-xl border border-slate-800 bg-slate-950 overflow-hidden shadow-inner">
                                <div className="flex items-center justify-between px-4 py-2 border-b border-slate-900 bg-slate-950/80">
                                    <span className="text-[10px] font-mono text-slate-500">server.js</span>
                                    <button 
                                        onClick={copyCode}
                                        className="p-1.5 rounded text-slate-500 hover:text-slate-300 hover:bg-slate-900 transition-colors"
                                    >
                                        {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                                    </button>
                                </div>
                                <pre className="p-4 text-[11px] font-mono text-indigo-300 overflow-x-auto select-all leading-relaxed">
                                    <code>{codeSnippet}</code>
                                </pre>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
}
