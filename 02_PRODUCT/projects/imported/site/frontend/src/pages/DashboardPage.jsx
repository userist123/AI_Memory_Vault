import React, { useState, useRef } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '../components/ui/card';
import { Button } from '../components/ui/button';
import { Badge } from '../components/ui/badge';
import { Progress } from '../components/ui/progress';
import { Avatar, AvatarFallback } from '../components/ui/avatar';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../components/ui/tabs';
import { Input } from '../components/ui/input';
import { Textarea } from '../components/ui/textarea';
import { Label } from '../components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger, DialogFooter, DialogDescription } from '../components/ui/dialog';
import { ScrollArea } from '../components/ui/scroll-area';
import { Separator } from '../components/ui/separator';
import { toast } from 'sonner';
import { 
  LayoutDashboard, 
  Car, 
  FileText, 
  MessageSquare, 
  Star,
  LogOut,
  Home,
  Clock,
  CheckCircle,
  Euro,
  Upload,
  Camera,
  Send,
  Download,
  Eye,
  Bell,
  Settings,
  User,
  Calendar,
  Phone,
  Mail,
  MapPin,
  Plus,
  X,
  Image as ImageIcon,
  Paperclip,
  ChevronRight,
  AlertCircle,
  Sparkles
} from 'lucide-react';

// Mock data for user projects
const mockProjects = [
  {
    id: 1,
    vehicle: 'BMW M4 Competition 2023',
    service: 'Full Wrap',
    color: 'Satin Midnight Blue',
    status: 'in_progress',
    progress: 65,
    stage: 'Colantare',
    stageIndex: 4,
    startDate: '2024-01-15',
    estimatedEnd: '2024-01-22',
    totalCost: 3200,
    paidAmount: 1600,
    images: {
      before: ['https://images.unsplash.com/photo-1699078042053-ecd9166d3f26?w=400&q=80'],
      during: ['https://images.unsplash.com/photo-1632823469850-2f77dd9c7f93?w=400&q=80'],
      after: [],
    },
    messages: [
      { id: 1, from: 'team', name: 'Echipa CrissCustoms', text: 'Am inceput procesul de colantare. Va trimitem update-uri!', date: '2024-01-18 10:30', read: true },
      { id: 2, from: 'user', name: 'Tu', text: 'Multumesc! Astept cu nerabdare rezultatul.', date: '2024-01-18 11:00', read: true },
      { id: 3, from: 'team', name: 'Echipa CrissCustoms', text: 'Am terminat 50% din colantare. Masina arata superb!', date: '2024-01-19 14:00', read: false },
    ],
    assignedTo: 'Cristian D.',
  },
  {
    id: 2,
    vehicle: 'Mercedes AMG GT 2022',
    service: 'PPF Full',
    color: 'Transparent',
    status: 'completed',
    progress: 100,
    stage: 'Livrat',
    stageIndex: 7,
    startDate: '2024-01-01',
    estimatedEnd: '2024-01-08',
    totalCost: 2800,
    paidAmount: 2800,
    images: {
      before: ['https://images.unsplash.com/photo-1604705528621-81b2755a320b?w=400&q=80'],
      during: ['https://images.unsplash.com/photo-1618390663742-79576b70fbaa?w=400&q=80'],
      after: ['https://images.unsplash.com/photo-1604705528621-81b2755a320b?w=400&q=80'],
    },
    messages: [],
    assignedTo: 'Mihai S.',
    canReview: true,
  }
];

const mockInvoices = [
  { id: 'INV-001', project: 'BMW M4 - Full Wrap', amount: 1600, status: 'paid', date: '2024-01-15', type: 'Avans 50%' },
  { id: 'INV-002', project: 'Mercedes AMG GT - PPF', amount: 2800, status: 'paid', date: '2024-01-08', type: 'Plata finala' },
  { id: 'INV-003', project: 'BMW M4 - Full Wrap', amount: 1600, status: 'pending', date: '2024-01-22', type: 'Rest de plata' },
];

const stages = [
  { name: 'Consultanta', icon: MessageSquare },
  { name: 'Design', icon: Sparkles },
  { name: 'Aprobare', icon: CheckCircle },
  { name: 'Pregatire', icon: Settings },
  { name: 'Colantare', icon: Car },
  { name: 'Control', icon: Eye },
  { name: 'Finalizare', icon: Star },
  { name: 'Livrare', icon: CheckCircle },
];

const getStatusBadge = (status) => {
  switch (status) {
    case 'in_progress':
      return <Badge className="status-active">In progres</Badge>;
    case 'completed':
      return <Badge className="status-completed">Finalizat</Badge>;
    case 'pending':
      return <Badge className="status-pending">In asteptare</Badge>;
    default:
      return null;
  }
};

export default function DashboardPage() {
  const navigate = useNavigate();
  const { user, logout, updateProfile } = useAuth();
  const [activeTab, setActiveTab] = useState('overview');
  const [selectedProject, setSelectedProject] = useState(null);
  const [newMessage, setNewMessage] = useState('');
  const [showProfileModal, setShowProfileModal] = useState(false);
  const [showReviewModal, setShowReviewModal] = useState(false);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [reviewData, setReviewData] = useState({ rating: 5, text: '', photos: [] });
  const [profileData, setProfileData] = useState({
    name: user?.name || '',
    email: user?.email || '',
    phone: user?.phone || '+40 722 123 456',
  });
  const fileInputRef = useRef(null);
  const [uploadedPhotos, setUploadedPhotos] = useState([]);
  const [notifications, setNotifications] = useState([
    { id: 1, text: 'Proiectul BMW M4 a avansat la etapa Colantare', time: '2 ore', read: false },
    { id: 2, text: 'Factura INV-003 asteapta plata', time: '1 zi', read: false },
    { id: 3, text: 'Mesaj nou de la echipa CrissCustoms', time: '3 ore', read: false },
  ]);

  const handleLogout = () => {
    logout();
    navigate('/');
  };

  const handleSendMessage = (projectId) => {
    if (!newMessage.trim()) return;
    
    toast.success('Mesaj trimis cu succes!');
    setNewMessage('');
  };

  const handleProfileUpdate = async () => {
    await updateProfile(profileData);
    toast.success('Profil actualizat cu succes!');
    setShowProfileModal(false);
  };

  const handleReviewSubmit = () => {
    toast.success('Recenzie trimisa pentru aprobare! Multumim!');
    setShowReviewModal(false);
    setReviewData({ rating: 5, text: '', photos: [] });
  };

  const handlePhotoUpload = (e) => {
    const files = Array.from(e.target.files);
    const newPhotos = files.map(file => ({
      name: file.name,
      preview: URL.createObjectURL(file),
      file
    }));
    setUploadedPhotos(prev => [...prev, ...newPhotos]);
    toast.success(`${files.length} fotografii incarcate!`);
  };

  const markNotificationsRead = () => {
    setNotifications(prev => prev.map(n => ({ ...n, read: true })));
    toast.success('Notificari marcate ca citite');
  };

  // Stats
  const totalProjects = mockProjects.length;
  const activeProjects = mockProjects.filter(p => p.status === 'in_progress').length;
  const totalInvested = mockProjects.reduce((sum, p) => sum + p.totalCost, 0);
  const unreadMessages = mockProjects.reduce((sum, p) => sum + p.messages.filter(m => !m.read && m.from === 'team').length, 0);

  return (
    <div className="min-h-screen bg-background">
      {/* Sidebar */}
      <aside className="fixed left-0 top-0 h-full w-64 glass-sidebar hidden lg:block z-40">
        <div className="p-6">
          <Link to="/" className="font-heading font-bold text-2xl gradient-text">
            CrissCustoms
          </Link>
        </div>

        <nav className="px-4 space-y-1">
          <Button 
            variant={activeTab === 'overview' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveTab('overview')}
          >
            <LayoutDashboard className="w-4 h-4 mr-3" />
            Prezentare generala
          </Button>
          <Button 
            variant={activeTab === 'projects' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveTab('projects')}
          >
            <Car className="w-4 h-4 mr-3" />
            Proiectele mele
            {activeProjects > 0 && (
              <Badge className="ml-auto bg-primary/20 text-primary text-xs">{activeProjects}</Badge>
            )}
          </Button>
          <Button 
            variant={activeTab === 'messages' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveTab('messages')}
          >
            <MessageSquare className="w-4 h-4 mr-3" />
            Mesaje
            {unreadMessages > 0 && (
              <Badge className="ml-auto bg-destructive text-destructive-foreground text-xs">{unreadMessages}</Badge>
            )}
          </Button>
          <Button 
            variant={activeTab === 'invoices' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveTab('invoices')}
          >
            <FileText className="w-4 h-4 mr-3" />
            Facturi
          </Button>
          <Button 
            variant={activeTab === 'photos' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveTab('photos')}
          >
            <Camera className="w-4 h-4 mr-3" />
            Galerie foto
          </Button>
          <Button 
            variant={activeTab === 'reviews' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveTab('reviews')}
          >
            <Star className="w-4 h-4 mr-3" />
            Recenzii
          </Button>
        </nav>

        <Separator className="my-4 mx-4 bg-border/50" />

        <div className="px-4 space-y-1">
          <Button 
            variant="ghost"
            className="w-full justify-start"
            onClick={() => setShowProfileModal(true)}
          >
            <Settings className="w-4 h-4 mr-3" />
            Setari profil
          </Button>
        </div>

        <div className="absolute bottom-0 left-0 right-0 p-4 border-t border-border/50">
          <Link to="/">
            <Button variant="ghost" className="w-full justify-start mb-2">
              <Home className="w-4 h-4 mr-3" />
              Inapoi la site
            </Button>
          </Link>
          <Button 
            variant="ghost" 
            className="w-full justify-start text-destructive hover:text-destructive"
            onClick={handleLogout}
          >
            <LogOut className="w-4 h-4 mr-3" />
            Deconectare
          </Button>
        </div>
      </aside>

      {/* Main content */}
      <main className="lg:ml-64">
        {/* Header */}
        <header className="sticky top-0 z-30 glass-header px-6 py-4">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="font-heading font-bold text-2xl text-foreground">
                Bine ai venit, {user?.name?.split(' ')[0] || 'Client'}!
              </h1>
              <p className="text-sm text-muted-foreground">
                Dashboard-ul tau pentru proiecte
              </p>
            </div>
            <div className="flex items-center gap-4">
              {/* Notifications */}
              <Dialog>
                <DialogTrigger asChild>
                  <Button variant="ghost" size="icon" className="relative">
                    <Bell className="w-5 h-5" />
                    {notifications.filter(n => !n.read).length > 0 && (
                      <span className="absolute -top-1 -right-1 w-5 h-5 bg-destructive text-destructive-foreground text-xs rounded-full flex items-center justify-center">
                        {notifications.filter(n => !n.read).length}
                      </span>
                    )}
                  </Button>
                </DialogTrigger>
                <DialogContent className="glass-card border-border">
                  <DialogHeader>
                    <DialogTitle className="flex items-center justify-between">
                      Notificari
                      <Button variant="ghost" size="sm" onClick={markNotificationsRead}>
                        Marcheaza citite
                      </Button>
                    </DialogTitle>
                  </DialogHeader>
                  <ScrollArea className="h-[300px]">
                    <div className="space-y-3">
                      {notifications.map((notif) => (
                        <div 
                          key={notif.id} 
                          className={`p-3 rounded-lg border ${notif.read ? 'border-border bg-card/30' : 'border-primary/30 bg-primary/5'}`}
                        >
                          <p className="text-sm text-foreground">{notif.text}</p>
                          <p className="text-xs text-muted-foreground mt-1">{notif.time}</p>
                        </div>
                      ))}
                    </div>
                  </ScrollArea>
                </DialogContent>
              </Dialog>

              {/* Profile */}
              <Avatar 
                className="w-10 h-10 border-2 border-primary/30 cursor-pointer hover:border-primary transition-colors"
                onClick={() => setShowProfileModal(true)}
              >
                <AvatarFallback className="bg-primary/10 text-primary font-semibold">
                  {user?.name?.split(' ').map(n => n[0]).join('') || 'C'}
                </AvatarFallback>
              </Avatar>
            </div>
          </div>
        </header>

        <div className="p-6">
          {/* Overview Tab */}
          {activeTab === 'overview' && (
            <div className="space-y-6 animate-fade-in">
              {/* Stats cards */}
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                <Card className="glass-card-pink card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Proiecte active</p>
                        <p className="text-3xl font-heading font-bold text-foreground">{activeProjects}</p>
                      </div>
                      <div className="p-3 rounded-xl bg-primary/20">
                        <Car className="w-6 h-6 text-primary animate-glow-pulse" />
                      </div>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Total proiecte</p>
                        <p className="text-3xl font-heading font-bold text-foreground">{totalProjects}</p>
                      </div>
                      <div className="p-3 rounded-xl bg-green-500/20">
                        <CheckCircle className="w-6 h-6 text-green-400" />
                      </div>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Total investit</p>
                        <p className="text-3xl font-heading font-bold text-foreground">{totalInvested.toLocaleString()} €</p>
                      </div>
                      <div className="p-3 rounded-xl bg-gold/20">
                        <Euro className="w-6 h-6 text-gold" />
                      </div>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Mesaje necitite</p>
                        <p className="text-3xl font-heading font-bold text-foreground">{unreadMessages}</p>
                      </div>
                      <div className="p-3 rounded-xl bg-blue-500/20">
                        <MessageSquare className="w-6 h-6 text-blue-400" />
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Active projects */}
              <Card className="glass-card">
                <CardHeader>
                  <CardTitle className="font-heading flex items-center gap-2">
                    <Car className="w-5 h-5 text-primary" />
                    Proiecte in derulare
                  </CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="space-y-6">
                    {mockProjects.filter(p => p.status === 'in_progress').map((project) => (
                      <div 
                        key={project.id}
                        className="glass-card p-4 rounded-xl cursor-pointer hover:border-primary/40 transition-all"
                        onClick={() => {
                          setSelectedProject(project);
                          setActiveTab('projects');
                        }}
                      >
                        <div className="flex flex-col lg:flex-row gap-4">
                          {/* Project image */}
                          <div className="w-full lg:w-40 h-28 rounded-lg overflow-hidden flex-shrink-0">
                            <img 
                              src={project.images.before[0]}
                              alt={project.vehicle}
                              className="w-full h-full object-cover"
                            />
                          </div>

                          {/* Project details */}
                          <div className="flex-1">
                            <div className="flex items-start justify-between mb-2">
                              <div>
                                <h3 className="font-heading font-semibold text-foreground">
                                  {project.vehicle}
                                </h3>
                                <p className="text-sm text-muted-foreground">
                                  {project.service} • {project.color}
                                </p>
                              </div>
                              {getStatusBadge(project.status)}
                            </div>

                            {/* Progress */}
                            <div className="mb-3">
                              <div className="flex items-center justify-between mb-1">
                                <span className="text-sm text-muted-foreground">
                                  Etapa: <span className="text-primary font-medium">{project.stage}</span>
                                </span>
                                <span className="text-sm font-medium text-primary">{project.progress}%</span>
                              </div>
                              <Progress value={project.progress} className="h-2 progress-glow" />
                            </div>

                            {/* Quick info */}
                            <div className="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                              <span className="flex items-center gap-1">
                                <Calendar className="w-4 h-4" />
                                Livrare: {new Date(project.estimatedEnd).toLocaleDateString('ro-RO')}
                              </span>
                              <span className="flex items-center gap-1">
                                <User className="w-4 h-4" />
                                {project.assignedTo}
                              </span>
                            </div>
                          </div>

                          <ChevronRight className="w-5 h-5 text-muted-foreground hidden lg:block" />
                        </div>
                      </div>
                    ))}

                    {mockProjects.filter(p => p.status === 'in_progress').length === 0 && (
                      <div className="text-center py-8">
                        <Car className="w-12 h-12 mx-auto text-muted-foreground/50 mb-3" />
                        <p className="text-muted-foreground">Nu ai proiecte active momentan</p>
                        <Link to="/#contact">
                          <Button className="mt-4 btn-neon">Solicita o oferta</Button>
                        </Link>
                      </div>
                    )}
                  </div>
                </CardContent>
              </Card>

              {/* Recent activity */}
              <div className="grid lg:grid-cols-2 gap-6">
                <Card className="glass-card">
                  <CardHeader>
                    <CardTitle className="font-heading text-lg">Ultimele mesaje</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {mockProjects
                        .flatMap(p => p.messages.map(m => ({ ...m, project: p.vehicle })))
                        .slice(0, 3)
                        .map((msg) => (
                          <div key={msg.id} className="p-3 rounded-lg bg-card/50 border border-border">
                            <div className="flex items-center justify-between mb-1">
                              <span className="text-sm font-medium text-foreground">{msg.name}</span>
                              <span className="text-xs text-muted-foreground">{msg.date}</span>
                            </div>
                            <p className="text-sm text-muted-foreground">{msg.text}</p>
                            <p className="text-xs text-primary mt-1">{msg.project}</p>
                          </div>
                        ))}
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card">
                  <CardHeader>
                    <CardTitle className="font-heading text-lg">Facturi recente</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {mockInvoices.slice(0, 3).map((invoice) => (
                        <div key={invoice.id} className="flex items-center justify-between p-3 rounded-lg bg-card/50 border border-border">
                          <div>
                            <p className="text-sm font-medium text-foreground">{invoice.id}</p>
                            <p className="text-xs text-muted-foreground">{invoice.type}</p>
                          </div>
                          <div className="text-right">
                            <p className="text-sm font-medium text-gold">{invoice.amount} €</p>
                            <Badge className={invoice.status === 'paid' ? 'status-completed' : 'status-pending'}>
                              {invoice.status === 'paid' ? 'Platit' : 'In asteptare'}
                            </Badge>
                          </div>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </div>
            </div>
          )}

          {/* Projects Tab */}
          {activeTab === 'projects' && (
            <div className="space-y-6 animate-fade-in">
              {selectedProject ? (
                <div>
                  <Button 
                    variant="ghost" 
                    className="mb-4"
                    onClick={() => setSelectedProject(null)}
                  >
                    ← Inapoi la lista
                  </Button>

                  <div className="grid lg:grid-cols-3 gap-6">
                    {/* Project details */}
                    <div className="lg:col-span-2 space-y-6">
                      <Card className="glass-card">
                        <CardHeader>
                          <div className="flex items-start justify-between">
                            <div>
                              <CardTitle className="font-heading">{selectedProject.vehicle}</CardTitle>
                              <CardDescription>{selectedProject.service} • {selectedProject.color}</CardDescription>
                            </div>
                            {getStatusBadge(selectedProject.status)}
                          </div>
                        </CardHeader>
                        <CardContent>
                          {/* Progress stages */}
                          <div className="mb-6">
                            <div className="flex items-center justify-between mb-2">
                              <span className="text-sm text-muted-foreground">Progres general</span>
                              <span className="text-sm font-medium text-primary">{selectedProject.progress}%</span>
                            </div>
                            <Progress value={selectedProject.progress} className="h-3 progress-glow mb-4" />
                            
                            <div className="grid grid-cols-4 lg:grid-cols-8 gap-2">
                              {stages.map((stage, index) => {
                                const isCompleted = index < selectedProject.stageIndex;
                                const isCurrent = index === selectedProject.stageIndex;
                                const StageIcon = stage.icon;
                                
                                return (
                                  <div 
                                    key={stage.name}
                                    className={`flex flex-col items-center p-2 rounded-lg text-center ${
                                      isCompleted 
                                        ? 'bg-green-500/20' 
                                        : isCurrent 
                                        ? 'bg-primary/20 animate-pulse-slow' 
                                        : 'bg-muted/30'
                                    }`}
                                  >
                                    <StageIcon className={`w-4 h-4 mb-1 ${
                                      isCompleted ? 'text-green-400' : isCurrent ? 'text-primary' : 'text-muted-foreground'
                                    }`} />
                                    <span className={`text-[10px] ${
                                      isCompleted ? 'text-green-400' : isCurrent ? 'text-primary' : 'text-muted-foreground'
                                    }`}>
                                      {stage.name}
                                    </span>
                                  </div>
                                );
                              })}
                            </div>
                          </div>

                          <Separator className="my-4" />

                          {/* Project info */}
                          <div className="grid grid-cols-2 gap-4">
                            <div>
                              <p className="text-sm text-muted-foreground">Data start</p>
                              <p className="font-medium">{new Date(selectedProject.startDate).toLocaleDateString('ro-RO')}</p>
                            </div>
                            <div>
                              <p className="text-sm text-muted-foreground">Data estimata livrare</p>
                              <p className="font-medium">{new Date(selectedProject.estimatedEnd).toLocaleDateString('ro-RO')}</p>
                            </div>
                            <div>
                              <p className="text-sm text-muted-foreground">Responsabil</p>
                              <p className="font-medium">{selectedProject.assignedTo}</p>
                            </div>
                            <div>
                              <p className="text-sm text-muted-foreground">Cost total</p>
                              <p className="font-medium text-gold">{selectedProject.totalCost} €</p>
                            </div>
                          </div>
                        </CardContent>
                      </Card>

                      {/* Photo gallery */}
                      <Card className="glass-card">
                        <CardHeader>
                          <CardTitle className="font-heading text-lg flex items-center gap-2">
                            <Camera className="w-5 h-5 text-primary" />
                            Galerie foto
                          </CardTitle>
                        </CardHeader>
                        <CardContent>
                          <Tabs defaultValue="before">
                            <TabsList className="grid grid-cols-3 mb-4">
                              <TabsTrigger value="before">Inainte</TabsTrigger>
                              <TabsTrigger value="during">In timpul</TabsTrigger>
                              <TabsTrigger value="after">Dupa</TabsTrigger>
                            </TabsList>
                            
                            {['before', 'during', 'after'].map((stage) => (
                              <TabsContent key={stage} value={stage}>
                                <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
                                  {selectedProject.images[stage]?.map((img, idx) => (
                                    <div key={idx} className="aspect-video rounded-lg overflow-hidden group relative">
                                      <img src={img} alt="" className="w-full h-full object-cover" />
                                      <div className="absolute inset-0 bg-black/50 opacity-0 group-hover:opacity-100 transition-opacity flex items-center justify-center">
                                        <Button size="icon" variant="ghost" className="text-white">
                                          <Eye className="w-5 h-5" />
                                        </Button>
                                      </div>
                                    </div>
                                  ))}
                                  
                                  {selectedProject.images[stage]?.length === 0 && (
                                    <div className="col-span-full text-center py-8 border-2 border-dashed border-border rounded-lg">
                                      <ImageIcon className="w-10 h-10 mx-auto text-muted-foreground/50 mb-2" />
                                      <p className="text-sm text-muted-foreground">
                                        Inca nu exista fotografii {stage === 'before' ? 'inainte' : stage === 'during' ? 'din timpul procesului' : 'finale'}
                                      </p>
                                    </div>
                                  )}
                                </div>
                              </TabsContent>
                            ))}
                          </Tabs>
                        </CardContent>
                      </Card>
                    </div>

                    {/* Messages sidebar */}
                    <div className="space-y-6">
                      <Card className="glass-card">
                        <CardHeader>
                          <CardTitle className="font-heading text-lg">Mesaje</CardTitle>
                        </CardHeader>
                        <CardContent>
                          <ScrollArea className="h-[300px] pr-4">
                            <div className="space-y-3">
                              {selectedProject.messages.map((msg) => (
                                <div 
                                  key={msg.id}
                                  className={`p-3 rounded-lg ${
                                    msg.from === 'team' 
                                      ? 'bg-primary/10 border border-primary/20' 
                                      : 'bg-card border border-border ml-4'
                                  }`}
                                >
                                  <div className="flex items-center justify-between mb-1">
                                    <span className="text-sm font-medium">{msg.name}</span>
                                    <span className="text-xs text-muted-foreground">{msg.date}</span>
                                  </div>
                                  <p className="text-sm text-muted-foreground">{msg.text}</p>
                                </div>
                              ))}
                            </div>
                          </ScrollArea>
                          
                          <div className="flex gap-2 mt-4">
                            <Input 
                              placeholder="Scrie un mesaj..."
                              value={newMessage}
                              onChange={(e) => setNewMessage(e.target.value)}
                              className="glass-input"
                            />
                            <Button 
                              size="icon" 
                              className="btn-neon"
                              onClick={() => handleSendMessage(selectedProject.id)}
                            >
                              <Send className="w-4 h-4" />
                            </Button>
                          </div>
                        </CardContent>
                      </Card>

                      {/* Payment status */}
                      <Card className="glass-card">
                        <CardHeader>
                          <CardTitle className="font-heading text-lg">Status plata</CardTitle>
                        </CardHeader>
                        <CardContent>
                          <div className="space-y-3">
                            <div className="flex items-center justify-between">
                              <span className="text-muted-foreground">Total</span>
                              <span className="font-medium">{selectedProject.totalCost} €</span>
                            </div>
                            <div className="flex items-center justify-between">
                              <span className="text-muted-foreground">Platit</span>
                              <span className="font-medium text-green-400">{selectedProject.paidAmount} €</span>
                            </div>
                            <Separator />
                            <div className="flex items-center justify-between">
                              <span className="text-muted-foreground">Rest de plata</span>
                              <span className="font-medium text-gold">
                                {selectedProject.totalCost - selectedProject.paidAmount} €
                              </span>
                            </div>
                          </div>
                          
                          {selectedProject.totalCost - selectedProject.paidAmount > 0 && (
                            <Button className="w-full mt-4 btn-neon">
                              Plateste acum
                            </Button>
                          )}
                        </CardContent>
                      </Card>

                      {/* Review button */}
                      {selectedProject.canReview && (
                        <Card className="glass-card-pink">
                          <CardContent className="p-4">
                            <div className="flex items-center gap-3">
                              <Star className="w-8 h-8 text-gold" />
                              <div className="flex-1">
                                <p className="font-medium">Proiect finalizat!</p>
                                <p className="text-sm text-muted-foreground">Lasa o recenzie</p>
                              </div>
                              <Button 
                                size="sm" 
                                className="btn-neon"
                                onClick={() => setShowReviewModal(true)}
                              >
                                Scrie recenzie
                              </Button>
                            </div>
                          </CardContent>
                        </Card>
                      )}
                    </div>
                  </div>
                </div>
              ) : (
                <div className="space-y-4">
                  {mockProjects.map((project) => (
                    <Card 
                      key={project.id}
                      className="glass-card cursor-pointer hover:border-primary/40 transition-all"
                      onClick={() => setSelectedProject(project)}
                    >
                      <CardContent className="p-4">
                        <div className="flex flex-col lg:flex-row gap-4">
                          <div className="w-full lg:w-48 h-32 rounded-lg overflow-hidden flex-shrink-0">
                            <img 
                              src={project.images.before[0]}
                              alt={project.vehicle}
                              className="w-full h-full object-cover"
                            />
                          </div>

                          <div className="flex-1">
                            <div className="flex items-start justify-between mb-2">
                              <div>
                                <h3 className="font-heading font-semibold text-lg text-foreground">
                                  {project.vehicle}
                                </h3>
                                <p className="text-sm text-muted-foreground">
                                  {project.service} • {project.color}
                                </p>
                              </div>
                              {getStatusBadge(project.status)}
                            </div>

                            <div className="mb-3">
                              <div className="flex items-center justify-between mb-1">
                                <span className="text-sm text-muted-foreground">Progres: {project.stage}</span>
                                <span className="text-sm font-medium text-primary">{project.progress}%</span>
                              </div>
                              <Progress value={project.progress} className="h-2" />
                            </div>

                            <div className="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                              <span className="flex items-center gap-1">
                                <Clock className="w-4 h-4" />
                                Start: {new Date(project.startDate).toLocaleDateString('ro-RO')}
                              </span>
                              <span className="flex items-center gap-1">
                                <Calendar className="w-4 h-4" />
                                Livrare: {new Date(project.estimatedEnd).toLocaleDateString('ro-RO')}
                              </span>
                              <span className="flex items-center gap-1 text-gold">
                                <Euro className="w-4 h-4" />
                                {project.totalCost} €
                              </span>
                            </div>
                          </div>

                          <ChevronRight className="w-6 h-6 text-muted-foreground hidden lg:block self-center" />
                        </div>
                      </CardContent>
                    </Card>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Messages Tab */}
          {activeTab === 'messages' && (
            <div className="space-y-6 animate-fade-in">
              <Card className="glass-card">
                <CardHeader>
                  <CardTitle className="font-heading">Toate mesajele</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="space-y-4">
                    {mockProjects.map((project) => (
                      <div key={project.id} className="space-y-3">
                        <h3 className="font-medium text-foreground flex items-center gap-2">
                          <Car className="w-4 h-4 text-primary" />
                          {project.vehicle}
                        </h3>
                        {project.messages.map((msg) => (
                          <div 
                            key={msg.id}
                            className={`p-4 rounded-lg ${
                              msg.from === 'team' 
                                ? 'bg-primary/10 border border-primary/20' 
                                : 'bg-card border border-border ml-8'
                            }`}
                          >
                            <div className="flex items-center justify-between mb-2">
                              <span className="font-medium">{msg.name}</span>
                              <span className="text-xs text-muted-foreground">{msg.date}</span>
                            </div>
                            <p className="text-muted-foreground">{msg.text}</p>
                          </div>
                        ))}
                        <Separator className="my-4" />
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            </div>
          )}

          {/* Invoices Tab */}
          {activeTab === 'invoices' && (
            <div className="space-y-6 animate-fade-in">
              <Card className="glass-card">
                <CardHeader>
                  <CardTitle className="font-heading">Facturi si plati</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="overflow-x-auto">
                    <table className="w-full">
                      <thead>
                        <tr className="border-b border-border">
                          <th className="text-left py-3 px-4 text-muted-foreground font-medium">Nr. Factura</th>
                          <th className="text-left py-3 px-4 text-muted-foreground font-medium">Proiect</th>
                          <th className="text-left py-3 px-4 text-muted-foreground font-medium">Tip</th>
                          <th className="text-left py-3 px-4 text-muted-foreground font-medium">Data</th>
                          <th className="text-right py-3 px-4 text-muted-foreground font-medium">Suma</th>
                          <th className="text-center py-3 px-4 text-muted-foreground font-medium">Status</th>
                          <th className="text-center py-3 px-4 text-muted-foreground font-medium">Actiuni</th>
                        </tr>
                      </thead>
                      <tbody>
                        {mockInvoices.map((invoice) => (
                          <tr key={invoice.id} className="border-b border-border/50 hover:bg-card/50">
                            <td className="py-3 px-4 font-medium">{invoice.id}</td>
                            <td className="py-3 px-4 text-muted-foreground">{invoice.project}</td>
                            <td className="py-3 px-4 text-muted-foreground">{invoice.type}</td>
                            <td className="py-3 px-4 text-muted-foreground">{invoice.date}</td>
                            <td className="py-3 px-4 text-right font-medium text-gold">{invoice.amount} €</td>
                            <td className="py-3 px-4 text-center">
                              <Badge className={invoice.status === 'paid' ? 'status-completed' : 'status-pending'}>
                                {invoice.status === 'paid' ? 'Platit' : 'In asteptare'}
                              </Badge>
                            </td>
                            <td className="py-3 px-4 text-center">
                              <div className="flex items-center justify-center gap-2">
                                <Button variant="ghost" size="icon">
                                  <Download className="w-4 h-4" />
                                </Button>
                                {invoice.status === 'pending' && (
                                  <Button size="sm" className="btn-neon">
                                    Plateste
                                  </Button>
                                )}
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </CardContent>
              </Card>
            </div>
          )}

          {/* Photos Tab */}
          {activeTab === 'photos' && (
            <div className="space-y-6 animate-fade-in">
              <Card className="glass-card">
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle className="font-heading">Galeria mea foto</CardTitle>
                    <Button className="btn-neon" onClick={() => setShowUploadModal(true)}>
                      <Upload className="w-4 h-4 mr-2" />
                      Incarca fotografii
                    </Button>
                  </div>
                </CardHeader>
                <CardContent>
                  <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
                    {mockProjects.flatMap(p => [
                      ...p.images.before.map((img, i) => ({ img, project: p.vehicle, stage: 'Inainte' })),
                      ...p.images.during.map((img, i) => ({ img, project: p.vehicle, stage: 'In timpul' })),
                      ...p.images.after.map((img, i) => ({ img, project: p.vehicle, stage: 'Dupa' })),
                    ]).map((item, idx) => (
                      <div key={idx} className="group relative aspect-square rounded-lg overflow-hidden">
                        <img src={item.img} alt="" className="w-full h-full object-cover" />
                        <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent opacity-0 group-hover:opacity-100 transition-opacity">
                          <div className="absolute bottom-0 left-0 right-0 p-3">
                            <p className="text-sm font-medium text-white">{item.project}</p>
                            <p className="text-xs text-white/70">{item.stage}</p>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            </div>
          )}

          {/* Reviews Tab */}
          {activeTab === 'reviews' && (
            <div className="space-y-6 animate-fade-in">
              <Card className="glass-card">
                <CardHeader>
                  <CardTitle className="font-heading">Recenziile mele</CardTitle>
                </CardHeader>
                <CardContent>
                  {mockProjects.filter(p => p.canReview).length > 0 ? (
                    <div className="space-y-4">
                      {mockProjects.filter(p => p.canReview).map((project) => (
                        <div key={project.id} className="glass-card-pink p-4 rounded-xl">
                          <div className="flex items-center justify-between mb-3">
                            <div>
                              <h3 className="font-medium">{project.vehicle}</h3>
                              <p className="text-sm text-muted-foreground">{project.service}</p>
                            </div>
                            <Button className="btn-neon" onClick={() => setShowReviewModal(true)}>
                              <Star className="w-4 h-4 mr-2" />
                              Scrie recenzie
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="text-center py-8">
                      <Star className="w-12 h-12 mx-auto text-muted-foreground/50 mb-3" />
                      <p className="text-muted-foreground">Nu ai proiecte finalizate pentru care sa lasi recenzie</p>
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          )}
        </div>
      </main>

      {/* Profile Modal */}
      <Dialog open={showProfileModal} onOpenChange={setShowProfileModal}>
        <DialogContent className="glass-card border-border">
          <DialogHeader>
            <DialogTitle>Setari profil</DialogTitle>
            <DialogDescription>Actualizeaza informatiile contului tau</DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-4">
            <div className="flex justify-center mb-4">
              <Avatar className="w-20 h-20 border-2 border-primary">
                <AvatarFallback className="bg-primary/20 text-primary text-2xl font-bold">
                  {profileData.name?.split(' ').map(n => n[0]).join('') || 'U'}
                </AvatarFallback>
              </Avatar>
            </div>
            <div className="space-y-2">
              <Label>Nume complet</Label>
              <Input 
                value={profileData.name}
                onChange={(e) => setProfileData({...profileData, name: e.target.value})}
                className="glass-input"
              />
            </div>
            <div className="space-y-2">
              <Label>Email</Label>
              <Input 
                value={profileData.email}
                onChange={(e) => setProfileData({...profileData, email: e.target.value})}
                className="glass-input"
              />
            </div>
            <div className="space-y-2">
              <Label>Telefon</Label>
              <Input 
                value={profileData.phone}
                onChange={(e) => setProfileData({...profileData, phone: e.target.value})}
                className="glass-input"
              />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowProfileModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={handleProfileUpdate}>Salveaza</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Review Modal */}
      <Dialog open={showReviewModal} onOpenChange={setShowReviewModal}>
        <DialogContent className="glass-card border-border">
          <DialogHeader>
            <DialogTitle>Scrie o recenzie</DialogTitle>
            <DialogDescription>Parerea ta conteaza pentru noi!</DialogDescription>
          </DialogHeader>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label>Rating</Label>
              <div className="flex gap-2">
                {[1, 2, 3, 4, 5].map((star) => (
                  <button
                    key={star}
                    onClick={() => setReviewData({...reviewData, rating: star})}
                    className="p-1"
                  >
                    <Star 
                      className={`w-8 h-8 ${star <= reviewData.rating ? 'fill-gold text-gold' : 'text-muted-foreground'}`}
                    />
                  </button>
                ))}
              </div>
            </div>
            <div className="space-y-2">
              <Label>Recenzia ta</Label>
              <Textarea 
                placeholder="Spune-ne experienta ta..."
                value={reviewData.text}
                onChange={(e) => setReviewData({...reviewData, text: e.target.value})}
                className="glass-input min-h-[100px]"
              />
            </div>
            <div className="space-y-2">
              <Label>Adauga fotografii (optional)</Label>
              <div className="upload-zone rounded-lg p-6 text-center cursor-pointer">
                <Upload className="w-8 h-8 mx-auto text-muted-foreground mb-2" />
                <p className="text-sm text-muted-foreground">Click pentru a incarca fotografii</p>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowReviewModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={handleReviewSubmit}>Trimite recenzie</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Upload Modal */}
      <Dialog open={showUploadModal} onOpenChange={setShowUploadModal}>
        <DialogContent className="glass-card border-border max-w-2xl">
          <DialogHeader>
            <DialogTitle>Incarca fotografii</DialogTitle>
            <DialogDescription>Adauga fotografii pentru proiectele tale</DialogDescription>
          </DialogHeader>
          <div className="py-4">
            <div 
              className="upload-zone rounded-lg p-8 text-center cursor-pointer mb-4"
              onClick={() => fileInputRef.current?.click()}
            >
              <input 
                type="file" 
                ref={fileInputRef}
                className="hidden"
                multiple
                accept="image/*"
                onChange={handlePhotoUpload}
              />
              <Upload className="w-12 h-12 mx-auto text-primary mb-3" />
              <p className="text-foreground font-medium">Trage fotografiile aici sau click pentru a selecta</p>
              <p className="text-sm text-muted-foreground mt-1">PNG, JPG pana la 10MB</p>
            </div>

            {uploadedPhotos.length > 0 && (
              <div className="grid grid-cols-3 gap-3">
                {uploadedPhotos.map((photo, idx) => (
                  <div key={idx} className="relative group">
                    <img src={photo.preview} alt="" className="w-full aspect-square object-cover rounded-lg" />
                    <button 
                      className="absolute top-2 right-2 p-1 bg-destructive rounded-full opacity-0 group-hover:opacity-100 transition-opacity"
                      onClick={() => setUploadedPhotos(prev => prev.filter((_, i) => i !== idx))}
                    >
                      <X className="w-4 h-4 text-white" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowUploadModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={() => {
              toast.success('Fotografii incarcate cu succes!');
              setShowUploadModal(false);
              setUploadedPhotos([]);
            }}>
              Salveaza fotografiile
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
