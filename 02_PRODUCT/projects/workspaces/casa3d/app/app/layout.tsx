import './globals.css';
import { PrefsProvider } from '@/lib/prefs';
import DialogHost from '@/components/DialogHost';
export const metadata = { title: 'Casa mea 3D', description: 'Floor plan, real furniture and budget in one project. / Plan, mobilier real și buget, într-un singur proiect.' };
export default function RootLayout({ children }: { children: React.ReactNode }){
  return (<html lang="en"><head>
    <link rel="preconnect" href="https://fonts.googleapis.com" /><link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="" />
    <link href="https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;500;600&family=Newsreader:opsz,wght@6..72,400;6..72,500&display=swap" rel="stylesheet" />
  </head><body><PrefsProvider>{children}<DialogHost /></PrefsProvider></body></html>);
}
