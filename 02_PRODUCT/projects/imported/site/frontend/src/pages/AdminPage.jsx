import React, { useState, useRef } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '../components/ui/card';
import { Button } from '../components/ui/button';
import { Badge } from '../components/ui/badge';
import { Avatar, AvatarFallback } from '../components/ui/avatar';
import { Progress } from '../components/ui/progress';
import { Input } from '../components/ui/input';
import { Textarea } from '../components/ui/textarea';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger, DialogFooter, DialogDescription } from '../components/ui/dialog';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../components/ui/tabs';
import { ScrollArea } from '../components/ui/scroll-area';
import { Separator } from '../components/ui/separator';
import { toast } from 'sonner';
import { 
  LayoutDashboard, 
  Users, 
  Car,
  FileText,
  Star,
  Package,
  Settings,
  LogOut,
  Home,
  TrendingUp,
  Euro,
  Calendar,
  Eye,
  CheckCircle,
  Clock,
  AlertTriangle,
  Plus,
  Edit,
  Trash2,
  Search,
  Filter,
  Download,
  Upload,
  Send,
  Bell,
  MessageSquare,
  Camera,
  X,
  ChevronRight,
  ChevronDown,
  MoreVertical,
  RefreshCw,
  BarChart3,
  PieChart,
  Activity,
  DollarSign,
  ShoppingCart,
  UserPlus,
  Mail,
  Phone,
  MapPin,
  Image as ImageIcon
} from 'lucide-react';
import { 
  AreaChart, 
  Area, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  ResponsiveContainer,
  PieChart as RePieChart,
  Pie,
  Cell,
  BarChart,
  Bar,
  Legend
} from 'recharts';

// Mock admin data
const mockStats = {
  totalRevenue: 125000,
  monthlyRevenue: 28500,
  totalProjects: 156,
  activeProjects: 12,
  completedProjects: 144,
  totalCustomers: 89,
  newCustomers: 8,
  averageRating: 4.8,
  pendingQuotes: 5,
  monthlyGrowth: 15,
  pendingReviews: 3,
};

const revenueData = [
  { month: 'Ian', revenue: 18500 },
  { month: 'Feb', revenue: 22000 },
  { month: 'Mar', revenue: 19500 },
  { month: 'Apr', revenue: 25000 },
  { month: 'Mai', revenue: 28500 },
  { month: 'Iun', revenue: 31500 },
];

const serviceData = [
  { name: 'Full Wrap', value: 45, color: 'hsl(328, 100%, 54%)' },
  { name: 'PPF', value: 25, color: 'hsl(199, 100%, 50%)' },
  { name: 'Partial', value: 15, color: 'hsl(51, 100%, 50%)' },
  { name: 'Custom', value: 15, color: 'hsl(142, 76%, 36%)' },
];

const projectsByStatus = [
  { status: 'Consultanta', count: 2 },
  { status: 'Design', count: 1 },
  { status: 'Aprobare', count: 2 },
  { status: 'Pregatire', count: 1 },
  { status: 'Colantare', count: 3 },
  { status: 'Control', count: 2 },
  { status: 'Finalizare', count: 1 },
];

const allProjects = [
  {
    id: 1,
    customer: 'Alexandru Ionescu',
    customerEmail: 'alex@email.com',
    customerPhone: '+40 722 111 222',
    vehicle: 'BMW M4 Competition 2023',
    service: 'Full Wrap',
    color: 'Satin Midnight Blue',
    status: 'in_progress',
    stage: 'Colantare',
    stageIndex: 4,
    progress: 65,
    value: 3200,
    paid: 1600,
    startDate: '2024-01-15',
    estimatedEnd: '2024-01-22',
    assignedTo: 'Cristian D.',
    notes: 'Client VIP, atentie la detalii',
  },
  {
    id: 2,
    customer: 'Maria Popescu',
    customerEmail: 'maria@email.com',
    customerPhone: '+40 722 333 444',
    vehicle: 'Mercedes AMG GT 2022',
    service: 'PPF Full',
    color: 'Transparent',
    status: 'pending_approval',
    stage: 'Aprobare',
    stageIndex: 2,
    progress: 25,
    value: 2800,
    paid: 0,
    startDate: '2024-01-20',
    estimatedEnd: '2024-01-28',
    assignedTo: 'Mihai S.',
    notes: '',
  },
  {
    id: 3,
    customer: 'Andrei Mihai',
    customerEmail: 'andrei@email.com',
    customerPhone: '+40 722 555 666',
    vehicle: 'Porsche 911 GT3 2023',
    service: 'Full Wrap + PPF',
    color: 'Racing Green + PPF',
    status: 'completed',
    stage: 'Livrat',
    stageIndex: 7,
    progress: 100,
    value: 4500,
    paid: 4500,
    startDate: '2024-01-01',
    estimatedEnd: '2024-01-12',
    assignedTo: 'Cristian D.',
    notes: 'Proiect premium finalizat',
  },
];

const allCustomers = [
  { id: 1, name: 'Alexandru Ionescu', email: 'alex@email.com', phone: '+40 722 111 222', projects: 3, totalSpent: 8500, lastProject: '2024-01-15', status: 'active' },
  { id: 2, name: 'Maria Popescu', email: 'maria@email.com', phone: '+40 722 333 444', projects: 1, totalSpent: 0, lastProject: '2024-01-20', status: 'new' },
  { id: 3, name: 'Andrei Mihai', email: 'andrei@email.com', phone: '+40 722 555 666', projects: 5, totalSpent: 15200, lastProject: '2024-01-12', status: 'vip' },
  { id: 4, name: 'Elena Dumitrescu', email: 'elena@email.com', phone: '+40 722 777 888', projects: 2, totalSpent: 5600, lastProject: '2023-12-20', status: 'active' },
];

const pendingQuotes = [
  { id: 1, customer: 'Ion Georgescu', vehicle: 'Audi RS6 2024', service: 'Full Wrap', estimatedValue: 3500, date: '2024-01-19', status: 'new' },
  { id: 2, customer: 'Ana Marinescu', vehicle: 'Tesla Model S', service: 'PPF Partial', estimatedValue: 1200, date: '2024-01-18', status: 'contacted' },
  { id: 3, customer: 'Mihai Tudor', vehicle: 'VW Golf R', service: 'Custom Design', estimatedValue: 2000, date: '2024-01-17', status: 'new' },
];

const pendingReviews = [
  { id: 1, customer: 'Elena Dumitrescu', rating: 5, text: 'Servicii excelente! Recomand cu incredere. Echipa foarte profesionista.', date: '2024-01-18', project: 'Mercedes C63 - Full Wrap' },
  { id: 2, customer: 'Mihai Stanescu', rating: 5, text: 'Profesionalism de top, rezultat impecabil. Masina arata superb!', date: '2024-01-17', project: 'BMW X5 - PPF' },
  { id: 3, customer: 'Ioana Vasilescu', rating: 4, text: 'Foarte multumita de rezultat. Timp de executie putin mai lung decat estimat.', date: '2024-01-16', project: 'Audi A6 - Partial Wrap' },
];

const inventoryItems = [
  { id: 1, name: '3M 1080 Satin Dark Grey', category: 'Folie Wrap', quantity: 15, unit: 'mp', minStock: 10, price: 45 },
  { id: 2, name: 'Avery Supreme Gloss Black', category: 'Folie Wrap', quantity: 8, unit: 'mp', minStock: 10, price: 42 },
  { id: 3, name: 'XPEL Ultimate Plus PPF', category: 'PPF', quantity: 25, unit: 'mp', minStock: 15, price: 120 },
  { id: 4, name: 'KPMF Matte White', category: 'Folie Wrap', quantity: 5, unit: 'mp', minStock: 10, price: 38 },
  { id: 5, name: 'Alcohol Izopropilic', category: 'Consumabile', quantity: 12, unit: 'L', minStock: 5, price: 15 },
];

const teamMembers = [
  { id: 1, name: 'Cristian D.', role: 'Senior Wrapper', activeProjects: 3, completedProjects: 45, rating: 4.9 },
  { id: 2, name: 'Mihai S.', role: 'Wrapper', activeProjects: 2, completedProjects: 32, rating: 4.7 },
  { id: 3, name: 'Andrei P.', role: 'PPF Specialist', activeProjects: 2, completedProjects: 28, rating: 4.8 },
];

const stages = ['Consultanta', 'Design', 'Aprobare', 'Pregatire', 'Colantare', 'Control', 'Finalizare', 'Livrare'];

const getStatusBadge = (status) => {
  switch (status) {
    case 'in_progress':
      return <Badge className="status-active">In progres</Badge>;
    case 'completed':
      return <Badge className="status-completed">Finalizat</Badge>;
    case 'pending_approval':
      return <Badge className="status-pending">Asteapta aprobare</Badge>;
    case 'cancelled':
      return <Badge className="status-cancelled">Anulat</Badge>;
    default:
      return null;
  }
};

export default function AdminPage() {
  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const [activeSection, setActiveSection] = useState('dashboard');
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedProject, setSelectedProject] = useState(null);
  const [showNewProjectModal, setShowNewProjectModal] = useState(false);
  const [showNewCustomerModal, setShowNewCustomerModal] = useState(false);
  const [showNewQuoteModal, setShowNewQuoteModal] = useState(false);
  const [showInventoryModal, setShowInventoryModal] = useState(false);
  const [notifications, setNotifications] = useState([
    { id: 1, text: '5 oferte noi asteapta raspuns', time: '10 min', type: 'quote' },
    { id: 2, text: '3 recenzii de aprobat', time: '1 ora', type: 'review' },
    { id: 3, text: 'Stoc scazut: Avery Supreme Gloss Black', time: '2 ore', type: 'inventory' },
    { id: 4, text: 'Proiect BMW M4 avansat la Colantare', time: '3 ore', type: 'project' },
  ]);

  const handleLogout = () => {
    logout();
    navigate('/');
  };

  const handleApproveReview = (id) => {
    toast.success('Recenzie aprobata si publicata!');
  };

  const handleRejectReview = (id) => {
    toast.success('Recenzie respinsa');
  };

  const handleApproveQuote = (id) => {
    toast.success('Oferta convertita in proiect!');
  };

  const handleUpdateProjectStatus = (projectId, newStage) => {
    toast.success(`Proiect actualizat la etapa: ${newStage}`);
  };

  return (
    <div className="min-h-screen bg-background">
      {/* Sidebar */}
      <aside className="fixed left-0 top-0 h-full w-64 glass-sidebar hidden lg:block z-40">
        <div className="p-6">
          <Link to="/" className="font-heading font-bold text-2xl gradient-text">
            CrissCustoms
          </Link>
          <Badge className="ml-2 bg-primary/20 text-primary text-xs">Admin</Badge>
        </div>

        <nav className="px-4 space-y-1">
          <Button 
            variant={activeSection === 'dashboard' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('dashboard')}
          >
            <LayoutDashboard className="w-4 h-4 mr-3" />
            Dashboard
          </Button>
          <Button 
            variant={activeSection === 'projects' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('projects')}
          >
            <Car className="w-4 h-4 mr-3" />
            Proiecte
            <Badge className="ml-auto bg-primary/20 text-primary text-xs">{mockStats.activeProjects}</Badge>
          </Button>
          <Button 
            variant={activeSection === 'customers' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('customers')}
          >
            <Users className="w-4 h-4 mr-3" />
            Clienti
          </Button>
          <Button 
            variant={activeSection === 'quotes' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('quotes')}
          >
            <FileText className="w-4 h-4 mr-3" />
            Oferte
            {mockStats.pendingQuotes > 0 && (
              <Badge className="ml-auto bg-destructive text-destructive-foreground text-xs">
                {mockStats.pendingQuotes}
              </Badge>
            )}
          </Button>
          <Button 
            variant={activeSection === 'reviews' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('reviews')}
          >
            <Star className="w-4 h-4 mr-3" />
            Recenzii
            {mockStats.pendingReviews > 0 && (
              <Badge className="ml-auto bg-gold/20 text-gold text-xs">
                {mockStats.pendingReviews}
              </Badge>
            )}
          </Button>
          <Button 
            variant={activeSection === 'inventory' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('inventory')}
          >
            <Package className="w-4 h-4 mr-3" />
            Inventar
          </Button>
          <Button 
            variant={activeSection === 'team' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('team')}
          >
            <Users className="w-4 h-4 mr-3" />
            Echipa
          </Button>
          <Button 
            variant={activeSection === 'analytics' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('analytics')}
          >
            <BarChart3 className="w-4 h-4 mr-3" />
            Rapoarte
          </Button>
          <Button 
            variant={activeSection === 'settings' ? 'secondary' : 'ghost'}
            className="w-full justify-start"
            onClick={() => setActiveSection('settings')}
          >
            <Settings className="w-4 h-4 mr-3" />
            Setari
          </Button>
        </nav>

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
                {activeSection === 'dashboard' && 'Dashboard'}
                {activeSection === 'projects' && 'Managementul Proiectelor'}
                {activeSection === 'customers' && 'Clienti'}
                {activeSection === 'quotes' && 'Oferte si Cereri'}
                {activeSection === 'reviews' && 'Recenzii'}
                {activeSection === 'inventory' && 'Inventar'}
                {activeSection === 'team' && 'Echipa'}
                {activeSection === 'analytics' && 'Rapoarte si Analize'}
                {activeSection === 'settings' && 'Setari'}
              </h1>
              <p className="text-sm text-muted-foreground">
                Bine ai venit, {user?.name || 'Admin'}
              </p>
            </div>
            <div className="flex items-center gap-4">
              {/* Search */}
              <div className="relative hidden md:block">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                <Input 
                  placeholder="Cauta..."
                  className="pl-9 w-64 glass-input"
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                />
              </div>

              {/* Notifications */}
              <Dialog>
                <DialogTrigger asChild>
                  <Button variant="ghost" size="icon" className="relative">
                    <Bell className="w-5 h-5" />
                    <span className="absolute -top-1 -right-1 w-5 h-5 bg-destructive text-destructive-foreground text-xs rounded-full flex items-center justify-center">
                      {notifications.length}
                    </span>
                  </Button>
                </DialogTrigger>
                <DialogContent className="glass-card border-border">
                  <DialogHeader>
                    <DialogTitle>Notificari</DialogTitle>
                  </DialogHeader>
                  <ScrollArea className="h-[300px]">
                    <div className="space-y-3">
                      {notifications.map((notif) => (
                        <div 
                          key={notif.id} 
                          className="p-3 rounded-lg border border-primary/20 bg-primary/5"
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
              <Avatar className="w-10 h-10 border-2 border-primary/30">
                <AvatarFallback className="bg-primary/10 text-primary font-semibold">
                  {user?.name?.split(' ').map(n => n[0]).join('') || 'A'}
                </AvatarFallback>
              </Avatar>
            </div>
          </div>
        </header>

        <div className="p-6">
          {/* Dashboard Section */}
          {activeSection === 'dashboard' && (
            <div className="space-y-6 animate-fade-in">
              {/* Stats grid */}
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
                <Card className="glass-card-pink card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Venituri luna aceasta</p>
                        <p className="text-2xl font-heading font-bold text-foreground">
                          {mockStats.monthlyRevenue.toLocaleString()} €
                        </p>
                        <p className="text-xs text-green-400 flex items-center gap-1 mt-1">
                          <TrendingUp className="w-3 h-3" />
                          +{mockStats.monthlyGrowth}% fata de luna trecuta
                        </p>
                      </div>
                      <div className="p-3 rounded-xl bg-gold/20">
                        <Euro className="w-6 h-6 text-gold animate-glow-pulse" />
                      </div>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Proiecte active</p>
                        <p className="text-2xl font-heading font-bold text-foreground">
                          {mockStats.activeProjects}
                        </p>
                        <p className="text-xs text-muted-foreground mt-1">
                          din {mockStats.totalProjects} total
                        </p>
                      </div>
                      <div className="p-3 rounded-xl bg-primary/20">
                        <Car className="w-6 h-6 text-primary" />
                      </div>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Clienti noi</p>
                        <p className="text-2xl font-heading font-bold text-foreground">
                          {mockStats.newCustomers}
                        </p>
                        <p className="text-xs text-green-400 flex items-center gap-1 mt-1">
                          <UserPlus className="w-3 h-3" />
                          luna aceasta
                        </p>
                      </div>
                      <div className="p-3 rounded-xl bg-blue-500/20">
                        <Users className="w-6 h-6 text-blue-400" />
                      </div>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card card-hover">
                  <CardContent className="p-6">
                    <div className="flex items-center justify-between">
                      <div>
                        <p className="text-sm text-muted-foreground mb-1">Rating mediu</p>
                        <p className="text-2xl font-heading font-bold text-foreground">
                          {mockStats.averageRating}
                        </p>
                        <p className="text-xs text-gold flex items-center gap-1 mt-1">
                          <Star className="w-3 h-3 fill-gold" />
                          Excelent
                        </p>
                      </div>
                      <div className="p-3 rounded-xl bg-gold/20">
                        <Star className="w-6 h-6 text-gold" />
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Charts row */}
              <div className="grid lg:grid-cols-2 gap-6">
                {/* Revenue Chart */}
                <Card className="glass-card">
                  <CardHeader>
                    <CardTitle className="font-heading flex items-center gap-2">
                      <BarChart3 className="w-5 h-5 text-primary" />
                      Evolutie venituri
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="h-[250px]">
                      <ResponsiveContainer width="100%" height="100%">
                        <AreaChart data={revenueData}>
                          <defs>
                            <linearGradient id="colorRevenue" x1="0" y1="0" x2="0" y2="1">
                              <stop offset="5%" stopColor="hsl(328, 100%, 54%)" stopOpacity={0.3}/>
                              <stop offset="95%" stopColor="hsl(328, 100%, 54%)" stopOpacity={0}/>
                            </linearGradient>
                          </defs>
                          <CartesianGrid strokeDasharray="3 3" stroke="hsl(0, 0%, 20%)" />
                          <XAxis dataKey="month" stroke="hsl(0, 0%, 50%)" />
                          <YAxis stroke="hsl(0, 0%, 50%)" />
                          <Tooltip 
                            contentStyle={{ 
                              background: 'hsl(0, 0%, 10%)', 
                              border: '1px solid hsl(328, 100%, 54%, 0.3)',
                              borderRadius: '8px'
                            }}
                            formatter={(value) => [`${value} €`, 'Venituri']}
                          />
                          <Area 
                            type="monotone" 
                            dataKey="revenue" 
                            stroke="hsl(328, 100%, 54%)" 
                            fillOpacity={1} 
                            fill="url(#colorRevenue)" 
                          />
                        </AreaChart>
                      </ResponsiveContainer>
                    </div>
                  </CardContent>
                </Card>

                {/* Services Pie Chart */}
                <Card className="glass-card">
                  <CardHeader>
                    <CardTitle className="font-heading flex items-center gap-2">
                      <PieChart className="w-5 h-5 text-primary" />
                      Distributie servicii
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="h-[250px] flex items-center">
                      <ResponsiveContainer width="100%" height="100%">
                        <RePieChart>
                          <Pie
                            data={serviceData}
                            cx="50%"
                            cy="50%"
                            innerRadius={60}
                            outerRadius={80}
                            paddingAngle={5}
                            dataKey="value"
                          >
                            {serviceData.map((entry, index) => (
                              <Cell key={`cell-${index}`} fill={entry.color} />
                            ))}
                          </Pie>
                          <Tooltip 
                            contentStyle={{ 
                              background: 'hsl(0, 0%, 10%)', 
                              border: '1px solid hsl(328, 100%, 54%, 0.3)',
                              borderRadius: '8px'
                            }}
                            formatter={(value) => [`${value}%`, '']}
                          />
                          <Legend 
                            verticalAlign="middle" 
                            align="right"
                            layout="vertical"
                            wrapperStyle={{ paddingLeft: '20px' }}
                          />
                        </RePieChart>
                      </ResponsiveContainer>
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Recent projects and reviews */}
              <div className="grid lg:grid-cols-2 gap-6">
                {/* Recent projects */}
                <Card className="glass-card">
                  <CardHeader className="flex flex-row items-center justify-between">
                    <CardTitle className="font-heading flex items-center gap-2">
                      <Car className="w-5 h-5 text-primary" />
                      Proiecte recente
                    </CardTitle>
                    <Button variant="ghost" size="sm" className="text-primary" onClick={() => setActiveSection('projects')}>
                      Vezi toate
                    </Button>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {allProjects.slice(0, 3).map((project) => (
                        <div 
                          key={project.id}
                          className="flex items-center justify-between p-3 rounded-lg glass-card hover:border-primary/30 cursor-pointer transition-all"
                          onClick={() => {
                            setSelectedProject(project);
                            setActiveSection('projects');
                          }}
                        >
                          <div>
                            <p className="font-medium text-foreground text-sm">{project.customer}</p>
                            <p className="text-xs text-muted-foreground">{project.vehicle} • {project.service}</p>
                          </div>
                          <div className="flex items-center gap-3">
                            <span className="text-sm font-medium text-gold">{project.value} €</span>
                            {getStatusBadge(project.status)}
                          </div>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>

                {/* Pending reviews */}
                <Card className="glass-card">
                  <CardHeader className="flex flex-row items-center justify-between">
                    <CardTitle className="font-heading flex items-center gap-2">
                      <Star className="w-5 h-5 text-gold" />
                      Recenzii de aprobat
                    </CardTitle>
                    <Button variant="ghost" size="sm" className="text-primary" onClick={() => setActiveSection('reviews')}>
                      Vezi toate
                    </Button>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {pendingReviews.slice(0, 2).map((review) => (
                        <div 
                          key={review.id}
                          className="p-3 rounded-lg glass-card"
                        >
                          <div className="flex items-center justify-between mb-2">
                            <p className="font-medium text-foreground text-sm">{review.customer}</p>
                            <div className="flex items-center gap-1">
                              {[...Array(review.rating)].map((_, i) => (
                                <Star key={i} className="w-3 h-3 fill-gold text-gold" />
                              ))}
                            </div>
                          </div>
                          <p className="text-sm text-muted-foreground mb-2 line-clamp-2">"{review.text}"</p>
                          <div className="flex items-center justify-between">
                            <span className="text-xs text-muted-foreground">{review.date}</span>
                            <div className="flex items-center gap-2">
                              <Button 
                                size="sm" 
                                variant="outline" 
                                className="h-7 text-green-400 border-green-500/30 hover:bg-green-500/10"
                                onClick={() => handleApproveReview(review.id)}
                              >
                                <CheckCircle className="w-3 h-3 mr-1" />
                                Aproba
                              </Button>
                              <Button 
                                size="sm" 
                                variant="outline" 
                                className="h-7 text-destructive border-destructive/30 hover:bg-destructive/10"
                                onClick={() => handleRejectReview(review.id)}
                              >
                                Respinge
                              </Button>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              </div>

              {/* Quick actions */}
              <Card className="glass-card">
                <CardHeader>
                  <CardTitle className="font-heading">Actiuni rapide</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <Button 
                      variant="outline" 
                      className="h-auto py-6 flex-col gap-2 glass-card hover:border-primary/40"
                      onClick={() => setShowNewProjectModal(true)}
                    >
                      <Plus className="w-6 h-6 text-primary" />
                      <span>Proiect nou</span>
                    </Button>
                    <Button 
                      variant="outline" 
                      className="h-auto py-6 flex-col gap-2 glass-card hover:border-primary/40"
                      onClick={() => setShowNewCustomerModal(true)}
                    >
                      <UserPlus className="w-6 h-6 text-blue-400" />
                      <span>Client nou</span>
                    </Button>
                    <Button 
                      variant="outline" 
                      className="h-auto py-6 flex-col gap-2 glass-card hover:border-primary/40"
                      onClick={() => setShowNewQuoteModal(true)}
                    >
                      <FileText className="w-6 h-6 text-green-400" />
                      <span>Oferta noua</span>
                    </Button>
                    <Button 
                      variant="outline" 
                      className="h-auto py-6 flex-col gap-2 glass-card hover:border-primary/40"
                      onClick={() => setShowInventoryModal(true)}
                    >
                      <Package className="w-6 h-6 text-gold" />
                      <span>Adauga stoc</span>
                    </Button>
                  </div>
                </CardContent>
              </Card>
            </div>
          )}

          {/* Projects Section */}
          {activeSection === 'projects' && (
            <div className="space-y-6 animate-fade-in">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                  <div className="relative">
                    <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                    <Input 
                      placeholder="Cauta proiecte..."
                      className="pl-9 w-64 glass-input"
                    />
                  </div>
                  <Select defaultValue="all">
                    <SelectTrigger className="w-40 glass-input">
                      <SelectValue placeholder="Status" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="all">Toate</SelectItem>
                      <SelectItem value="in_progress">In progres</SelectItem>
                      <SelectItem value="pending">In asteptare</SelectItem>
                      <SelectItem value="completed">Finalizate</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <Button className="btn-neon" onClick={() => setShowNewProjectModal(true)}>
                  <Plus className="w-4 h-4 mr-2" />
                  Proiect nou
                </Button>
              </div>

              <Card className="glass-card">
                <CardContent className="p-0">
                  <div className="overflow-x-auto">
                    <table className="w-full">
                      <thead>
                        <tr className="border-b border-border">
                          <th className="text-left py-4 px-4 text-muted-foreground font-medium">Client</th>
                          <th className="text-left py-4 px-4 text-muted-foreground font-medium">Vehicul</th>
                          <th className="text-left py-4 px-4 text-muted-foreground font-medium">Serviciu</th>
                          <th className="text-left py-4 px-4 text-muted-foreground font-medium">Etapa</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Progres</th>
                          <th className="text-right py-4 px-4 text-muted-foreground font-medium">Valoare</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Status</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Actiuni</th>
                        </tr>
                      </thead>
                      <tbody>
                        {allProjects.map((project) => (
                          <tr 
                            key={project.id} 
                            className="border-b border-border/50 hover:bg-primary/5 cursor-pointer transition-colors"
                            onClick={() => setSelectedProject(project)}
                          >
                            <td className="py-4 px-4">
                              <div>
                                <p className="font-medium text-foreground">{project.customer}</p>
                                <p className="text-xs text-muted-foreground">{project.customerEmail}</p>
                              </div>
                            </td>
                            <td className="py-4 px-4 text-muted-foreground">{project.vehicle}</td>
                            <td className="py-4 px-4 text-muted-foreground">{project.service}</td>
                            <td className="py-4 px-4">
                              <Badge variant="outline" className="border-primary/30">{project.stage}</Badge>
                            </td>
                            <td className="py-4 px-4">
                              <div className="flex items-center gap-2">
                                <Progress value={project.progress} className="w-20 h-2" />
                                <span className="text-sm text-primary">{project.progress}%</span>
                              </div>
                            </td>
                            <td className="py-4 px-4 text-right font-medium text-gold">{project.value} €</td>
                            <td className="py-4 px-4 text-center">{getStatusBadge(project.status)}</td>
                            <td className="py-4 px-4 text-center">
                              <div className="flex items-center justify-center gap-1">
                                <Button variant="ghost" size="icon" className="h-8 w-8">
                                  <Eye className="w-4 h-4" />
                                </Button>
                                <Button variant="ghost" size="icon" className="h-8 w-8">
                                  <Edit className="w-4 h-4" />
                                </Button>
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

          {/* Customers Section */}
          {activeSection === 'customers' && (
            <div className="space-y-6 animate-fade-in">
              <div className="flex items-center justify-between">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                  <Input 
                    placeholder="Cauta clienti..."
                    className="pl-9 w-64 glass-input"
                  />
                </div>
                <Button className="btn-neon" onClick={() => setShowNewCustomerModal(true)}>
                  <UserPlus className="w-4 h-4 mr-2" />
                  Client nou
                </Button>
              </div>

              <div className="grid gap-4">
                {allCustomers.map((customer) => (
                  <Card key={customer.id} className="glass-card hover:border-primary/30 transition-all">
                    <CardContent className="p-4">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-4">
                          <Avatar className="w-12 h-12 border-2 border-primary/30">
                            <AvatarFallback className="bg-primary/10 text-primary font-semibold">
                              {customer.name.split(' ').map(n => n[0]).join('')}
                            </AvatarFallback>
                          </Avatar>
                          <div>
                            <div className="flex items-center gap-2">
                              <p className="font-medium text-foreground">{customer.name}</p>
                              {customer.status === 'vip' && (
                                <Badge className="bg-gold/20 text-gold">VIP</Badge>
                              )}
                              {customer.status === 'new' && (
                                <Badge className="bg-green-500/20 text-green-400">Nou</Badge>
                              )}
                            </div>
                            <div className="flex items-center gap-4 text-sm text-muted-foreground mt-1">
                              <span className="flex items-center gap-1">
                                <Mail className="w-3 h-3" />
                                {customer.email}
                              </span>
                              <span className="flex items-center gap-1">
                                <Phone className="w-3 h-3" />
                                {customer.phone}
                              </span>
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center gap-8">
                          <div className="text-center">
                            <p className="text-2xl font-heading font-bold text-foreground">{customer.projects}</p>
                            <p className="text-xs text-muted-foreground">Proiecte</p>
                          </div>
                          <div className="text-center">
                            <p className="text-2xl font-heading font-bold text-gold">{customer.totalSpent.toLocaleString()} €</p>
                            <p className="text-xs text-muted-foreground">Total cheltuit</p>
                          </div>
                          <div className="flex items-center gap-2">
                            <Button variant="ghost" size="icon">
                              <MessageSquare className="w-4 h-4" />
                            </Button>
                            <Button variant="ghost" size="icon">
                              <Eye className="w-4 h-4" />
                            </Button>
                          </div>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            </div>
          )}

          {/* Quotes Section */}
          {activeSection === 'quotes' && (
            <div className="space-y-6 animate-fade-in">
              <div className="flex items-center justify-between">
                <h2 className="font-heading text-xl">Oferte in asteptare ({pendingQuotes.length})</h2>
                <Button className="btn-neon" onClick={() => setShowNewQuoteModal(true)}>
                  <Plus className="w-4 h-4 mr-2" />
                  Oferta noua
                </Button>
              </div>

              <div className="grid gap-4">
                {pendingQuotes.map((quote) => (
                  <Card key={quote.id} className="glass-card hover:border-primary/30 transition-all">
                    <CardContent className="p-4">
                      <div className="flex items-center justify-between">
                        <div>
                          <div className="flex items-center gap-2 mb-1">
                            <p className="font-medium text-foreground">{quote.customer}</p>
                            {quote.status === 'new' && (
                              <Badge className="bg-primary/20 text-primary">Nou</Badge>
                            )}
                            {quote.status === 'contacted' && (
                              <Badge className="bg-blue-500/20 text-blue-400">Contactat</Badge>
                            )}
                          </div>
                          <p className="text-sm text-muted-foreground">{quote.vehicle} • {quote.service}</p>
                          <p className="text-xs text-muted-foreground mt-1">Primit: {quote.date}</p>
                        </div>
                        <div className="flex items-center gap-6">
                          <div className="text-right">
                            <p className="text-xl font-heading font-bold text-gold">{quote.estimatedValue} €</p>
                            <p className="text-xs text-muted-foreground">Estimat</p>
                          </div>
                          <div className="flex items-center gap-2">
                            <Button 
                              size="sm" 
                              className="btn-neon"
                              onClick={() => handleApproveQuote(quote.id)}
                            >
                              Converteste in proiect
                            </Button>
                            <Button variant="ghost" size="icon">
                              <MessageSquare className="w-4 h-4" />
                            </Button>
                          </div>
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            </div>
          )}

          {/* Reviews Section */}
          {activeSection === 'reviews' && (
            <div className="space-y-6 animate-fade-in">
              <Tabs defaultValue="pending">
                <TabsList className="glass-card">
                  <TabsTrigger value="pending">De aprobat ({pendingReviews.length})</TabsTrigger>
                  <TabsTrigger value="approved">Aprobate</TabsTrigger>
                  <TabsTrigger value="rejected">Respinse</TabsTrigger>
                </TabsList>

                <TabsContent value="pending" className="mt-6">
                  <div className="grid gap-4">
                    {pendingReviews.map((review) => (
                      <Card key={review.id} className="glass-card">
                        <CardContent className="p-4">
                          <div className="flex items-start justify-between mb-3">
                            <div>
                              <div className="flex items-center gap-3 mb-1">
                                <Avatar className="w-10 h-10">
                                  <AvatarFallback className="bg-primary/10 text-primary">
                                    {review.customer.split(' ').map(n => n[0]).join('')}
                                  </AvatarFallback>
                                </Avatar>
                                <div>
                                  <p className="font-medium text-foreground">{review.customer}</p>
                                  <p className="text-xs text-muted-foreground">{review.project}</p>
                                </div>
                              </div>
                            </div>
                            <div className="flex items-center gap-1">
                              {[...Array(review.rating)].map((_, i) => (
                                <Star key={i} className="w-5 h-5 fill-gold text-gold" />
                              ))}
                            </div>
                          </div>
                          
                          <p className="text-muted-foreground mb-4">"{review.text}"</p>
                          
                          <div className="flex items-center justify-between">
                            <span className="text-sm text-muted-foreground">{review.date}</span>
                            <div className="flex items-center gap-2">
                              <Button 
                                className="bg-green-500/20 text-green-400 hover:bg-green-500/30"
                                onClick={() => handleApproveReview(review.id)}
                              >
                                <CheckCircle className="w-4 h-4 mr-2" />
                                Aproba
                              </Button>
                              <Button 
                                variant="outline"
                                className="border-destructive/30 text-destructive hover:bg-destructive/10"
                                onClick={() => handleRejectReview(review.id)}
                              >
                                Respinge
                              </Button>
                            </div>
                          </div>
                        </CardContent>
                      </Card>
                    ))}
                  </div>
                </TabsContent>
              </Tabs>
            </div>
          )}

          {/* Inventory Section */}
          {activeSection === 'inventory' && (
            <div className="space-y-6 animate-fade-in">
              <div className="flex items-center justify-between">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                  <Input 
                    placeholder="Cauta produse..."
                    className="pl-9 w-64 glass-input"
                  />
                </div>
                <Button className="btn-neon" onClick={() => setShowInventoryModal(true)}>
                  <Plus className="w-4 h-4 mr-2" />
                  Adauga produs
                </Button>
              </div>

              <Card className="glass-card">
                <CardContent className="p-0">
                  <div className="overflow-x-auto">
                    <table className="w-full">
                      <thead>
                        <tr className="border-b border-border">
                          <th className="text-left py-4 px-4 text-muted-foreground font-medium">Produs</th>
                          <th className="text-left py-4 px-4 text-muted-foreground font-medium">Categorie</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Cantitate</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Stoc minim</th>
                          <th className="text-right py-4 px-4 text-muted-foreground font-medium">Pret/unitate</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Status</th>
                          <th className="text-center py-4 px-4 text-muted-foreground font-medium">Actiuni</th>
                        </tr>
                      </thead>
                      <tbody>
                        {inventoryItems.map((item) => (
                          <tr key={item.id} className="border-b border-border/50 hover:bg-primary/5">
                            <td className="py-4 px-4 font-medium text-foreground">{item.name}</td>
                            <td className="py-4 px-4 text-muted-foreground">{item.category}</td>
                            <td className="py-4 px-4 text-center">{item.quantity} {item.unit}</td>
                            <td className="py-4 px-4 text-center text-muted-foreground">{item.minStock} {item.unit}</td>
                            <td className="py-4 px-4 text-right font-medium">{item.price} €/{item.unit}</td>
                            <td className="py-4 px-4 text-center">
                              {item.quantity < item.minStock ? (
                                <Badge className="status-cancelled">Stoc scazut</Badge>
                              ) : (
                                <Badge className="status-active">In stoc</Badge>
                              )}
                            </td>
                            <td className="py-4 px-4 text-center">
                              <div className="flex items-center justify-center gap-1">
                                <Button variant="ghost" size="icon" className="h-8 w-8">
                                  <Edit className="w-4 h-4" />
                                </Button>
                                <Button variant="ghost" size="icon" className="h-8 w-8">
                                  <RefreshCw className="w-4 h-4" />
                                </Button>
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

          {/* Team Section */}
          {activeSection === 'team' && (
            <div className="space-y-6 animate-fade-in">
              <div className="grid md:grid-cols-3 gap-6">
                {teamMembers.map((member) => (
                  <Card key={member.id} className="glass-card hover:border-primary/30 transition-all">
                    <CardContent className="p-6 text-center">
                      <Avatar className="w-20 h-20 mx-auto mb-4 border-2 border-primary/30">
                        <AvatarFallback className="bg-primary/10 text-primary text-xl font-bold">
                          {member.name.split(' ').map(n => n[0]).join('')}
                        </AvatarFallback>
                      </Avatar>
                      <h3 className="font-heading font-semibold text-foreground">{member.name}</h3>
                      <p className="text-sm text-muted-foreground mb-4">{member.role}</p>
                      
                      <div className="grid grid-cols-3 gap-2 mb-4">
                        <div className="p-2 rounded-lg bg-card">
                          <p className="text-xl font-bold text-primary">{member.activeProjects}</p>
                          <p className="text-xs text-muted-foreground">Active</p>
                        </div>
                        <div className="p-2 rounded-lg bg-card">
                          <p className="text-xl font-bold text-foreground">{member.completedProjects}</p>
                          <p className="text-xs text-muted-foreground">Finalizate</p>
                        </div>
                        <div className="p-2 rounded-lg bg-card">
                          <p className="text-xl font-bold text-gold">{member.rating}</p>
                          <p className="text-xs text-muted-foreground">Rating</p>
                        </div>
                      </div>
                      
                      <Button variant="outline" className="w-full">Vezi profilul</Button>
                    </CardContent>
                  </Card>
                ))}
              </div>
            </div>
          )}

          {/* Analytics Section */}
          {activeSection === 'analytics' && (
            <div className="space-y-6 animate-fade-in">
              <div className="grid lg:grid-cols-2 gap-6">
                <Card className="glass-card">
                  <CardHeader>
                    <CardTitle className="font-heading">Proiecte pe etape</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="h-[300px]">
                      <ResponsiveContainer width="100%" height="100%">
                        <BarChart data={projectsByStatus}>
                          <CartesianGrid strokeDasharray="3 3" stroke="hsl(0, 0%, 20%)" />
                          <XAxis dataKey="status" stroke="hsl(0, 0%, 50%)" angle={-45} textAnchor="end" height={80} />
                          <YAxis stroke="hsl(0, 0%, 50%)" />
                          <Tooltip 
                            contentStyle={{ 
                              background: 'hsl(0, 0%, 10%)', 
                              border: '1px solid hsl(328, 100%, 54%, 0.3)',
                              borderRadius: '8px'
                            }}
                          />
                          <Bar dataKey="count" fill="hsl(328, 100%, 54%)" radius={[4, 4, 0, 0]} />
                        </BarChart>
                      </ResponsiveContainer>
                    </div>
                  </CardContent>
                </Card>

                <Card className="glass-card">
                  <CardHeader>
                    <CardTitle className="font-heading">Statistici generale</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-4">
                      <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                        <span className="text-muted-foreground">Venituri totale</span>
                        <span className="text-2xl font-bold text-gold">{mockStats.totalRevenue.toLocaleString()} €</span>
                      </div>
                      <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                        <span className="text-muted-foreground">Proiecte finalizate</span>
                        <span className="text-2xl font-bold text-foreground">{mockStats.completedProjects}</span>
                      </div>
                      <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                        <span className="text-muted-foreground">Rata de conversie oferte</span>
                        <span className="text-2xl font-bold text-green-400">78%</span>
                      </div>
                      <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                        <span className="text-muted-foreground">Valoare medie proiect</span>
                        <span className="text-2xl font-bold text-primary">2,850 €</span>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </div>
            </div>
          )}

          {/* Settings Section */}
          {activeSection === 'settings' && (
            <div className="space-y-6 animate-fade-in">
              <Card className="glass-card">
                <CardHeader>
                  <CardTitle className="font-heading">Setari generale</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="grid gap-4">
                    <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                      <div>
                        <p className="font-medium">Notificari email</p>
                        <p className="text-sm text-muted-foreground">Primeste notificari pentru proiecte noi</p>
                      </div>
                      <Button variant="outline">Configureaza</Button>
                    </div>
                    <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                      <div>
                        <p className="font-medium">Backup date</p>
                        <p className="text-sm text-muted-foreground">Exporta toate datele platformei</p>
                      </div>
                      <Button variant="outline">
                        <Download className="w-4 h-4 mr-2" />
                        Export
                      </Button>
                    </div>
                    <div className="flex items-center justify-between p-4 rounded-lg bg-card">
                      <div>
                        <p className="font-medium">Categorii servicii</p>
                        <p className="text-sm text-muted-foreground">Gestioneaza categoriile de servicii</p>
                      </div>
                      <Button variant="outline">Editeaza</Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            </div>
          )}
        </div>
      </main>

      {/* New Project Modal */}
      <Dialog open={showNewProjectModal} onOpenChange={setShowNewProjectModal}>
        <DialogContent className="glass-card border-border max-w-2xl">
          <DialogHeader>
            <DialogTitle>Proiect nou</DialogTitle>
            <DialogDescription>Adauga un proiect nou in sistem</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Client</Label>
                <Select>
                  <SelectTrigger className="glass-input">
                    <SelectValue placeholder="Selecteaza clientul" />
                  </SelectTrigger>
                  <SelectContent>
                    {allCustomers.map(c => (
                      <SelectItem key={c.id} value={String(c.id)}>{c.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Serviciu</Label>
                <Select>
                  <SelectTrigger className="glass-input">
                    <SelectValue placeholder="Selecteaza serviciul" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="fullwrap">Full Wrap</SelectItem>
                    <SelectItem value="partial">Partial Wrap</SelectItem>
                    <SelectItem value="ppf">PPF</SelectItem>
                    <SelectItem value="custom">Custom Design</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="space-y-2">
              <Label>Vehicul</Label>
              <Input placeholder="Ex: BMW M4 Competition 2023" className="glass-input" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Culoare/Folie</Label>
                <Input placeholder="Ex: Satin Midnight Blue" className="glass-input" />
              </div>
              <div className="space-y-2">
                <Label>Valoare (€)</Label>
                <Input type="number" placeholder="3000" className="glass-input" />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Data start</Label>
                <Input type="date" className="glass-input" />
              </div>
              <div className="space-y-2">
                <Label>Data estimata livrare</Label>
                <Input type="date" className="glass-input" />
              </div>
            </div>
            <div className="space-y-2">
              <Label>Note</Label>
              <Textarea placeholder="Note aditionale..." className="glass-input" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowNewProjectModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={() => {
              toast.success('Proiect creat cu succes!');
              setShowNewProjectModal(false);
            }}>Creeaza proiect</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* New Customer Modal */}
      <Dialog open={showNewCustomerModal} onOpenChange={setShowNewCustomerModal}>
        <DialogContent className="glass-card border-border">
          <DialogHeader>
            <DialogTitle>Client nou</DialogTitle>
            <DialogDescription>Adauga un client nou in baza de date</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="space-y-2">
              <Label>Nume complet</Label>
              <Input placeholder="Ion Popescu" className="glass-input" />
            </div>
            <div className="space-y-2">
              <Label>Email</Label>
              <Input type="email" placeholder="email@example.com" className="glass-input" />
            </div>
            <div className="space-y-2">
              <Label>Telefon</Label>
              <Input type="tel" placeholder="+40 722 123 456" className="glass-input" />
            </div>
            <div className="space-y-2">
              <Label>Note</Label>
              <Textarea placeholder="Note despre client..." className="glass-input" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowNewCustomerModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={() => {
              toast.success('Client adaugat cu succes!');
              setShowNewCustomerModal(false);
            }}>Adauga client</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* New Quote Modal */}
      <Dialog open={showNewQuoteModal} onOpenChange={setShowNewQuoteModal}>
        <DialogContent className="glass-card border-border max-w-2xl">
          <DialogHeader>
            <DialogTitle>Oferta noua</DialogTitle>
            <DialogDescription>Creeaza o oferta pentru un potential client</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Nume client</Label>
                <Input placeholder="Ion Popescu" className="glass-input" />
              </div>
              <div className="space-y-2">
                <Label>Telefon</Label>
                <Input type="tel" placeholder="+40 722 123 456" className="glass-input" />
              </div>
            </div>
            <div className="space-y-2">
              <Label>Email</Label>
              <Input type="email" placeholder="email@example.com" className="glass-input" />
            </div>
            <div className="space-y-2">
              <Label>Vehicul</Label>
              <Input placeholder="Ex: Audi RS6 2024" className="glass-input" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Serviciu dorit</Label>
                <Select>
                  <SelectTrigger className="glass-input">
                    <SelectValue placeholder="Selecteaza" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="fullwrap">Full Wrap</SelectItem>
                    <SelectItem value="partial">Partial Wrap</SelectItem>
                    <SelectItem value="ppf">PPF</SelectItem>
                    <SelectItem value="custom">Custom Design</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Valoare estimata (€)</Label>
                <Input type="number" placeholder="2500" className="glass-input" />
              </div>
            </div>
            <div className="space-y-2">
              <Label>Descriere cerere</Label>
              <Textarea placeholder="Detalii despre cererea clientului..." className="glass-input" />
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowNewQuoteModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={() => {
              toast.success('Oferta creata cu succes!');
              setShowNewQuoteModal(false);
            }}>Creeaza oferta</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Inventory Modal */}
      <Dialog open={showInventoryModal} onOpenChange={setShowInventoryModal}>
        <DialogContent className="glass-card border-border">
          <DialogHeader>
            <DialogTitle>Adauga produs</DialogTitle>
            <DialogDescription>Adauga un produs nou in inventar</DialogDescription>
          </DialogHeader>
          <div className="grid gap-4 py-4">
            <div className="space-y-2">
              <Label>Nume produs</Label>
              <Input placeholder="Ex: 3M 1080 Satin Dark Grey" className="glass-input" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label>Categorie</Label>
                <Select>
                  <SelectTrigger className="glass-input">
                    <SelectValue placeholder="Selecteaza" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="wrap">Folie Wrap</SelectItem>
                    <SelectItem value="ppf">PPF</SelectItem>
                    <SelectItem value="consumabile">Consumabile</SelectItem>
                    <SelectItem value="altele">Altele</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>Unitate masura</Label>
                <Select>
                  <SelectTrigger className="glass-input">
                    <SelectValue placeholder="Selecteaza" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="mp">mp</SelectItem>
                    <SelectItem value="L">Litri</SelectItem>
                    <SelectItem value="buc">Bucati</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-4">
              <div className="space-y-2">
                <Label>Cantitate</Label>
                <Input type="number" placeholder="10" className="glass-input" />
              </div>
              <div className="space-y-2">
                <Label>Stoc minim</Label>
                <Input type="number" placeholder="5" className="glass-input" />
              </div>
              <div className="space-y-2">
                <Label>Pret/unitate (€)</Label>
                <Input type="number" placeholder="45" className="glass-input" />
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowInventoryModal(false)}>Anuleaza</Button>
            <Button className="btn-neon" onClick={() => {
              toast.success('Produs adaugat cu succes!');
              setShowInventoryModal(false);
            }}>Adauga produs</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
