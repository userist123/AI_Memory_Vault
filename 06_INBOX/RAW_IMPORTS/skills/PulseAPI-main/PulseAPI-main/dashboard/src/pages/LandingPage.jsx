import { useEffect } from 'react';
import { motion } from 'framer-motion';
import Navbar from '../components/landing/Navbar';
import Hero from '../components/landing/Hero';
import ProblemSolution from '../components/landing/ProblemSolution';
import Features from '../components/landing/Features';
import HowItWorks from '../components/landing/HowItWorks';
import DocsPreview from '../components/landing/DocsPreview';
import About from '../components/landing/About';
import Footer from '../components/landing/Footer';

export function LandingPage() {
    // Set landing page specific browser title on load
    useEffect(() => {
        const originalTitle = document.title;
        document.title = "PulseAPI — Developer-first API Monitoring & Observability";
        return () => {
            document.title = originalTitle;
        };
    }, []);

    const fadeInUpVariants = {
        hidden: { opacity: 0, y: 24 },
        visible: { 
            opacity: 1, 
            y: 0,
            transition: { duration: 0.6, ease: [0.16, 1, 0.3, 1] } 
        }
    };

    return (
        <div className="min-h-screen bg-background text-foreground selection:bg-indigo-500/30 selection:text-indigo-200">
            {/* Header navbar */}
            <Navbar />

            {/* Hero section */}
            <motion.div
                initial="hidden"
                animate="visible"
                variants={fadeInUpVariants}
            >
                <Hero />
            </motion.div>

            {/* Problem & Solution block */}
            <motion.div
                initial="hidden"
                whileInView="visible"
                viewport={{ once: true, margin: "-120px" }}
                variants={fadeInUpVariants}
            >
                <ProblemSolution />
            </motion.div>

            {/* Core Features cards grid */}
            <motion.div
                initial="hidden"
                whileInView="visible"
                viewport={{ once: true, margin: "-120px" }}
                variants={fadeInUpVariants}
            >
                <Features />
            </motion.div>

            {/* Onboarding process timeline */}
            <motion.div
                initial="hidden"
                whileInView="visible"
                viewport={{ once: true, margin: "-120px" }}
                variants={fadeInUpVariants}
            >
                <HowItWorks />
            </motion.div>

            {/* Developer interactive doc preview */}
            <motion.div
                initial="hidden"
                whileInView="visible"
                viewport={{ once: true, margin: "-120px" }}
                variants={fadeInUpVariants}
            >
                <DocsPreview />
            </motion.div>

            {/* Philosophy description */}
            <motion.div
                initial="hidden"
                whileInView="visible"
                viewport={{ once: true, margin: "-120px" }}
                variants={fadeInUpVariants}
            >
                <About />
            </motion.div>

            {/* CTA and links footer */}
            <Footer />
        </div>
    );
}

export default LandingPage;
