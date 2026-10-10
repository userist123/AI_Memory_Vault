import './globals.css';
import { PrefsProvider } from '@/lib/prefs';
export const metadata = { title: 'Casa mea 3D', description: 'Floor plan, real furniture and budget in one project. / Plan, mobilier real și buget, într-un singur proiect.' };
export default function RootLayout({ children }: { children: React.ReactNode }){
  return (<html lang="en"><head>
    <link rel="preconnect" href="https://fonts.googleapis.com" /><link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="" />
    <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap" rel="stylesheet" />
  </head><body><PrefsProvider>{children}</PrefsProvider></body></html>);
}
