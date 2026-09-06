import { Collection, ObjectId } from 'mongodb';
import { getDb } from './mongo';

export interface ConsultingTicket {
  _id?: string;
  userId: string;
  userEmail: string;
  userName?: string | null;
  userPlan: 'free' | 'pro' | 'elite';

  subject: string;
  category: 'fiscal' | 'trading' | 'technical' | 'billing' | 'other';
  priority: 'low' | 'normal' | 'high' | 'urgent';

  // Content
  messages: Array<{
    from: 'user' | 'owner';
    content: string;
    createdAt: Date;
  }>;

  // Status
  status: 'open' | 'waiting_owner' | 'waiting_user' | 'resolved' | 'closed';

  // Booking (if user requested paid session)
  bookingRequested?: boolean;
  bookingScheduledFor?: Date;
  bookingDurationMinutes?: number;
  bookingFeeRon?: number;
  bookingPaid?: boolean;
  bookingNotes?: string;

  createdAt: Date;
  updatedAt: Date;
  resolvedAt?: Date;
}

async function getTicketsCol(): Promise<Collection<ConsultingTicket> | null> {
  const db = await getDb();
  if (!db) return null;
  const col = db.collection<ConsultingTicket>('consulting_tickets');
  try {
    await col.createIndex({ userId: 1, createdAt: -1 });
    await col.createIndex({ status: 1, priority: 1 });
  } catch {}
  return col;
}

const memTickets = new Map<string, ConsultingTicket>();

export async function createTicket(ticket: Omit<ConsultingTicket, '_id' | 'createdAt' | 'updatedAt'>): Promise<string> {
  const now = new Date();
  const full: ConsultingTicket = { ...ticket, createdAt: now, updatedAt: now };

  const col = await getTicketsCol();
  if (col) {
    const result = await col.insertOne(full);
    return result.insertedId.toString();
  }
  const id = `ticket_${Date.now()}_${Math.random().toString(36).slice(2, 9)}`;
  memTickets.set(id, { ...full, _id: id });
  return id;
}

export async function getTicket(id: string): Promise<ConsultingTicket | null> {
  const col = await getTicketsCol();
  if (col) {
    try {
      if (ObjectId.isValid(id)) {
        return col.findOne({ _id: new ObjectId(id) } as unknown as Record<string, unknown>);
      }
    } catch {}
  }
  return memTickets.get(id) || null;
}

export async function getUserTickets(userId: string, limit = 50): Promise<ConsultingTicket[]> {
  const col = await getTicketsCol();
  if (col) {
    return col.find({ userId }).sort({ updatedAt: -1 }).limit(limit).toArray();
  }
  return Array.from(memTickets.values())
    .filter((t) => t.userId === userId)
    .sort((a, b) => b.updatedAt.getTime() - a.updatedAt.getTime())
    .slice(0, limit);
}

/**
 * Owner view - all tickets (admin only).
 */
export async function getAllTickets(
  options: { status?: ConsultingTicket['status']; limit?: number } = {}
): Promise<ConsultingTicket[]> {
  const { limit = 100, status } = options;
  const col = await getTicketsCol();
  const filter: Record<string, unknown> = {};
  if (status) filter.status = status;

  if (col) {
    return col.find(filter).sort({ updatedAt: -1 }).limit(limit).toArray();
  }

  let tickets = Array.from(memTickets.values());
  if (status) tickets = tickets.filter((t) => t.status === status);
  return tickets.sort((a, b) => b.updatedAt.getTime() - a.updatedAt.getTime()).slice(0, limit);
}

export async function addMessageToTicket(
  ticketId: string,
  from: 'user' | 'owner',
  content: string
): Promise<boolean> {
  const col = await getTicketsCol();
  const now = new Date();
  const newMessage = { from, content, createdAt: now };
  const newStatus = from === 'user' ? 'waiting_owner' : 'waiting_user';

  if (col) {
    try {
      const filter = ObjectId.isValid(ticketId)
        ? { _id: new ObjectId(ticketId) } as unknown as Record<string, unknown>
        : { _id: ticketId } as unknown as Record<string, unknown>;
      const result = await col.updateOne(
        filter,
        {
          $push: { messages: newMessage },
          $set: { status: newStatus, updatedAt: now },
        }
      );
      return result.modifiedCount > 0;
    } catch {
      return false;
    }
  }

  const ticket = memTickets.get(ticketId);
  if (!ticket) return false;
  ticket.messages.push(newMessage);
  ticket.status = newStatus;
  ticket.updatedAt = now;
  return true;
}

export async function updateTicketStatus(
  ticketId: string,
  status: ConsultingTicket['status']
): Promise<boolean> {
  const col = await getTicketsCol();
  const now = new Date();
  const updates: Record<string, unknown> = { status, updatedAt: now };
  if (status === 'resolved' || status === 'closed') updates.resolvedAt = now;

  if (col) {
    try {
      const filter = ObjectId.isValid(ticketId)
        ? { _id: new ObjectId(ticketId) } as unknown as Record<string, unknown>
        : { _id: ticketId } as unknown as Record<string, unknown>;
      const result = await col.updateOne(filter, { $set: updates });
      return result.modifiedCount > 0;
    } catch {
      return false;
    }
  }

  const ticket = memTickets.get(ticketId);
  if (!ticket) return false;
  Object.assign(ticket, updates);
  return true;
}

export async function updateTicketBooking(
  ticketId: string,
  booking: {
    scheduledFor: Date;
    durationMinutes: number;
    feeRon: number;
    notes?: string;
  }
): Promise<boolean> {
  const col = await getTicketsCol();
  const updates = {
    bookingRequested: true,
    bookingScheduledFor: booking.scheduledFor,
    bookingDurationMinutes: booking.durationMinutes,
    bookingFeeRon: booking.feeRon,
    bookingNotes: booking.notes,
    bookingPaid: false,
    updatedAt: new Date(),
  };

  if (col) {
    try {
      const filter = ObjectId.isValid(ticketId)
        ? { _id: new ObjectId(ticketId) } as unknown as Record<string, unknown>
        : { _id: ticketId } as unknown as Record<string, unknown>;
      const result = await col.updateOne(filter, { $set: updates });
      return result.modifiedCount > 0;
    } catch {
      return false;
    }
  }

  const ticket = memTickets.get(ticketId);
  if (!ticket) return false;
  Object.assign(ticket, updates);
  return true;
}
