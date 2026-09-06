import React, { useState } from 'react';
import { useScrollAnimation } from '../hooks/useScrollAnimation';
import { Card, CardContent } from './ui/card';
import { Button } from './ui/button';
import { Badge } from './ui/badge';
import { Dialog, DialogContent, DialogTrigger } from './ui/dialog';
import { Heart, Eye, X, ChevronLeft, ChevronRight } from 'lucide-react';

const portfolioItems = [
  {
    id: 1,
    title: 'BMW M4 Competition',
    category: 'Full Wrap',
    finish: 'Satin',
    color: 'Midnight Blue',
    image: 'https://images.unsplash.com/photo-1699078042053-ecd9166d3f26?w=800&q=80',
    likes: 234,
    views: 1250,
  },
  {
    id: 2,
    title: 'Porsche 911 GT3',
    category: 'PPF + Accente',
    finish: 'Gloss',
    color: 'Racing Green',
    image: 'https://images.unsplash.com/photo-1618390663742-79576b70fbaa?w=800&q=80',
    likes: 189,
    views: 980,
  },
  {
    id: 3,
    title: 'Mercedes AMG GT',
    category: 'Full Wrap',
    finish: 'Matte',
    color: 'Nardo Grey',
    image: 'https://images.unsplash.com/photo-1604705528621-81b2755a320b?w=800&q=80',
    likes: 312,
    views: 1560,
  },
  {
    id: 4,
    title: 'Lamborghini Huracan',
    category: 'Custom Design',
    finish: 'Chrome Delete',
    color: 'Neon Pink',
    image: 'https://images.unsplash.com/photo-1630769660701-3454835913dc?w=800&q=80',
    likes: 456,
    views: 2100,
  },
  {
    id: 5,
    title: 'Audi RS6 Avant',
    category: 'Full Wrap',
    finish: 'Satin',
    color: 'Daytona Grey',
    image: 'https://images.unsplash.com/photo-1674666735108-e3adc95ead30?w=800&q=80',
    likes: 278,
    views: 1340,
  },
  {
    id: 6,
    title: 'Tesla Model S',
    category: 'PPF Full',
    finish: 'Gloss',
    color: 'Pearl White',
    image: 'https://images.unsplash.com/photo-1556448851-9359658faa54?w=800&q=80',
    likes: 198,
    views: 890,
  },
];

const filters = ['Toate', 'Full Wrap', 'PPF', 'Partial', 'Custom Design'];

export function PortfolioSection() {
  const { ref, isVisible } = useScrollAnimation(0.05);
  const [activeFilter, setActiveFilter] = useState('Toate');
  const [selectedImage, setSelectedImage] = useState(null);
  const [likes, setLikes] = useState({});

  const filteredItems = activeFilter === 'Toate' 
    ? portfolioItems 
    : portfolioItems.filter(item => {
        if (activeFilter === 'PPF') {
          return item.category.toLowerCase().includes('ppf');
        }
        return item.category.toLowerCase().includes(activeFilter.toLowerCase().replace(' ', ''));
      });

  const handleLike = (id, e) => {
    e.stopPropagation();
    setLikes(prev => ({
      ...prev,
      [id]: !prev[id]
    }));
  };

  const navigateImage = (direction) => {
    const currentIndex = portfolioItems.findIndex(item => item.id === selectedImage.id);
    let newIndex;
    if (direction === 'next') {
      newIndex = (currentIndex + 1) % portfolioItems.length;
    } else {
      newIndex = (currentIndex - 1 + portfolioItems.length) % portfolioItems.length;
    }
    setSelectedImage(portfolioItems[newIndex]);
  };

  return (
    <section
      id="portofoliu"
      ref={ref}
      className="py-20 px-4 lg:px-8 max-w-7xl mx-auto"
    >
      <div
        className="text-center mb-12 transition-all duration-700 ease-out"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(30px)',
        }}
      >
        <h2 className="font-heading font-bold text-3xl md:text-4xl lg:text-5xl mb-4 gradient-text">
          Portofoliu
        </h2>
        <p className="text-muted-foreground text-lg max-w-2xl mx-auto">
          Exploreaza proiectele noastre recente si descopera cum transformam masinile in opere de arta
        </p>
      </div>

      {/* Filter buttons */}
      <div
        className="flex flex-wrap justify-center gap-3 mb-10 transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
          transitionDelay: '0.2s',
        }}
      >
        {filters.map((filter) => (
          <Button
            key={filter}
            variant={activeFilter === filter ? 'default' : 'outline'}
            size="sm"
            onClick={() => setActiveFilter(filter)}
            className={`font-medium transition-all duration-300 ${
              activeFilter === filter 
                ? 'btn-neon text-primary-foreground' 
                : 'border-primary/30 text-muted-foreground hover:text-primary hover:border-primary/50'
            }`}
          >
            {filter}
          </Button>
        ))}
      </div>

      {/* Portfolio grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {filteredItems.map((item, i) => (
          <Dialog key={item.id}>
            <DialogTrigger asChild>
              <Card
                className="group cursor-pointer overflow-hidden border-border hover:border-primary/40 transition-all duration-500 card-hover bg-card/50"
                style={{
                  opacity: isVisible ? 1 : 0,
                  transform: isVisible ? 'translateY(0) scale(1)' : 'translateY(30px) scale(0.95)',
                  transition: `all 0.5s ease-out ${0.3 + i * 0.1}s`,
                }}
                onClick={() => setSelectedImage(item)}
              >
                <CardContent className="p-0">
                  <div className="relative aspect-[4/3] overflow-hidden">
                    <img
                      src={item.image}
                      alt={item.title}
                      className="w-full h-full object-cover transition-transform duration-700 group-hover:scale-110"
                    />
                    {/* Overlay on hover */}
                    <div className="absolute inset-0 bg-gradient-to-t from-background via-background/50 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-500" />
                    
                    {/* Category badge */}
                    <Badge 
                      className="absolute top-4 left-4 bg-primary/90 text-primary-foreground"
                    >
                      {item.category}
                    </Badge>

                    {/* Stats */}
                    <div className="absolute bottom-4 left-4 right-4 flex items-center justify-between opacity-0 group-hover:opacity-100 transition-all duration-500 transform translate-y-4 group-hover:translate-y-0">
                      <div className="flex items-center gap-4">
                        <button 
                          className="flex items-center gap-1 text-foreground/80 hover:text-primary transition-colors"
                          onClick={(e) => handleLike(item.id, e)}
                        >
                          <Heart 
                            className={`w-5 h-5 ${likes[item.id] ? 'fill-primary text-primary' : ''}`} 
                          />
                          <span className="text-sm">{item.likes + (likes[item.id] ? 1 : 0)}</span>
                        </button>
                        <div className="flex items-center gap-1 text-foreground/80">
                          <Eye className="w-5 h-5" />
                          <span className="text-sm">{item.views}</span>
                        </div>
                      </div>
                    </div>
                  </div>
                  
                  <div className="p-4">
                    <h3 className="font-heading font-semibold text-foreground mb-2 group-hover:text-primary transition-colors">
                      {item.title}
                    </h3>
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                      <span>{item.finish}</span>
                      <span>•</span>
                      <span>{item.color}</span>
                    </div>
                  </div>
                </CardContent>
              </Card>
            </DialogTrigger>
            
            <DialogContent className="max-w-4xl bg-background/95 backdrop-blur-xl border-border p-0">
              <div className="relative">
                <img
                  src={selectedImage?.image || item.image}
                  alt={selectedImage?.title || item.title}
                  className="w-full h-auto max-h-[70vh] object-contain"
                />
                
                {/* Navigation arrows */}
                <button
                  onClick={() => navigateImage('prev')}
                  className="absolute left-4 top-1/2 -translate-y-1/2 p-2 rounded-full bg-background/80 text-foreground hover:bg-primary hover:text-primary-foreground transition-all"
                >
                  <ChevronLeft className="w-6 h-6" />
                </button>
                <button
                  onClick={() => navigateImage('next')}
                  className="absolute right-4 top-1/2 -translate-y-1/2 p-2 rounded-full bg-background/80 text-foreground hover:bg-primary hover:text-primary-foreground transition-all"
                >
                  <ChevronRight className="w-6 h-6" />
                </button>
              </div>
              
              <div className="p-6">
                <h3 className="font-heading font-bold text-2xl text-foreground mb-2">
                  {selectedImage?.title || item.title}
                </h3>
                <div className="flex flex-wrap items-center gap-4 text-muted-foreground">
                  <Badge variant="outline" className="border-primary/30">
                    {selectedImage?.category || item.category}
                  </Badge>
                  <span>{selectedImage?.finish || item.finish}</span>
                  <span>•</span>
                  <span>{selectedImage?.color || item.color}</span>
                </div>
              </div>
            </DialogContent>
          </Dialog>
        ))}
      </div>

      {/* View more button */}
      <div
        className="text-center mt-10 transition-all duration-700"
        style={{
          opacity: isVisible ? 1 : 0,
          transform: isVisible ? 'translateY(0)' : 'translateY(20px)',
          transitionDelay: '0.8s',
        }}
      >
        <Button 
          variant="outline" 
          size="lg"
          className="border-primary/30 text-primary hover:bg-primary/10 hover:border-primary/50"
        >
          Vezi toate proiectele
        </Button>
      </div>
    </section>
  );
}

export default PortfolioSection;
