import React, { useState, useEffect, lazy, Suspense } from 'react';
import { Link } from 'react-router-dom';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '../components/ui/card';
import { Button } from '../components/ui/button';
import { Badge } from '../components/ui/badge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../components/ui/tabs';
import { ScrollArea } from '../components/ui/scroll-area';
import { Separator } from '../components/ui/separator';
import { Slider } from '../components/ui/slider';
import { Label } from '../components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '../components/ui/dialog';
import { toast } from 'sonner';
import { 
  ArrowLeft, 
  Car, 
  Palette, 
  Sparkles, 
  RotateCcw, 
  Camera, 
  Share2, 
  Heart,
  MapPin,
  Phone,
  Mail,
  Clock,
  Check,
  ChevronRight,
  Download,
  ZoomIn,
  ZoomOut,
  Move3D,
  Sun,
  Moon,
  Info
} from 'lucide-react';

// Lazy load the 3D viewer
const CarViewer3D = lazy(() => import('../components/CarViewer3D'));

// Car types with details
const carTypes = [
  { id: 'sedan', name: 'Sedan', description: 'BMW, Mercedes, Audi...', icon: '🚗' },
  { id: 'suv', name: 'SUV', description: 'X5, GLE, Q7...', icon: '🚙' },
  { id: 'sports', name: 'Sports', description: 'M4, AMG GT, RS...', icon: '🏎️' },
];

// Comprehensive vinyl wrap colors organized by category
const wrapColors = {
  'Clasice': [
    { name: 'Gloss Black', hex: '#0a0a0a', finish: 'gloss' },
    { name: 'Matte Black', hex: '#1a1a1a', finish: 'matte' },
    { name: 'Satin Black', hex: '#151515', finish: 'satin' },
    { name: 'Gloss White', hex: '#f5f5f5', finish: 'gloss' },
    { name: 'Matte White', hex: '#e8e8e8', finish: 'matte' },
    { name: 'Nardo Grey', hex: '#6e6e6e', finish: 'matte' },
    { name: 'Gunmetal Grey', hex: '#4a4a4a', finish: 'metallic' },
  ],
  'Albastru': [
    { name: 'Midnight Blue', hex: '#191970', finish: 'satin' },
    { name: 'Electric Blue', hex: '#0066ff', finish: 'gloss' },
    { name: 'Ocean Blue', hex: '#006994', finish: 'gloss' },
    { name: 'Matte Navy', hex: '#1a1a4a', finish: 'matte' },
    { name: 'Chrome Blue', hex: '#4169e1', finish: 'chrome' },
    { name: 'Ice Blue Metallic', hex: '#a5d8ff', finish: 'metallic' },
    { name: 'Laguna Seca Blue', hex: '#4aa8d8', finish: 'gloss' },
  ],
  'Rosu': [
    { name: 'Gloss Red', hex: '#cc0000', finish: 'gloss' },
    { name: 'Matte Red', hex: '#990000', finish: 'matte' },
    { name: 'Racing Red', hex: '#ff0000', finish: 'gloss' },
    { name: 'Burgundy', hex: '#722f37', finish: 'gloss' },
    { name: 'Chrome Red', hex: '#dc143c', finish: 'chrome' },
    { name: 'Candy Red', hex: '#cc0033', finish: 'metallic' },
    { name: 'Matte Crimson', hex: '#8b0000', finish: 'matte' },
  ],
  'Verde': [
    { name: 'Racing Green', hex: '#004225', finish: 'gloss' },
    { name: 'Matte Military', hex: '#4a5d23', finish: 'matte' },
    { name: 'Emerald', hex: '#50c878', finish: 'metallic' },
    { name: 'Lime Green', hex: '#32cd32', finish: 'gloss' },
    { name: 'Forest Green', hex: '#228b22', finish: 'satin' },
    { name: 'Teal', hex: '#008080', finish: 'gloss' },
    { name: 'Olive Matte', hex: '#556b2f', finish: 'matte' },
  ],
  'Galben & Portocaliu': [
    { name: 'Gloss Yellow', hex: '#ffd700', finish: 'gloss' },
    { name: 'Matte Yellow', hex: '#f0c000', finish: 'matte' },
    { name: 'Neon Yellow', hex: '#dfff00', finish: 'gloss' },
    { name: 'Sunset Orange', hex: '#ff6600', finish: 'gloss' },
    { name: 'Matte Orange', hex: '#cc5500', finish: 'matte' },
    { name: 'Chrome Gold', hex: '#ffd700', finish: 'chrome' },
    { name: 'Bronze Metallic', hex: '#cd7f32', finish: 'metallic' },
  ],
  'Mov & Roz': [
    { name: 'Gloss Purple', hex: '#800080', finish: 'gloss' },
    { name: 'Matte Purple', hex: '#4b0082', finish: 'matte' },
    { name: 'Hot Pink', hex: '#ff1493', finish: 'gloss' },
    { name: 'Matte Pink', hex: '#ff69b4', finish: 'matte' },
    { name: 'Chrome Purple', hex: '#9400d3', finish: 'chrome' },
    { name: 'Lavender', hex: '#e6e6fa', finish: 'satin' },
    { name: 'Magenta Metallic', hex: '#ff00ff', finish: 'metallic' },
  ],
  'Special & Chameleon': [
    { name: 'Chameleon Purple-Green', hex: '#8b008b', finish: 'metallic' },
    { name: 'Chameleon Blue-Purple', hex: '#663399', finish: 'metallic' },
    { name: 'Chameleon Gold-Green', hex: '#9acd32', finish: 'metallic' },
    { name: 'Chrome Silver', hex: '#c0c0c0', finish: 'chrome' },
    { name: 'Chrome Rose Gold', hex: '#b76e79', finish: 'chrome' },
    { name: 'Brushed Aluminum', hex: '#848482', finish: 'satin' },
    { name: 'Brushed Steel', hex: '#43464b', finish: 'satin' },
  ],
};

// Finish types
const finishTypes = [
  { id: 'gloss', name: 'Gloss', description: 'Luciu intens, aspect clasic', icon: '✨' },
  { id: 'matte', name: 'Matte', description: 'Fara reflexii, modern', icon: '🌑' },
  { id: 'satin', name: 'Satin', description: 'Semi-luciu elegant', icon: '🌟' },
  { id: 'metallic', name: 'Metallic', description: 'Particule metalice stralucitoare', icon: '💎' },
  { id: 'chrome', name: 'Chrome', description: 'Efect oglinda spectaculos', icon: '🪞' },
];

// Studio location (fictional coordinates for Bucharest)
const studioLocation = {
  lat: 44.4268,
  lng: 26.1025,
  address: 'Str. Industriei 123, Sector 2, Bucuresti',
  phone: '+40 722 123 456',
  email: 'contact@crisscustoms.ro',
  hours: 'Luni - Vineri: 09:00 - 18:00',
};

export default function ConfiguratorPage() {
  const [selectedCar, setSelectedCar] = useState('sedan');
  const [selectedColor, setSelectedColor] = useState('#ff1493');
  const [selectedFinish, setSelectedFinish] = useState('gloss');
  const [selectedColorName, setSelectedColorName] = useState('Hot Pink');
  const [favorites, setFavorites] = useState([]);
  const [showMapModal, setShowMapModal] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [viewAngle, setViewAngle] = useState(0);

  useEffect(() => {
    // Simulate loading
    const timer = setTimeout(() => setIsLoading(false), 1500);
    return () => clearTimeout(timer);
  }, []);

  const handleColorSelect = (color) => {
    setSelectedColor(color.hex);
    setSelectedFinish(color.finish);
    setSelectedColorName(color.name);
    toast.success(`Culoare aplicata: ${color.name}`);
  };

  const handleFinishChange = (finish) => {
    setSelectedFinish(finish);
    toast.success(`Finisaj schimbat: ${finish}`);
  };

  const handleAddToFavorites = () => {
    const config = {
      car: selectedCar,
      color: selectedColor,
      colorName: selectedColorName,
      finish: selectedFinish,
      timestamp: new Date().toISOString(),
    };
    setFavorites([...favorites, config]);
    toast.success('Configuratie salvata in favorite!');
  };

  const handleRequestQuote = () => {
    toast.success('Cerere trimisa! Te vom contacta in curand pentru oferta personalizata.');
  };

  const handleShareConfig = () => {
    const configUrl = `${window.location.origin}/configurator?car=${selectedCar}&color=${encodeURIComponent(selectedColor)}&finish=${selectedFinish}`;
    navigator.clipboard.writeText(configUrl);
    toast.success('Link copiat in clipboard!');
  };

  const handleDownloadImage = () => {
    toast.success('Imagine salvata! (Functionalitate demo)');
  };

  // Estimate price based on selections
  const getEstimatedPrice = () => {
    let base = selectedCar === 'suv' ? 3000 : selectedCar === 'sports' ? 3500 : 2500;
    if (selectedFinish === 'chrome') base += 1000;
    if (selectedFinish === 'metallic') base += 500;
    return base;
  };

  return (
    <div className="min-h-screen bg-background">
      {/* Header */}
      <header className="sticky top-0 z-50 glass-header">
        <div className="max-w-7xl mx-auto px-4 py-4 flex items-center justify-between">
          <div className="flex items-center gap-4">
            <Link to="/" className="flex items-center gap-2 text-muted-foreground hover:text-primary transition-colors">
              <ArrowLeft className="w-5 h-5" />
              <span className="hidden sm:inline">Inapoi la site</span>
            </Link>
            <Separator orientation="vertical" className="h-6" />
            <h1 className="font-heading font-bold text-xl gradient-text">Configurator 3D</h1>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="icon" onClick={handleShareConfig}>
              <Share2 className="w-5 h-5" />
            </Button>
            <Button variant="ghost" size="icon" onClick={handleAddToFavorites}>
              <Heart className="w-5 h-5" />
            </Button>
            <Button variant="ghost" size="icon" onClick={handleDownloadImage}>
              <Camera className="w-5 h-5" />
            </Button>
          </div>
        </div>
      </header>

      <div className="max-w-7xl mx-auto px-4 py-6">
        <div className="grid lg:grid-cols-3 gap-6">
          {/* 3D Viewer */}
          <div className="lg:col-span-2">
            <Card className="glass-card overflow-hidden">
              <div className="relative h-[400px] md:h-[500px] bg-gradient-to-b from-background to-card">
                {isLoading ? (
                  <div className="absolute inset-0 flex items-center justify-center">
                    <div className="text-center">
                      <div className="w-16 h-16 border-4 border-primary/30 border-t-primary rounded-full animate-spin mx-auto mb-4" />
                      <p className="text-muted-foreground">Se incarca vizualizatorul 3D...</p>
                    </div>
                  </div>
                ) : (
                  <Suspense fallback={
                    <div className="absolute inset-0 flex items-center justify-center">
                      <div className="w-16 h-16 border-4 border-primary/30 border-t-primary rounded-full animate-spin" />
                    </div>
                  }>
                    <CarViewer3D 
                      carType={selectedCar}
                      color={selectedColor}
                      finish={selectedFinish}
                    />
                  </Suspense>
                )}

                {/* Controls overlay */}
                <div className="absolute bottom-4 left-4 right-4 flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Badge className="glass px-3 py-1">
                      <Move3D className="w-4 h-4 mr-1" />
                      Trage pentru a roti
                    </Badge>
                  </div>
                  <div className="flex items-center gap-2">
                    <Button variant="ghost" size="icon" className="glass">
                      <ZoomOut className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="icon" className="glass">
                      <ZoomIn className="w-4 h-4" />
                    </Button>
                    <Button 
                      variant="ghost" 
                      size="icon" 
                      className="glass"
                      onClick={() => toast.success('Rotatie resetata')}
                    >
                      <RotateCcw className="w-4 h-4" />
                    </Button>
                  </div>
                </div>

                {/* Current selection badge */}
                <div className="absolute top-4 left-4">
                  <Badge className="glass-card-pink px-4 py-2">
                    <div 
                      className="w-4 h-4 rounded-full mr-2 border border-white/30"
                      style={{ backgroundColor: selectedColor }}
                    />
                    {selectedColorName} • {selectedFinish}
                  </Badge>
                </div>
              </div>
            </Card>

            {/* Car type selector */}
            <Card className="glass-card mt-4">
              <CardHeader className="pb-2">
                <CardTitle className="font-heading text-lg flex items-center gap-2">
                  <Car className="w-5 h-5 text-primary" />
                  Selecteaza tipul de masina
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid grid-cols-3 gap-3">
                  {carTypes.map((car) => (
                    <button
                      key={car.id}
                      onClick={() => {
                        setSelectedCar(car.id);
                        toast.success(`Tip masina: ${car.name}`);
                      }}
                      className={`p-4 rounded-xl border-2 transition-all ${
                        selectedCar === car.id
                          ? 'border-primary bg-primary/10 shadow-neon'
                          : 'border-border hover:border-primary/50 bg-card/50'
                      }`}
                    >
                      <span className="text-3xl mb-2 block">{car.icon}</span>
                      <p className="font-medium text-foreground">{car.name}</p>
                      <p className="text-xs text-muted-foreground">{car.description}</p>
                    </button>
                  ))}
                </div>
              </CardContent>
            </Card>
          </div>

          {/* Configuration panel */}
          <div className="space-y-4">
            {/* Color selection */}
            <Card className="glass-card">
              <CardHeader className="pb-2">
                <CardTitle className="font-heading text-lg flex items-center gap-2">
                  <Palette className="w-5 h-5 text-primary" />
                  Culoare & Folie
                </CardTitle>
                <CardDescription>Alege din peste 40 de culori premium</CardDescription>
              </CardHeader>
              <CardContent>
                <ScrollArea className="h-[300px] pr-4">
                  {Object.entries(wrapColors).map(([category, colors]) => (
                    <div key={category} className="mb-4">
                      <p className="text-sm font-medium text-muted-foreground mb-2">{category}</p>
                      <div className="grid grid-cols-4 gap-2">
                        {colors.map((color) => (
                          <button
                            key={color.name}
                            onClick={() => handleColorSelect(color)}
                            className={`group relative w-full aspect-square rounded-lg transition-all hover:scale-110 ${
                              selectedColor === color.hex && selectedFinish === color.finish
                                ? 'ring-2 ring-primary ring-offset-2 ring-offset-background'
                                : ''
                            }`}
                            style={{ backgroundColor: color.hex }}
                            title={`${color.name} (${color.finish})`}
                          >
                            {selectedColor === color.hex && selectedFinish === color.finish && (
                              <Check className="absolute inset-0 m-auto w-4 h-4 text-white drop-shadow-lg" />
                            )}
                            <span className="absolute -bottom-6 left-1/2 -translate-x-1/2 text-[10px] text-muted-foreground whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity">
                              {color.name}
                            </span>
                          </button>
                        ))}
                      </div>
                    </div>
                  ))}
                </ScrollArea>
              </CardContent>
            </Card>

            {/* Finish type */}
            <Card className="glass-card">
              <CardHeader className="pb-2">
                <CardTitle className="font-heading text-lg flex items-center gap-2">
                  <Sparkles className="w-5 h-5 text-primary" />
                  Finisaj
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="grid grid-cols-5 gap-2">
                  {finishTypes.map((finish) => (
                    <button
                      key={finish.id}
                      onClick={() => handleFinishChange(finish.id)}
                      className={`p-2 rounded-lg text-center transition-all ${
                        selectedFinish === finish.id
                          ? 'bg-primary/20 border-2 border-primary'
                          : 'bg-card border-2 border-border hover:border-primary/50'
                      }`}
                      title={finish.description}
                    >
                      <span className="text-lg block">{finish.icon}</span>
                      <span className="text-xs">{finish.name}</span>
                    </button>
                  ))}
                </div>
              </CardContent>
            </Card>

            {/* Price estimate */}
            <Card className="glass-card-pink">
              <CardContent className="p-4">
                <div className="flex items-center justify-between mb-3">
                  <span className="text-muted-foreground">Pret estimat</span>
                  <span className="text-2xl font-heading font-bold text-gold">
                    de la {getEstimatedPrice().toLocaleString()} €
                  </span>
                </div>
                <p className="text-xs text-muted-foreground mb-4">
                  * Pretul final depinde de dimensiunile exacte si complexitatea proiectului
                </p>
                <Button 
                  className="w-full btn-neon font-semibold"
                  onClick={handleRequestQuote}
                >
                  Solicita oferta personalizata
                </Button>
              </CardContent>
            </Card>

            {/* Location card */}
            <Card className="glass-card">
              <CardHeader className="pb-2">
                <CardTitle className="font-heading text-lg flex items-center gap-2">
                  <MapPin className="w-5 h-5 text-primary" />
                  Locatie Studio
                </CardTitle>
              </CardHeader>
              <CardContent>
                {/* Map preview */}
                <div 
                  className="h-32 rounded-lg mb-3 overflow-hidden relative cursor-pointer"
                  onClick={() => setShowMapModal(true)}
                >
                  <iframe
                    src={`https://www.google.com/maps/embed?pb=!1m18!1m12!1m3!1d2848.8!2d${studioLocation.lng}!3d${studioLocation.lat}!2m3!1f0!2f0!3f0!3m2!1i1024!2i768!4f13.1!3m3!1m2!1s0x0%3A0x0!2zNDTCsDI1JzM2LjUiTiAyNsKwMDYnMDkuMCJF!5e0!3m2!1sen!2sro!4v1234567890`}
                    width="100%"
                    height="100%"
                    style={{ border: 0 }}
                    allowFullScreen=""
                    loading="lazy"
                    referrerPolicy="no-referrer-when-downgrade"
                    className="grayscale hover:grayscale-0 transition-all"
                  />
                  <div className="absolute inset-0 flex items-center justify-center bg-black/30 opacity-0 hover:opacity-100 transition-opacity">
                    <Button variant="secondary" size="sm">
                      <MapPin className="w-4 h-4 mr-2" />
                      Vezi pe harta
                    </Button>
                  </div>
                </div>

                <div className="space-y-2 text-sm">
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <MapPin className="w-4 h-4 text-primary" />
                    <span>{studioLocation.address}</span>
                  </div>
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Phone className="w-4 h-4 text-primary" />
                    <a href={`tel:${studioLocation.phone}`} className="hover:text-primary transition-colors">
                      {studioLocation.phone}
                    </a>
                  </div>
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Clock className="w-4 h-4 text-primary" />
                    <span>{studioLocation.hours}</span>
                  </div>
                </div>

                <Button 
                  variant="outline" 
                  className="w-full mt-3 border-primary/30"
                  onClick={() => window.open(`https://www.google.com/maps?q=${studioLocation.lat},${studioLocation.lng}`, '_blank')}
                >
                  <MapPin className="w-4 h-4 mr-2" />
                  Obtine directii
                </Button>
              </CardContent>
            </Card>

            {/* Info card */}
            <Card className="glass-card">
              <CardContent className="p-4">
                <div className="flex items-start gap-3">
                  <Info className="w-5 h-5 text-primary mt-0.5" />
                  <div className="text-sm text-muted-foreground">
                    <p className="mb-2">
                      <strong className="text-foreground">Cum functioneaza:</strong>
                    </p>
                    <ol className="list-decimal list-inside space-y-1">
                      <li>Selecteaza tipul masinii tale</li>
                      <li>Alege culoarea si finisajul dorit</li>
                      <li>Roteste modelul 3D pentru a vizualiza</li>
                      <li>Solicita oferta personalizata</li>
                    </ol>
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>
        </div>
      </div>

      {/* Map Modal */}
      <Dialog open={showMapModal} onOpenChange={setShowMapModal}>
        <DialogContent className="glass-card border-border max-w-3xl">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <MapPin className="w-5 h-5 text-primary" />
              CrissCustoms Studio
            </DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div className="h-[400px] rounded-lg overflow-hidden">
              <iframe
                src={`https://www.google.com/maps/embed?pb=!1m18!1m12!1m3!1d2848.8!2d${studioLocation.lng}!3d${studioLocation.lat}!2m3!1f0!2f0!3f0!3m2!1i1024!2i768!4f13.1!3m3!1m2!1s0x0%3A0x0!2zNDTCsDI1JzM2LjUiTiAyNsKwMDYnMDkuMCJF!5e0!3m2!1sen!2sro!4v1234567890`}
                width="100%"
                height="100%"
                style={{ border: 0 }}
                allowFullScreen=""
                loading="lazy"
                referrerPolicy="no-referrer-when-downgrade"
              />
            </div>
            <div className="grid md:grid-cols-2 gap-4">
              <div className="space-y-2">
                <h3 className="font-medium">Adresa</h3>
                <p className="text-sm text-muted-foreground">{studioLocation.address}</p>
              </div>
              <div className="space-y-2">
                <h3 className="font-medium">Contact</h3>
                <p className="text-sm text-muted-foreground">{studioLocation.phone}</p>
                <p className="text-sm text-muted-foreground">{studioLocation.email}</p>
              </div>
            </div>
            <Button 
              className="w-full btn-neon"
              onClick={() => window.open(`https://www.google.com/maps?q=${studioLocation.lat},${studioLocation.lng}`, '_blank')}
            >
              Deschide in Google Maps
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
